using PhotoPlatform.Domain.Entities;

namespace PhotoPlatform.UnitTests.Domain;

public class ImageTests
{
    [Fact]
    public void Constructor_PreservesUploadData_AndInitializesPendingImage()
    {
        // Arrange：準備 Batch ID 與建立時間。
        var batchId = Guid.NewGuid();
        var createdAt = new DateTimeOffset(2026, 9, 9, 2, 0, 0, TimeSpan.Zero);

        // Act：建立 Image。
        var image = new Image(batchId, "photo.jpg", "original/photo.jpg", 1024L, "image/jpeg", createdAt);

        // Assert：檢查 Image 屬性是否正確初始化。
        Assert.Equal(0L, image.Id);
        Assert.Equal(batchId, image.BatchId);
        Assert.Equal("photo.jpg", image.OriginalFileName);
        Assert.Equal("original/photo.jpg", image.StoredPath);
        Assert.Equal(1024L, image.FileSize);
        Assert.Equal("image/jpeg", image.MimeType);

        // 確認 Image 初始狀態。
        Assert.Equal("Pending", image.Status);

        // 確認建立時間與更新時間。
        Assert.Equal(createdAt, image.CreatedAt);
        Assert.Equal(TimeSpan.Zero, image.CreatedAt.Offset);
        Assert.Equal(image.CreatedAt, image.UpdatedAt);
        Assert.Equal(TimeSpan.Zero, image.UpdatedAt.Offset);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\r\n")]
    public void Constructor_RejectsMissingOriginalFileName(string? value)
    {
        // 驗證 OriginalFileName 不可為 null、空字串或純空白。
        var exception = Assert.ThrowsAny<ArgumentException>(() =>
            new Image(Guid.NewGuid(), value!, "original/photo.jpg", 1024L, "image/jpeg", CreatedAt));

        // 確認拋出的例外包含正確的參數名稱。
        Assert.Equal("originalFileName", exception.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\r\n")]
    public void Constructor_RejectsMissingStoredPath(string? value)
    {
        // 驗證 StoredPath 不可為 null、空字串或純空白。
        var exception = Assert.ThrowsAny<ArgumentException>(() =>
            new Image(Guid.NewGuid(), "photo.jpg", value!, 1024L, "image/jpeg", CreatedAt));

        Assert.Equal("storedPath", exception.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\r\n")]
    public void Constructor_RejectsMissingMimeType(string? value)
    {
        // 驗證 MimeType 不可為 null、空字串或純空白。
        var exception = Assert.ThrowsAny<ArgumentException>(() =>
            new Image(Guid.NewGuid(), "photo.jpg", "original/photo.jpg", 1024L, value!, CreatedAt));

        Assert.Equal("mimeType", exception.ParamName);
    }

    // 共用固定時間，避免每個測試重複建立。
    private static DateTimeOffset CreatedAt =>
        new(2026, 9, 9, 2, 0, 0, TimeSpan.Zero);
}
