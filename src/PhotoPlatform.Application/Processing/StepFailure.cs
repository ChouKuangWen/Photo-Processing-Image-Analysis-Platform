using PhotoPlatform.Domain.Enums;

namespace PhotoPlatform.Application.Processing;

// 只攜帶錯誤分類與代碼，不接收原始例外訊息，也不在此決定是否重試。
public sealed class StepFailure
{
    public StepFailure(ProcessingErrorClassification classification, string errorCode)
    {
        if (!Enum.IsDefined(classification))
            throw new ArgumentOutOfRangeException(nameof(classification));
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        if (errorCode.Length > 50)
            throw new ArgumentOutOfRangeException(nameof(errorCode));
        // 拒絕控制字元與非 ASCII；實際核准代碼清單及分類對應仍由各 Step policy 檢查。
        if (errorCode.Any(character => character < '!' || character > '~'))
            throw new ArgumentException("A non-whitespace printable ASCII error code is required.", nameof(errorCode));
        Classification = classification;
        ErrorCode = errorCode;
    }

    public ProcessingErrorClassification Classification { get; }
    public string ErrorCode { get; }
}
