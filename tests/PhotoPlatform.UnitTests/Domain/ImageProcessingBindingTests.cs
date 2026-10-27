using PhotoPlatform.Domain.Entities;
using PhotoPlatform.Domain.Enums;

namespace PhotoPlatform.UnitTests.Domain;
// 只測綁定物件的輸入與保存內容；不在這裡確認資料庫中的工作、圖片和批次是否互相對應。
public sealed class ImageProcessingBindingTests
{
    // 測試目的：確認建立綁定時保留呼叫端提供的資料，且不能從外部直接改屬性。
    // 測試情境：分別傳入 Naming、Analysis、DuplicateDetection、Full 四種合法 Workflow。
    // 預期結果：圖片、批次、工作、狀態與時間保持原值，所有屬性的 setter 都不是公開的。
    [Theory]
    [InlineData(WorkflowType.Naming)]
    [InlineData(WorkflowType.Analysis)]
    [InlineData(WorkflowType.DuplicateDetection)]
    [InlineData(WorkflowType.Full)]
    public void Constructor_PreservesSuppliedBinding(WorkflowType workflow)
    {
        var batchId = Guid.NewGuid();
        var timestamp = DateTimeOffset.UtcNow;
        var binding = new ImageProcessingBinding(10, batchId, 20, workflow, ProcessingJobStatus.Pending, timestamp);
        Assert.Equal(10, binding.ImageId);
        Assert.Equal(batchId, binding.BatchId);
        Assert.Equal(20, binding.PrimaryJobId);
        Assert.Equal(workflow, binding.PrimaryWorkflow);
        Assert.Equal(ProcessingJobStatus.Pending, binding.PrimaryStatus);
        Assert.Equal(timestamp, binding.UpdatedAt);
        Assert.All(typeof(ImageProcessingBinding).GetProperties(), property => Assert.False(property.SetMethod!.IsPublic));
    }

    // 測試目的：確認錯誤的圖片／工作編號與未定義狀態會被立即拒絕。
    // 測試情境：傳入零值編號，以及未定義的 Workflow 或 Status。
    // 預期結果：建構子拋出參數範圍錯誤；編號是否真的存在，仍須由資料庫與後續交易確認。
    [Fact]
    public void Constructor_RejectsInvalidIdsAndEnums()
    {
        var batch = Guid.NewGuid();
        var timestamp = DateTimeOffset.UtcNow;
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageProcessingBinding(0, batch, 1, WorkflowType.Naming, ProcessingJobStatus.Pending, timestamp));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageProcessingBinding(1, batch, 0, WorkflowType.Naming, ProcessingJobStatus.Pending, timestamp));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageProcessingBinding(1, batch, 1, (WorkflowType)99, ProcessingJobStatus.Pending, timestamp));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageProcessingBinding(1, batch, 1, WorkflowType.Naming, (ProcessingJobStatus)99, timestamp));
    }
}
