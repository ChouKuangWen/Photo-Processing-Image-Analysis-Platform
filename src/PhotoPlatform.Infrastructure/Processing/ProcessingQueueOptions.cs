namespace PhotoPlatform.Infrastructure.Processing;

// 承接 Queue 設定，讓容量可由執行環境提供，不必修改 Queue 實作。
public sealed class ProcessingQueueOptions
{
    // 未指定時採用模組規格的 100 筆上限；限制緩衝 Job 數，而非 Worker 並行數。
    // 正整數驗證由 ChannelProcessingQueue 建構子執行。
    public int Capacity { get; set; } = 100;
}
