using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PhotoPlatform.Application.Exceptions;
using PhotoPlatform.Domain.Entities;
using PhotoPlatform.Domain.Enums;
using PhotoPlatform.IntegrationTests.Fixtures;
using PhotoPlatform.IntegrationTests.TestDoubles;

namespace PhotoPlatform.IntegrationTests.Api;

// MOD-01 Backend Acceptance Test 共用設定。
// 每個測試使用獨立 fixture，InitializeAsync 建立 SQL / Storage / Host 等環境，
// DisposeAsync 負責清理，避免案例彼此污染。
public sealed class UploadAcceptanceTests : IAsyncLifetime
{
    private readonly UploadAcceptanceFixture fixture = new();
    private static TimeSpan Bound => TimeSpan.FromSeconds(10);
    public Task InitializeAsync() => fixture.InitializeAsync();
    public Task DisposeAsync() => fixture.DisposeAsync();

    private Task<HttpResponseMessage> PostAsync(string marker, HttpContent body, CancellationToken token = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/images/upload") { Content = body };
        request.Headers.Add("X-Task12-Request", marker);
        request.Headers.Authorization = new("Bearer", "TASK12_SECRET");
        return SendAsync(request, token);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        using (request) return await fixture.Host.Client.SendAsync(request, token);
    }

    // 成功 Upload 的完整 Backend Acceptance Test。
    // 從正式 HTTP Request 驗證整條成功流程：
    //
    // 1. HTTP：回傳 202，且 response contract 正確。
    // 2. Transaction：只 Commit，不 Rollback、不做 Storage compensation。
    // 3. Persistence：以新 DbContext 驗證 Batch / Image / ProcessingJob 與實際檔案。
    // 4. Queue：以 persisted Jobs 為預期值，比對 enqueue 與實際 Channel dequeue。
    // 5. Status：確認已 Commit 的 Batch 可由 Status API 正確查詢。
    // 6. Logging：確認成功事件存在、無 failure event，且不洩漏敏感資訊。
    //
    // [Theory] 使用不同檔案數量與 Workflow 驗證相同的 Upload contract。
    [Theory]
    [InlineData(1, "Naming")]
    [InlineData(3, "Full")]
    [InlineData(2, "Analysis")]
    [InlineData(1, "DuplicateDetection")]
    public async Task Success_CommitsExactGraphAndBytesBeforeRealQueueAndStatus(int count, string workflow)
    {
        // 建立本次 Request 專用的測試狀態，並啟動 Acceptance Test Host。
        var state = new UploadFaultInjection();
        fixture.Requests["success"] = state;
        await fixture.StartAsync();
        // 建立 multipart upload request，並透過正式 HTTP pipeline 執行 Upload。
        using var body = UploadApiTests.Multipart(count, workflow);
        using var response = await PostAsync("success", body);
        // 驗證 HTTP 202 與 success response contract。
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(new[] { "success", "data" }, json.RootElement.EnumerateObject().Select(x => x.Name));
        Assert.True(json.RootElement.GetProperty("success").GetBoolean());
        var data = json.RootElement.GetProperty("data");
        Assert.Equal(new[] { "batchId", "totalCount", "status" }, data.EnumerateObject().Select(x => x.Name));
        Assert.Equal(state.BatchId, data.GetProperty("batchId").GetGuid());
        Assert.Equal(count, data.GetProperty("totalCount").GetInt32());
        Assert.Equal("Pending", data.GetProperty("status").GetString());
        // 成功流程必須 Commit，且不得 Rollback 或執行 Storage compensation。
        Assert.Equal(1, state.Commits);
        Assert.Equal(0, state.Rollbacks);
        Assert.Empty(state.Deleted);
        // 使用新 DbContext 驗證真正已 Commit 的資料與 Storage 內容。
        await AssertCommittedAsync(state, count, Enum.Parse<WorkflowType>(workflow));
        // 以 persisted ProcessingJobs 作為獨立預期值，驗證實際 Queue enqueue / dequeue。
        await AssertPersistedQueueAsync([state], count);
        // 確認已 Commit 的 Batch 可由正式 Status API 查詢。
        await AssertStatusAsync(state.BatchId!.Value, count);
        // 驗證成功事件、無 failure event，並確認 Log 不洩漏敏感資訊。
        Assert.Single(fixture.Logs.Entries, x => x.EventId.Name == "TransactionCommitted");
        Assert.Single(fixture.Logs.Entries, x => x.EventId.Name == "UploadAccepted");
        Assert.Empty(Failures());
        AssertSafeLogs();
    }

    // Validation / Workflow failure 的 Backend Acceptance Test。
    // 使用不同非法輸入驗證 request 必須在產生系統副作用前被拒絕：
    //
    // 1. HTTP：回傳對應 400 error code。
    // 2. Persistence：不得建立 Batch / Image / ProcessingJob。
    // 3. Storage：不得留下檔案或執行 compensation。
    // 4. Transaction：Validation 階段不得開始 Transaction。
    // 5. Queue：不得發生 enqueue。
    // 6. Observability：依錯誤發生層級驗證 failure event、TraceId 與安全 logging。
    //
    // [Theory] 涵蓋 extension、signature、size、empty file/request、MIME 與 Workflow 驗證。
    [Theory]
    [InlineData("extension", "UNSUPPORTED_FORMAT")]
    [InlineData("signature", "INVALID_FILE")]
    [InlineData("oversize", "FILE_TOO_LARGE")]
    [InlineData("empty-request", "INVALID_FILE")]
    [InlineData("empty-file", "INVALID_FILE")]
    [InlineData("mime", "INVALID_FILE")]
    [InlineData("workflow-missing", "INVALID_WORKFLOW")]
    [InlineData("workflow-blank", "INVALID_WORKFLOW")]
    [InlineData("workflow-invalid", "INVALID_WORKFLOW")]
    public async Task ValidationAndWorkflowFailures_HaveNoPersistentStorageOrQueueSideEffects(string scenario, string code)
    {
        // 建立本次 validation failure 專用的測試狀態並啟動 Acceptance Test Host。
        var state = new UploadFaultInjection();
        fixture.Requests["invalid"] = state;
        await fixture.StartAsync();
        // 依 scenario 建立對應的非法 multipart request。
        using var body = scenario == "signature" ? InvalidSignature() : UploadApiTests.Multipart(
            scenario == "empty-request" ? 0 : 1,
            scenario switch { "workflow-missing" => null, "workflow-blank" => " ", "workflow-invalid" => "0", _ => "Full" },
            scenario == "extension" ? "photo.exe" : "photo.jpg",
            scenario == "mime" ? "image/png" : "image/jpeg",
            scenario switch { "oversize" => 2048, "empty-file" => 0, _ => 3 });
        // 驗證 HTTP 400 與預期 error contract。
        using var response = await PostAsync("invalid", body);
        var trace = await AssertErrorAsync(response, HttpStatusCode.BadRequest, code);
        // Validation failure 不得留下 DB、Storage、Transaction 或 Queue side effect。
        await AssertEmptyDatabaseAsync();
        Assert.Empty(fixture.Files());
        Assert.Empty(state.Saved);
        Assert.Empty(state.Deleted);
        Assert.Equal(0, state.Begins);
        Assert.Equal(0, state.Rollbacks);
        Assert.Equal(0, state.EnqueueAttempts);
        await AssertQueueAsync([]);
        // Workflow binding failure 發生於 HTTP 層；其他 validation failure 才會進入 Application failure logging。
        Assert.Equal(scenario.StartsWith("workflow-", StringComparison.Ordinal) ? 0 : 1, Failures().Length);
        // 驗證 error trace correlation 與 log 敏感資訊保護。
        AssertTrace(trace);
        AssertSafeLogs();
    }

