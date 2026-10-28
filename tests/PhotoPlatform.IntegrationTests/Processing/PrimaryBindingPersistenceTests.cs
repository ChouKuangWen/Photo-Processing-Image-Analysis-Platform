using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PhotoPlatform.Application.DTOs;
using PhotoPlatform.Application.Interfaces;
using PhotoPlatform.Application.Services;
using PhotoPlatform.Domain.Entities;
using PhotoPlatform.Domain.Enums;
using PhotoPlatform.Infrastructure.Persistence;
using PhotoPlatform.IntegrationTests.Fixtures;

namespace PhotoPlatform.IntegrationTests.Processing;

// 沿用每個案例獨立的 GUID 資料庫；檔案與 Queue 用替身，SQL 和 Upload 交易使用正式實作。
public sealed class PrimaryBindingPersistenceTests : IAsyncLifetime
{
    private readonly ProcessingSchemaFixture fixture = new();
    public async Task InitializeAsync()
    {
        await fixture.InitializeAsync();
        await using var db = fixture.CreateContext();
        await db.Database.MigrateAsync();
    }
    public Task DisposeAsync() => fixture.DisposeAsync();

    // 測試目的：確認四種 Workflow 的 Binding 都由 Upload 原始 Job 產生。
    // 測試情境：經正式 UploadService 上傳兩張圖片，再用新的 Context 核對。
    // 預期結果：綁定與 Job 完全對應，Batch 可准入，查詢不留下追蹤或寫入。
    [Theory]
    [InlineData(WorkflowType.Naming)]
    [InlineData(WorkflowType.Analysis)]
    [InlineData(WorkflowType.DuplicateDetection)]
    [InlineData(WorkflowType.Full)]
    public async Task Upload_CreatesBindingsForEveryWorkflow(WorkflowType workflow)
    {
        var batchId = await UploadAsync(workflow, 2);
        await using var db = fixture.CreateContext();
        var jobs = await db.ProcessingJobs.AsNoTracking().ToListAsync();
        var before = await SnapshotAsync(db);
        var result = await new ProcessingBindingPersistence(db).AssessBatchAsync(batchId, default);
        Assert.True(result.Ready);
        Assert.Equal(0, result.Count);
        var bindings = await db.ImageProcessingBindings.AsNoTracking().ToListAsync();
        Assert.Equal(2, bindings.Count);
        Assert.All(bindings, binding => Assert.Contains(jobs, job => job.Id == binding.PrimaryJobId &&
            job.ImageId == binding.ImageId && job.BatchId == binding.BatchId && job.Workflow == binding.PrimaryWorkflow &&
            binding.PrimaryWorkflow == workflow && binding.PrimaryStatus == ProcessingJobStatus.Pending));
        Assert.Equal(before, await SnapshotAsync(db));
        Assert.Empty(db.ChangeTracker.Entries());
    }

    // 測試目的：確認歷史缺綁定不會因只有一個 Job 就自動選主。
    // 測試情境：移除測試資料的 Binding，另外保留一個完整 Batch。
    // 預期結果：只有缺資料的 Batch 被阻擋，反覆查詢也不補建任何資料。
    [Fact]
    public async Task MissingBinding_BlocksOnlyAffectedBatchWithoutRepair()
    {
        var blocked = await UploadAsync();
        var ready = await UploadAsync();
        await using var db = fixture.CreateContext();
        await db.ImageProcessingBindings.Where(value => value.BatchId == blocked).ExecuteDeleteAsync();
        var before = await SnapshotAsync(db);
        var service = new ProcessingBindingPersistence(db);
        Assert.Contains((await service.AssessBatchAsync(blocked, default)).Reasons, value => value.ReasonCode == "BINDING_MISSING");
        Assert.True((await service.AssessBatchAsync(ready, default)).Ready);
        Assert.False((await service.AssessBatchAsync(Guid.NewGuid(), default)).Exists);
        Assert.Equal(before, await SnapshotAsync(db));
    }

