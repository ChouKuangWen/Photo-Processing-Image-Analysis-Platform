using Microsoft.AspNetCore.Mvc;
using PhotoPlatform.Api.Contracts;
using PhotoPlatform.Application.DTOs;
using PhotoPlatform.Application.Interfaces;

namespace PhotoPlatform.Api.Controllers;

// 提供 Batch 狀態查詢 HTTP endpoint。
// Controller 僅負責 HTTP 邊界：呼叫 Application Query、記錄查詢事件，並映射為 200 / 404 Response。
[Route("api/v1/images/batches")]
public sealed class BatchStatusController(IBatchStatusQuery query, ILogger<BatchStatusController> logger) : ControllerBase
{
    [HttpGet("{batchId:guid}/status")]
    public async Task<IActionResult> Status(Guid batchId, CancellationToken cancellationToken)
    {
        var result = await query.GetBatchStatusAsync(batchId, cancellationToken);

        // 記錄查詢結果與 TraceId，方便將 HTTP Request 與 Log 關聯。
        logger.LogInformation(new EventId(11020, "BatchStatusQueried"),
            "Batch query {BatchId}; code {ErrorCode}; trace {TraceId}",
            batchId, result is null ? "BATCH_NOT_FOUND" : "Success", HttpContext.TraceIdentifier);

        // Batch 不存在時沿用統一錯誤格式；存在時回傳標準 success/data envelope。
        return result is null
            ? NotFound(ApiErrors.Create("BATCH_NOT_FOUND", HttpContext.TraceIdentifier))
            : Ok(new ApiSuccessResponse<BatchStatusResult>(true, result));
    }
}