    // 建立「副檔名與 MIME 看似合法，但實際 bytes 不符合 JPG signature」的 multipart request。
    private static MultipartFormDataContent InvalidSignature()
    {
        var body = new MultipartFormDataContent();
        body.Add(new StringContent("Full"), "workflow");
        var file = new ByteArrayContent([1, 2, 3]);
        file.Headers.ContentType = new("image/jpeg");
        body.Add(file, "files", "photo.jpg");
        return body;
    }

    // Commit 前 Failure Boundary 的 Backend Acceptance Test。
    // 透過 Storage / Database Save / Commit failure 驗證：
    //
    // 1. Commit 前失敗不得留下任何 DB 資料。
    // 2. 已成功儲存的檔案必須做 compensation。
    // 3. Transaction 尚未建立時不得 Rollback。
    // 4. Transaction 已建立但尚未 Commit 時必須 Rollback。
    // 5. Commit 不得成功，Queue 也不得 enqueue。
    // 6. 必須保留原始 failure stage / category / exception。
    // 7. Rollback、compensation 與 failure logging 必須符合實際失敗階段。
    //
    // [Theory] 分別驗證 StorageSave、DatabaseSave 與 DatabaseCommit failure。
    [Theory]
    [InlineData("store2", "STORAGE_ERROR", UploadFailureStage.StorageSave, 0, 1)]
    [InlineData("save1", "INTERNAL_ERROR", UploadFailureStage.DatabaseSave, 1, 2)]
    [InlineData("save2", "INTERNAL_ERROR", UploadFailureStage.DatabaseSave, 1, 2)]
    [InlineData("commit", "INTERNAL_ERROR", UploadFailureStage.DatabaseCommit, 1, 2)]
    public async Task PreCommitFailure_UsesRealSqlRollbackAndOnlyOwnFileCompensation(string failure,
        string code, UploadFailureStage stage, int rollbacks, int stored)
    {
        // 設定本次失敗發生階段，並啟動 Acceptance Test Host。
        var state = new UploadFaultInjection { FailAt = failure };
        fixture.Requests["failure"] = state;
        await fixture.StartAsync();
        using var body = UploadApiTests.Multipart(2, "Full");
        // 執行 Upload，驗證對應的 500 error contract。
        using var response = await PostAsync("failure", body);
        var trace = await AssertErrorAsync(response, HttpStatusCode.InternalServerError, code);
        // Commit 前失敗不得留下 DB 或 Storage 資料；
        // 已存檔案只能補償本次 request 自己建立的內容。
        await AssertEmptyDatabaseAsync();
        Assert.Empty(fixture.Files());
        Assert.Equal(stored, state.Saved.Count);
        Assert.Equal(state.Saved, state.Deleted);
        // 驗證 Transaction boundary：
        // Transaction 前失敗不 Rollback；Transaction 建立後失敗則 Rollback；不得 Commit 或 enqueue。
        Assert.Equal(rollbacks, state.Begins);
        Assert.Equal(rollbacks, state.Rollbacks);
        Assert.Equal(0, state.Commits);
        Assert.Equal(0, state.EnqueueAttempts);
        Assert.True(state.CleanupTokensIndependent);
        await AssertQueueAsync([]);
        // 驗證 failure stage / category 與原始 exception 均被正確保留。
        var error = Assert.IsType<UploadFailureException>(state.ObservedFailure);
        Assert.Equal(stage, error.Stage);
        Assert.Equal(code == "STORAGE_ERROR" ? UploadFailureCategory.Storage : UploadFailureCategory.Internal, error.Category);
        Assert.Same(state.Primary, error.InnerException);
        if (failure is "save1" or "save2") Assert.IsType<DbUpdateException>(state.Primary);
        // 驗證 rollback / compensation / failure event 數量與實際失敗階段一致。
        Assert.Equal(stage, Assert.Single(Failures()).Fields["FailureStage"]);
        Assert.Equal(rollbacks, fixture.Logs.Entries.Count(x => x.EventId.Name == "TransactionRolledBack"));
        Assert.Equal(stored, fixture.Logs.Entries.Count(x => x.EventId.Name == "StorageCompensationStarted"));
        AssertTrace(trace);
        AssertSafeLogs();
    }

