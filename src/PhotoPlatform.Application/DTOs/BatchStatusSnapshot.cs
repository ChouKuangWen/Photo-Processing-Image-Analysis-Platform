namespace PhotoPlatform.Application.DTOs;
// 表示從持久化層查詢到的 Batch 狀態。
// 僅保存資料庫中的原始計數與狀態；進度百分比等衍生資料由 Application 層計算。
public sealed record BatchStatusSnapshot(Guid BatchId, int TotalCount, int ProcessedCount,
    int SuccessCount, int FailedCount, string Status);
