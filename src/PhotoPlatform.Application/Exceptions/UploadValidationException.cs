namespace PhotoPlatform.Application.Exceptions;
// 表示 Upload 驗證失敗，保存錯誤代碼、訊息與失敗階段。
// 驗證由其他服務執行，本類別只負責將錯誤資訊傳給上層。
public sealed class UploadValidationException(string errorCode, string errorMessage,
    UploadFailureStage stage = UploadFailureStage.RequestValidation) : Exception(errorMessage)
{
    public UploadFailureCategory Category => UploadFailureCategory.Validation;
    public UploadFailureStage Stage { get; } = stage;
    public string ErrorCode { get; } = errorCode;
    public string ErrorMessage { get; } = errorMessage;
}
