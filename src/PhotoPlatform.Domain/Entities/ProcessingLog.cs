using PhotoPlatform.Domain.Enums;
using PhotoPlatform.Domain.Policies;

namespace PhotoPlatform.Domain.Entities;

// 記錄某個處理步驟的一次執行；恢復執行時另建一筆，不覆蓋舊紀錄。
// Timestamp 保留最初開始的時間，不能在結束時改掉，以免歷史排序跟著變動。

public sealed class ProcessingLog
{
    // 這個私有建構子讓 EF Core 讀回既有資料；外部程式仍須使用公開建構子，不能跳過輸入驗證。
    private ProcessingLog()
    {
    }

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


    // 只有還在 Running 的紀錄可以結束一次；已結束的紀錄不能再改寫，開始時間與 Timestamp 也要保留。
    // 不知道耗時就傳入 null，不能用零代替；呼叫端提供的錯誤訊息不能包含密碼等敏感內容。

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
