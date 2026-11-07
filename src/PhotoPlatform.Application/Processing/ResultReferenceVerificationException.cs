namespace PhotoPlatform.Application.Processing;

// 固定訊息避免傳出 SQL、路徑或秘密；不接受任意訊息或底層例外。
// 本例外不授予 Retry、Fallback 或關閉 Running 的權限，Cancellation 不應轉成此型別。
public sealed class ResultReferenceVerificationException : Exception
{
    public ResultReferenceVerificationException(ResultReferenceVerificationFailureReason reason)
        : base("Result reference verification failed.")
    {
        if (!Enum.IsDefined(reason))
            throw new ArgumentOutOfRangeException(nameof(reason));
        Reason = reason;
    }

    public ResultReferenceVerificationFailureReason Reason { get; }
}
