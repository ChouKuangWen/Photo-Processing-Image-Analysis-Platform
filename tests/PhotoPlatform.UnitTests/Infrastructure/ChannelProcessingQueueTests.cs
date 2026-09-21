using PhotoPlatform.Domain.Entities;
using PhotoPlatform.Domain.Enums;
using PhotoPlatform.Infrastructure.Processing;

namespace PhotoPlatform.UnitTests.Infrastructure;

public sealed class ChannelProcessingQueueTests
{
    /* 測試用，集中建立 ProcessingJob，減少各測試案例重複準備資料。
    imageId 預設為 1；需要區分多筆工作時可指定其他值。每次以 Guid.NewGuid()
    產生不同 BatchId，讓測試取得獨立的 Job 資料，而不必依賴資料庫建立紀錄。*/
    private static ProcessingJob Job(int imageId = 1) =>
        new(imageId, Guid.NewGuid(), WorkflowType.Full, DateTimeOffset.UtcNow);

    /* 0和-1兩者都違反 Capacity 必須大於 0 的規則。
    建構 Queue 並 ArgumentOutOfRangeException，
    拒絕容量範圍錯誤，而不是等到 Enqueue / Dequeue 才暴露問題。*/
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_RejectsInvalidCapacity(int capacity)
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ChannelProcessingQueue(new ProcessingQueueOptions { Capacity = capacity }));

    [Fact]
    // 第 101 筆必須等待讀取端釋放空間；逐筆比對確認 First In, First Out 且沒有被丟棄。
    public async Task DefaultCapacity_Allows100ItemsThenWaitsWithoutDropping()
    {
        // 使用預設 Capacity = 100；準備 101 筆跨越容量邊界，先填滿 Queue，保留最後一筆用來觀察滿載行為。
        var queue = new ChannelProcessingQueue(new ProcessingQueueOptions());
        // 萬一 Queue 行為壞掉，某個 await 永遠卡住，10 秒後會取消，避免整個測試永久卡住。
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var jobs = Enumerable.Range(1, 101).Select(Job).ToArray();
        foreach (var job in jobs.Take(100))
            await queue.EnqueueAsync(job, timeout.Token);

        /*Queue 已滿時，第 101 筆寫入應進入等待狀態。
         先保留 EnqueueAsync 回傳的 Task 而不立即 await，
         才能確認 FullMode.Wait 的 Backpressure 行為。
         pending 未完成表示該 Job 既未超出容量直接入列，也未被丟棄。*/
        var pending = queue.EnqueueAsync(jobs[100], timeout.Token);
        Assert.False(pending.IsCompleted);
        // 取出第一筆後便有可用空間，Channel 應喚醒等待中的 Writer。
        // await pending 確認第 101 筆確實已入列，再檢查其餘資料。
        Assert.Same(jobs[0], await queue.DequeueAsync(timeout.Token));
        await pending;
        // 第一筆已取出，因此 Skip(1) 從第二筆開始逐一比對到第 101 筆。
        // 完整順序及物件比對一起保護 Bounded Queue、Backpressure、Wait、FIFO 與 No Drop。
        foreach (var job in jobs.Skip(1))
            Assert.Same(job, await queue.DequeueAsync(timeout.Token));
    }

    [Fact]
    // 空 Queue 不立即完成讀取；新 Job 加入後喚醒 Reader，並保留同一物件與原始欄位。
    public async Task EmptyQueue_WaitsUntilItemArrivesAndPreservesJob()
    {
        /* Arrange：建立尚未入列任何工作的 Queue；snapshot 保存傳遞前的識別、狀態、
           RetryCount、Error 與時間欄位，避免只檢查物件參考而漏掉原物件被修改的情況。*/
        var queue = new ChannelProcessingQueue(new ProcessingQueueOptions());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var job = Job();
        var snapshot = (job.Id, job.ImageId, job.BatchId, job.Workflow, job.Status,
            job.RetryCount, job.ErrorCode, job.ErrorMessage, job.CreatedAt, job.StartedAt, job.CompletedAt);
        // Queue 是空的，所以 Dequeue 不應該已經完成。。
        var pending = queue.DequeueAsync(timeout.Token).AsTask();
        Assert.False(pending.IsCompleted);
        // 加入工作提供可讀資料，應喚醒剛才等待中的 Reader。
        await queue.EnqueueAsync(job, timeout.Token);
        var result = await pending;
        // Assert.Same 驗證同一個 instance；snapshot 比對則驗證其欄位沒有被 Queue 改寫。
        // Queue 的責任是傳遞 Job，業務狀態變更應由後續處理流程負責。
        Assert.Same(job, result);
        Assert.Equal(snapshot, (result.Id, result.ImageId, result.BatchId, result.Workflow, result.Status,
            result.RetryCount, result.ErrorCode, result.ErrorMessage, result.CreatedAt, result.StartedAt, result.CompletedAt));
    }

    [Fact]
    // Reader 取消必須向上傳遞，且已取消的等待不能搶走之後加入的 Job。
    public async Task EmptyQueue_CancellationPropagatesAndDoesNotConsumeNextItem()
    {
        // Arrange：canceled 專門控制本次 Reader 的主動取消；timeout 則限制後續
        // Enqueue / Dequeue 的等待時間，兩者分開才能在取消後繼續驗證 Queue。
        var queue = new ChannelProcessingQueue(new ProcessingQueueOptions());
        using var canceled = new CancellationTokenSource();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        // Act / Assert：空 Queue 讓 Reader 進入等待，確認未完成後再 Cancel，
        // 才能驗證「等待中的 ReadAsync 被中止」，而不只是傳入一個已取消的 Token。
        var pending = queue.DequeueAsync(canceled.Token).AsTask();
        Assert.False(pending.IsCompleted);
        canceled.Cancel();
        // 取消應透過 OperationCanceledException 向上傳遞。WaitAsync 提供獨立的 10 秒上限：
        // 若實作退化為忽略 Token，會以 TimeoutException 使 assertion 失敗，而非永久掛住。
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => pending.WaitAsync(TimeSpan.FromSeconds(10)));
        // 確認例外攜帶本次 Reader 的 Token，取消來源沒有被替換。
        Assert.Equal(canceled.Token, exception.CancellationToken);
        // 取消完成後再加入新 Job，確認舊 Reader 不會繼續消耗後來的工作，
        // 且新的 Reader 仍能取得同一筆 Job。
        var job = Job();
        await queue.EnqueueAsync(job, timeout.Token);
        Assert.Same(job, await queue.DequeueAsync(timeout.Token));
    }

    [Fact]
    // 容量設為 1 以觸發 Writer 等待；取消後不得偷偷入列，也不能移除原有 Job。
    public async Task ConfiguredCapacity_CanceledWriterDoesNotInsertOrDropItems()
    {
        // 刻意指定 Capacity = 1，只需一筆工作就能填滿 Queue，隔離滿載情境。
        // canceled 控制第二筆 Writer；timeout 留給其他正常操作，避免取消後無法繼續檢查。
        var queue = new ChannelProcessingQueue(new ProcessingQueueOptions { Capacity = 1 });
        using var canceled = new CancellationTokenSource();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var first = Job(1);
        var next = Job(3);
        await queue.EnqueueAsync(first, timeout.Token);
        // 第一筆已占滿空間，第二筆應依 FullMode.Wait 留在等待狀態。
        // 先保存 pending 並確認未完成，才能主動取消這個確實被背壓擋住的 Writer。
        var pending = queue.EnqueueAsync(Job(2), canceled.Token);
        Assert.False(pending.IsCompleted);
        canceled.Cancel();
        // 預期寫入取消且保留原 Token；獨立 WaitAsync 忽略取消時令測試失敗，
        // 不依靠被測操作本身的 CancellationToken 來保證測試能結束。
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => pending.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(canceled.Token, exception.CancellationToken);
        // 第一筆必須仍在，證明沒有為了第二筆而 Drop 原有工作。
        Assert.Same(first, await queue.DequeueAsync(timeout.Token));
        /*不只是測Job2 有沒有拋取消例外，
        還要測取消後 Queue 裡的資料仍然正確，而且下一個 Job3 還能正常進出。*/
        await queue.EnqueueAsync(next, timeout.Token);
        Assert.Same(next, await queue.DequeueAsync(timeout.Token));
    }
}
