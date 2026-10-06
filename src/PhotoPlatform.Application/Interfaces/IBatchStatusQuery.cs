using PhotoPlatform.Application.DTOs;

namespace PhotoPlatform.Application.Interfaces;

// 定義 Application 對外提供的 Batch 狀態查詢合約。
// 回傳已完成進度計算的查詢結果；找不到指定 Batch 時回傳 null。
public interface IBatchStatusQuery
{
    Task<BatchStatusResult?> GetBatchStatusAsync(Guid batchId, CancellationToken cancellationToken);
}
