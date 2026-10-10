using PhotoPlatform.Application.DTOs;
using PhotoPlatform.Application.Interfaces;
using PhotoPlatform.Domain.Entities;

namespace PhotoPlatform.IntegrationTests.TestDoubles;

// TASK-12 專用的 test-only Fault Injection / Observation 控制器。
// 每個 HTTP request 都有自己獨立的 state，用來：
// - 指定在哪個階段故意 failure / pause
// - 記錄 Save、Commit、Rollback、Delete、Enqueue 等實際行為
// - 驗證 Cancellation 與 cleanup boundary
//
// 正式 Storage / Persistence / Queue / UploadService 仍照常執行，
// 此類別只透過 Decorator 在外層加入測試用控制與觀察，不改 production behavior。
internal sealed class UploadFaultInjection
{
    // FailAt 指定要在哪個階段故意丟出錯誤；
    // PauseAt 則指定在哪個階段暫停流程，供 Cancellation / Concurrent Test 精確控制時機。
    public string? FailAt { get; init; }
    public string? PauseAt { get; init; }
    // 模擬 cleanup 本身再次失敗，用來驗證原始 exception 是否仍被保留。
    public bool DeleteFails { get; init; }
    public bool RollbackFails { get; init; }
    public bool DisposeFails { get; init; }
    // Primary 保存本次 fault 的主要 exception；
    // ObservedFailure 則記錄最終從正式 UploadService 拋出的 exception。
    public Exception Primary { get; set; } = new IOException("TASK12_SECRET SQL Detail Stack Trace C:\\private JWT Authorization Header");
    public Exception? ObservedFailure { get; private set; }
    // 記錄本 request 建立的 Batch、Storage 操作與實際 enqueue Jobs，
    // 供 Acceptance Test 驗證 ownership 與 side effects。
    public Guid? BatchId { get; private set; }
    public List<string> Saved { get; } = [];
    public List<string> Deleted { get; } = [];
    public List<ProcessingJob> Enqueued { get; } = [];
    public IFileStorageService? StorageInner { get; private set; }
    // 記錄 Transaction / Persistence / Queue 各階段實際執行次數。
    public int Begins { get; private set; }
    public int Saves { get; private set; }
    public int Commits { get; private set; }
    public int Rollbacks { get; private set; }
    public int EnqueueAttempts { get; private set; }
    // 驗證 Cancellation 是否真的由本 request token 觸發，
    // 以及 cleanup 是否使用不受原 request cancellation 影響的獨立 token。
    public bool CancellationObserved { get; private set; }
    public bool CleanupTokensIndependent { get; private set; } = true;
    public Func<ProcessingJob, CancellationToken, Task>? VerifyCommitted { get; set; }
    // deterministic synchronization signals：
    // Entered = 已到達指定暫停點
    // Release = 測試允許流程繼續
    // Completed = 正式 UploadService 呼叫已結束（HTTP pipeline 可能仍在收尾）
    //
    // 用來避免 Task.Delay / Thread.Sleep 這類不穩定 timing 測試。
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int storageAttempts;

    // 若目前 stage 等於 PauseAt，先通知測試「已進入此階段」，
    // 再等待 Release 訊號後繼續。
    // 用來精確控制 Cancellation / Concurrent Request 發生時機。
    private async Task PauseAsync(string stage, CancellationToken token)
    {
        if (PauseAt != stage) return;
        Entered.TrySetResult();
        await Release.Task.WaitAsync(token);
    }

    // 將正式服務包成 test-only Decorator。
    // inner 仍是真正 production implementation；Decorator 只負責 fault injection / observation。
    public IFileStorageService Storage(IFileStorageService inner)
    {
        StorageInner = inner;
        return new StorageDecorator(this, inner);
    }
    // 將正式服務包成 test-only Decorator。
    // inner 仍是真正 production implementation；Decorator 只負責 fault injection / observation。
    public IUploadPersistence Persistence(IUploadPersistence inner) => new PersistenceDecorator(this, inner);
    // 將正式服務包成 test-only Decorator。
    // inner 仍是真正 production implementation；Decorator 只負責 fault injection / observation。
    public IProcessingQueue Queue(IProcessingQueue inner) => new QueueDecorator(this, inner);
    // 將正式服務包成 test-only Decorator。
    // inner 仍是真正 production implementation；Decorator 只負責 fault injection / observation。
    public IUploadService Service(IUploadService inner) => new ServiceDecorator(this, inner);

