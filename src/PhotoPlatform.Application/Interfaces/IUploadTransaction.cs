namespace PhotoPlatform.Application.Interfaces;

// Application 的 Transaction abstraction，讓 UploadService 不直接依賴 EF Core 的 IDbContextTransaction。
// Infrastructure 實作實際交易；繼承 IAsyncDisposable 可使用 await using 非同步釋放資源，
// 也可像 UploadService 一樣在 finally 明確呼叫 DisposeAsync。
public interface IUploadTransaction : IAsyncDisposable
{
    // 正式提交兩次 SaveChanges 所在的交易；成功後 UploadService 才能 Enqueue。
    Task CommitAsync(CancellationToken cancellationToken);
    // 撤銷尚未 Commit 的資料庫修改，不包含 Storage 檔案；檔案清理由 UploadService 另外協調。
    Task RollbackAsync(CancellationToken cancellationToken);
    // DisposeAsync 由 IAsyncDisposable 繼承，負責交易資源 cleanup，不是提交操作。
}
