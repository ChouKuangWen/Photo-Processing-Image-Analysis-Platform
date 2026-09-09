namespace PhotoPlatform.Domain.Entities;

// 代表一次圖片上傳批次。
public class Batch
{
    // 建立 Batch，必要資料由呼叫端提供。

    public Batch(Guid id, int totalCount, DateTimeOffset createdAt)
    {
        Id = id;
        TotalCount = totalCount;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    // 此批次的圖片總數。
    public int TotalCount { get; private set; }

    // 已處理、成功、失敗數量，建立時皆為 0。
    public int ProcessedCount { get; private set; } = 0;
    public int SuccessCount { get; private set; } = 0;
    public int FailedCount { get; private set; } = 0;

    // Batch 初始狀態。
    public string Status { get; private set; } = "Pending";
    public DateTimeOffset CreatedAt { get; private set; }

    // 尚未完成時為 null。
    public DateTimeOffset? CompletedAt { get; private set; } = null;
}
