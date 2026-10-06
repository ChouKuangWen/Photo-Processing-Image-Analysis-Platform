using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PhotoPlatform.Api.Contracts;
using PhotoPlatform.Api.Controllers;
using PhotoPlatform.Application.DTOs;
using PhotoPlatform.Application.Interfaces;
using PhotoPlatform.UnitTests.TestDoubles;

namespace PhotoPlatform.UnitTests.Api;

// 確認 Controller 有把請求正確交給 Query，
// 並根據「找到／找不到」回傳正確的 HTTP Response，同時正確記錄 Log。
public sealed class BatchStatusApiTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]

    // Controller Unit Test 專用 Query 假實作。
    // 不執行真正的 Application Query，由測試透過 execute 自訂查詢結果。
    public async Task ThinEndpoint_ForwardsTokenAndCorrelatesResult(bool found)
    {
        var id = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource();
        var logger = new RecordingLogger<BatchStatusController>();
        var query = new Stub((batchId, token) =>
        {
            Assert.Equal(id, batchId);
            Assert.Equal(cancellation.Token, token);
            return Task.FromResult<BatchStatusResult?>(found ? new(id, 5, 2, 1, 1, 40m, "Processing") : null);
        });
        var controller = new BatchStatusController(query, logger)
        {
            ControllerContext = new() { HttpContext = new DefaultHttpContext { TraceIdentifier = "query-trace" } }
        };
        var action = await controller.Status(id, cancellation.Token);
        if (found)
        {
            var data = Assert.IsType<ApiSuccessResponse<BatchStatusResult>>(Assert.IsType<OkObjectResult>(action).Value);
            Assert.True(data.Success);
            Assert.Equal(id, data.Data.BatchId);
        }
        else
        {
            var error = Assert.IsType<ApiErrorResponse>(Assert.IsType<NotFoundObjectResult>(action).Value);
            Assert.Equal(ApiErrors.Create("BATCH_NOT_FOUND", "query-trace"), error);
        }
        var entry = Assert.Single(logger.Entries);
        Assert.Equal("BatchStatusQueried", entry.EventId.Name);
        Assert.Equal(id, entry.Fields["BatchId"]);
        Assert.Equal("query-trace", entry.Fields["TraceId"]);
        Assert.Null(entry.Exception);
    }

    private sealed class Stub(Func<Guid, CancellationToken, Task<BatchStatusResult?>> execute) : IBatchStatusQuery
    {
        public Task<BatchStatusResult?> GetBatchStatusAsync(Guid id, CancellationToken token) => execute(id, token);
    }
}
