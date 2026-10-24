namespace PhotoPlatform.Api.Contracts;

// API 對外回傳的單一錯誤內容。
public sealed record ApiError(string Code, string Message, string TraceId);

// API 失敗時的固定回應格式。
public sealed record ApiErrorResponse(bool Success, ApiError Error);

// API 成功時的固定回應格式。
public sealed record ApiSuccessResponse<T>(bool Success, T Data);

// 集中管理錯誤代碼對應的 HTTP Status Code 與回應訊息。
public static class ApiErrors
{
    // 將錯誤代碼轉成對應的 HTTP Status Code。
    public static int StatusCode(string code) => code switch
    {
        "INVALID_FILE" or "UNSUPPORTED_FORMAT" or "FILE_TOO_LARGE" or "INVALID_WORKFLOW" => 400,
        "BATCH_NOT_FOUND" => 404,
        _ => 500
    };

    // 根據錯誤代碼建立統一的 API 錯誤回應。
    // 對外只使用固定訊息，不直接回傳 Exception.Message。
    public static ApiErrorResponse Create(string code, string traceId) => new(false,
        new(code, code switch
        {
            "INVALID_FILE" => "The uploaded file is invalid.",
            "UNSUPPORTED_FORMAT" => "The uploaded file format is not supported.",
            "FILE_TOO_LARGE" => "The uploaded file exceeds the maximum allowed size.",
            "INVALID_WORKFLOW" => "The specified workflow is not supported.",
            "BATCH_NOT_FOUND" => "Batch was not found.",
            "STORAGE_ERROR" => "The uploaded file could not be stored.",
            _ => "An internal error occurred."
        }, traceId));
}
