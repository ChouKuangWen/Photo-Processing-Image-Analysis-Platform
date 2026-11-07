namespace PhotoPlatform.Application.Processing;

// Application 的失敗原因，不是 API 錯誤碼或 Retry／Job 終態決策。
// Missing 表示確認資料缺少；SourceUnavailable 表示無法可靠取得來源，不能混為一談。
public enum ResultReferenceVerificationFailureReason
{
    Missing,
    NotCompleted,
    InputMismatch,
    VersionMismatch,
    OwnershipUnverified,
    VersionUnavailable,
    SourceUnavailable
}
