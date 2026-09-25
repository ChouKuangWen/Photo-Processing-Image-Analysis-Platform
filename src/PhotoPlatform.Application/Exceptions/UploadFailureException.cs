namespace PhotoPlatform.Application.Exceptions;
// 本檔案功能:統一表示 Upload 流程失敗，記錄錯誤分類、失敗階段與原始原因。
public enum UploadFailureCategory
{
    Validation, //輸入不合法
    Storage, //儲存服務異常
    Internal //系統內部處理失敗
}

public enum UploadFailureStage
{
    RequestValidation, // Request 驗證失敗
    FileValidation, // 單一檔案驗證失敗
    StorageSave, // 儲存檔案失敗
    DatabaseBegin, // 資料庫事務開始失敗
    DatabaseSave, // 資料庫儲存失敗
    DatabaseCommit, // 資料庫提交失敗
    QueueEnqueue, // 佇列加入失敗
    Unexpected, // 未預期錯誤
    DatabaseRollback, // Rollback 失敗
    StorageCompensation, // 儲存補償失敗
    TransactionDispose // Transaction 清理失敗
}

// 依「哪個上傳步驟失敗」決定錯誤分類，不用 Exception 型別或錯誤訊息來猜。
// 原始錯誤保存在 InnerException，方便除錯；API 與 Log 不可直接輸出其中的敏感內容。
public sealed class UploadFailureException(UploadFailureStage stage, Exception originalException)
    : Exception("Upload operation failed.", originalException)
{
    // 記錄實際失敗的處理階段，例如 StorageSave、DatabaseCommit、QueueEnqueue。
    public UploadFailureStage Stage { get; } = stage;

    // StorageSave 歸類為 Storage，其餘系統處理錯誤歸類為 Internal。
    public UploadFailureCategory Category { get; } = stage == UploadFailureStage.StorageSave
        ? UploadFailureCategory.Storage : UploadFailureCategory.Internal;
}
