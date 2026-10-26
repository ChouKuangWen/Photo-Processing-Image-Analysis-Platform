using PhotoPlatform.Domain.Enums;
using PhotoPlatform.Domain.Policies;

namespace PhotoPlatform.Domain.Entities;

/// <summary>
/// 記錄單一 Checkpoint 的一次 invocation；恢復另建 Log，不覆寫舊紀錄。
/// Timestamp 固定為建立時的開始時間，與終態寫入時間分離。
/// </summary>
public sealed class ProcessingLog
{
    public ProcessingLog(long stepCheckpointId, long jobId, long imageId,
        ProcessingAttemptIdentity attempt, DateTimeOffset startedAt, string? traceId = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stepCheckpointId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(jobId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(imageId);
        ArgumentNullException.ThrowIfNull(attempt);
        if (traceId is not null)
            ProcessingStateGuard.Text(traceId, 100, nameof(traceId));

        StepCheckpointId = stepCheckpointId;
        JobId = jobId;
        ImageId = imageId;
        AttemptNo = attempt.AttemptNo;
        RetryOrdinal = attempt.RetryOrdinal;
        ResumeNo = attempt.ResumeNo;
        StartedAt = startedAt;
        Timestamp = startedAt;
        TraceId = traceId;
    }

    public long Id { get; private set; }
    public long StepCheckpointId { get; private set; }
    public long JobId { get; private set; }
    public long ImageId { get; private set; }
    public int AttemptNo { get; private set; }
    public int RetryOrdinal { get; private set; }
    public int ResumeNo { get; private set; }
    public ProcessingLogStatus Status { get; private set; } = ProcessingLogStatus.Running;
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public long? DurationMs { get; private set; }
    public string? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTimeOffset Timestamp { get; private set; }
    public string? TraceId { get; private set; }

    /// <summary>
    /// 將 Running 紀錄一次性轉為終態，保留原始開始時間與 Timestamp。
    /// 無法量測的耗時應傳入 null，不以零假裝已量測；錯誤內容須由呼叫端提供安全訊息。
    /// </summary>
    public void Finish(ProcessingLogStatus terminalStatus, DateTimeOffset completedAt,
        long? durationMs, string? errorCode = null, string? errorMessage = null)
    {
        ProcessingStateGuard.Require(Status == ProcessingLogStatus.Running);
        if (!Enum.IsDefined(terminalStatus))
            throw new ArgumentOutOfRangeException(nameof(terminalStatus));
        ProcessingStateGuard.Require(terminalStatus != ProcessingLogStatus.Running);
        ProcessingStateGuard.TimeNotBefore(completedAt, StartedAt);
        if (durationMs < 0)
            throw new ArgumentOutOfRangeException(nameof(durationMs));
        if (terminalStatus == ProcessingLogStatus.Failed)
            ProcessingStateGuard.Text(errorCode!, 50, nameof(errorCode));
        else if (errorCode is not null)
            ProcessingStateGuard.Text(errorCode, 50, nameof(errorCode));

        Status = terminalStatus;
        CompletedAt = completedAt;
        DurationMs = durationMs;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }
}