    // 包在正式 UploadService 外層的測試觀察器。
    // 不改 Upload 邏輯，只記錄：
    // - 最終 exception
    // - 是否真的觀察到 Cancellation
    // - 正式 UploadService 呼叫是否已完成
    private sealed class ServiceDecorator(UploadFaultInjection owner, IUploadService inner) : IUploadService
    {
        public async Task<UploadResult> UploadAsync(UploadRequest request, CancellationToken token)
        {
            try { return await inner.UploadAsync(request, token); }
            // 記錄正式 UploadService 最後拋出的錯誤，供 Acceptance Test 驗證。
            catch (Exception exception)
            {
                owner.ObservedFailure = exception;
                owner.CancellationObserved = exception is OperationCanceledException canceled
                    && token.IsCancellationRequested && canceled.CancellationToken == token;
                throw;
            }
            // 不論成功、失敗或取消，都通知測試正式 UploadService 呼叫已結束，可驗證其清理結果。
            finally { owner.Completed.TrySetResult(); }
        }
    }

    // 包住正式 IFileStorageService。
    // 保留真實檔案 I/O，但可在指定次數故意失敗，
    // 同時記錄成功 Save 的 keys 與嘗試 Delete 的 keys。
    private sealed class StorageDecorator(UploadFaultInjection owner, IFileStorageService inner) : IFileStorageService
    {
        // 可讓第二次 Save 故意失敗，模擬部分檔案已存成功、後續存檔失敗的情境。
        public async Task<string> SaveAsync(IUploadFile file, CancellationToken token)
        {
            if (++owner.storageAttempts == 2 && owner.FailAt == "store2") throw owner.Primary;
            var path = await inner.SaveAsync(file, token);
            owner.Saved.Add(path);
            return path;
        }
        // 記錄 compensation delete，並驗證 cleanup 使用獨立 token。
        // 需要時可讓第一次 delete 再次失敗，測試 best-effort cleanup。
        public Task DeleteAsync(string path, CancellationToken token)
        {
            owner.CleanupTokensIndependent &= !token.IsCancellationRequested && token == CancellationToken.None;
            owner.Deleted.Add(path);
            if (owner.DeleteFails && owner.Deleted.Count == 1)
                throw new IOException("TASK12_SECRET compensation failure");
            return inner.DeleteAsync(path, token);
        }
    }

    // 包住正式 IUploadPersistence。
    // 仍使用真實 SQL Server / EF Core，
    // 但可在 Save1 / Save2 階段製造真實 constraint failure，
    // 並記錄 Begin / Save 次數與 BatchId。
    private sealed class PersistenceDecorator(UploadFaultInjection owner, IUploadPersistence inner) : IUploadPersistence
    {
        // 記錄 Transaction Begin，真正 Transaction 仍交由正式 persistence 建立。
        public async Task<IUploadTransaction> BeginTransactionAsync(CancellationToken token)
        {
            owner.Begins++;
            return new TransactionDecorator(owner, await inner.BeginTransactionAsync(token));
        }
        // 保存本次 request 的 BatchId，供後續 DB / Log / Status 驗證。
        public void AddBatch(Batch batch) { owner.BatchId = batch.Id; inner.AddBatch(batch); }
        public void AddImages(IReadOnlyList<Image> images)
        {
            // save1：故意建立錯誤 BatchId 的 Image，讓真實 SQL Server 觸發 FK violation。
            inner.AddImages(owner.FailAt == "save1"
                ? [new Image(Guid.NewGuid(), "photo.jpg", images[0].StoredPath, 3, "image/jpeg", DateTimeOffset.UtcNow)]
                : images);
        }
        public void AddProcessingJobs(IReadOnlyList<ProcessingJob> jobs)
        {
            inner.AddProcessingJobs(jobs);
            // save2：故意加入重複 ProcessingJob，讓真實 SQL constraint 觸發失敗。
            if (owner.FailAt == "save2")
                inner.AddProcessingJobs([new(jobs[0].ImageId, jobs[0].BatchId, jobs[0].Workflow, jobs[0].CreatedAt)]);
        }
        // 真正執行 SaveChanges；若 SQL Server 回傳錯誤，保存為本次 Primary exception。
        // Save 成功後也可在指定 stage 暫停，供 cancellation / concurrency 測試。
        public async Task SaveChangesAsync(CancellationToken token)
        {
            owner.Saves++;
            try { await inner.SaveChangesAsync(token); }
            catch (Exception exception) { owner.Primary = exception; throw; }
            await owner.PauseAsync($"save{owner.Saves}-after", token);
        }
    }

