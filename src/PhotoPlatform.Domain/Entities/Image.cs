namespace PhotoPlatform.Domain.Entities;

// 代表系統中的一張圖片紀錄。
// 圖片本體存於 Storage。
public class Image
{
    public Image(
        Guid batchId,
        string originalFileName,
        string storedPath,
        long fileSize,
        string mimeType,
        DateTimeOffset createdAt)
    {
        // 必要字串不得為空。
        ArgumentException.ThrowIfNullOrWhiteSpace(originalFileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(storedPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);

        BatchId = batchId;
        OriginalFileName = originalFileName;
        StoredPath = storedPath;
        FileSize = fileSize;
        MimeType = mimeType;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    // 由 Persistence / Database 產生正式 ID。
    public long Id { get; private set; } = 0;

    // 所屬 Batch。
    public Guid BatchId { get; private set; }

    public string OriginalFileName { get; private set; }

    // 圖片在 Storage 中的位置。
    public string StoredPath { get; private set; }

    public long FileSize { get; private set; }

    public string MimeType { get; private set; }

    // Image 初始狀態。
    public string Status { get; private set; } = "Pending";

    public DateTimeOffset CreatedAt { get; private set; }

    // 建立時與 CreatedAt 相同。
    public DateTimeOffset UpdatedAt { get; private set; }
}
