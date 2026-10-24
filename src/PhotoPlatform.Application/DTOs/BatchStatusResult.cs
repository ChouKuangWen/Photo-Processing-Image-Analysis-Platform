namespace PhotoPlatform.Application.DTOs;

// 表示 Application 層完成 Batch 狀態處理後的查詢結果。
// 包含持久化的 Batch 計數與狀態，以及由 Application 計算出的進度百分比。
public sealed record BatchStatusResult(Guid BatchId, int TotalCount, int ProcessedCount,
    int SuccessCount, int FailedCount, decimal ProgressPercentage, string Status);
