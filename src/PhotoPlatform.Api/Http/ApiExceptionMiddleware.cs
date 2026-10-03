using PhotoPlatform.Api.Contracts;
using PhotoPlatform.Application.Exceptions;

namespace PhotoPlatform.Api.Http;

// 統一處理 API 執行過程中的例外，並轉成固定的 HTTP 錯誤回應。
public sealed class ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        // 確保每個 Request 都有 TraceId，方便對照 API 回應與 Log。
        if (string.IsNullOrWhiteSpace(context.TraceIdentifier))
            context.TraceIdentifier = Guid.NewGuid().ToString("N");

        // 將 TraceId 加入這次 Request 的 Logging Scope。
        using var scope = logger.BeginScope(new Dictionary<string, object?> { ["TraceId"] = context.TraceIdentifier });
        try
        {
            // 將 Request 繼續交給下一個 Middleware / Controller。
            await next(context);
            // Request 正常完成時記錄 HTTP Status Code。
            logger.LogInformation("HTTP result {StatusCode}; trace {TraceId}",
                context.Response.StatusCode, context.TraceIdentifier);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Client 已取消 Request 時直接中止連線。
            context.Abort();
        }
        // 回應開始傳送後不能再改寫 envelope，例外會繼續交給外層處理。
        catch (Exception exception) when (!context.Response.HasStarted)
        {
            // 將已知的 Application 例外轉成對外的錯誤代碼。
            // 無法辨識的例外統一視為 INTERNAL_ERROR。
            var code = exception switch
            {
                UploadValidationException validation when validation.ErrorCode is
                    "INVALID_FILE" or "UNSUPPORTED_FORMAT" or "FILE_TOO_LARGE" or "INVALID_WORKFLOW"
                    => validation.ErrorCode,
                UploadFailureException { Category: UploadFailureCategory.Storage }
                    => "STORAGE_ERROR",
                _ => "INTERNAL_ERROR"
            };

            // 根據錯誤代碼決定 HTTP Status Code。
            var status = ApiErrors.StatusCode(code);
            // 只記錄受控資訊，不把 Exception 內容直接寫進 Log。
            logger.LogInformation("HTTP error mapping {StatusCode} {ErrorCode}; trace {TraceId}",
                status, code, context.TraceIdentifier);

            // 清除尚未送出的 Response，改寫成統一錯誤格式。
            context.Response.Clear();
            context.Response.StatusCode = status;
            await context.Response.WriteAsJsonAsync(ApiErrors.Create(code, context.TraceIdentifier), context.RequestAborted);
        }
    }
}
