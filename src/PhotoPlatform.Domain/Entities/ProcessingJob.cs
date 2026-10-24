using PhotoPlatform.Domain.Enums;

namespace PhotoPlatform.Domain.Entities;

// 代表一個待背景處理的工作。
public class ProcessingJob
{
    public ProcessingJob(
        long imageId,
        Guid batchId,
        WorkflowType workflow,
        DateTimeOffset createdAt)
    {
        // Workflow 必須是已定義的 Enum 值。
        if (!Enum.IsDefined(workflow))
        {
            throw new ArgumentOutOfRangeException(nameof(workflow), workflow, "Workflow must be a defined value.");
        }

        ImageId = imageId;
        BatchId = batchId;
        Workflow = workflow;
        CreatedAt = createdAt;
    }

    // 由 Persistence / Database 產生正式 ID。
    public long Id { get; private set; } = 0;

    // 要處理的圖片。
    public long ImageId { get; private set; }

    // 所屬 Batch。
    public Guid BatchId { get; private set; }

    public WorkflowType Workflow { get; private set; }

    // 新 Job 初始為 Pending。
    public ProcessingJobStatus Status { get; private set; } = ProcessingJobStatus.Pending;

    public int RetryCount { get; private set; } = 0;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; } = null;
    public DateTimeOffset? CompletedAt { get; private set; } = null;
    public string? ErrorCode { get; private set; } = null;
    public string? ErrorMessage { get; private set; } = null;
}
