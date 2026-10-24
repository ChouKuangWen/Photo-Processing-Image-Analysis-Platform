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

    // 對齊既有 Schema 的選填結果欄位；此處只定義資料形狀，命名、雜湊與中繼資料由後續模組產生。
    public string? NewFileName { get; private set; }
    public string? SHA256 { get; private set; }
    public DateTimeOffset? TakenAt { get; private set; }
    public string? CameraModel { get; private set; }
    public int? ISO { get; private set; }
    public string? ShutterSpeed { get; private set; }
    public string? Aperture { get; private set; }
    public decimal? Latitude { get; private set; }
    public decimal? Longitude { get; private set; }
    public string? LocationName { get; private set; }
}
