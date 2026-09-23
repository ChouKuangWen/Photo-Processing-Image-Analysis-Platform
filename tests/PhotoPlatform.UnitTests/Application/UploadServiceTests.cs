using PhotoPlatform.Application.DTOs;
using PhotoPlatform.Application.Exceptions;
using PhotoPlatform.Application.Interfaces;
using PhotoPlatform.Application.Services;
using PhotoPlatform.Domain.Entities;
using PhotoPlatform.Domain.Enums;
using PhotoPlatform.UnitTests.TestDoubles;

namespace PhotoPlatform.UnitTests.Application;


// UploadService 的 Unit Tests。
// 使用測試替身驗證 Upload 流程、錯誤處理與各元件呼叫順序，不連真實 SQL Server 或檔案系統。
public sealed class UploadServiceTests
{
    // 測試目標：確認成功 Upload 時資料關聯正確，且 Commit 一定發生在 Queue 之前。
    // 同時測試單檔與多檔 Upload。
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task Success_PreservesRelationshipsAndCommitsBeforeQueue(int count)
    {
        var rig = new Rig();
        var result = await rig.Service.UploadAsync(Request(count), rig.Token);
        Assert.Equal(count, result.TotalCount);
        Assert.Equal("Pending", result.Status);
        Assert.Equal(rig.Batch!.Id, result.BatchId);
        Assert.Equal(count, rig.Jobs.Count);
        Assert.All(rig.Jobs, job =>
        {
            Assert.True(job.Id > 0);
            Assert.Contains(rig.Images, image => image.Id == job.ImageId);
            Assert.Equal(result.BatchId, job.BatchId);
            Assert.Equal(WorkflowType.Full, job.Workflow);
            Assert.Equal(ProcessingJobStatus.Pending, job.Status);
        });
        // 全部驗證都在第一個 Storage 前；兩次 Save 與 Commit 完成後，才可出現任何 Queue 呼叫。
        Assert.Equal(Enumerable.Repeat("validate", count).Concat(Enumerable.Repeat("store", count))
            .Concat(new[] { "begin", "batch", "images", "save1", "jobs", "save2", "commit" })
            .Concat(Enumerable.Repeat("queue", count)).Append("dispose"), rig.Events);
    }

    // 測試目標：確認 Validation 失敗後，流程會立即停止，不會再進行存檔、資料庫或 Queue 操作。
    // 同時確認錯誤代碼與訊息能正確往上傳。
    [Theory]
    [InlineData("empty", "INVALID_FILE")]
    [InlineData("workflow", "INVALID_WORKFLOW")]
    [InlineData("file", "UNSUPPORTED_FORMAT")]
    public async Task ValidationFailure_HasNoStorageOrDatabaseSideEffects(string kind, string code)
    {
        var rig = new Rig { InvalidFile = kind == "file" };
        var request = kind == "empty" ? Request(0) : Request(2);
        if (kind == "workflow") request = request with { Workflow = (WorkflowType)999 };
        var error = await Assert.ThrowsAsync<UploadValidationException>(() => rig.Service.UploadAsync(request, rig.Token));
        Assert.Equal(code, error.ErrorCode);
        Assert.Equal(error.ErrorMessage, error.Message);
        Assert.DoesNotContain("store", rig.Events);
        Assert.DoesNotContain("begin", rig.Events);
        Assert.DoesNotContain("queue", rig.Events);
    }

    // 測試目標：確認 Commit 前任何階段失敗時，都會清理已存檔案。
    // Transaction 已建立時也必須嘗試 Rollback，且不能進入 Queue。
    [Theory]
    [InlineData("store", false)]
    [InlineData("begin", false)]
    [InlineData("save1", true)]
    [InlineData("save2", true)]
    [InlineData("commit", true)]
    public async Task PreCommitFailure_CleansStoredFilesAndPreservesOriginalException(string stage, bool rollback)
    {
        // 第二次 Storage 才失敗，才能驗證前一個成功檔案需要清理。
        var rig = new Rig { FailAt = stage };
        var error = await Assert.ThrowsAsync<IOException>(() => rig.Service.UploadAsync(Request(2), rig.Token));
        Assert.Same(rig.Primary, error);
        Assert.Equal(rollback, rig.Events.Contains("rollback"));
        Assert.Equal(stage == "store" ? 1 : 2, rig.Deleted.Count);
        Assert.DoesNotContain("queue", rig.Events);
    }

