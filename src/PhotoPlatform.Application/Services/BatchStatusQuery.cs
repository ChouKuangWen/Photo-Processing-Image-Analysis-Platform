using PhotoPlatform.Application.DTOs;
using PhotoPlatform.Application.Interfaces;

namespace PhotoPlatform.Application.Services;

// 實作 Batch 狀態查詢流程：
// 取得持久化的 Batch 狀態快照，計算進度百分比後轉成 Application 查詢結果。
public sealed class BatchStatusQuery(IBatchStatusPersistence persistence) : IBatchStatusQuery
{
    public async Task<BatchStatusResult?> GetBatchStatusAsync(Guid batchId, CancellationToken cancellationToken)
    {
        // 由 Persistence 取得資料庫目前保存的 Batch 狀態；不存在時直接回傳 null。
        var batch = await persistence.FindAsync(batchId, cancellationToken);
        if (batch is null) return null;

        // 進度 = 已處理數 / 總數 × 100。
        // TotalCount 為 0 時回傳 0%，並將結果限制在 0～100%。
        var progress = batch.TotalCount == 0 ? 0m
            : Math.Clamp((decimal)batch.ProcessedCount / batch.TotalCount * 100m, 0m, 100m);
        return new(batch.BatchId, batch.TotalCount, batch.ProcessedCount,
            batch.SuccessCount, batch.FailedCount, progress, batch.Status);
    }
}
