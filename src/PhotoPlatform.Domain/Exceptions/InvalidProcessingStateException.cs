namespace PhotoPlatform.Domain.Exceptions;

/// <summary>表示狀態轉換前提未滿足，供上層映射為 INVALID_STATE_TRANSITION，而非直接承載 HTTP 語意。</summary>
public sealed class InvalidProcessingStateException : InvalidOperationException
{
    public const string ErrorCode = "INVALID_STATE_TRANSITION";

    public InvalidProcessingStateException() : base("The requested state transition is not allowed.")
    {
    }
}
