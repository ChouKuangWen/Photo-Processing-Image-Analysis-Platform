using Microsoft.EntityFrameworkCore;
using PhotoPlatform.Application.DTOs;
using PhotoPlatform.Application.Interfaces;

namespace PhotoPlatform.Infrastructure.Persistence;

// 實作 Batch 狀態的唯讀持久化查詢。
// 僅投影狀態查詢需要的欄位，不追蹤 Entity，也不載入 Images 或 ProcessingJobs。
public sealed class BatchStatusPersistence(PhotoPlatformDbContext db) : IBatchStatusPersistence
{
    public Task<BatchStatusSnapshot?> FindAsync(Guid batchId, CancellationToken cancellationToken) =>
        db.Batches.AsNoTracking().Where(batch => batch.Id == batchId)
            .Select(batch => new BatchStatusSnapshot(batch.Id, batch.TotalCount, batch.ProcessedCount,
                batch.SuccessCount, batch.FailedCount, batch.Status))
            .SingleOrDefaultAsync(cancellationToken);
}
