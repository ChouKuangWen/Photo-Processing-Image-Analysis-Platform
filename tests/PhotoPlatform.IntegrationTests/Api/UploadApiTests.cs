using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PhotoPlatform.Application.DTOs;
using PhotoPlatform.Application.Exceptions;
using PhotoPlatform.Application.Interfaces;
using PhotoPlatform.Application.Services;
using PhotoPlatform.Domain.Enums;

namespace PhotoPlatform.IntegrationTests.Api;

// 使用真實 HTTP 測試 Upload API 的路由、multipart、回應格式與取消流程。
public sealed class UploadApiTests
{
    [Theory]
    [InlineData(1, "Naming")]
    [InlineData(3, "Analysis")]
    [InlineData(1, "DuplicateDetection")]
    [InlineData(2, "Full")]
    // 測試單檔、多檔與不同 workflow，並確認成功時會回傳正確的 202 與 response envelope。
    public async Task MultipartUpload_ReturnsAcceptedEnvelope(int count, string workflow)
    {
        var batchId = Guid.NewGuid();
        await using var host = await Start(async (request, token) =>
        {
            // 確認 HTTP workflow 已正確轉成 WorkflowType。
            Assert.Equal(Enum.Parse<WorkflowType>(workflow), request.Workflow);
            // 確認檔案數量正確傳到 Application 層。
            Assert.Equal(count, request.Files.Count);
            // 確認 HTTP Request 的 cancellation token 有往下傳。
            Assert.True(token.CanBeCanceled);

            var validator = new FileValidationService(1024);
            foreach (var file in request.Files)
            {
                // 驗證測試檔案可以通過正式 FileValidationService。
                Assert.True((await validator.ValidateAsync(file, token)).IsValid);
                // 確認檔案內容可以被 Application 層正常讀取。
                Assert.Equal(0xff, file.OpenReadStream().ReadByte());
            }
            // Stub Application 回傳固定 UploadResult。
            return new(batchId, count, "Pending");
        });
        using var body = Multipart(count, workflow);
        using var response = await host.Client.PostAsync("/api/v1/images/upload", body);

        // Upload API 成功後應回傳 202 Accepted。
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        
        // 成功 response 必須維持固定 envelope。
        Assert.Equal(new[] { "success", "data" }, json.RootElement.EnumerateObject().Select(x => x.Name));
        Assert.True(json.RootElement.GetProperty("success").GetBoolean());
        var data = json.RootElement.GetProperty("data");
        Assert.Equal(batchId, data.GetProperty("batchId").GetGuid());
        Assert.Equal(count, data.GetProperty("totalCount").GetInt32());
        Assert.Equal("Pending", data.GetProperty("status").GetString());
    }

    [Theory]
    [InlineData(0, "photo.jpg", "image/jpeg", 3, "INVALID_FILE")]  // 沒有任何檔案。
    [InlineData(1, "photo.exe", "image/jpeg", 3, "UNSUPPORTED_FORMAT")]  // 副檔名錯誤：exe。
    [InlineData(1, "photo.jpg", "image/png", 3, "INVALID_FILE")]  // jpg 副檔名，但是 MIME 是 image/png。
    [InlineData(1, "photo.jpg", "image/jpeg", 0, "INVALID_FILE")]  // 空檔案。
    [InlineData(1, "photo.jpg", "image/jpeg", 2048, "FILE_TOO_LARGE")]  // 檔案大小 2048 bytes，但測試設定最大只有 1024 bytes。
    // 使用真實 UploadService 與 FileValidationService，確認常見檔案驗證錯誤會回傳正確的 HTTP 400 與錯誤代碼。
    public async Task Validation_ReturnsApprovedEnvelope(int count, string name, string mime, int length, string code)
    {
        await using var host = await StartValidation();
        using var body = Multipart(count, "Full", name, mime, length);
        using var response = await host.Client.PostAsync("/api/v1/images/upload", body);
        await AssertError(response, HttpStatusCode.BadRequest, code);
    }

