using Microsoft.EntityFrameworkCore;
using PhotoPlatform.Application.Interfaces;
using PhotoPlatform.Domain.Entities;

namespace PhotoPlatform.Infrastructure.Persistence;

// UploadPersistence 是 IUploadPersistence 的 EF Core 實作。
// Application 只透過 IUploadPersistence 操作資料，不需要直接知道 DbContext 或 SQL Server。
// PhotoPlatformDbContext 由 DI 傳入，因此這個類別不負責 Dispose DbContext。
public sealed class UploadPersistence(PhotoPlatformDbContext db) : IUploadPersistence
{
    // 額外記錄「這次 Upload 加入 DbContext 的 Entity」。
    // Rollback 或 Dispose 時，只 Detach 這些 Entity，不影響 DbContext 中其他資料。
    private readonly List<object> tracked = [];

    // 使用 DbContext 開啟真正的 Database Transaction，
    // 再包成 IUploadTransaction 回傳給 Application。
    // DetachUploadEntities 是傳給 UploadTransaction 的 cleanup 方法，之後 Rollback 或 Dispose 時才會執行。
    public async Task<IUploadTransaction> BeginTransactionAsync(CancellationToken cancellationToken)
        => new UploadTransaction(await db.Database.BeginTransactionAsync(cancellationToken), DetachUploadEntities);

    // 記住這個 Batch，之後如果 Rollback 可以把它從 EF Tracking 移除。
    // db.Batches.Add 只是把 Batch 標記為待新增，這時還沒有真正寫入 SQL Server。
    public void AddBatch(Batch batch)
    {
        tracked.Add(batch);
        db.Batches.Add(batch);
    }

    // 將這次 Upload 的 Images 記錄下來，並加入 EF Core Tracking。
    // 真正寫入 Database 要等 SaveChangesAsync。
    public void AddImages(IReadOnlyList<Image> images)
    {
        tracked.AddRange(images);
        db.Images.AddRange(images);
    }

    // ProcessingJob 已經使用正式的 Image.Id 建立完成。
    // 這裡只把 Jobs 加入 EF Core Tracking，真正 INSERT 要等 SaveChangesAsync。
    public void AddProcessingJobs(IReadOnlyList<ProcessingJob> jobs)
    {
        tracked.AddRange(jobs);
        db.ProcessingJobs.AddRange(jobs);
    }

    // 將目前 DbContext 追蹤的變更真正送到 Database。
    // TASK-08 第一次 SaveChanges 後，SQL Server 會產生 Image.Id，EF Core 再把 Id 回填到原本的 Image 物件。
    // 注意：SaveChanges 不等於 Commit，資料仍然屬於目前的 Transaction。
    public async Task SaveChangesAsync(CancellationToken cancellationToken)
        => await db.SaveChangesAsync(cancellationToken);

    private void DetachUploadEntities()
    {
        // Database Rollback 只撤銷資料庫交易，EF Core 仍可能記得這些 Entity 是 Added / Modified。
        // 因此把這次 Upload 加入的 Entity 設成 Detached，避免之後再次 SaveChanges 時又被送到 Database。
        foreach (var entity in tracked)
            db.Entry(entity).State = EntityState.Detached;
        // 清空自己的紀錄。
        // 之後如果 Dispose 再次呼叫這個方法，也不會重複處理。
        tracked.Clear();
    }
}
