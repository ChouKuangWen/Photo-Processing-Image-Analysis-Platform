using PhotoPlatform.Domain.Enums;
using PhotoPlatform.Domain.Policies;

namespace PhotoPlatform.Domain.Entities;

/// <summary>
/// 保存單一 Run 中步驟的進度、重試期限與結果摘要；個別執行歷史由 ProcessingLog 記錄。
/// 已完成或失敗的 Checkpoint 不重新開啟。
/// </summary>
public sealed class ProcessingStepCheckpoint
{
    public ProcessingStepCheckpoint(long runId, string step, int stepOrder,
        string ruleVersion, string inputFingerprint)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(runId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stepOrder);
        ProcessingStateGuard.Text(step, 50, nameof(step));
        ProcessingStateGuard.Text(ruleVersion, 50, nameof(ruleVersion));
        if (inputFingerprint is null || inputFingerprint.Length != 64 ||
            inputFingerprint.Any(character => character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new ArgumentException("Fingerprint must be a lowercase SHA-256 value.", nameof(inputFingerprint));

        RunId = runId;
        Step = step;
        StepOrder = stepOrder;
        RuleVersion = ruleVersion;
        InputFingerprint = inputFingerprint;
    }

    public long Id { get; private set; }
    public long RunId { get; private set; }
    public string Step { get; private set; }
    public int StepOrder { get; private set; }
    public string RuleVersion { get; private set; }
    public string InputFingerprint { get; private set; }
    public ProcessingCheckpointStatus Status { get; private set; } = ProcessingCheckpointStatus.Pending;
    public int RetryOrdinal { get; private set; }
    public DateTimeOffset? NextRetryAt { get; private set; }
    public string? ResultReference { get; private set; }
    public ProcessingResultStatus? ResultStatus { get; private set; }
    public string? ErrorCode { get; private set; }
    public ProcessingErrorClassification? ErrorClassification { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>
    /// 開始 Pending 或已到期的 RetryWaiting 步驟，要求識別符合目前重試槽位。
    /// 保留步驟首次開始時間；執行識別的持久化唯一性與執行權由呼叫端保證。
    /// </summary>
    public void Start(ProcessingAttemptIdentity attempt, DateTimeOffset startedAt)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        ProcessingStateGuard.Require(Status is ProcessingCheckpointStatus.Pending or ProcessingCheckpointStatus.RetryWaiting);
        ProcessingStateGuard.Require(attempt.RetryOrdinal == RetryOrdinal);
        if (NextRetryAt is { } deadline)
            ProcessingStateGuard.Require(startedAt >= deadline);
        if (StartedAt is { } firstStart)
            ProcessingStateGuard.TimeNotBefore(startedAt, firstStart);

        StartedAt ??= startedAt;
        NextRetryAt = null;
        Status = ProcessingCheckpointStatus.Processing;
    }

    /// <summary>將暫時性失敗轉為 RetryWaiting，預留下一槽位及一、二、四秒的重試期限。</summary>
    public void WaitForRetry(string errorCode, DateTimeOffset failedAt)
    {
        ProcessingStateGuard.Require(Status == ProcessingCheckpointStatus.Processing && RetryOrdinal < 3);
        ProcessingStateGuard.Text(errorCode, 50, nameof(errorCode));
        ProcessingStateGuard.TimeNotBefore(failedAt, StartedAt!.Value);
        var nextRetryAt = failedAt.AddSeconds(1 << RetryOrdinal);

        RetryOrdinal++;
        NextRetryAt = nextRetryAt;
        ErrorCode = errorCode;
        ErrorClassification = ProcessingErrorClassification.Transient;
        Status = ProcessingCheckpointStatus.RetryWaiting;
    }

    /// <summary>
    /// 以成功、警告或略過結果完成 Processing 步驟；呼叫端須確認警告降級或略過符合核准契約。
    /// 此結果不改寫個別 invocation 的失敗歷史。
    /// </summary>
    public void Complete(ProcessingResultStatus resultStatus, string? resultReference, DateTimeOffset completedAt)
    {
        ProcessingStateGuard.Require(Status == ProcessingCheckpointStatus.Processing);
        if (!Enum.IsDefined(resultStatus))
            throw new ArgumentOutOfRangeException(nameof(resultStatus));
        if (resultReference is not null)
            ProcessingStateGuard.Text(resultReference, 500, nameof(resultReference));
        ProcessingStateGuard.TimeNotBefore(completedAt, StartedAt!.Value);

        ResultStatus = resultStatus;
        ResultReference = resultReference;
        CompletedAt = completedAt;
        ErrorCode = null;
        ErrorClassification = null;
        Status = ProcessingCheckpointStatus.Completed;
    }

    /// <summary>只允許永久失敗或已耗盡重試槽位的暫時性失敗結束 Processing 步驟。</summary>
    public void Fail(string errorCode, ProcessingErrorClassification classification, DateTimeOffset completedAt)
    {
        ProcessingStateGuard.Require(Status == ProcessingCheckpointStatus.Processing);
        ProcessingStateGuard.Text(errorCode, 50, nameof(errorCode));
        if (!Enum.IsDefined(classification))
            throw new ArgumentOutOfRangeException(nameof(classification));
        ProcessingStateGuard.Require(classification == ProcessingErrorClassification.Permanent || RetryOrdinal == 3);
        ProcessingStateGuard.TimeNotBefore(completedAt, StartedAt!.Value);

        ErrorCode = errorCode;
        ErrorClassification = classification;
        CompletedAt = completedAt;
        Status = ProcessingCheckpointStatus.Failed;
    }

    /// <summary>
    /// 經安全確認後將 Processing 退回 Pending，保留槽位與首次開始時間。
    /// RetryWaiting 不走此轉換，避免破壞尚未執行的重試期限。
    /// </summary>
    public void Recover(ProcessingRecoveryAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ProcessingStateGuard.Require(Status == ProcessingCheckpointStatus.Processing);
        Status = ProcessingCheckpointStatus.Pending;
    }
}
