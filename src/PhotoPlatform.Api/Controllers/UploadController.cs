using Microsoft.AspNetCore.Mvc;
using PhotoPlatform.Api.Contracts;
using PhotoPlatform.Api.Http;
using PhotoPlatform.Application.DTOs;
using PhotoPlatform.Application.Interfaces;

namespace PhotoPlatform.Api.Controllers;

// 接收圖片上傳請求，呼叫 UploadService，最後回傳 HTTP 結果。
[Route("api/v1/images")]
public sealed class UploadController(IUploadService upload) : ControllerBase
{
    [HttpPost("upload")]
    // 關閉 ASP.NET Core 的 Request 大小限制。
    // 實際檔案大小會在表單解析後由 FileValidationService 驗證。
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = long.MaxValue)]
    public async Task<IActionResult> Upload(CancellationToken cancellationToken)
    {
        // 讀取 multipart/form-data，轉成 Application 層可使用的上傳資料。
        // using 會在方法結束時釋放其中開啟的檔案串流。
        using var input = await UploadHttpRequest.ReadAsync(Request, cancellationToken);

        // 呼叫 Application 層的 UploadService 執行實際上傳流程。
        var result = await upload.UploadAsync(input.Request, cancellationToken);

        // 回傳 HTTP 202，表示上傳工作已接受，不代表後續背景影像處理已完成。
        return Accepted(new ApiSuccessResponse<UploadResult>(true, result));
    }
}
