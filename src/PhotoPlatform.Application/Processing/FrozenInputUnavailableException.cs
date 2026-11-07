namespace PhotoPlatform.Application.Processing;

// 固定訊息避免傳出 SQL、路徑或秘密；不接受任意訊息或底層例外。
// 本例外不授予 Retry、Fallback 或關閉 Running 的權限，Cancellation 不應轉成此型別。
public sealed class FrozenInputUnavailableException : Exception
{
    public FrozenInputUnavailableException(long jobId, FrozenInputFailureReason reason)
        : base("Frozen input is unavailable.")
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(jobId);
        if (!Enum.IsDefined(reason))
            throw new ArgumentOutOfRangeException(nameof(reason));
        JobId = jobId;
        Reason = reason;
    }

    public long JobId { get; }
    public FrozenInputFailureReason Reason { get; }
}
