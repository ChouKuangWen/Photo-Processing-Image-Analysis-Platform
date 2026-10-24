using PhotoPlatform.Domain.Entities;
using PhotoPlatform.Domain.Enums;

namespace PhotoPlatform.UnitTests.Domain;

public class ProcessingJobTests
{
    [Theory]
    [InlineData(WorkflowType.Naming)]
    [InlineData(WorkflowType.Analysis)]
    [InlineData(WorkflowType.DuplicateDetection)]
    [InlineData(WorkflowType.Full)]
    public void Constructor_PreservesReferencesAndWorkflow_AndInitializesPendingJob(WorkflowType workflow)
    {
        // Arrange：準備 Batch ID 與建立時間。
        var batchId = Guid.NewGuid();
        var createdAt = new DateTimeOffset(2026, 9, 9, 2, 0, 0, TimeSpan.Zero);

        // Act：建立不同 Workflow 的 ProcessingJob
        var job = new ProcessingJob(1024L, batchId, workflow, createdAt);

        // Assert：確認識別資料與 Workflow 正確保存。
        Assert.Equal(0L, job.Id);
        Assert.Equal(1024L, job.ImageId);
        Assert.Equal(batchId, job.BatchId);
        Assert.Equal(workflow, job.Workflow);

        // 確認建立時間。
        Assert.Equal(createdAt, job.CreatedAt);
        Assert.Equal(TimeSpan.Zero, job.CreatedAt.Offset);

        // 確認新 Job 的初始狀態。
        Assert.Equal(ProcessingJobStatus.Pending, job.Status);
        Assert.Equal(0, job.RetryCount);

        // Job 尚未開始或完成，因此相關欄位應為 null。
        Assert.Null(job.StartedAt);
        Assert.Null(job.CompletedAt);
        Assert.Null(job.ErrorCode);
        Assert.Null(job.ErrorMessage);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void Constructor_RejectsUndefinedWorkflow(int value)
    {
        // Arrange：準備固定建立時間。
        var createdAt = new DateTimeOffset(2026, 9, 9, 2, 0, 0, TimeSpan.Zero);

        // 將不存在的整數強制轉成 WorkflowType，
        // 建立 ProcessingJob 時應拋出 ArgumentOutOfRangeException。
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ProcessingJob(1024L, Guid.NewGuid(), (WorkflowType)value, createdAt));

        // Assert：確認拋出的例外包含正確的參數名稱。
        Assert.Equal("workflow", exception.ParamName);
    }
}
