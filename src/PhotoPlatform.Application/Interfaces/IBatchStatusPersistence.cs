using PhotoPlatform.Application.DTOs;

namespace PhotoPlatform.Application.Interfaces;

// 定義 Application 查詢 Batch 持久化狀態所需的唯讀資料存取合約。
// 實際資料庫查詢由 Infrastructure 實作；找不到指定 Batch 時回傳 null。
public interface IBatchStatusPersistence
{
    Task<BatchStatusSnapshot?> FindAsync(Guid batchId, CancellationToken cancellationToken);
}