    [Fact]
    // 即使副檔名與 MIME 正確，檔案內容 signature 不符仍必須被拒絕。
    public async Task InvalidSignature_ReturnsInvalidFile()
    {
        await using var host = await StartValidation();
        using var body = new MultipartFormDataContent();
        body.Add(new StringContent("Full"), "workflow");

        // 建立內容 signature 不合法的 JPEG 測試資料。
        var file = new ByteArrayContent([1, 2, 3]);
        file.Headers.ContentType = new("image/jpeg");
        body.Add(file, "files", "bad.jpg");
        using var response = await host.Client.PostAsync("/api/v1/images/upload", body);
        await AssertError(response, HttpStatusCode.BadRequest, "INVALID_FILE");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("unknown")]
    [InlineData("0")]
    // workflow 不合法時應直接在 HTTP 層拒絕，不應呼叫 Application Service。
    public async Task InvalidWorkflow_DoesNotCallApplication(string? workflow)
    {
        var calls = 0;
        await using var host = await Start((_, _) => { calls++; throw new Exception("must not run"); });
        using var body = Multipart(1, workflow);
        using var response = await host.Client.PostAsync("/api/v1/images/upload", body);
        await AssertError(response, HttpStatusCode.BadRequest, "INVALID_WORKFLOW");
        // Application 不應被呼叫。
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData("text/plain", "arbitrary body")]  // 根本不是 multipart/form-data。
    [InlineData("multipart/form-data", "missing boundary")]  // 宣告 multipart/form-data，但是沒有 boundary。
    [InlineData("multipart/form-data; boundary=abc", "--abc\r\ninvalid header")]// 有 boundary，但 multipart 內容格式本身是壞的。
    // Content-Type 或 multipart 格式錯誤時，也必須回傳自訂錯誤格式，而不是 ASP.NET Core 預設錯誤。
    public async Task BindingFailure_UsesCustomEnvelope(string contentType, string bodyText)
    {
        await using var host = await Start((_, _) => throw new Exception("must not run"));
        using var body = new StringContent(bodyText);
        body.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        using var response = await host.Client.PostAsync("/api/v1/images/upload", body);
        await AssertError(response, HttpStatusCode.BadRequest, "INVALID_FILE");
    }

    [Theory]
    [InlineData(UploadFailureStage.StorageSave, "STORAGE_ERROR")]  // StorageSave 屬於 Storage Error。
    [InlineData(UploadFailureStage.DatabaseSave, "INTERNAL_ERROR")]  // DB 錯誤對外統一隱藏成 INTERNAL_ERROR。
    [InlineData(UploadFailureStage.QueueEnqueue, "INTERNAL_ERROR")]  // Queue 錯誤也是 INTERNAL_ERROR。
    [InlineData(UploadFailureStage.Unexpected, "INTERNAL_ERROR")]  // 未預期錯誤也是 INTERNAL_ERROR。
    // 模擬 Application 已分類的失敗，確認 HTTP 錯誤映射正確且不洩漏原始例外資訊。
    public async Task ServerFailure_IsSanitized(UploadFailureStage stage, string code)
    {
        await using var host = await Start((_, _) =>
            throw new UploadFailureException(stage, new IOException("secret SQL StackTrace C:\\private")));
        using var body = Multipart(1, "Full");
        using var response = await host.Client.PostAsync("/api/v1/images/upload", body);
        await AssertError(response, HttpStatusCode.InternalServerError, code);
    }

    [Fact]
    // 未分類的 Exception 應統一轉成 INTERNAL_ERROR，且不能將原始例外內容回傳給 Client。
    public async Task UnexpectedException_IsSanitized()
    {
        await using var host = await Start((_, _) => throw new InvalidOperationException("secret SQL StackTrace"));
        using var body = Multipart(1, "Full");
        using var response = await host.Client.PostAsync("/api/v1/images/upload", body);
        await AssertError(response, HttpStatusCode.InternalServerError, "INTERNAL_ERROR");
    }

    [Fact]
    // Client 中斷 Request 時，cancellation token 必須傳到 Application 層。
    public async Task ClientDisconnect_CancelsApplicationToken()
    {
        // entered：用來標記「Application 已經真的收到 Request」。
        // 如果沒有這個標記，測試可能在 Request 還沒進到 Application 前就先 Cancel，
        // 那就無法證明 CancellationToken 有真的傳到 Application。
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // canceled：用來標記「Application 裡面的 token 已經真的被取消」。
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var host = await Start(async (_, token) =>
        {
            // 表示 Application 已經收到 Request。
            entered.SetResult();
            try
            {
                // 一直等待，直到 Request cancellation token 被取消。
                await Task.Delay(Timeout.Infinite, token);
            }
            catch (OperationCanceledException)
            {
                canceled.TrySetResult(); throw;
            }
            throw new InvalidOperationException("Unreachable.");
        });

        // 建立一個 CancellationTokenSource。
        // 等一下測試會主動呼叫 cancellation.Cancel()，模擬 Client 中斷 HTTP Request。
        using var cancellation = new CancellationTokenSource();
        using var body = Multipart(1, "Full");
        var request = host.Client.PostAsync("/api/v1/images/upload", body, cancellation.Token);

        // 先確認 Request 已經進入 Application。
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // 模擬 Client 取消 Request。
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);

