using Microsoft.EntityFrameworkCore.Storage;
using PhotoPlatform.Application.Interfaces;

namespace PhotoPlatform.Infrastructure.Persistence;

// Infrastructure Adapter，由 UploadPersistence 建立，供 UploadService 透過 IUploadTransaction 使用。
// 真正的 EF Core IDbContextTransaction 留在 Infrastructure，Application 只操作自己的交易抽象。
// transaction 是 EF Core 建立的交易物件，實際 Commit / Rollback / Dispose 都委派給它。
// Action detach 是無參數、無回傳值的 .NET Delegate；UploadPersistence 傳入 DetachUploadEntities 作為 Callback，
// 讓此 Adapter 通知 Persistence 清理本次 Upload 的 Tracking，而不必知道 Entity 清單內容。
internal sealed class UploadTransaction(IDbContextTransaction transaction, Action detach) : IUploadTransaction
{
    // SaveChanges 先將修改寫入目前 Transaction；此處才要求 EF Core 正式提交整筆交易，不自行執行 SQL。
    // 直接回傳底層 Task，呼叫端仍須 await 成功才可 Enqueue；Tracking cleanup 留到 Dispose 時處理。
    public Task CommitAsync(CancellationToken cancellationToken) => transaction.CommitAsync(cancellationToken);
    // 撤銷尚未提交的 Database Transaction；Database Rollback 不會自動讓 ChangeTracker 忘記 Entity。
    // await 讓 Rollback 操作先結束，再於 finally 嘗試 detach，即使 Rollback 拋出 Exception 也進入清理。
    // finally 不代表吞掉例外；若 Callback 本身拋錯，仍可能取代底層例外，由上層處理失敗。
    public async Task RollbackAsync(CancellationToken cancellationToken)
    {
        try { await transaction.RollbackAsync(cancellationToken); }
        finally { detach(); }
    }
    public async ValueTask DisposeAsync()
    {
        // 釋放底層 Transaction resource，不是 Dispose 整個 DbContext；ValueTask 符合 IAsyncDisposable 契約。
        // 在本專案 SQL Server provider 下，未完成的交易在 Dispose 時撤銷；已 Commit 的資料不會因此回滾。
        // Transaction resource 與 ChangeTracker 是不同狀態，因此即使 Dispose 失敗仍需嘗試 detach。
        // Rollback 後可能再次清理；目前 Callback 會清空自有清單，重複呼叫時不會再次處理已清除的 Entity。
        try { await transaction.DisposeAsync(); }
        finally { detach(); }
    }
}