    // 測試目標：確認 Cleanup 本身失敗時，不會蓋掉最初造成 Upload 失敗的 Exception。
    // 即使其中一個清理動作失敗，也要繼續嘗試其他 Cleanup。
    [Fact]
    public async Task CleanupFailures_DoNotMaskPrimaryOrPreventRemainingDeletes()
    {
        var rig = new Rig { FailAt = "save2", CleanupFails = true };
        var error = await Assert.ThrowsAsync<IOException>(() => rig.Service.UploadAsync(Request(2), rig.Token));
        Assert.Same(rig.Primary, error);
        Assert.Equal(2, rig.Deleted.Count);
        Assert.Contains("dispose", rig.Events);
    }

    // 測試目標：確認 Commit 後 Queue 失敗時，不會再 Rollback Database 或刪除來源檔案。
    // 即使部分 Job 已成功入列，也必須保留已 Commit 的資料。
    [Fact]
    public async Task QueueFailure_AfterPartialEnqueuePreservesCommittedDataAndFiles()
    {
        var rig = new Rig { FailAt = "queue" };
        var error = await Assert.ThrowsAsync<IOException>(() => rig.Service.UploadAsync(Request(3), rig.Token));
        Assert.Same(rig.Primary, error);
        Assert.Equal(2, rig.Events.Count(x => x == "queue"));
        Assert.Contains("commit", rig.Events);
        Assert.DoesNotContain("rollback", rig.Events);
        Assert.Empty(rig.Deleted);
    }