        // 確認取消訊號確實傳到 Application token。
        await canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    // workflow 欄位出現多次時應拒絕 Request，避免任意選其中一個值。
    public async Task RepeatedWorkflow_IsRejected()
    {
        await using var host = await Start((_, _) => throw new Exception("must not run"));
        using var body = Multipart(1, "Full");

        // 故意加入第二個 workflow。
        body.Add(new StringContent("Naming"), "workflow");
        using var response = await host.Client.PostAsync("/api/v1/images/upload", body);
        await AssertError(response, HttpStatusCode.BadRequest, "INVALID_WORKFLOW");
    }

    [Fact]
    // 檔案欄位名稱不是 files 時，不應被視為正式的上傳檔案。
    public async Task WrongFileField_IsNotAcceptedAsFiles()
    {
        await using var host = await StartValidation();
        using var body = new MultipartFormDataContent();
        body.Add(new StringContent("Full"), "workflow");
        var file = new ByteArrayContent([0xff, 0xd8, 0xff]);
        file.Headers.ContentType = new("image/jpeg");

        // 故意使用錯誤的欄位名稱 other。
        body.Add(file, "other", "photo.jpg");
        using var response = await host.Client.PostAsync("/api/v1/images/upload", body);
        await AssertError(response, HttpStatusCode.BadRequest, "INVALID_FILE");
    }

    // 使用 StubUpload 替換正式 UploadService，讓測試只關注 HTTP 層行為，不進入 DB 或 Storage。
    private static Task<UploadApiHost> Start(Func<UploadRequest, CancellationToken, Task<UploadResult>> execute) =>
        UploadApiHost.StartAsync(services =>
        {
            // 移除正式 UploadService。
            services.RemoveAll<IUploadService>();
            // 加入測試用 StubUpload。
            services.AddSingleton<IUploadService>(new StubUpload(execute));
        });

    // 使用真實 UploadService 與 FileValidationService。
    // 如果驗證流程意外進入 Persistence，測試應因無法連線 DB 而失敗。
    private static Task<UploadApiHost> StartValidation() => UploadApiHost.StartAsync(_ => { }, new()
    {
        // 故意提供不可連線的 SQL 位址，確保 Validation failure 不應進入資料庫流程。
        ["ConnectionStrings:PhotoPlatform"] = "Server=127.0.0.1,1;Database=MustNotConnect;Integrated Security=true;Connect Timeout=1",
        // Validation 測試不應真正寫入 Storage，但正式 UploadApiComposition 仍需要這個設定。
        ["Upload:StorageRoot"] = Path.GetTempPath(),
        // 測試檔案大小上限為 1024 bytes。
        ["Upload:MaxFileSizeBytes"] = "1024"
    });

    // 建立測試用 multipart/form-data，包含 workflow 與指定數量的 files。
    internal static MultipartFormDataContent Multipart(int count, string? workflow, string name = "photo.jpg",
        string mime = "image/jpeg", int length = 3)
    {
        var body = new MultipartFormDataContent();
        // workflow 為 null 時不加入欄位，用來測試缺少 workflow 的情境。
        if (workflow is not null) body.Add(new StringContent(workflow), "workflow");
        for (var i = 0; i < count; i++)
        {
            var bytes = new byte[length];
            // 建立最基本的 JPEG signature。
            if (length >= 3) { bytes[0] = 0xff; bytes[1] = 0xd8; bytes[2] = 0xff; }
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new(mime);
            body.Add(file, "files", name);
        }
        return body;
    }

    // 驗證錯誤 Status Code、JSON envelope、ErrorCode 與 TraceId，
    // 並確認原始例外中的敏感資訊不會出現在 Response。
    private static async Task AssertError(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        var text = await response.Content.ReadAsStringAsync();

        // 原始 Exception 內容不應直接回傳。
        Assert.DoesNotContain("secret", text);
        Assert.DoesNotContain("StackTrace", text);
        using var json = JsonDocument.Parse(text);

        // Error response 必須使用固定 envelope。
        Assert.Equal(new[] { "success", "error" }, json.RootElement.EnumerateObject().Select(x => x.Name));
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        var error = json.RootElement.GetProperty("error");
        Assert.Equal(new[] { "code", "message", "traceId" }, error.EnumerateObject().Select(x => x.Name));
        Assert.Equal(code, error.GetProperty("code").GetString());

        // 每個錯誤 response 都必須包含 TraceId。
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("traceId").GetString()));
    }

    // 測試用 UploadService。不實作真正 Upload 流程，只執行測試案例傳入的 delegate。
    private sealed class StubUpload(Func<UploadRequest, CancellationToken, Task<UploadResult>> execute) : IUploadService
    {
        public Task<UploadResult> UploadAsync(UploadRequest request, CancellationToken token) => execute(request, token);
    }
}
