using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using PhotoPlatform.Application.DTOs;
using PhotoPlatform.Application.Exceptions;
using PhotoPlatform.Infrastructure.Storage;
using PhotoPlatform.Application.Interfaces;
using PhotoPlatform.Application.Services;
using PhotoPlatform.Domain.Entities;
using PhotoPlatform.Domain.Enums;
using PhotoPlatform.Infrastructure.Persistence;

namespace PhotoPlatform.IntegrationTests.Persistence;
// 使用真實 SQL Server 驗證 Identity、資料約束與 Transaction；Unit Test 替身無法證明這些資料庫行為。
// 每個案例使用獨立 Database，避免測試資料互相影響。
public sealed class UploadPersistenceTests : IAsyncLifetime
{
    private readonly string _databaseName = "PhotoPlatform_Task06_" + Guid.NewGuid().ToString("N");
    private DbContextOptions<PhotoPlatformDbContext>? _options;
    private bool _created;
    private readonly string _storageRoot = Path.Combine(Path.GetTempPath(), "PhotoPlatform-Task09", Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset CreatedAt = new(2026, 9, 17, 1, 2, 3, TimeSpan.Zero);

    public async Task InitializeAsync()
    {
        // 為每個案例建立隔離 Database 並套用 Migration，不操作連線字串原先指定的 Database。
        var connectionString = Environment.GetEnvironmentVariable("PHOTO_PLATFORM_TEST_DB_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Set PHOTO_PLATFORM_TEST_DB_CONNECTION_STRING to a Docker SQL Server test instance with database creation permission.");
        var builder = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" };
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE [{_databaseName}]";
        await command.ExecuteNonQueryAsync();
        _created = true;
        builder.InitialCatalog = _databaseName;
        _options = new DbContextOptionsBuilder<PhotoPlatformDbContext>().UseSqlServer(builder.ConnectionString).Options;
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }
    public async Task DisposeAsync()
    {
        // 只清理本案例成功建立且已備妥連線設定的 Database。
        if (_created && _options is not null)
        {
            await using var db = CreateContext();
            await db.Database.EnsureDeletedAsync();
        }
        DeleteTestStorage();
    }
    private void DeleteTestStorage()
    {
        // 只刪本案例的 GUID 目錄；確認父目錄，避免誤碰使用者檔案。
        var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "PhotoPlatform-Task09"));
        var target = Path.GetFullPath(_storageRoot);
        if (Path.GetDirectoryName(target) != parent) throw new InvalidOperationException("Unexpected test directory.");
        if (Directory.Exists(target)) Directory.Delete(target, true);
    }
    private PhotoPlatformDbContext CreateContext() => new(_options ?? throw new InvalidOperationException("Database has not been initialized."));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    // 使用真實 SQL Server 驗證 Batch Status Query。
    // 確認 Batch 存在時可正確取得狀態，不存在時回傳 null；
    // 同時驗證查詢使用新的 DbContext、AsNoTracking 與單次 Projection，
    // 只查詢 batches 所需欄位，不額外查詢或 JOIN Images / ProcessingJobs。
    public async Task BatchStatusQuery_UsesOneUntrackedProjectionFromNewContext(bool exists)
    {
        var id = Guid.NewGuid();
        if (exists)
        {
            await using var seed = CreateContext();
            var batch = new Batch(id, 100, CreatedAt);
            seed.Batches.Add(batch);
            // 直接建立已持久化的處理結果作為測試資料，
            // 避免為了準備測試資料而額外觸發 Domain lifecycle 行為。
            seed.Entry(batch).Property(x => x.ProcessedCount).CurrentValue = 80;
            seed.Entry(batch).Property(x => x.SuccessCount).CurrentValue = 78;
            seed.Entry(batch).Property(x => x.FailedCount).CurrentValue = 2;
            seed.Entry(batch).Property(x => x.Status).CurrentValue = "Processing";
            await seed.SaveChangesAsync();
        }
        // 註冊測試用 SQL Interceptor，記錄 EF Core 實際執行的 SQL 與查詢參數。
        var commands = new QueryCommands();
        var options = new DbContextOptionsBuilder<PhotoPlatformDbContext>(_options!)
            .AddInterceptors(commands).Options;

        // 使用新的 DbContext 執行查詢，避免受到 Seed Context Tracking 狀態影響。
        await using var read = new PhotoPlatformDbContext(options);
        var result = await new BatchStatusQuery(new BatchStatusPersistence(read))
            .GetBatchStatusAsync(id, CancellationToken.None);
        if (exists)
            Assert.Equal(new BatchStatusResult(id, 100, 80, 78, 2, 80m, "Processing"), result);
        else
            Assert.Null(result);

        // Batch Status 為唯讀查詢，AsNoTracking 後不應留下任何追蹤中的 Entity。
        Assert.Empty(read.ChangeTracker.Entries());

        // 整個 Batch Status 查詢只能執行一次 SQL，
        // 且只從 batches 投影需要的欄位，不查 Images / ProcessingJobs、不使用 JOIN。
        var sql = Assert.Single(commands.Commands);
        Assert.Contains("[batches]", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[images]", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[processing_jobs]", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CreatedAt", sql);
        Assert.DoesNotContain("CompletedAt", sql);
        Assert.DoesNotContain("JOIN", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(id, commands.BatchId);
    }

    // 測試專用 EF Core SQL Interceptor。
    // 攔截實際執行的讀取 SQL，記錄 SQL 文字與 BatchId Parameter，
    // 用來驗證 Query 是否維持單次、精簡且正確參數化的查詢。
    private sealed class QueryCommands : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];
        public Guid? BatchId { get; private set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            // 記錄 EF Core 實際送出的 SQL。
            Commands.Add(command.CommandText);

            // 此 Query 預期只有一個 BatchId Parameter，並確認其型別為 Guid。
            BatchId = Assert.IsType<Guid>(Assert.Single(command.Parameters.Cast<DbParameter>()).Value);
            return ValueTask.FromResult(result);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    // 使用真實 HTTP、SQL 與 Storage 跑完整 Upload 流程。
    // 驗證 DB 已 Commit 後，即使 Queue enqueue 失敗，也不能撤銷已提交資料或刪除已保存檔案。

    public async Task UploadApi_RealSqlAndStorage_PreserveAcceptanceBoundary(bool queueFails)
    {
        // 建立測試 API 所需設定，使用目前 Integration Test 的 SQL 與暫存 Storage。
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:PhotoPlatform"] = CreateConnectionString(),
            ["Upload:StorageRoot"] = _storageRoot,
            ["Upload:MaxFileSizeBytes"] = "1024"
        };
        // 啟動完整 Upload API。
        // queueFails = true 時，才將 IProcessingQueue 換成故意失敗的測試實作；
        // 其餘 UploadService、SQL、Storage 流程皆使用正式實作。
        await using var host = await Api.UploadApiHost.StartAsync(services =>
        {
            if (queueFails)
                // 模擬 Commit 完成後，Queue.EnqueueAsync() 發生失敗。
                services.AddSingleton<IProcessingQueue>(
                    new UploadDependencies((_, _) => throw new IOException("secret SQL detail")));
        }, settings);

        // 建立正常的 multipart/form-data：2 個 JPEG，workflow = Full。
        using var body = Api.UploadApiTests.Multipart(2, "Full");

        // 透過真實 HTTP 呼叫 Upload API。
        using var response = await host.Client.PostAsync("/api/v1/images/upload", body);

        // Queue 正常時應回 202；
        // Queue 失敗時應回 500。
        Assert.Equal(queueFails ? HttpStatusCode.InternalServerError : HttpStatusCode.Accepted,
            response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();

        // 原始例外中的敏感資訊不得出現在 HTTP Response。
        Assert.DoesNotContain("secret", text);
        using var json = JsonDocument.Parse(text);

        // Queue 失敗時，對外應統一回 INTERNAL_ERROR，並包含 TraceId。
        if (queueFails)
        {
            var error = json.RootElement.GetProperty("error");
            Assert.Equal("INTERNAL_ERROR", error.GetProperty("code").GetString());
            Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("traceId").GetString()));
        }
        // 使用新的 DbContext 重新查詢 SQL，確認資料是真的已 Commit，而不是只存在原本 DbContext 的追蹤狀態中。
        // 即使 Queue 失敗、API 回 500，已提交的 Batch、Image、ProcessingJob 仍必須存在。
        await using var verification = CreateContext();
        Assert.Equal(1, await verification.Batches.CountAsync());
        Assert.Equal(2, await verification.Images.CountAsync());
        Assert.Equal(2, await verification.ProcessingJobs.CountAsync());

        var persistedId = await verification.Batches.Select(x => x.Id).SingleAsync();
        using var statusResponse = await host.Client.GetAsync($"/api/v1/images/batches/{persistedId}/status");
        Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
        using var statusJson = JsonDocument.Parse(await statusResponse.Content.ReadAsStringAsync());
        var statusData = statusJson.RootElement.GetProperty("data");
        Assert.Equal(persistedId, statusData.GetProperty("batchId").GetGuid());
        Assert.Equal(2, statusData.GetProperty("totalCount").GetInt32());
        Assert.Equal(0m, statusData.GetProperty("progressPercentage").GetDecimal());
        Assert.Equal("Pending", statusData.GetProperty("status").GetString());

        // 再確認 Storage 中的原始檔案仍然存在，證明 Commit 後的 Queue 失敗沒有觸發錯誤補償刪檔。
        foreach (var image in await verification.Images.ToListAsync())
            Assert.Equal(new byte[] { 0xff, 0xd8, 0xff }, await File.ReadAllBytesAsync(
                Path.Combine(_storageRoot, image.StoredPath.Replace('/', Path.DirectorySeparatorChar))));
    }

    // 取得目前 Integration Test 使用的 SQL Connection String，提供給 UploadApiHost 使用。
    private string CreateConnectionString()
    {
        using var db = CreateContext();
        return db.Database.GetConnectionString()!;
    }
    private static Image NewImage(Guid batchId) => new(batchId, "旅行照片.jpg", "original/550e8400e29b41d4a716446655440000", 1024, "image/jpeg", CreatedAt);

    [Fact]
    public async Task UploadService_QueueObservesCommittedRowsFromAnotherConnection()
    {
        // 測試目標：確認 Job 進入 Queue 前，Database 已完成 Commit。
        // 使用另一個 DbContext 查詢，確認 Batch、Image、ProcessingJob 都已真正寫入 SQL Server。
        await using var db = CreateContext();
        using var cancellation = new CancellationTokenSource();
        var dependencies = new UploadDependencies(async (job, token) =>
        {
            Assert.Equal(cancellation.Token, token);
            await using var read = CreateContext();
            Assert.True(job.Id > 0);
            Assert.True(job.ImageId > 0);
            Assert.True(await read.Batches.AnyAsync(x => x.Id == job.BatchId, token));
            Assert.True(await read.Images.AnyAsync(x => x.Id == job.ImageId && x.BatchId == job.BatchId, token));
            Assert.True(await read.ProcessingJobs.AnyAsync(x => x.Id == job.Id && x.ImageId == job.ImageId, token));
        });
        var service = new UploadService(dependencies, dependencies, new UploadPersistence(db), dependencies, NullLogger<UploadService>.Instance);
        var result = await service.UploadAsync(new UploadRequest([new UploadFile(), new UploadFile()], WorkflowType.Full), cancellation.Token);
        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, dependencies.Enqueued);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task UploadPersistence_SecondSaveFailureRollsBackAndDetachesOnlyUploadEntities()
    {
        // 測試目標：確認第二次 SaveChanges 失敗時，第一次 SaveChanges 的資料也能一起 Rollback。
        // 同時確認 Cleanup 只移除本次 Upload 的 Entity，不影響 DbContext 中其他資料。
        await using var db = CreateContext();
        var persistence = new UploadPersistence(db);
        var existing = new Batch(Guid.NewGuid(), 0, CreatedAt);
        db.Batches.Add(existing);
        await db.SaveChangesAsync();
        var batch = new Batch(Guid.NewGuid(), 1, CreatedAt);
        var image = NewImage(batch.Id);
        await using (var transaction = await persistence.BeginTransactionAsync(CancellationToken.None))
        {
            persistence.AddBatch(batch);
            persistence.AddImages([image]);
            await persistence.SaveChangesAsync(CancellationToken.None);
            // 確認第一次 SaveChanges 後，SQL Server 已產生並回填 Image.Id。
            Assert.True(image.Id > 0);
            persistence.AddProcessingJobs([new(image.Id, batch.Id, WorkflowType.Full, CreatedAt),
                new(image.Id, batch.Id, WorkflowType.Full, CreatedAt)]);
            await Assert.ThrowsAsync<DbUpdateException>(() => persistence.SaveChangesAsync(CancellationToken.None));
            await transaction.RollbackAsync(CancellationToken.None);
        }
        Assert.Single(db.ChangeTracker.Entries());
        Assert.Same(existing, db.ChangeTracker.Entries().Single().Entity);
        await db.SaveChangesAsync();
        await using var read = CreateContext();
        Assert.True(await read.Batches.AnyAsync(x => x.Id == existing.Id));
        Assert.False(await read.Batches.AnyAsync(x => x.Id == batch.Id));
        Assert.Empty(await read.Images.ToListAsync());
        Assert.Empty(await read.ProcessingJobs.ToListAsync());
    }

    [Fact]
    public async Task UploadPersistence_DisposeWithoutCommitRollsBackBothSaves()
    {
        // 測試目標：確認兩次 SaveChanges 後若未 Commit，Dispose Transaction 不會留下資料。
        await using var db = CreateContext();
        var persistence = new UploadPersistence(db);
        await using (var transaction = await persistence.BeginTransactionAsync(CancellationToken.None))
        {
            var batch = new Batch(Guid.NewGuid(), 1, CreatedAt);
            var image = NewImage(batch.Id);
            persistence.AddBatch(batch);
            persistence.AddImages([image]);
            await persistence.SaveChangesAsync(CancellationToken.None);
            persistence.AddProcessingJobs([new(image.Id, batch.Id, WorkflowType.Full, CreatedAt)]);
            await persistence.SaveChangesAsync(CancellationToken.None);
        }
        Assert.Empty(db.ChangeTracker.Entries());
        await using var read = CreateContext();
        Assert.Empty(await read.Batches.ToListAsync());
        Assert.Empty(await read.Images.ToListAsync());
        Assert.Empty(await read.ProcessingJobs.ToListAsync());
    }
    private sealed class UploadFile : IUploadFile
    {
        public string FileName => "照片.jpg";
        public string MimeType => "image/jpeg";
        public long Length => 3;
        public Stream OpenReadStream() => throw new NotSupportedException();
    }
    // Storage 與 Queue 使用替身；本組測試聚焦真正的 Database Transaction 行為。
    private sealed class UploadDependencies(Func<ProcessingJob, CancellationToken, Task> enqueue)
        : IFileValidationService, IFileStorageService, IProcessingQueue
    {
        public int Enqueued { get; private set; }
        public Task<FileValidationResult> ValidateAsync(IUploadFile file, CancellationToken token)
            => Task.FromResult(new FileValidationResult(true, null, null));
        public Task<string> SaveAsync(IUploadFile file, CancellationToken token)
            => Task.FromResult($"original/{Guid.NewGuid():N}");
        public Task DeleteAsync(string path, CancellationToken token) => Task.CompletedTask;
        public async Task EnqueueAsync(ProcessingJob job, CancellationToken token)
        {
            await enqueue(job, token);
            Enqueued++;
        }
        public ValueTask<ProcessingJob> DequeueAsync(CancellationToken token) => throw new NotSupportedException();
    }

    // 使用真實 SQL Server 驗證 Commit 前失敗會 Rollback，並只補償本次 Upload 的新檔案；既有 DB 資料與檔案不得受影響。
    // Rollback 後也必須 Detach 新增 Entity，避免之後 SaveChanges 又重新寫入。
    [Theory]
    [InlineData("save1", UploadFailureStage.DatabaseSave)]
    [InlineData("save2", UploadFailureStage.DatabaseSave)]
    [InlineData("commit", UploadFailureStage.DatabaseCommit)]
    public async Task UploadFailure_RealSqlRollbackCompensatesOnlyNewFiles(string failureAt, UploadFailureStage expectedStage)
    {
        // 真實 SQL constraint failure（或 Commit 前注入失敗）必須撤銷兩階段寫入，且不影響既有資料 / 檔案。
        await using var db = CreateContext();
        var existing = new Batch(Guid.NewGuid(), 0, CreatedAt);
        db.Batches.Add(existing);
        await db.SaveChangesAsync();
        var storage = new LocalFileStorageService(_storageRoot);
        using var oldFile = new StoredUploadFile();
        var oldPath = await storage.SaveAsync(oldFile, default);
        using var first = new StoredUploadFile();
        using var second = new StoredUploadFile();
        var queue = new UploadDependencies((_, _) => Task.CompletedTask);
        var persistence = new FaultingPersistence(new UploadPersistence(db), failureAt);
        var service = new UploadService(new FileValidationService(1024), storage, persistence, queue,
            NullLogger<UploadService>.Instance);

        var error = await Assert.ThrowsAsync<UploadFailureException>(() => service.UploadAsync(
            new UploadRequest([first, second], WorkflowType.Full), default));

        Assert.Equal(expectedStage, error.Stage);
        Assert.Equal(UploadFailureCategory.Internal, error.Category);
        if (failureAt == "commit") Assert.IsType<IOException>(error.InnerException);
        else Assert.IsType<DbUpdateException>(error.InnerException);
        Assert.Equal(0, queue.Enqueued);
        Assert.Equal(1, persistence.Rollbacks);
        Assert.Same(existing, Assert.Single(db.ChangeTracker.Entries()).Entity);
        // 重用同一 DbContext Save 不可重新寫入已 rollback 的 Upload entities。
        await db.SaveChangesAsync();
        await using var read = CreateContext();
        Assert.Equal(existing.Id, (await read.Batches.SingleAsync()).Id);
        Assert.Empty(await read.Images.ToListAsync());
        Assert.Empty(await read.ProcessingJobs.ToListAsync());
        Assert.Equal(Path.Combine(_storageRoot, oldPath.Replace('/', Path.DirectorySeparatorChar)),
            Assert.Single(Directory.GetFiles(Path.Combine(_storageRoot, "original"))));
    }

    // 驗證 Commit 已真正完成後才進入 Queue。
    // 即使第一筆或部分 Enqueue 失敗，已 Commit 的 Batch / Image / Job與 Storage 檔案都必須保留，不得 Rollback 或 Compensation。
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task CommittedUpload_RealRowsAndFilesSurviveFirstOrPartialQueueFailure(int failOnEnqueue)
    {
        // 0 為成功路徑，1 / 2 為第一次或部分入列失敗；從新連線確認 Commit，使用真實 Local Storage。
        await using var db = CreateContext();
        var storage = new LocalFileStorageService(_storageRoot);
        var calls = 0;
        var primary = new IOException("Queue unavailable.");
        var queue = new UploadDependencies(async (job, token) =>
        {
            await using var read = CreateContext();
            Assert.True(await read.ProcessingJobs.AnyAsync(x => x.Id == job.Id, token));
            if (++calls == failOnEnqueue) throw primary;
        });
        var persistence = new FaultingPersistence(new UploadPersistence(db), null);
        var service = new UploadService(new FileValidationService(1024), storage, persistence, queue,
            NullLogger<UploadService>.Instance);
        using var first = new StoredUploadFile();
        using var second = new StoredUploadFile();
        var request = new UploadRequest([first, second], WorkflowType.Full);
        if (failOnEnqueue == 0)
            Assert.Equal(2, (await service.UploadAsync(request, default)).TotalCount);
        else
        {
            var error = await Assert.ThrowsAsync<UploadFailureException>(() => service.UploadAsync(request, default));
            Assert.Equal(UploadFailureStage.QueueEnqueue, error.Stage);
            Assert.Same(primary, error.InnerException);
        }
        Assert.Equal(0, persistence.Rollbacks);
        Assert.Empty(db.ChangeTracker.Entries());
        await using var verification = CreateContext();
        Assert.Equal(1, await verification.Batches.CountAsync());
        Assert.Equal(2, await verification.Images.CountAsync());
        Assert.Equal(2, await verification.ProcessingJobs.CountAsync());
        Assert.Equal(2, Directory.GetFiles(Path.Combine(_storageRoot, "original")).Length);
        foreach (var image in await verification.Images.ToListAsync())
            Assert.Equal(new byte[] { 0xff, 0xd8, 0xff }, await File.ReadAllBytesAsync(
                Path.Combine(_storageRoot, image.StoredPath.Replace('/', Path.DirectorySeparatorChar))));
        Assert.Equal(failOnEnqueue == 0 ? 2 : failOnEnqueue - 1, queue.Enqueued);
    }

    // Integration Test 用的最小 JPEG 上傳檔案。
    // 每次開啟 Stream 前重設 Position，確保可重複讀取。
    private sealed class StoredUploadFile : IUploadFile, IDisposable
    {
        private readonly MemoryStream content = new([0xff, 0xd8, 0xff]);
        public string FileName => "photo.jpg";
        public string MimeType => "image/jpeg";
        public long Length => content.Length;
        public Stream OpenReadStream() { content.Position = 0; return content; }
        public void Dispose() => content.Dispose();
    }

    // 僅測試用 fault injection；SQL Transaction / Save / Rollback / Detach 仍委派正式實作。
    private sealed class FaultingPersistence(IUploadPersistence inner, string? failAt) : IUploadPersistence
    {
        public int Rollbacks { get; private set; }
        public async Task<IUploadTransaction> BeginTransactionAsync(CancellationToken token)
            => new FaultingTransaction(await inner.BeginTransactionAsync(token), this, failAt);
        public void AddBatch(Batch batch) => inner.AddBatch(batch);
        public void AddImages(IReadOnlyList<Image> images)
            => inner.AddImages(failAt == "save1" ? [NewImage(Guid.NewGuid())] : images);
        public void AddProcessingJobs(IReadOnlyList<ProcessingJob> jobs)
        {
            inner.AddProcessingJobs(jobs);
            if (failAt == "save2")
                inner.AddProcessingJobs([new ProcessingJob(jobs[0].ImageId, jobs[0].BatchId, jobs[0].Workflow, CreatedAt)]);
        }
        public Task SaveChangesAsync(CancellationToken token) => inner.SaveChangesAsync(token);
        private sealed class FaultingTransaction(IUploadTransaction inner, FaultingPersistence owner, string? failAt)
            : IUploadTransaction
        {
            public Task CommitAsync(CancellationToken token)
                => failAt == "commit" ? throw new IOException("Commit not submitted.") : inner.CommitAsync(token);
            public Task RollbackAsync(CancellationToken token)
            { owner.Rollbacks++; return inner.RollbackAsync(token); }
            public ValueTask DisposeAsync() => inner.DisposeAsync();
        }
    }

    private async Task<(Guid BatchId, long ImageId)> SaveImageAsync()
    {
        await using var db = CreateContext();
        var batch = new Batch(Guid.NewGuid(), 1, CreatedAt);
        db.Batches.Add(batch);
        await db.SaveChangesAsync();
        var image = NewImage(batch.Id);
        db.Images.Add(image);
        await db.SaveChangesAsync();
        return (batch.Id, image.Id);
    }
    [Fact]
    public async Task Migration_CreatesApprovedTablesAndConstraints()
    {
        // 測試目標：確認 Migration 在 SQL Server 實際建立核准的資料表、欄位、索引與約束。
        await using var db = CreateContext();
        Assert.Single(await db.Database.GetAppliedMigrationsAsync());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges());
        var tables = await db.Database.SqlQueryRaw<string>("SELECT name AS [Value] FROM sys.tables WHERE name <> '__EFMigrationsHistory' ORDER BY name").ToListAsync();
        Assert.Equal(new[] { "batches", "images", "processing_jobs" }, tables);
        var actions = await db.Database.SqlQueryRaw<int>("SELECT CONVERT(int, delete_referential_action) AS [Value] FROM sys.foreign_keys").ToListAsync();
        Assert.Equal(3, actions.Count);
        // SQL Server 系統目錄以 0 表示 NoAction。
        Assert.All(actions, action => Assert.Equal(0, action));
        Assert.Equal(38, await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id IN (OBJECT_ID('batches'), OBJECT_ID('images'), OBJECT_ID('processing_jobs'))").SingleAsync());
        Assert.Equal(9, await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.indexes WHERE is_primary_key = 0 AND index_id > 0 AND object_id IN (OBJECT_ID('batches'), OBJECT_ID('images'), OBJECT_ID('processing_jobs'))").SingleAsync());
        Assert.Equal(2, await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.identity_columns WHERE object_id IN (OBJECT_ID('batches'), OBJECT_ID('images'), OBJECT_ID('processing_jobs'))").SingleAsync());
        Assert.Equal(5, await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.default_constraints WHERE parent_object_id IN (OBJECT_ID('batches'), OBJECT_ID('images'), OBJECT_ID('processing_jobs'))").SingleAsync());
    }
    [Fact]
    public async Task Batch_RoundTripsSuppliedDataAndInitialState()
    {
        // 測試目標：確認 Batch 的初始狀態與時間能經由 SQL Server 正確寫入及回讀。
        var id = Guid.NewGuid();
        await using (var db = CreateContext())
        {
            db.Batches.Add(new Batch(id, 3, CreatedAt));
            await db.SaveChangesAsync();
        }
        await using var read = CreateContext();
        var batch = await read.Batches.SingleAsync(x => x.Id == id);
        Assert.Equal(3, batch.TotalCount);
        Assert.Equal(0, batch.ProcessedCount);
        Assert.Equal(0, batch.SuccessCount);
        Assert.Equal(0, batch.FailedCount);
        Assert.Equal("Pending", batch.Status);
        Assert.Equal(CreatedAt, batch.CreatedAt);
        Assert.Null(batch.CompletedAt);
    }
    [Fact]
    public async Task Image_RoundTripsIdentityUnicodeAndNullableMetadata()
    {
        // 測試目標：確認 SQL Server 產生 Image Identity，並正確保存 Batch 關聯、Unicode 與 nullable 欄位。
        var (batchId, imageId) = await SaveImageAsync();
        Assert.True(imageId > 0);
        await using var db = CreateContext();
        var image = await db.Images.SingleAsync(x => x.Id == imageId);
        Assert.Equal(batchId, image.BatchId);
        Assert.True(await db.Batches.AnyAsync(x => x.Id == image.BatchId));
        Assert.Equal("旅行照片.jpg", image.OriginalFileName);
        Assert.Equal(1024, image.FileSize);
        Assert.Equal("Pending", image.Status);
        Assert.Equal(CreatedAt, image.CreatedAt);
        Assert.Equal(CreatedAt, image.UpdatedAt);
        Assert.Null(image.NewFileName);
        Assert.Null(image.SHA256);
        Assert.Null(image.TakenAt);
        Assert.Null(image.CameraModel);
        Assert.Null(image.ISO);
        Assert.Null(image.ShutterSpeed);
        Assert.Null(image.Aperture);
        Assert.Null(image.Latitude);
        Assert.Null(image.Longitude);
        Assert.Null(image.LocationName);
    }
    [Fact]
    public async Task Job_RoundTripsRelationshipsAndStringEnums()
    {
        // 測試目標：確認 Job 關聯與初始值正確保存，且 SQL Server 中的 Enum 欄位儲存名稱而非數字。
        var (batchId, imageId) = await SaveImageAsync();
        await using (var db = CreateContext())
        {
            db.ProcessingJobs.Add(new ProcessingJob(imageId, batchId, WorkflowType.Full, CreatedAt));
            await db.SaveChangesAsync();
        }
        await using var read = CreateContext();
        var job = await read.ProcessingJobs.SingleAsync();
        Assert.True(job.Id > 0);
        Assert.Equal(imageId, job.ImageId);
        Assert.Equal(batchId, job.BatchId);
        Assert.Equal(WorkflowType.Full, job.Workflow);
        Assert.Equal(ProcessingJobStatus.Pending, job.Status);
        Assert.Equal(0, job.RetryCount);
        Assert.Equal(CreatedAt, job.CreatedAt);
        Assert.Null(job.StartedAt);
        Assert.Null(job.CompletedAt);
        Assert.Null(job.ErrorCode);
        Assert.Null(job.ErrorMessage);
        Assert.Equal("Full", await read.Database.SqlQueryRaw<string>("SELECT Workflow AS [Value] FROM processing_jobs").SingleAsync());
        Assert.Equal("Pending", await read.Database.SqlQueryRaw<string>("SELECT Status AS [Value] FROM processing_jobs").SingleAsync());
    }
    [Fact]
    public async Task Job_DuplicateImageAndWorkflowIsRejectedByDatabase()
    {
        // 測試目標：確認 SQL Server 唯一索引拒絕同一 Image 與 Workflow 的重複 Job。
        var (batchId, imageId) = await SaveImageAsync();
        await using var db = CreateContext();
        db.ProcessingJobs.Add(new ProcessingJob(imageId, batchId, WorkflowType.Full, CreatedAt));
        await db.SaveChangesAsync();
        db.ProcessingJobs.Add(new ProcessingJob(imageId, batchId, WorkflowType.Full, CreatedAt));
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains(Assert.IsType<SqlException>(error.InnerException).Number, new[] { 2601, 2627 });
        await using var read = CreateContext();
        Assert.Equal(1, await read.ProcessingJobs.CountAsync());
    }
    [Fact]
    public async Task Job_DifferentWorkflowsForSameImageAreAllowed()
    {
        // 測試目標：確認 SQL Server 唯一索引允許同一 Image 建立不同 Workflow 的 Job。
        var (batchId, imageId) = await SaveImageAsync();
        await using var db = CreateContext();
        db.ProcessingJobs.AddRange(new ProcessingJob(imageId, batchId, WorkflowType.Full, CreatedAt),
            new ProcessingJob(imageId, batchId, WorkflowType.Naming, CreatedAt));
        await db.SaveChangesAsync();
        await using var read = CreateContext();
        Assert.Equal(2, await read.ProcessingJobs.CountAsync(x => x.ImageId == imageId));
    }
    [Theory]
    [InlineData("ImageBatch")]
    [InlineData("JobImage")]
    [InlineData("JobBatch")]
    public async Task InvalidForeignKey_IsRejectedByDatabase(string relationship)
    {
        // 測試目標：確認三條關聯的無效外鍵都由 SQL Server 拒絕，而非只靠 Application 驗證。
        var (batchId, imageId) = await SaveImageAsync();
        await using var db = CreateContext();
        if (relationship == "ImageBatch")
            db.Images.Add(NewImage(Guid.NewGuid()));
        else
            db.ProcessingJobs.Add(new ProcessingJob(relationship == "JobImage" ? long.MaxValue : imageId,
                relationship == "JobBatch" ? Guid.NewGuid() : batchId, WorkflowType.Full, CreatedAt));
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(547, Assert.IsType<SqlException>(error.InnerException).Number);
    }
    [Fact]
    public async Task Transaction_RollbackRemovesBatchImageAndJob()
    {
        // 測試目標：確認尚未 Commit 時，已 SaveChanges 的 Batch、Image 與 Job 都能一起 Rollback。
        // 使用另一個 DbContext 查詢真實 SQL Server，確認沒有留下交易內的資料。
        await using (var db = CreateContext())
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            var batch = new Batch(Guid.NewGuid(), 1, CreatedAt);
            db.Batches.Add(batch);
            await db.SaveChangesAsync();
            var image = NewImage(batch.Id);
            db.Images.Add(image);
            await db.SaveChangesAsync();
            db.ProcessingJobs.Add(new ProcessingJob(image.Id, batch.Id, WorkflowType.Full, CreatedAt));
            await db.SaveChangesAsync();
            await transaction.RollbackAsync();
        }
        await using var read = CreateContext();
        Assert.Empty(await read.Batches.ToListAsync());
        Assert.Empty(await read.Images.ToListAsync());
        Assert.Empty(await read.ProcessingJobs.ToListAsync());
    }
    [Fact]
    public async Task NullableMetadataAndLongUnicodeError_RoundTripWithoutNewDomainBehavior()
    {
        // 測試目標：確認 SQL Server 正確保存選填欄位，且 nvarchar(max) 不截斷長 Unicode 錯誤訊息。
        var (batchId, imageId) = await SaveImageAsync();
        var errorMessage = new string('錯', 5000);
        await using (var db = CreateContext())
        {
            var image = await db.Images.SingleAsync(x => x.Id == imageId);
            var entry = db.Entry(image);
            entry.Property(x => x.NewFileName).CurrentValue = "新照片.jpg";
            entry.Property(x => x.SHA256).CurrentValue = new string('a', 64);
            entry.Property(x => x.TakenAt).CurrentValue = CreatedAt.AddTicks(1234567);
            entry.Property(x => x.CameraModel).CurrentValue = "相機型號";
            entry.Property(x => x.ISO).CurrentValue = 100;
            entry.Property(x => x.ShutterSpeed).CurrentValue = "1/125";
            entry.Property(x => x.Aperture).CurrentValue = "f/2.8";
            entry.Property(x => x.Latitude).CurrentValue = 25.1234567m;
            entry.Property(x => x.Longitude).CurrentValue = 121.1234567m;
            entry.Property(x => x.LocationName).CurrentValue = "臺北";
            var job = new ProcessingJob(imageId, batchId, WorkflowType.Full, CreatedAt);
            db.ProcessingJobs.Add(job);
            db.Entry(job).Property(x => x.ErrorMessage).CurrentValue = errorMessage;
            await db.SaveChangesAsync();
        }
        await using var read = CreateContext();
        var saved = await read.Images.SingleAsync();
        Assert.Equal("新照片.jpg", saved.NewFileName);
        Assert.Equal(new string('a', 64), saved.SHA256);
        Assert.Equal(CreatedAt.AddTicks(1234567), saved.TakenAt);
        Assert.Equal("相機型號", saved.CameraModel);
        Assert.Equal(100, saved.ISO);
        Assert.Equal("1/125", saved.ShutterSpeed);
        Assert.Equal("f/2.8", saved.Aperture);
        Assert.Equal(25.1234567m, saved.Latitude);
        Assert.Equal(121.1234567m, saved.Longitude);
        Assert.Equal("臺北", saved.LocationName);
        Assert.Equal(errorMessage, (await read.ProcessingJobs.SingleAsync()).ErrorMessage);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NoAction_RejectsPrincipalDeleteWithoutDeletingDependents(bool deleteImage)
    {
        // 測試目標：確認 SQL Server 的 NoAction 外鍵拒絕刪除仍被參照的 Batch 或 Image，並保留關聯資料。
        var (batchId, imageId) = await SaveImageAsync();
        await using (var db = CreateContext())
        {
            db.ProcessingJobs.Add(new ProcessingJob(imageId, batchId, WorkflowType.Full, CreatedAt));
            await db.SaveChangesAsync();
        }
        await using (var db = CreateContext())
        {
            if (deleteImage)
                db.Images.Remove(await db.Images.SingleAsync());
            else
                db.Batches.Remove(await db.Batches.SingleAsync());
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Equal(547, Assert.IsType<SqlException>(error.InnerException).Number);
        }
        await using var read = CreateContext();
        Assert.Equal(1, await read.Batches.CountAsync());
        Assert.Equal(1, await read.Images.CountAsync());
        Assert.Equal(1, await read.ProcessingJobs.CountAsync());
    }
}