    // 測試目的：確認錯誤關聯、狀態或計數不會被當成可執行。
    // 測試情境：直接 SQL 改壞其中一項；外鍵都仍指向存在的資料。
    // 預期結果：回報安全原因，不修復、不更換 Primary，也不修改原本持久狀態。
    [Theory]
    [InlineData("workflow", "RELATIONSHIP_MISMATCH")]
    [InlineData("batch", "RELATIONSHIP_MISMATCH")]
    [InlineData("status", "AGGREGATE_INCONSISTENT")]
    [InlineData("counts", "AGGREGATE_INCONSISTENT")]
    [InlineData("total", "COLLECTION_UNCONFIRMED")]
    public async Task InconsistentData_IsRejected(string fault, string expected)
    {
        var batchId = await UploadAsync();
        var other = await UploadAsync();
        await using var db = fixture.CreateContext();
        switch (fault)
        {
            case "workflow":
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE image_processing_bindings SET PrimaryWorkflow='Naming' WHERE BatchId={batchId}");
                break;
            case "batch":
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE image_processing_bindings SET BatchId={other} WHERE BatchId={batchId}");
                break;
            case "status":
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE images SET Status='Completed' WHERE BatchId={batchId}");
                break;
            case "counts":
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE batches SET ProcessedCount=1 WHERE Id={batchId}");
                break;
            case "total":
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE batches SET TotalCount=2 WHERE Id={batchId}");
                break;
        }
        var before = await SnapshotAsync(db);
        var result = await new ProcessingBindingPersistence(db).AssessBatchAsync(batchId, default);
        Assert.False(result.Ready);
        Assert.Contains(result.Reasons, reason => reason.ReasonCode == expected);
        Assert.Equal(before, await SnapshotAsync(db));
    }

    // 測試目的：確認新增圖片後舊准入結果不能繼續代表目前集合。
    // 測試情境：先取得 ready，再替同一批次加入沒有 Binding 的圖片。
    // 預期結果：重新核對會阻擋；也能在呼叫端的 Serializable 交易內核對且不替它 Commit。
    [Fact]
    public async Task NewImage_InvalidatesPreviousAssessment()
    {
        var batchId = await UploadAsync();
        await using var db = fixture.CreateContext();
        var service = new ProcessingBindingPersistence(db);
        Assert.True((await service.AssessBatchAsync(batchId, default)).Ready);
        db.Images.Add(new Image(batchId, "new.jpg", "original/new", 3, "image/jpeg", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var result = await service.AssessBatchAsync(batchId, default);
        Assert.False(result.Ready);
        Assert.Contains(result.Reasons, reason => reason.ReasonCode == "BINDING_MISSING");
        Assert.Same(transaction, db.Database.CurrentTransaction);
    }

    // 測試目的：確認診斷只限制顯示筆數，不限制實際核對的圖片集合。
    // 測試情境：建立 101 張缺 Binding 的圖片，並先取消另一個查詢。
    // 預期結果：取消往上傳遞；正常查詢計入 101 個原因，只顯示 100 筆，絕不回 ready。
    [Fact]
    public async Task CompleteCollection_IsCheckedBeforeTruncationAndCancellationPropagates()
    {
        var batchId = await UploadAsync(WorkflowType.Full, 101);
        await using var db = fixture.CreateContext();
        await db.ImageProcessingBindings.ExecuteDeleteAsync();
        var service = new ProcessingBindingPersistence(db);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.AssessBatchAsync(batchId, new CancellationToken(true)));
        var result = await service.AssessBatchAsync(batchId, default);
        Assert.False(result.Ready);
        Assert.Equal(101, result.Count);
        Assert.Equal(100, result.Reasons.Count);
        Assert.True(result.Truncated);
        Assert.Equal(result.Reasons.OrderBy(value => value.ImageId), result.Reasons);
    }

    // 測試目的：確認新增次要工作和重試不會換掉 Upload 的主要綁定。
    // 測試情境：新增 Naming Job 並對原 Job 模擬失敗重試，再同時嘗試插入兩個替代 Binding。
    // 預期結果：兩個替代寫入都被主鍵拒絕，原 PrimaryJobId 和 Workflow 保留。
    [Fact]
    public async Task SecondaryRetryAndConcurrentDuplicates_DoNotReplaceBinding()
    {
        var batchId = await UploadAsync();
        await using var db = fixture.CreateContext();
        var original = await db.ImageProcessingBindings.AsNoTracking().SingleAsync();
        var job = await db.ProcessingJobs.SingleAsync();
        var now = DateTimeOffset.UtcNow;
        job.Start(now);
        job.Fail("TIMEOUT", null, now);
        job.PrepareManualRetry(ProcessingErrorClassification.Transient, 2);
        db.ProcessingJobs.Add(new ProcessingJob(job.ImageId, batchId, WorkflowType.Naming, now));
        await db.SaveChangesAsync();
        var secondary = await db.ProcessingJobs.SingleAsync(value => value.Workflow == WorkflowType.Naming);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task AttemptAsync(long jobId, WorkflowType workflow)
        {
            await using var contender = fixture.CreateContext();
            contender.ImageProcessingBindings.Add(new(original.ImageId, batchId, jobId, workflow, ProcessingJobStatus.Pending, now));
            await gate.Task;
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => contender.SaveChangesAsync());
            Assert.Contains(Assert.IsType<SqlException>(error.InnerException).Number, new[] { 2601, 2627 });
        }
        var first = AttemptAsync(job.Id, job.Workflow);
        var second = AttemptAsync(secondary.Id, secondary.Workflow);
        gate.SetResult();
        await Task.WhenAll(first, second);
        await using var read = fixture.CreateContext();
        var saved = await read.ImageProcessingBindings.SingleAsync();
        Assert.Equal(original.PrimaryJobId, saved.PrimaryJobId);
        Assert.Equal(original.PrimaryWorkflow, saved.PrimaryWorkflow);
        Assert.True((await new ProcessingBindingPersistence(read).AssessBatchAsync(batchId, default)).Ready);
    }

