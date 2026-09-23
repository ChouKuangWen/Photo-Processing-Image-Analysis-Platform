namespace PhotoPlatform.Application.Exceptions;

// Application 的驗證例外，由 UploadService 將已判定的錯誤往上層傳遞；本類別不執行 Validation。
// 判斷來自 FileValidationService 或 UploadService 的 request-level checks。
// 保留 ErrorCode / ErrorMessage，供未來 API Layer 決定 HTTP Status 與 Response mapping；此處不處理 HTTP。
public sealed class UploadValidationException(string errorCode, string errorMessage) : Exception(errorMessage)
{
    public string ErrorCode { get; } = errorCode;
    public string ErrorMessage { get; } = errorMessage;
}