    // Commit 後 Queue failure 的 Backend Acceptance Test。
    // 驗證 Commit 為不可逆邊界：
    //
    // 1. DB 已 Commit 後不得 Rollback。
    // 2. 已保存的 Storage files 不得 compensation / delete。
    // 3. Batch / Image / ProcessingJob 必須保留。
    // 4. Queue 可發生部分 enqueue，但 committed Jobs 不得刪除。
    // 5. Batch Status API 仍必須能查到已持久化資料。
    // 6. Queue failure 必須被正確分類與記錄。
    // 7. 不得產生 Rollback、Storage compensation 或 UploadAccepted event。
    //
    // [Theory] 驗證第一筆或第二筆 Queue enqueue 發生失敗的情境。
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task QueueFailureAfterCommit_PreservesAllRowsFilesAndQueryableBatch(int failOn)
    {
        // 設定指定第幾次 enqueue 發生失敗。
        var state = new UploadFaultInjection { FailAt = $"enqueue{failOn}" };
        fixture.Requests["queue"] = state;
        await fixture.StartAsync();
        using var body = UploadApiTests.Multipart(2, "Full");
        // 執行 Upload，確認 Queue failure 對外回傳安全的 internal error。
        using var response = await PostAsync("queue", body);
        var trace = await AssertErrorAsync(response, HttpStatusCode.InternalServerError, "INTERNAL_ERROR");
        // Queue failure 發生於 Commit 之後，因此 DB / Storage 必須保留，且不得 Rollback 或 compensation。
        Assert.Equal(1, state.Commits);
        Assert.Equal(0, state.Rollbacks);
        Assert.Empty(state.Deleted);
        Assert.Equal(failOn - 1, state.Enqueued.Count);
        // 驗證完整 committed graph，以及失敗前已成功 enqueue 的 Queue prefix。
        await AssertCommittedAsync(state, 2, WorkflowType.Full);
        await AssertPersistedQueueAsync([state], failOn - 1, allowPartial: true);
        // 即使 Queue failure，已 Commit 的 Batch 仍必須可由 Status API 查詢。
        await AssertStatusAsync(state.BatchId!.Value, 2);
        // 驗證 QueueEnqueue failure stage、相關 Job / Batch context 與 failure logging。
        var error = Assert.IsType<UploadFailureException>(state.ObservedFailure);
        Assert.Equal(UploadFailureStage.QueueEnqueue, error.Stage);
        Assert.Same(state.Primary, error.InnerException);
        var entry = Assert.Single(Failures());
        Assert.Equal(UploadFailureStage.QueueEnqueue, entry.Fields["FailureStage"]);
        Assert.Equal(state.BatchId, entry.Fields["BatchId"]);
        await using var read = fixture.CreateContext();
        Assert.True(await read.ProcessingJobs.AnyAsync(x => x.Id == (long)entry.Fields["JobId"]!));
        Assert.DoesNotContain(fixture.Logs.Entries, x => x.EventId.Name is "TransactionRolledBack" or "StorageCompensationStarted" or "UploadAccepted");
        AssertTrace(trace);
        AssertSafeLogs();
    }

