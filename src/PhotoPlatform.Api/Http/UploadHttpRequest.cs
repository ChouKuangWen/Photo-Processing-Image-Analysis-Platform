using Microsoft.Net.Http.Headers;
using PhotoPlatform.Application.DTOs;
using PhotoPlatform.Application.Exceptions;
using PhotoPlatform.Domain.Enums;

namespace PhotoPlatform.Api.Http;

// 將 HTTP multipart/form-data 轉成 Application 層使用的 UploadRequest，
// 並統一管理本次上傳檔案 adapter 的生命週期。
public sealed class UploadHttpRequest : IDisposable
{
    private readonly FormUploadFile[] files;
    public UploadRequest Request { get; }

    private UploadHttpRequest(FormUploadFile[] files, WorkflowType workflow)
    {
        this.files = files;
        // 建立 Application 層真正使用的 UploadRequest。
        Request = new(files, workflow);
    }

    // 讀取 HTTP Request，解析 multipart/form-data，並轉成 Application 層可以使用的上傳資料。
    public static async Task<UploadHttpRequest> ReadAsync(HttpRequest request, CancellationToken token)
    {
        // Request 必須是 multipart/form-data，否則視為無效的上傳請求。
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var contentType) ||
            !string.Equals(contentType.MediaType.Value, "multipart/form-data", StringComparison.OrdinalIgnoreCase))
            throw InvalidFile();

        IFormCollection form;
        try
        {
            // 將 multipart/form-data 解析成表單欄位與上傳檔案。
            form = await request.ReadFormAsync(token);
        }
        catch (InvalidDataException)
        {
            // 表單內容格式錯誤時，統一轉成 Upload 驗證錯誤。
            throw InvalidFile();
        }
        catch (BadHttpRequestException)
        {
            // HTTP Request 無法正常解析時，統一轉成 Upload 驗證錯誤。
            throw InvalidFile();
        }

        // 取得 workflow 欄位。
        var workflow = form["workflow"];
        // workflow 必須只有一個值，且必須完全符合 WorkflowType 的 enum 名稱。
        if (workflow.Count != 1 || !Enum.GetNames<WorkflowType>().Contains(workflow[0], StringComparer.Ordinal))
            throw new UploadValidationException("INVALID_WORKFLOW", "The specified workflow is not supported.");

        // 只取得欄位名稱為 files 的上傳檔案，並將每個 IFormFile 包裝成 FormUploadFile。
        var files = form.Files.GetFiles("files").Select(file => new FormUploadFile(file)).ToArray();

        // 將 workflow 字串轉成 WorkflowType enum，再建立 UploadHttpRequest。
        return new(files, Enum.Parse<WorkflowType>(workflow[0]!));
    }

    // 建立統一的無效檔案驗證例外。
    private static UploadValidationException InvalidFile() =>
        new("INVALID_FILE", "The uploaded file is invalid.");

    // 釋放本次 Request 建立的所有檔案 adapter。
    public void Dispose()
    {
        foreach (var file in files) file.Dispose();
    }
}
