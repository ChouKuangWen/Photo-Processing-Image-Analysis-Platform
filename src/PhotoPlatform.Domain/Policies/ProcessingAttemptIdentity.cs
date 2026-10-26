namespace PhotoPlatform.Domain.Policies;

/// <summary>
/// 區分 Checkpoint 內的執行序號、邏輯重試槽位與同槽位恢復次數。
/// 只驗證識別值的不變量，不負責從持久化歷史配置唯一序號。
/// </summary>
public sealed record ProcessingAttemptIdentity
{
    public ProcessingAttemptIdentity(int attemptNo, int retryOrdinal, int resumeNo)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(attemptNo);
        ArgumentOutOfRangeException.ThrowIfNegative(retryOrdinal);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(retryOrdinal, 3);
        ArgumentOutOfRangeException.ThrowIfNegative(resumeNo);
        // 初始槽位沒有先前槽位的執行次數，故序號必須等於恢復次數加一。
        // 後續槽位可能累積先前的恢復次數，只限制下界；以 long 避免驗證運算溢位。
        if ((long)retryOrdinal + resumeNo + 1 > attemptNo ||
            (retryOrdinal == 0 && attemptNo != (long)resumeNo + 1))
            throw new ArgumentOutOfRangeException(nameof(attemptNo));

        AttemptNo = attemptNo;
        RetryOrdinal = retryOrdinal;
        ResumeNo = resumeNo;
    }

    public int AttemptNo { get; }
    public int RetryOrdinal { get; }
    public int ResumeNo { get; }
    public bool StartsRetrySlot => RetryOrdinal > 0 && ResumeNo == 0;

    public static ProcessingAttemptIdentity Initial() => new(1, 0, 0);

    /// <summary>在同一槽位建立下一次恢復識別，不消耗新重試槽位；序號溢位時拒絕產生識別。</summary>
    public ProcessingAttemptIdentity Resume() => new(checked(AttemptNo + 1), RetryOrdinal, checked(ResumeNo + 1));

    /// <summary>進入下一重試槽位並重設恢復次數；超過第三個重試槽位或執行序號溢位時拒絕。</summary>
    public ProcessingAttemptIdentity NextRetry()
    {
        ProcessingStateGuard.Require(RetryOrdinal < 3);
        return new ProcessingAttemptIdentity(checked(AttemptNo + 1), RetryOrdinal + 1, 0);
    }
}
