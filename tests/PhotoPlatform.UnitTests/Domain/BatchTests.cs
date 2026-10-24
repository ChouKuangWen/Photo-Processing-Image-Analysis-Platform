using PhotoPlatform.Domain.Entities;

namespace PhotoPlatform.UnitTests.Domain;

public class BatchTests
{
    [Fact]
    public void Constructor_PreservesSuppliedData_AndInitializesPendingBatch()
    {
        // Arrange：準備測試用 Batch ID 與建立時間。
        var id = Guid.NewGuid();
        var createdAt = new DateTimeOffset(2026, 9, 9, 2, 0, 0, TimeSpan.Zero);

        // Act：建立 Batch。
        var batch = new Batch(id, 3, createdAt);

        // Assert：檢查 Batch 屬性是否正確初始化。
        Assert.Equal(id, batch.Id);
        Assert.Equal(3, batch.TotalCount);
        Assert.Equal(createdAt, batch.CreatedAt);

        // 確認建立時間保持 UTC Offset。
        Assert.Equal(TimeSpan.Zero, batch.CreatedAt.Offset);

        // 確認 Batch 初始狀態。
        Assert.Equal("Pending", batch.Status);
        Assert.Equal(0, batch.ProcessedCount);
        Assert.Equal(0, batch.SuccessCount);
        Assert.Equal(0, batch.FailedCount);

        // Batch 尚未完成，因此完成時間應為 null。
        Assert.Null(batch.CompletedAt);
    }
}