    // 測試目標：確認 Cancellation 可以在 Upload 各階段正常往上傳遞。
    // Commit 前取消仍需 Cleanup；Commit 後取消則不能刪除已提交資料。
    [Theory]
    [InlineData("before")]
    [InlineData("validate")]
    [InlineData("store")]
    [InlineData("begin")]
    [InlineData("save1")]
    [InlineData("save2")]
    [InlineData("commit")]
    [InlineData("queue")]
    public async Task Cancellation_PropagatesAndUsesIndependentCleanupToken(string stage)
    {
        var rig = new Rig { CancelAt = stage };
        if (stage == "before") rig.Cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rig.Service.UploadAsync(Request(2), rig.Token));
        // Queue 階段已經提交，取消不能刪除 Worker 將要使用的來源；之前則清理所有已成功儲存檔案。
        Assert.Equal(stage == "queue" ? 0 : rig.StoredCount, rig.Deleted.Count);
        if (stage == "queue") Assert.DoesNotContain("rollback", rig.Events);
        if (stage == "before") Assert.Empty(rig.Events);
    }

    // 建立測試用 UploadRequest。
    // 如果 UploadService 自己直接讀取 Stream，就讓測試失敗，確保 Stream 由 Validation / Storage 負責。
    private static UploadRequest Request(int count) => new(Enumerable.Range(0, count)
        .Select(i => (IUploadFile)new FakeUploadFile($"照片{i}.jpg", "image/jpeg", 3,
            () => throw new InvalidOperationException("Service must delegate stream access."))).ToArray(), WorkflowType.Full);

    // 測試替身，同時模擬 Validation、Storage、Persistence、Transaction 與 Queue。
    // 用來記錄呼叫順序、模擬失敗與 Cancellation，不會真的連 SQL Server 或檔案系統。
    private sealed class Rig : IFileValidationService, IFileStorageService, IUploadPersistence, IUploadTransaction, IProcessingQueue
    {
        // 記錄各階段實際執行順序。
        public readonly List<string> Events = [];

        // 記錄 Cleanup 時嘗試刪除的檔案。
        public readonly List<string> Deleted = [];
        public readonly CancellationTokenSource Cancellation = new();
        public CancellationToken Token => Cancellation.Token;

        // 測試中共用的主要 Exception，用來確認原始錯誤不會被 Cleanup Exception 蓋掉。
        public readonly IOException Primary = new("Simulated dependency failure.");

        // 指定哪個階段要模擬 Exception。
        public string? FailAt;

        // 指定哪個階段要模擬 Cancellation。
        public string? CancelAt;
        public bool InvalidFile;

        // 控制 Rollback / Delete / Dispose 是否故意失敗。
        public bool CleanupFails;
        public int StoredCount;

        // 保存 UploadService 建立的 Entity，供測試檢查資料關聯。
        public Batch? Batch;
        public IReadOnlyList<Image> Images = [];
        public IReadOnlyList<ProcessingJob> Jobs = [];
        private int saves;

        // Rig 自己同時實作 UploadService 所需的四個依賴。
        public UploadService Service => new(this, this, this, this);

        // 記錄目前執行階段，並依測試設定模擬 Cancellation 或 Exception。
        // Storage / Queue 在第二次呼叫才觸發，方便測試「部分成功後失敗」的情境。
        private void Step(string stage, CancellationToken token)
        {
            Assert.Equal(Token, token);
            Events.Add(stage);
            var eligible = stage is not ("store" or "queue") || Events.Count(x => x == stage) == 2;
            if (CancelAt == stage && eligible) { Cancellation.Cancel(); token.ThrowIfCancellationRequested(); }
            if (FailAt == stage && eligible) throw Primary;
        }
        public Task<FileValidationResult> ValidateAsync(IUploadFile file, CancellationToken token)
        {
            Step("validate", token);
            return Task.FromResult(InvalidFile ? new FileValidationResult(false, "UNSUPPORTED_FORMAT", "Unsupported sample.") : new(true, null, null));
        }
        public Task<string> SaveAsync(IUploadFile file, CancellationToken token)
        {
            Step("store", token);
            return Task.FromResult($"original/{++StoredCount}");
        }
        public Task DeleteAsync(string path, CancellationToken token)
        {
            // Cleanup 不使用原本 Request Token，避免 Request 已取消後連 Cleanup 也被取消。
            Assert.Equal(CancellationToken.None, token);
            Deleted.Add(path);
            if (CleanupFails) throw new IOException("Cleanup failed.");
            return Task.CompletedTask;
        }
        public Task<IUploadTransaction> BeginTransactionAsync(CancellationToken token)
        { Step("begin", token); return Task.FromResult<IUploadTransaction>(this); }
        public void AddBatch(Batch batch) { Events.Add("batch"); Batch = batch; }
        public void AddImages(IReadOnlyList<Image> images) { Events.Add("images"); Images = images; }
        public void AddProcessingJobs(IReadOnlyList<ProcessingJob> jobs) { Events.Add("jobs"); Jobs = jobs; }
        public Task SaveChangesAsync(CancellationToken token)
        {
            Step($"save{++saves}", token);
            // Unit Test 模擬 Database Identity 回填。
            // 真正的 SQL Server Identity 行為由 Integration Test 驗證。
            if (saves == 1)
                for (var i = 0; i < Images.Count; i++) typeof(Image).GetProperty(nameof(Image.Id))!.SetValue(Images[i], (long)i + 10);
            else
                for (var i = 0; i < Jobs.Count; i++) typeof(ProcessingJob).GetProperty(nameof(ProcessingJob.Id))!.SetValue(Jobs[i], (long)i + 20);
            return Task.CompletedTask;
        }
        public Task CommitAsync(CancellationToken token) { Step("commit", token); return Task.CompletedTask; }

        // Rollback cleanup 使用獨立 Token，不受原本 Request Cancellation 影響。
        public Task RollbackAsync(CancellationToken token)
        {
            Assert.Equal(CancellationToken.None, token);
            Events.Add("rollback");
            if (CleanupFails) throw new IOException("Rollback failed.");
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync()
        {
            Events.Add("dispose");
            if (CleanupFails) throw new IOException("Dispose failed.");
            return ValueTask.CompletedTask;
        }
        public Task EnqueueAsync(ProcessingJob job, CancellationToken token)
        { Step("queue", token); return Task.CompletedTask; }

       // UploadService 在這組 Unit Tests 不會主動 Dequeue，
        // 因此如果意外呼叫就直接讓測試失敗。
       public ValueTask<ProcessingJob> DequeueAsync(CancellationToken token) => throw new NotSupportedException();
    }
}
