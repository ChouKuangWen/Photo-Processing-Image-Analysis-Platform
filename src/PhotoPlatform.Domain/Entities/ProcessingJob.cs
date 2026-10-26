using PhotoPlatform.Domain.Enums;
using PhotoPlatform.Domain.Policies;

namespace PhotoPlatform.Domain.Entities;

/// <summary>
/// 保存工作的目前執行狀態與跨步驟、跨 Run 的累計重試次數；歷史由 Run、Checkpoint 與 Log 保留。
/// 狀態方法只維護領域規則，不授予資料庫執行權。
/// </summary>
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

    /// <summary>開始 Pending 工作；同一 Run 恢復時保留首次開始時間。</summary>
    public void Start(DateTimeOffset startedAt)
    {
        ProcessingStateGuard.Require(Status == ProcessingJobStatus.Pending);
        ProcessingStateGuard.TimeNotBefore(startedAt, StartedAt ?? CreatedAt);
        StartedAt ??= startedAt;
        Status = ProcessingJobStatus.Processing;
    }

    public void Complete(DateTimeOffset completedAt)
    {
        ProcessingStateGuard.Require(Status == ProcessingJobStatus.Processing);
        ProcessingStateGuard.TimeNotBefore(completedAt, StartedAt!.Value);
        CompletedAt = completedAt;
        ErrorCode = null;
        ErrorMessage = null;
        Status = ProcessingJobStatus.Completed;
    }

    public void Fail(string errorCode, string? errorMessage, DateTimeOffset completedAt)
    {
        ProcessingStateGuard.Require(Status == ProcessingJobStatus.Processing);
        ProcessingStateGuard.Text(errorCode, 50, nameof(errorCode));
        ProcessingStateGuard.TimeNotBefore(completedAt, StartedAt!.Value);
        CompletedAt = completedAt;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        Status = ProcessingJobStatus.Failed;
    }

    /// <summary>
    /// 在 Processing 狀態記錄重試槽位的首次開始；初次執行與同槽位恢復不增加計數。
    /// 呼叫端須在同一交易中保證每個新槽位只記錄一次，本方法不負責持久化去重。
    /// </summary>
    public void RecordRetryStart(ProcessingAttemptIdentity attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        ProcessingStateGuard.Require(Status == ProcessingJobStatus.Processing);
        if (attempt.StartsRetrySlot)
            RetryCount = checked(RetryCount + 1);
    }

    /// <summary>
    /// 將符合暫時性失敗與 Run 編號範圍的 Failed 工作轉為 Pending，清除目前執行投影。
    /// 保留建立時間與累計重試次數；完整資格、歷史及下一 Run 編號須由呼叫端核對。
    /// </summary>
    public void PrepareManualRetry(ProcessingErrorClassification classification, int nextRunNo)
    {
        ProcessingStateGuard.Require(Status == ProcessingJobStatus.Failed &&
            classification == ProcessingErrorClassification.Transient && nextRunNo is >= 2 and <= 4);
        Status = ProcessingJobStatus.Pending;
        StartedAt = null;
        CompletedAt = null;
        ErrorCode = null;
        ErrorMessage = null;
    }

    /// <summary>經外部確認可安全恢復後，僅將 Processing 工作退回 Pending，保留首次開始時間。</summary>
    public void Recover(ProcessingRecoveryAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ProcessingStateGuard.Require(Status == ProcessingJobStatus.Processing);
        Status = ProcessingJobStatus.Pending;
    }
}
