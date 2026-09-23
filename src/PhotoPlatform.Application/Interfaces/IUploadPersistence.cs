using PhotoPlatform.Domain.Entities;

namespace PhotoPlatform.Application.Interfaces;

// Application 定義的 Upload 專用 Persistence contract，由 UploadService 使用、Infrastructure 實作。
// 只暴露開始 Transaction、登記 Entity 與 SaveChanges，讓 Use Case 不必依賴 EF Core、DbContext 或 SQL Server。
public interface IUploadPersistence
{
    // 兩階段 SaveChanges 共用同一個 Transaction；回傳自己的抽象，避免洩漏 EF Core 交易型別。
    Task<IUploadTransaction> BeginTransactionAsync(CancellationToken cancellationToken);
    // 以下 Add 方法只登記待新增 Entity；在 EF Core 實作中加入 DbContext Tracking，不代表已 INSERT。
    // Batch、Images 與 Jobs 由 UploadService 建立，Persistence 不決定 Workflow 或業務規則。
    void AddBatch(Batch batch);
    void AddImages(IReadOnlyList<Image> images);
    void AddProcessingJobs(IReadOnlyList<ProcessingJob> jobs);
    // 將目前追蹤的修改送往 Database，並回填資料庫產生的 Identity。
    // 存在明確 Transaction 時，SaveChanges 不等於 Commit；必須另行 Commit 才完成整筆交易。
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