    // 包住正式 Transaction。
    // 觀察 Commit / Rollback / Dispose，並可模擬 Commit 或 cleanup failure。
    private sealed class TransactionDecorator(UploadFaultInjection owner, IUploadTransaction inner) : IUploadTransaction
    {
        // commit fault 只模擬「確定尚未成功 Commit」的失敗。
        // 不涵蓋 server 已 Commit 但 client 不知道結果的 unknown commit scenario。
        public async Task CommitAsync(CancellationToken token)
        {
            if (owner.FailAt == "commit") throw owner.Primary;
            await inner.CommitAsync(token);
            owner.Commits++;
            if (owner.PauseAt == "commit-after")
            {
                owner.Entered.TrySetResult();
                // Commit 已成功後，可暫停流程等待 cancellation，
                // 用來驗證「Commit 後取消不得 Rollback / compensation」。
                try { await owner.Release.Task.WaitAsync(token); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            }
        }
        // 真正執行 Rollback，並確認 cleanup token 不受原 request cancellation 影響。
        // 需要時可在 Rollback 完成後再模擬 cleanup failure。
        public async Task RollbackAsync(CancellationToken token)
        {
            owner.Rollbacks++;
            owner.CleanupTokensIndependent &= token == CancellationToken.None;
            await inner.RollbackAsync(token);
            if (owner.RollbackFails) throw new IOException("TASK12_SECRET rollback cleanup failure");
        }
        // 先正常 Dispose 正式 Transaction；需要時再模擬 Dispose cleanup failure。
        public async ValueTask DisposeAsync()
        {
            await inner.DisposeAsync();
            if (owner.DisposeFails) throw new IOException("TASK12_SECRET dispose cleanup failure");
        }
    }

    // 包住正式 Processing Queue。
    // 驗證 Job 必須在 DB Commit 後才能 enqueue，
    // 並可模擬指定第幾次 enqueue failure 或真正 Channel backpressure。
    private sealed class QueueDecorator(UploadFaultInjection owner, IProcessingQueue inner) : IProcessingQueue
    {
        public async Task EnqueueAsync(ProcessingJob job, CancellationToken token)
        {
            // 每次 enqueue 都記錄 attempt；正式 Queue 行為仍由 inner 執行。
            var attempt = ++owner.EnqueueAttempts;
            // Queue 不得早於成功 Commit；若發生代表 Upload boundary 錯誤。
            if (owner.Commits != 1) throw new InvalidOperationException("Queue entered before successful Commit.");
            // enqueue 前使用新的 DbContext 驗證 Job 已經可從 SQL Server 查到。
            if (owner.VerifyCommitted is not null) await owner.VerifyCommitted(job, token);
            // 可指定 enqueue1 / enqueue2 等位置故意失敗。
            if (owner.FailAt == $"enqueue{attempt}") throw owner.Primary;
            var pending = inner.EnqueueAsync(job, token);
            // Capacity=1 時，第二筆 enqueue 應真的被 bounded Channel backpressure 擋住。
            // 測試利用這個真實等待點觸發 cancellation，而不是靠固定 delay。
            if (owner.PauseAt == "enqueue-wait" && attempt == 2)
            {
                if (pending.IsCompleted) throw new InvalidOperationException("Expected bounded queue wait.");
                owner.Entered.TrySetResult();
            }
            await pending;
            // 只有 inner Queue 真正 enqueue 成功後，才記錄為已入列。
            owner.Enqueued.Add(job);
        }
        // Dequeue 不加額外測試行為，直接交給正式 Queue。
        public ValueTask<ProcessingJob> DequeueAsync(CancellationToken token) => inner.DequeueAsync(token);
    }
}