    // Client Cancellation 的 Backend Acceptance Test。
    // 使用 deterministic operation gate 控制取消發生時機，不依賴固定 delay。
    // 核心驗證 cancellation 是否發生在 Commit 前或 Commit 後：
    //
    // 1. Commit 前取消：Rollback + Storage compensation，DB 不保留資料。
    // 2. Commit 後取消：不得 Rollback 或 compensation，DB / Storage 必須保留。
    // 3. Queue wait 中取消：保留已 Commit 資料與已 enqueue 的工作。
    // 4. Client 收到 cancellation 後，仍等待 server operation 真正完成再驗證結果。
    // 5. Cancellation 必須被正確記錄，且不得留下背景等待中的 request。
    //
    // [Theory] 分別驗證 Commit 前、Commit 後及 Queue wait 中取消。
    [Theory]
    [InlineData("save2-after", false, 0)]
    [InlineData("commit-after", true, 0)]
    [InlineData("enqueue-wait", true, 1)]
    public async Task ClientCancellation_RespectsRealCommitBoundary(string pause, bool committed, int queued)
    {
        // 設定取消要暫停的操作邊界，並依 Queue 情境設定容量。
        var state = new UploadFaultInjection { PauseAt = pause };
        fixture.Requests["cancel"] = state;
        await fixture.StartAsync(pause == "enqueue-wait" ? 1 : 100);
        using var body = UploadApiTests.Multipart(2, "Full");
        using var cancellation = new CancellationTokenSource();
        // 啟動可被 CancellationToken 中斷的 Upload request。
        var pending = PostAsync("cancel", body, cancellation.Token);
        try
        {
            // 等待真正操作邊界後才中斷 Client；不靠猜測 SQL / HTTP 執行時間。
            await state.Entered.Task.WaitAsync(Bound);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(Bound));
            // Client cancellation 不代表 server 已完成清理；
            // 等待 server operation 完成後再驗證 DB / Storage / Queue。
            await state.Completed.Task.WaitAsync(Bound);
            Assert.True(state.CancellationObserved);
            Assert.Equal(committed ? 1 : 0, state.Commits);
            Assert.Equal(committed ? 0 : 1, state.Rollbacks);
            Assert.True(state.CleanupTokensIndependent);
            // 依 Commit boundary 驗證：
            // Commit 後保留資料；Commit 前則 Rollback 並 compensation。
            if (committed)
            {
                Assert.Empty(state.Deleted);
                await AssertCommittedAsync(state, 2, WorkflowType.Full);
                await AssertStatusAsync(state.BatchId!.Value, 2);
            }
            else
            {
                Assert.Equal(state.Saved, state.Deleted);
                await AssertEmptyDatabaseAsync();
                Assert.Empty(fixture.Files());
            }
            // 驗證取消發生前實際 enqueue 的工作數量與 persisted Jobs 一致。
            Assert.Equal(queued, state.Enqueued.Count);
            await AssertPersistedQueueAsync([state], queued, allowPartial: committed);
            // 驗證 cancellation event 被正確記錄，且 log 仍符合安全規則。
            var entry = Assert.Single(Failures());
            Assert.Equal("Canceled", entry.Fields["ErrorCode"]);
            AssertSafeLogs();
        }
        finally
        {
            cancellation.Cancel();
            state.Release.TrySetResult();
            // 確保錯誤 assertion 也不留下等待中的 server request。
            try { using var response = await pending.WaitAsync(Bound); }
            catch (OperationCanceledException) { }
        }
    }

    // Cleanup failure 的 Backend Acceptance Test。
    // 模擬 DatabaseSave 失敗後，Rollback、Storage delete、Transaction Dispose 又陸續失敗：
    //
    // 1. 必須保留最初的 DatabaseSave failure，不得被後續 cleanup failure 覆蓋。
    // 2. DB 最終不得留下未 Commit 資料。
    // 3. 即使部分檔案刪除失敗，也必須繼續嘗試其他 compensation。
    // 4. 各 cleanup failure 必須依正確 FailureStage 被記錄。
    // 5. Queue 不得 enqueue，log 仍需符合安全規則。
    [Fact]
    public async Task CleanupFailures_PreserveOriginalSqlFailureAndAttemptEveryFile()
    {
        // 同時注入 Database Save、Rollback、Storage delete 與 Dispose failure。
        var state = new UploadFaultInjection { FailAt = "save2", DeleteFails = true, RollbackFails = true, DisposeFails = true };
        fixture.Requests["cleanup"] = state;
        await fixture.StartAsync();
        using var body = UploadApiTests.Multipart(2, "Full");
        using var response = await PostAsync("cleanup", body);
        var trace = await AssertErrorAsync(response, HttpStatusCode.InternalServerError, "INTERNAL_ERROR");
        // 驗證最初的 DatabaseSave exception 仍是主要 failure。
        var error = Assert.IsType<UploadFailureException>(state.ObservedFailure);
        Assert.Equal(UploadFailureStage.DatabaseSave, error.Stage);
        Assert.Same(state.Primary, error.InnerException);
        Assert.IsType<DbUpdateException>(state.Primary);
        // 即使 cleanup 本身失敗，未 Commit DB 資料仍不得保留。
        await AssertEmptyDatabaseAsync();
        // 驗證所有檔案都嘗試 compensation；刪除失敗的檔案可能留下，但不得阻止後續清理。
        Assert.Equal(state.Saved, state.Deleted);
        Assert.Equal(fixture.PhysicalPath(state.Saved[0]), Assert.Single(fixture.Files()));
        Assert.Equal(new byte[] { 0xff, 0xd8, 0xff }, await File.ReadAllBytesAsync(fixture.PhysicalPath(state.Saved[0])));
        Assert.False(File.Exists(fixture.PhysicalPath(state.Saved[1])));
        Assert.True(state.CleanupTokensIndependent);
        // 驗證原始 failure 與各 cleanup failure 依發生順序被記錄。
        Assert.Equal(new[] { UploadFailureStage.DatabaseSave, UploadFailureStage.DatabaseRollback,
            UploadFailureStage.StorageCompensation, UploadFailureStage.TransactionDispose },
            Failures().Select(x => Assert.IsType<UploadFailureStage>(x.Fields["FailureStage"])));
        Assert.Equal(0, state.EnqueueAttempts);
        await AssertQueueAsync([]);
        AssertTrace(trace);
        AssertSafeLogs();
    }

    // Existing Data Protection 的 Backend Acceptance Test。
    // 先完成一筆成功 Upload，再讓後續 Request 在 Database Save 階段失敗：
    //
    // 1. 已 Commit 的 Batch / Image / ProcessingJob 必須完整保留。
    // 2. 先前已成功 Commit 的 Storage files 不得被後續失敗 Request 的 compensation 刪除。
    // 3. Failed request 只能清理自己建立的檔案。
    // 4. 原有 Queue jobs 與 Batch Status 必須維持可查詢。
    // 5. 後續 request failure 不得污染先前成功 request。
    [Fact]
    public async Task ExistingCommittedGraphAndBytes_SurviveLaterFailedRequest()
    {
        // 建立一筆成功 request 與一筆後續失敗 request。
        var existing = new UploadFaultInjection();
        var failed = new UploadFaultInjection { FailAt = "save2" };
        fixture.Requests["old"] = existing;
        fixture.Requests["new"] = failed;
        await fixture.StartAsync();
        // 先完成並保存既有 committed graph，作為後續保護基準。
        using var first = UploadApiTests.Multipart(2, "Full");
        using var accepted = await PostAsync("old", first);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        await using var before = fixture.CreateContext();
        // 重新讀取並保存成功 request 的 DB 狀態，供失敗後逐項比對。
        var oldImages = await before.Images.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync();
        var oldJobs = await before.ProcessingJobs.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync();
        // 執行新的失敗 Upload，確認 compensation 只影響本次 request。
        using var second = UploadApiTests.Multipart(2, "Full");
        using var rejected = await PostAsync("new", second);
        await AssertErrorAsync(rejected, HttpStatusCode.InternalServerError, "INTERNAL_ERROR");
        // 驗證原有 Batch / Images / Jobs 在後續失敗後仍完全一致。
        await AssertCommittedAsync(existing, 2, WorkflowType.Full);
        await using var read = fixture.CreateContext();
        Assert.Equal(existing.BatchId, (await read.Batches.SingleAsync()).Id);
        Assert.Equal(oldImages.Select(x => (x.Id, x.BatchId, x.StoredPath, x.Status, x.OriginalFileName, x.FileSize, x.CreatedAt, x.UpdatedAt)),
            (await read.Images.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync()).Select(x => (x.Id, x.BatchId, x.StoredPath, x.Status, x.OriginalFileName, x.FileSize, x.CreatedAt, x.UpdatedAt)));
        Assert.Equal(oldJobs.Select(x => (x.Id, x.ImageId, x.BatchId, x.Workflow, x.Status, x.RetryCount, x.CreatedAt)),
            (await read.ProcessingJobs.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync()).Select(x => (x.Id, x.ImageId, x.BatchId, x.Workflow, x.Status, x.RetryCount, x.CreatedAt)));
        // Failed request 不得刪除先前 successful request 的 Storage objects。
        Assert.DoesNotContain(failed.Deleted, key => existing.Saved.Contains(key));
        await AssertPersistedQueueAsync([existing, failed], 2);
        await AssertStatusAsync(existing.BatchId!.Value, 2);
        AssertSafeLogs();
    }

    // Concurrent Request isolation 的 Backend Acceptance Test。
    // 讓兩個 Upload Request 在時間上重疊，驗證彼此資源與 Transaction 不互相污染：
    //
    // 1. Batch / Image / Job identity 必須保持唯一。
    // 2. Storage key 必須保持唯一。
    // 3. 兩個 request 共用正式 shared Storage instance。
    // 4. 第二個 request 若失敗，只能 Rollback / compensation 自己的資料。
    // 5. 第一個成功 request 不得被另一個 request 的失敗影響。
    // 6. Queue 最終內容必須與 persisted Jobs 一致。
    //
    // [Theory] 分別驗證第二個 request 成功與失敗兩種情境。
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentRequests_IsolateIdentityTransactionAndCompensation(bool secondFails)
    {
        // 第一個 request 暫停於指定 DB 階段，讓第二個 request 在其尚未完成時開始。
        var first = new UploadFaultInjection { PauseAt = "save2-after" };
        var second = new UploadFaultInjection { FailAt = secondFails ? "save2" : null };
        fixture.Requests["first"] = first;
        fixture.Requests["second"] = second;
        await fixture.StartAsync();
        using var bodyA = UploadApiTests.Multipart(2, "Full");
        using var bodyB = UploadApiTests.Multipart(2, "Full");
        // 先啟動第一個 Upload，製造真實重疊的 request lifecycle。
        var pending = PostAsync("first", bodyA);
        try
        {
            // 等第一個 request 進入指定邊界後，再執行第二個 request，避免依賴 timing guess。
            await first.Entered.Task.WaitAsync(Bound);
            using var responseB = await PostAsync("second", bodyB);
            Assert.Equal(secondFails ? HttpStatusCode.InternalServerError : HttpStatusCode.Accepted, responseB.StatusCode);
            first.Release.TrySetResult();
            using var responseA = await pending.WaitAsync(Bound);
            Assert.Equal(HttpStatusCode.Accepted, responseA.StatusCode);
            // 確認兩個 request 共用正式 singleton Storage instance。
            Assert.NotNull(first.StorageInner);
            Assert.Same(first.StorageInner, second.StorageInner);
            await AssertCommittedAsync(first, 2, WorkflowType.Full, secondFails ? 2 : 4);
            if (!secondFails) await AssertCommittedAsync(second, 2, WorkflowType.Full, 4);
            await using var read = fixture.CreateContext();
            var batches = await read.Batches.Select(x => x.Id).ToArrayAsync();
            var images = await read.Images.ToArrayAsync();
            var jobs = await read.ProcessingJobs.ToArrayAsync();
            // 驗證 Batch、Image、Job 與 Storage key 在並行情境下仍保持唯一。
            Assert.Equal(secondFails ? 1 : 2, batches.Length);
            Assert.Equal(batches.Length, batches.Distinct().Count());
            Assert.Equal(images.Length, images.Select(x => x.StoredPath).Distinct().Count());
            Assert.Equal(images.Length, images.Select(x => x.Id).Distinct().Count());
            Assert.Equal(jobs.Length, jobs.Select(x => x.Id).Distinct().Count());
            // 驗證 Transaction 與 compensation 只作用於各自 request，不得跨 request 清理。
            Assert.Equal(0, first.Rollbacks);
            Assert.Equal(secondFails ? 1 : 0, second.Rollbacks);
            Assert.Empty(first.Deleted);
            Assert.DoesNotContain(second.Deleted, key => first.Saved.Contains(key));
            Assert.Equal(secondFails ? 2 : 4, fixture.Files().Length);
            await AssertPersistedQueueAsync([first, second], secondFails ? 2 : 4, ordered: false);
            AssertSafeLogs();
        }
        finally
        {
            first.Release.TrySetResult();
            using var response = await pending.WaitAsync(Bound);
        }
    }

    // Acceptance Fixture 初始化失敗的 infrastructure test。
    // 驗證測試環境本身初始化失敗時：
    //
    // 1. 最初的 initialization exception 必須被保留。
    // 2. Host / Database / Storage cleanup 都必須繼續嘗試。
    // 3. Cleanup failure 不得取代原始 initialization failure。
    // 4. 多個 cleanup failure 以附加診斷資訊保存，方便追查測試環境問題。
    [Fact]
    public async Task FixtureInitializationFailure_PreservesOriginalAndAttemptsEveryCleanup()
    {
        // 建立原始 initialization failure，以及 Database / Storage cleanup failure。
        var original = new InvalidOperationException("Initialization failed.");
        var databaseFailure = new IOException("Database cleanup failed.");
        var storageFailure = new IOException("Storage cleanup failed.");
        var attempts = new List<string>();
        // 模擬初始化失敗後，依序執行所有 best-effort cleanup。
        var observed = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            UploadAcceptanceFixture.InitializeWithCleanupAsync(
                () => Task.FromException(original),
                () => UploadAcceptanceFixture.CleanupAsync(
                    () => { attempts.Add("host"); return Task.CompletedTask; },
                    () => { attempts.Add("database"); throw databaseFailure; },
                    () => { attempts.Add("storage"); throw storageFailure; })));
        // 最終拋出的 exception 必須仍是最初的 initialization failure。
        Assert.Same(original, observed);
        Assert.Equal(new[] { "host", "database", "storage" }, attempts);
        // Cleanup failures 僅附加於原始 exception，不得覆蓋主要錯誤。
        var cleanup = Assert.IsType<AggregateException>(observed.Data["FixtureCleanupFailure"]);
        Assert.Equal(new Exception[] { databaseFailure, storageFailure }, cleanup.InnerExceptions);
    }

    // Zero-count Batch 的 Status Acceptance Test。
    // 直接建立一筆 TotalCount = 0 的 persisted Batch，驗證：
    //
    // 1. Status API 能正常查詢 zero-count Batch。
    // 2. Progress 應維持 0，不得發生除以零或錯誤狀態。
    // 3. 不應建立任何 Image / ProcessingJob。
    // 4. 不應產生 Storage file。
    // 5. 不需啟動實際 processing lifecycle，且 logging 仍需安全。
    [Fact]
    public async Task PersistedZeroCountBatch_ReturnsZeroProgressWithoutProcessingLifecycle()
    {
        // 直接在真實 SQL Server 建立 zero-count Batch，模擬已持久化的邊界資料。
        var id = Guid.NewGuid();
        await using (var seed = fixture.CreateContext())
        {
            seed.Batches.Add(new Batch(id, 0, DateTimeOffset.UtcNow));
            await seed.SaveChangesAsync();
        }
        await fixture.StartAsync();
        // 透過正式 Status API 驗證 zero-count progress 與 Batch 狀態。
        await AssertStatusAsync(id, 0);
        // 使用新的 DbContext 確認沒有 Image / Job 或 Storage side effect。
        await using var read = fixture.CreateContext();
        Assert.Equal(0, (await read.Batches.SingleAsync()).TotalCount);
        Assert.Empty(await read.Images.ToListAsync());
        Assert.Empty(await read.ProcessingJobs.ToListAsync());
        Assert.Empty(fixture.Files());
        AssertSafeLogs();
    }

    // 共用的成功 Persistence / Storage 驗證 helper。
    // 使用新的 DbContext 重新查詢已 Commit 資料，避免 EF Change Tracker 造成 false positive。
    // 驗證 Batch、Image、ProcessingJob 與實際 Storage file 是否完整一致。
    private async Task AssertCommittedAsync(UploadFaultInjection state, int count, WorkflowType workflow, int? totalFiles = null)
    {
        // 使用新的 DbContext 查詢真正已持久化的資料。
        await using var read = fixture.CreateContext();
        var batch = await read.Batches.AsNoTracking().SingleAsync(x => x.Id == state.BatchId);
        // 驗證 Batch 初始狀態與計數符合 Upload 完成後、Processing 尚未開始的狀態。
        Assert.Equal(count, batch.TotalCount);
        Assert.Equal("Pending", batch.Status);
        Assert.Equal(0, batch.ProcessedCount);
        Assert.Equal(0, batch.SuccessCount);
        Assert.Equal(0, batch.FailedCount);
        Assert.Null(batch.CompletedAt);
        // 依 BatchId 重新查詢 Image 與 ProcessingJob，確認完整 persistence graph。
        var images = await read.Images.AsNoTracking().Where(x => x.BatchId == batch.Id).ToArrayAsync();
        var jobs = await read.ProcessingJobs.AsNoTracking().Where(x => x.BatchId == batch.Id).ToArrayAsync();
        // DB records 與實際 Storage file 數量必須與本次 Upload 一致。
        Assert.Equal(count, images.Length);
        Assert.Equal(count, jobs.Length);
        Assert.Equal(totalFiles ?? count, fixture.Files().Length);
        // 逐一驗證 Image metadata、實際檔案 bytes，以及對應的唯一 ProcessingJob。
        foreach (var image in images)
        {
            Assert.True(image.Id > 0);
            Assert.Equal("Pending", image.Status);
            Assert.Equal(3, image.FileSize);
            Assert.Equal("image/jpeg", image.MimeType);
            Assert.Equal("photo.jpg", image.OriginalFileName);
            Assert.Contains(image.StoredPath, state.Saved);
            Assert.Equal(new byte[] { 0xff, 0xd8, 0xff }, await File.ReadAllBytesAsync(fixture.PhysicalPath(image.StoredPath)));
            // 每張 Image 必須對應一筆 Pending ProcessingJob，且尚未開始或完成處理。
            var job = Assert.Single(jobs, x => x.ImageId == image.Id);
            Assert.True(job.Id > 0);
            Assert.Equal(workflow, job.Workflow);
            Assert.Equal(ProcessingJobStatus.Pending, job.Status);
            Assert.Equal(0, job.RetryCount);
            Assert.Null(job.StartedAt);
            Assert.Null(job.CompletedAt);
            Assert.Null(job.ErrorCode);
            Assert.Null(job.ErrorMessage);
        }
    }

    // 共用的零 Persistence side-effect 驗證 helper。
    // 使用新的 DbContext 確認 failed request 沒有留下 Batch / Image / ProcessingJob。
    private async Task AssertEmptyDatabaseAsync()
    {
        // 重新查詢真實 SQL Server，確認三張核心資料表皆無殘留資料。
        await using var read = fixture.CreateContext();
        Assert.Empty(await read.Batches.AsNoTracking().ToListAsync());
        Assert.Empty(await read.Images.AsNoTracking().ToListAsync());
        Assert.Empty(await read.ProcessingJobs.AsNoTracking().ToListAsync());
    }

    // 驗證「資料庫中的 ProcessingJob」與「實際放入 Queue 的 Job」是否一致。
    // 預期值直接從新的 DbContext 查詢已 Commit 的 Job，
    // 避免使用 enqueue 結果自己驗證自己而產生 false positive。
    // Success 必須驗證全部 Job；failure / cancellation 則可只驗證失敗前已成功 enqueue 的部分。
    private async Task AssertPersistedQueueAsync(UploadFaultInjection[] states, int expectedCount, bool ordered = true,
        bool allowPartial = false)
    {
        await using var read = fixture.CreateContext();
        var batchIds = states.Where(x => x.BatchId.HasValue).Select(x => x.BatchId!.Value).ToArray();
        // 從真實 SQL Server 查出這些 Batch 已 Commit 的 ProcessingJobs。
        var persisted = await read.ProcessingJobs.AsNoTracking().Where(x => batchIds.Contains(x.BatchId))
            .OrderBy(x => x.Id).ToArrayAsync();
        // 失敗或取消時允許只入列部分 Job；成功時必須涵蓋全部已 Commit 的 Job。
        if (allowPartial) Assert.True(persisted.Length >= expectedCount);
        else Assert.Equal(expectedCount, persisted.Length);
        // 比對 DB Job 與實際 enqueue 記錄，確認數量、Id 與關聯資料一致且沒有重複。
        var expected = persisted.Take(expectedCount).ToArray();
        var enqueued = states.SelectMany(x => x.Enqueued).ToArray();
        Assert.Equal(expectedCount, enqueued.Length);
        Assert.Equal(expected.Select(x => x.Id).Order(), enqueued.Select(x => x.Id).Order());
        Assert.Equal(enqueued.Length, enqueued.Select(x => x.Id).Distinct().Count());
        foreach (var job in enqueued)
        {
            var row = Assert.Single(expected, x => x.Id == job.Id);
            Assert.Equal((row.ImageId, row.BatchId, row.Workflow), (job.ImageId, job.BatchId, job.Workflow));
        }
        // 最後再從正式 Channel 實際 dequeue，完成 DB → enqueue → Queue 三方驗證。
        await AssertQueueAsync(expected, ordered);
    }

    // 從正式 Processing Queue 實際取出 Job，確認 Queue 內容與預期一致。
    // 同時再次回 SQL Server 核對每個 Job，最後確認 Queue 已經沒有多餘工作。
    private async Task AssertQueueAsync(IReadOnlyList<ProcessingJob> expected, bool ordered = true)
    {
        using var timeout = new CancellationTokenSource(Bound);
        var actual = new List<ProcessingJob>();
        // 依預期數量逐筆從 Queue 取出，Bound 用來避免測試異常時永久等待。
        foreach (var _ in expected) actual.Add(await fixture.Queue.DequeueAsync(timeout.Token));
        // 驗證 Queue 中沒有缺少、重複的 Job；需要比對順序時也確認順序正確。
        Assert.Equal(expected.Count, actual.Count);
        Assert.Equal(actual.Count, actual.Select(x => x.Id).Distinct().Count());
        if (ordered) Assert.Equal(expected.Select(x => x.Id), actual.Select(x => x.Id));
        else Assert.Equal(expected.Select(x => x.Id).Order(), actual.Select(x => x.Id).Order());
        // 重新查 DB，確認 Queue 取出的 Job 與 persisted ProcessingJob 完全對應。
        await using var read = fixture.CreateContext();
        foreach (var job in actual)
        {
            var row = await read.ProcessingJobs.AsNoTracking().SingleAsync(x => x.Id == job.Id);
            Assert.Equal((row.Id, row.ImageId, row.BatchId, row.Workflow, row.Status),
                (job.Id, job.ImageId, job.BatchId, job.Workflow, job.Status));
        }
        // 再嘗試讀一次空 Queue；正常情況應持續等待。
        // 主動取消後確認等待工作能正確結束，不留下背景 pending task。
        using var cancellation = new CancellationTokenSource();
        var empty = fixture.Queue.DequeueAsync(cancellation.Token).AsTask();
        try { Assert.False(empty.IsCompleted); }
        finally { cancellation.Cancel(); }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => empty.WaitAsync(Bound));
    }

    // 驗證正式 Batch Status API 回傳內容。
    // Upload 剛完成但 Processing 尚未開始，因此 processed / success / failed / progress 都應為 0，
    // Batch 狀態則維持 Pending。
    private async Task AssertStatusAsync(Guid id, int total)
    {
        // 呼叫正式 Status endpoint，確認 HTTP 200 與 response contract。
        using var response = await fixture.Host.Client.GetAsync($"/api/v1/images/batches/{id}/status");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetProperty("success").GetBoolean());
        var data = json.RootElement.GetProperty("data");
        // 驗證 Batch 基本資料與尚未開始 Processing 時的初始統計值。
        Assert.Equal(new[] { "batchId", "totalCount", "processedCount", "successCount", "failedCount", "progressPercentage", "status" },
            data.EnumerateObject().Select(x => x.Name));
        Assert.Equal(id, data.GetProperty("batchId").GetGuid());
        Assert.Equal(total, data.GetProperty("totalCount").GetInt32());
        Assert.Equal(0, data.GetProperty("processedCount").GetInt32());
        Assert.Equal(0, data.GetProperty("successCount").GetInt32());
        Assert.Equal(0, data.GetProperty("failedCount").GetInt32());
        Assert.Equal(0m, data.GetProperty("progressPercentage").GetDecimal());
        Assert.Equal("Pending", data.GetProperty("status").GetString());
    }

    // 驗證失敗 HTTP Response 的安全 error contract。
    // 除了確認 status / code / traceId，也確認 Client 只能看到核准的安全訊息，
    // 不得洩漏 JWT、Storage path、SQL 或其他內部 exception detail。
    private async Task<string> AssertErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        // 先確認 HTTP status、Content-Type，以及 response 沒有直接洩漏測試 secret / Storage path。
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        var text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("TASK12_SECRET", text);
        Assert.DoesNotContain(fixture.StorageRoot, text);
        // 驗證 error envelope 只包含 code、message、traceId。
        using var json = JsonDocument.Parse(text);
        Assert.Equal(new[] { "success", "error" }, json.RootElement.EnumerateObject().Select(x => x.Name));
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        var error = json.RootElement.GetProperty("error");
        Assert.Equal(new[] { "code", "message", "traceId" }, error.EnumerateObject().Select(x => x.Name));
        Assert.Equal(code, error.GetProperty("code").GetString());
        // Error code 必須對應固定安全訊息，不得直接回傳內部 exception message。
        Assert.Equal(code switch
        {
            "INVALID_FILE" => "The uploaded file is invalid.",
            "UNSUPPORTED_FORMAT" => "The uploaded file format is not supported.",
            "FILE_TOO_LARGE" => "The uploaded file exceeds the maximum allowed size.",
            "INVALID_WORKFLOW" => "The specified workflow is not supported.",
            "STORAGE_ERROR" => "The uploaded file could not be stored.",
            "INTERNAL_ERROR" => "An internal error occurred.",
            _ => throw new InvalidOperationException("Missing expected safe message.")
        }, error.GetProperty("message").GetString());
        AssertNoInternalDetails(text);
        // traceId 必須存在，供後續與 Log 做同一次 request 的關聯。
        var trace = error.GetProperty("traceId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(trace));
        return trace!;
    }

    // 取得 Application 層記錄的 Upload failure events（EventId = 9001），
    // 供各測試檢查 failure stage、次數與 TraceId。
    private CapturingLoggerProvider.Entry[] Failures() => fixture.Logs.Entries.Where(x => x.EventId.Id == 9001).ToArray();

    // 驗證 HTTP error.traceId 能對應到同一次 Request 的 Log。
    // 同時確認真正的 failure event 只由 UploadService 記錄，
    // HTTP middleware 只負責錯誤 mapping，不重複產生 failure event。
    private void AssertTrace(string trace)
    {
        // 找出具有相同 TraceId 的所有 Log，確認 HTTP 與 Application 可互相追蹤。
        var entries = fixture.Logs.Entries.Where(x => x.Scope.TryGetValue("TraceId", out var value) && Equals(value, trace)).ToArray();
        Assert.NotEmpty(entries);
        Assert.Single(entries, x => x.Fields.TryGetValue("ErrorCode", out _) && x.Category.EndsWith("ApiExceptionMiddleware", StringComparison.Ordinal));
        Assert.All(Failures(), entry => Assert.Equal(trace, entry.Scope["TraceId"]));
        // 真正的 Upload failure 只應由 Application / UploadService 記錄一次；
        // HTTP middleware 只處理 response mapping，不能再重複記一筆 failure event。
        Assert.All(Failures(), entry => Assert.EndsWith("UploadService", entry.Category));
        // 同一個 TraceId + failure stage + storage key 不得重複記錄相同 failure event。
        Assert.All(Failures().GroupBy(x => (x.Scope["TraceId"], x.Fields["FailureStage"], x.Fields["StorageKey"])),
            group => Assert.Single(group));
    }

    // 共用的 Log 安全檢查。
    // 逐筆檢查 captured logs，避免記錄敏感資訊、內部錯誤細節、
    // raw binary、connection string 或不應公開的實體檔案路徑。
    private void AssertSafeLogs()
    {
        Assert.All(fixture.Logs.Entries, entry =>
        {
            // Host 啟動資訊可能包含 fixture diagnostics；Application 與 HTTP request logs 另檢查實體路徑。
            var requestLog = entry.Category.StartsWith("PhotoPlatform.", StringComparison.Ordinal)
                || entry.Scope.ContainsKey("RequestId");
            Assert.Null(entry.Exception);
            var text = entry.Message + string.Join("|", entry.Fields.Values) + string.Join("|", entry.Scope.Values);
            // 先用已知敏感字串與本次實際 exception detail 做基本洩漏檢查。
            foreach (var forbidden in new[] { "TASK12_SECRET", "SQL Detail", "Stack Trace", "JWT", "Authorization Header", "C:\\private", fixture.StorageRoot })
                Assert.DoesNotContain(forbidden, text);
            AssertNoInternalDetails(text);
            Assert.All(entry.Fields.Concat(entry.Scope), field =>
            {
                // 欄位只接受簡單型別與已知的 framework metadata，避免直接記錄 raw binary 或其他內部物件。
                Assert.True(field.Value is null or string or Guid or int or long or double or float or decimal
                    or TimeSpan or UploadFailureStage or Type or System.Reflection.MemberInfo
                    or Microsoft.AspNetCore.Routing.RouteEndpoint,
                    $"Unexpected structured payload type for {field.Key}: {field.Value?.GetType().FullName}.");
                Assert.DoesNotContain(field.Key, new[] { "Authorization", "JWT", "Exception", "RawBinary", "ConnectionString" });
                if (field.Value is string value)
                {
                    if (field.Key is "RequestPath" or "Path")
                        Assert.Matches(@"^/api/v1/images/(?:upload|batches/[A-Fa-f0-9-]{36}/status)$", value);
                    else if (requestLog)
                    {
                        Assert.False(Path.IsPathRooted(value), $"Rooted value for structured field {field.Key}.");
                        Assert.DoesNotMatch(@"(?:^|\s)[A-Za-z]:[\\/]|\\\\|(?:^|\s)/(?:[^\s/]+/)+", value);
                    }
                    Assert.DoesNotMatch(@"(?i)\bauthorization\b|\bbearer\s|\beyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+", value);
                    AssertNoInternalDetails(value);
                }
            });
            if (entry.Category.StartsWith("PhotoPlatform.", StringComparison.Ordinal))
            {
                var keys = new[] { "FailureStage", "ErrorCode", "BatchId", "ImageId", "JobId", "StorageKey",
                    "TraceId", "StatusCode", "{OriginalFormat}" };
                // Application log 的欄位名稱必須落在核准白名單內；欄位值也要符合預期型別與範圍。
                Assert.All(entry.Fields.Keys, key => Assert.Contains(key, keys));
                Assert.All(entry.Scope.Keys, key => Assert.Contains(key, new[] { "TraceId", "SpanId", "ParentId", "RequestId", "RequestPath", "ConnectionId", "ActionId", "ActionName" }));
                Assert.All(entry.Fields, field =>
                {
                    switch (field.Key)
                    {
                        case "FailureStage": Assert.IsType<UploadFailureStage>(field.Value); break;
                        case "ErrorCode":
                            Assert.Contains(Assert.IsType<string>(field.Value), new[] { "Validation", "Storage", "Internal", "Canceled",
                                "INVALID_FILE", "UNSUPPORTED_FORMAT", "FILE_TOO_LARGE", "INVALID_WORKFLOW",
                                "STORAGE_ERROR", "INTERNAL_ERROR", "BATCH_NOT_FOUND", "Success" });
                            break;
                        case "BatchId": if (field.Value is not null) Assert.IsType<Guid>(field.Value); break;
                        case "ImageId": case "JobId": if (field.Value is not null) Assert.True(Assert.IsType<long>(field.Value) > 0); break;
                        case "StatusCode": Assert.InRange(Assert.IsType<int>(field.Value), 100, 599); break;
                        case "TraceId": Assert.False(string.IsNullOrWhiteSpace(Assert.IsType<string>(field.Value))); break;
                        case "StorageKey": if (field.Value is not null) Assert.IsType<string>(field.Value); break;
                        case "{OriginalFormat}": Assert.IsType<string>(field.Value); break;
                    }
                });
            }
        });
        var allowed = new[] { "FailureStage", "ErrorCode", "BatchId", "ImageId", "JobId", "StorageKey", "{OriginalFormat}" };
        Assert.All(Failures(), entry => Assert.All(entry.Fields.Keys, key => Assert.Contains(key, allowed)));
        // StorageKey 只能是 logical key（original/<guid>），不得是實體 absolute path。
        Assert.All(fixture.Logs.Entries.Where(x => x.Fields.ContainsKey("StorageKey")), entry =>
        {
            if (entry.Fields["StorageKey"] is not string key) return;
            Assert.StartsWith("original/", key);
            Assert.Equal(41, key.Length);
            Assert.True(Guid.TryParseExact(key.AsSpan(9), "N", out _));
        });
    }

    // 檢查指定文字是否洩漏本次真正發生的內部 exception 或 SQL / Storage detail。
    // 用於 HTTP response 與 Log 的共用安全驗證。
    private void AssertNoInternalDetails(string text)
    {
        // 逐一檢查本次 fault injection 真正產生的 exception chain，確認其中的 message 沒有被直接輸出。
        foreach (var state in fixture.Requests.Values)
        {
            // Validation 訊息是受控的輸入驗證結果；這裡檢查的是不能對外顯示的 infrastructure failure。
            if (state.ObservedFailure is not UploadFailureException) continue;
            for (Exception? exception = state.Primary; exception is not null; exception = exception.InnerException)
                if (!string.IsNullOrWhiteSpace(exception.Message)) Assert.DoesNotContain(exception.Message, text);
        }
        // 再檢查常見 SQL Server / EF Core / Storage 內部關鍵字。
        foreach (var detail in new[] { "FOREIGN KEY", "UNIQUE KEY", "duplicate key", "SqlException",
            "Microsoft.Data.SqlClient", "INSERT statement conflicted", "Cannot insert duplicate", fixture.StorageRoot })
            Assert.DoesNotContain(detail, text, StringComparison.OrdinalIgnoreCase);
    }
}
