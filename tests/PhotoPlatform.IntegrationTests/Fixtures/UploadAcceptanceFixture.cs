using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using PhotoPlatform.Application.Interfaces;
using PhotoPlatform.Application.Services;
using PhotoPlatform.Infrastructure.Persistence;
using PhotoPlatform.Infrastructure.Processing;
using PhotoPlatform.Infrastructure.Storage;
using PhotoPlatform.IntegrationTests.Api;
using PhotoPlatform.IntegrationTests.TestDoubles;

namespace PhotoPlatform.IntegrationTests.Fixtures;

// TASK-12 Backend Acceptance Test 的測試環境。
// 每個測試案例建立自己獨立的 SQL Database、temporary Storage、Queue 與 Log capture，
// 避免不同 Acceptance Test 互相污染。
// 同一案例中的多個 HTTP request 可共用正式 Storage instance，
// 但各 request 的 fault state / transaction / compensation 仍彼此隔離。
internal sealed class UploadAcceptanceFixture : IAsyncLifetime
{
    // 每個 fixture 使用唯一 GUID database，避免 SQL 測試彼此共用資料。
    private readonly string databaseName = "PhotoPlatform_Task12_" + Guid.NewGuid().ToString("N");
    // Storage 使用 TASK-12 專用 temporary directory；
    // Dispose 時會檢查路徑範圍後再遞迴刪除，避免誤刪其他資料夾。
    private readonly string parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "PhotoPlatform-Task12"));
    private string? masterConnection;
    private string? testConnection;
    private bool created;
    public string StorageRoot { get; } = Path.Combine(Path.GetTempPath(), "PhotoPlatform-Task12", Guid.NewGuid().ToString("N"));
    // Logs 收集正式 pipeline 的 log；
    // Requests 依 HTTP Header marker 保存每個 request 自己的 fault injection state。
    public CapturingLoggerProvider Logs { get; } = new();
    public ConcurrentDictionary<string, UploadFaultInjection> Requests { get; } = new();
    // Queue 與 Host 每個測試案例各自建立。
    // sharedStorage 則在同一 fixture 內共用，模擬 production 的 shared / singleton storage lifetime。
    public ChannelProcessingQueue Queue { get; private set; } = null!;
    public UploadApiHost Host { get; private set; } = null!;
    private LocalFileStorageService sharedStorage = null!;

    // 初始化 Acceptance Test 環境。
    // 若初始化失敗，仍會嘗試執行 cleanup，並保留最初的 initialization exception。
    public Task InitializeAsync() => InitializeWithCleanupAsync(InitializeDatabaseAsync, DisposeAsync);

    // 共用的初始化保護 helper。
    // initialize 失敗時仍執行 cleanup；若 cleanup 也失敗，
    // cleanup error 只附加在原始 exception 上，不取代真正的初始化錯誤。
    internal static async Task InitializeWithCleanupAsync(Func<Task> initialize, Func<Task> cleanup)
    {
        try { await initialize(); }
        // 先保存原始初始化錯誤，再 best-effort cleanup。
        catch (Exception original)
        {
            try { await cleanup(); }
            catch (Exception failure) { original.Data["FixtureCleanupFailure"] = failure; }
            throw;
        }
    }

    // 建立本次 Acceptance Test 專用的真實 SQL Server Database。
    // 先連 master 建立 GUID database，再切換到該 database 並套用 EF Core migration。
    private async Task InitializeDatabaseAsync()
    {
        // SQL integration 必須使用外部提供的測試連線字串。
        var supplied = Environment.GetEnvironmentVariable("PHOTO_PLATFORM_TEST_DB_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(supplied))
            throw new InvalidOperationException("SQL acceptance requires PHOTO_PLATFORM_TEST_DB_CONNECTION_STRING.");
        var builder = new SqlConnectionStringBuilder(supplied) { InitialCatalog = "master" };
        masterConnection = builder.ConnectionString;
        await using var connection = new SqlConnection(masterConnection);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        // 每個 fixture 建立自己的 database，避免案例間互相污染。
        command.CommandText = $"CREATE DATABASE [{databaseName}]";
        await command.ExecuteNonQueryAsync();
        created = true;
        builder.InitialCatalog = databaseName;
        testConnection = builder.ConnectionString;
        await using var db = CreateContext();
        // 使用正式 migration 建立 schema，不使用 InMemory provider 或手動假 schema。
        await db.Database.MigrateAsync();
    }

    // 建立新的 DbContext 連到本 fixture 的測試 database。
    // Acceptance assertion 會刻意重新建立 DbContext，
    // 以確認資料真的已持久化到 SQL Server，而不是只存在 EF Change Tracker。
    public PhotoPlatformDbContext CreateContext() => new(new DbContextOptionsBuilder<PhotoPlatformDbContext>()
        .UseSqlServer(testConnection ?? throw new InvalidOperationException("SQL fixture not initialized.")).Options);

    // 啟動 Acceptance Test 的正式 HTTP Host，並建立 Queue、shared Storage 與 test-only decorators。
    // 目的不是 mock 掉 production behavior，而是在正式服務外層加入可控制的 fault injection / observation。
    public async Task StartAsync(int capacity = 100)
    {
        // 每個 fixture 使用獨立 Queue；同一 fixture 內的 request 共用同一個正式 Storage instance。
        Queue = new(new ProcessingQueueOptions { Capacity = capacity });
        sharedStorage = new LocalFileStorageService(StorageRoot);
        Host = await UploadApiHost.StartAsync(services =>
        {
            services.AddHttpContextAccessor();
            services.AddSingleton<ILoggerProvider>(Logs);
            // 依 X-Task12-Request Header 找到這次 HTTP request 對應的 fault state。
            services.AddScoped(provider =>
            {
                var state = Requests[provider.GetRequiredService<IHttpContextAccessor>()
                    .HttpContext!.Request.Headers["X-Task12-Request"].ToString()];
                // Queue enqueue 前重新查 SQL Server，確認 Job 已先 Commit 且資料內容一致。
                state.VerifyCommitted = async (job, token) =>
                {
                    await using var read = CreateContext();
                    var row = await read.ProcessingJobs.AsNoTracking().SingleAsync(x => x.Id == job.Id, token);
                    Assert.Equal((job.ImageId, job.BatchId, job.Workflow), (row.ImageId, row.BatchId, row.Workflow));
                };
                return state;
            });
            // 用 test-only decorator 包住正式 Storage，控制失敗點但保留真實檔案存取行為。
            services.RemoveAll<IFileStorageService>();
            services.AddScoped<IFileStorageService>(provider => provider.GetRequiredService<UploadFaultInjection>()
                .Storage(sharedStorage));
            // 用 test-only decorator 包住正式 SQL persistence，控制 Save / Commit / Rollback failure。
            services.RemoveAll<IUploadPersistence>();
            services.AddScoped<IUploadPersistence>(provider => provider.GetRequiredService<UploadFaultInjection>()
                .Persistence(new UploadPersistence(provider.GetRequiredService<PhotoPlatformDbContext>())));
            // 用 test-only decorator 包住正式 Channel Queue，觀察 enqueue 或注入 Queue failure。
            services.RemoveAll<IProcessingQueue>();
            services.AddScoped<IProcessingQueue>(provider => provider.GetRequiredService<UploadFaultInjection>().Queue(Queue));
            // 使用正式 UploadService，只替換其依賴為本次 Acceptance Test 的 decorated services。
            services.RemoveAll<IUploadService>();
            services.AddScoped<IUploadService>(provider => provider.GetRequiredService<UploadFaultInjection>().Service(
                new UploadService(provider.GetRequiredService<IFileValidationService>(),
                    provider.GetRequiredService<IFileStorageService>(), provider.GetRequiredService<IUploadPersistence>(),
                    provider.GetRequiredService<IProcessingQueue>(), provider.GetRequiredService<ILogger<UploadService>>())));
        }, new()
        {
            ["ConnectionStrings:PhotoPlatform"] = testConnection,
            ["Upload:StorageRoot"] = StorageRoot,
            ["Upload:MaxFileSizeBytes"] = "1024"
        });
    }

    // 列出本 fixture StorageRoot 下實際存在的所有檔案，
    // 供 Acceptance Test 驗證是否有檔案殘留或成功保存。
    public string[] Files() => Directory.Exists(StorageRoot)
        ? Directory.GetFiles(StorageRoot, "*", SearchOption.AllDirectories) : [];

    // 將 logical StorageKey（例如 original/<guid>）轉成 temporary StorageRoot 下的實體路徑。
    // 僅供測試驗證實際檔案 bytes 使用。
    public string PhysicalPath(string key) => Path.Combine(StorageRoot, key.Replace('/', Path.DirectorySeparatorChar));

    // 測試結束後依序關閉 Host、刪除測試 Database、刪除 temporary Storage。
    // 所有 cleanup 都採 best-effort，不因前一步失敗就跳過後續清理。
    public Task DisposeAsync() => CleanupAsync(
        async () => { if (Host is not null) await Host.DisposeAsync(); },
        DropDatabaseAsync,
        () =>
        {
            // 刪除 Storage 前再次確認路徑位於 TASK-12 專用 temp root，避免 recursive delete 誤刪其他資料。
            var target = Path.GetFullPath(StorageRoot);
            if (!string.Equals(Path.GetDirectoryName(target), parent, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unexpected acceptance storage directory.");
            if (Directory.Exists(target)) Directory.Delete(target, true);
            return Task.CompletedTask;
        });

    // 依序嘗試所有 cleanup operation。
    // 即使其中一項失敗，也繼續執行後續 cleanup；
    // 最後再用 AggregateException 回報所有 cleanup failures。
    internal static async Task CleanupAsync(params Func<Task>[] operations)
    {
        var failures = new List<Exception>();
        // 每個 cleanup operation 都獨立執行，避免單一失敗中斷其他清理。
        foreach (var operation in operations)
        {
            try { await operation(); }
            catch (Exception exception) { failures.Add(exception); }
        }
        if (failures.Count > 0) throw new AggregateException("Acceptance fixture cleanup failed.", failures);
    }

    // 只刪除本 fixture 自己建立的 GUID database。
    // 不操作環境變數原本連線字串所指向的既有 Database。
    private async Task DropDatabaseAsync()
    {
        if (!created || masterConnection is null) return;
        await using var connection = new SqlConnection(masterConnection);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}]";
        await command.ExecuteNonQueryAsync();
        created = false;
    }
}