    // 測試目的：確認空 Batch 保持 Pending，且准入核對不能借用隔離不足的交易。
    // 測試情境：建立空 Batch，先正常查詢，再於 ReadCommitted 交易內查詢。
    // 預期結果：空 Batch 可通過來源集合檢查且不產生資料；較弱交易會明確拒絕。
    [Fact]
    public async Task EmptyBatchAndTransactionBoundary_AreExplicit()
    {
        await using var db = fixture.CreateContext();
        var batch = new Batch(Guid.NewGuid(), 0, DateTimeOffset.UtcNow);
        db.Batches.Add(batch);
        await db.SaveChangesAsync();
        var service = new ProcessingBindingPersistence(db);
        Assert.True((await service.AssessBatchAsync(batch.Id, default)).Ready);
        Assert.Empty(await db.ImageProcessingBindings.ToListAsync());
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AssessBatchAsync(batch.Id, default));
        Assert.Same(transaction, db.Database.CurrentTransaction);
    }

    // 使用正式 UploadService 和 SQL Persistence，避免手動造 Binding 掩蓋交接流程錯誤。
    private async Task<Guid> UploadAsync(WorkflowType workflow = WorkflowType.Full, int count = 1)
    {
        await using var db = fixture.CreateContext();
        var dependencies = new UploadDependencies();
        var service = new UploadService(dependencies, dependencies, new UploadPersistence(db), dependencies,
            NullLogger<UploadService>.Instance);
        return (await service.UploadAsync(new UploadRequest(
            Enumerable.Range(0, count).Select(_ => (IUploadFile)new TestFile()).ToArray(), workflow), default)).BatchId;
    }

    // 比較完整 JSON 資料內容，不只比筆數，避免查詢偷偷改狀態卻沒被發現。
    private static async Task<string[]> SnapshotAsync(PhotoPlatformDbContext db)
    {
        var result = new List<string>();
        foreach (var table in new[] { "batches", "images", "processing_jobs", "image_processing_bindings" })
        {
            var key = table == "image_processing_bindings" ? "ImageId" : "Id";
            var connection = db.Database.GetDbConnection();
            await db.Database.OpenConnectionAsync();
            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = $"SELECT * FROM [{table}] ORDER BY [{key}] FOR JSON PATH, INCLUDE_NULL_VALUES";
                await using var reader = await command.ExecuteReaderAsync();
                var json = "";
                while (await reader.ReadAsync()) json += reader.GetString(0);
                result.Add(json);
            }
            finally { await db.Database.CloseConnectionAsync(); }
        }
        return result.ToArray();
    }

    private sealed class TestFile : IUploadFile
    {
        public string FileName => "photo.jpg";
        public string MimeType => "image/jpeg";
        public long Length => 3;
        public Stream OpenReadStream() => new MemoryStream([0xff, 0xd8, 0xff]);
    }

    private sealed class UploadDependencies : IFileValidationService, IFileStorageService, IProcessingQueue
    {
        public Task<FileValidationResult> ValidateAsync(IUploadFile file, CancellationToken cancellationToken)
            => Task.FromResult(new FileValidationResult(true, null, null));
        public Task<string> SaveAsync(IUploadFile file, CancellationToken cancellationToken)
            => Task.FromResult($"original/{Guid.NewGuid():N}");
        public Task DeleteAsync(string path, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task EnqueueAsync(ProcessingJob job, CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask<ProcessingJob> DequeueAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
