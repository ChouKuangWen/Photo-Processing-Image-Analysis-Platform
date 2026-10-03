using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using PhotoPlatform.Api;
using PhotoPlatform.Api.Contracts;
using PhotoPlatform.Api.Controllers;
using PhotoPlatform.Api.Http;
using PhotoPlatform.Application.DTOs;
using PhotoPlatform.Application.Exceptions;
using PhotoPlatform.Application.Interfaces;
using PhotoPlatform.Domain.Enums;
using PhotoPlatform.Infrastructure.Persistence;
using PhotoPlatform.UnitTests.TestDoubles;

namespace PhotoPlatform.UnitTests.Api;

// 不啟動 HTTP Server，也不連 SQL Server。
// 直接測試 API 層的錯誤映射、Controller、資源釋放與 DI 註冊。
public sealed class UploadApiTests
{
    [Fact]
    // 驗證 BATCH_NOT_FOUND 可以透過共用 ApiErrors 產生固定的 404、安全訊息與 TraceId。
    // 這只是測試錯誤回應工具，不代表目前已有 Batch 查詢 API。
    public void BatchNotFound_CommonResponseIsReusableWithoutAddingQueryEndpoint()
    {
        Assert.Equal(404, ApiErrors.StatusCode("BATCH_NOT_FOUND"));
        var response = ApiErrors.Create("BATCH_NOT_FOUND", "query-trace");
        Assert.False(response.Success);
        Assert.Equal("BATCH_NOT_FOUND", response.Error.Code);
        Assert.Equal("Batch was not found.", response.Error.Message);
        Assert.Equal("query-trace", response.Error.TraceId);
    }

    [Theory]
    [InlineData("INVALID_FILE")]
    [InlineData("UNSUPPORTED_FORMAT")]
    [InlineData("FILE_TOO_LARGE")]
    [InlineData("INVALID_WORKFLOW")]
    // 驗證允許對外公開的 Validation Error，
    // 即使原始 Exception Message 含敏感資訊，
    // HTTP Response 仍只能使用系統預先定義的安全訊息。
    public Task Validation_UsesControlledMessage(string code) =>
        AssertError(new UploadValidationException(code, "secret SQL path"), 400, code);

    [Theory]
    [InlineData(UploadFailureStage.StorageSave, "STORAGE_ERROR")]
    [InlineData(UploadFailureStage.DatabaseBegin, "INTERNAL_ERROR")]
    [InlineData(UploadFailureStage.DatabaseSave, "INTERNAL_ERROR")]
    [InlineData(UploadFailureStage.DatabaseCommit, "INTERNAL_ERROR")]
    [InlineData(UploadFailureStage.QueueEnqueue, "INTERNAL_ERROR")]
    [InlineData(UploadFailureStage.TransactionDispose, "INTERNAL_ERROR")]
    [InlineData(UploadFailureStage.Unexpected, "INTERNAL_ERROR")]

    // 驗證 UploadFailureException 的 HTTP Error Code
    // 是依 Application 提供的 Failure 分類決定，
    // 而不是單純根據 InnerException 的 CLR 型別判斷。
    public Task Failure_UsesStageRatherThanInnerExceptionType(UploadFailureStage stage, string code) =>
        AssertError(new UploadFailureException(stage, new IOException("secret SQL path")), 500, code);

    [Fact]
    // 驗證沒有經 Application 分類的一般 IOException
    // 不應自行推定成 Storage failure，而是統一當成 INTERNAL_ERROR。
    public Task UnclassifiedIOException_IsInternal() =>
        AssertError(new IOException("secret SQL path"), 500, "INTERNAL_ERROR");

    [Fact]
    // 驗證未知的 Validation ErrorCode 不得直接回傳給 Client。
    // 只有白名單中的 Validation Code 可以公開，其他一律轉成 INTERNAL_ERROR。
    public Task UnapprovedValidationCode_IsNotExposed() =>
        AssertError(new UploadValidationException("secret SQL path", "secret SQL path"), 500, "INTERNAL_ERROR");

    // 共用錯誤驗證 Helper。
    // 直接建立 HttpContext 並呼叫 ApiExceptionMiddleware，
    // 同時檢查 HTTP Response 與結構化 Log，確認錯誤映射正確且敏感資訊沒有外洩。
    private static async Task AssertError(Exception exception, int status, string code)
    {
        // 建立測試用 Logger。
        // 用來記錄 ApiExceptionMiddleware 寫出的結構化 log，方便後面驗證是否有洩漏敏感資訊。
        var logger = new RecordingLogger<ApiExceptionMiddleware>();

        // 建立假的 HttpContext。
        // TraceIdentifier 固定成 known-trace，後面可以確認 Middleware 有沒有把同一個 TraceId 放進 Response 與 Log。
        var context = new DefaultHttpContext { TraceIdentifier = "known-trace" };

        // 建立最基本的 DI ServiceProvider，提供 Middleware 在處理 Response 時可能需要的 ASP.NET Core 服務。
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();

        // 把上面的 ServiceProvider 掛到這個假的 HttpContext。
        context.RequestServices = services;

        // 把 Response Body 換成 MemoryStream。
        // 這樣 Middleware 寫出的 JSON 不會真的送到網路，而是留在記憶體裡，方便測試讀回來驗證。
        context.Response.Body = new MemoryStream();

        // 直接建立並執行 ApiExceptionMiddleware。
        // _ => throw exception
        // 代表假的下一層 Middleware / Controller 一執行就故意丟出 exception。
        // 這樣可以直接測 ApiExceptionMiddleware是否能正確捕捉並轉換指定的 Exception。
        await new ApiExceptionMiddleware(_ => throw exception, logger).InvokeAsync(context);

        // 驗證 Middleware 最後設定的 HTTP Status Code 是否符合預期。
        Assert.Equal(status, context.Response.StatusCode);

        // Middleware 已經把 JSON 寫進 MemoryStream，此時 Position 在串流尾端。
        // 重設成 0，才能從頭讀取 Response Body。
        context.Response.Body.Position = 0;

        // 將 Middleware 寫出的 JSON Response 解析成 JsonDocument。
        using var json = await JsonDocument.ParseAsync(context.Response.Body);

        // 錯誤 Response 的 success 必須是 false。
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());

        var error = json.RootElement.GetProperty("error");
        // 驗證 Middleware 映射出的 Error Code 是否正確。
        Assert.Equal(code, error.GetProperty("code").GetString());

        // 確認 Response 使用原本 HttpContext 的 TraceId。
        Assert.Equal("known-trace", error.GetProperty("traceId").GetString());

        // Response 不得包含原始 Exception 中的敏感文字。
        Assert.DoesNotContain("secret", json.RootElement.GetRawText());

        // Middleware 這次應該只產生一筆 Log。
        var entry = Assert.Single(logger.Entries);

        // Log 不應直接保存原始 Exception，避免 StackTrace、路徑或其他敏感資訊被記錄。
        Assert.Null(entry.Exception);

        // Log Message 也不能包含敏感文字。
        Assert.DoesNotContain("secret", entry.Message);

        // Log 裡的 TraceId 必須和 Response / HttpContext 相同。
        Assert.Equal("known-trace", entry.Fields["TraceId"]);

        // Log 不應額外暴露內部 FailureStage。
        Assert.DoesNotContain("FailureStage", entry.Fields.Keys);
    }

    [Fact]
    // 如果 Request 本身沒有 TraceId，Middleware 必須自動建立新的 TraceId，
    // 讓後續 Response 與 Log 可以互相追蹤。
    public async Task MissingTraceId_IsCreated()
    {
        var context = new DefaultHttpContext { TraceIdentifier = "" };
        var logger = new RecordingLogger<ApiExceptionMiddleware>();
        await new ApiExceptionMiddleware(_ => Task.CompletedTask, logger).InvokeAsync(context);
        Assert.False(string.IsNullOrWhiteSpace(context.TraceIdentifier));
    }

    [Fact]
    // Request 已被 Client 取消時，OperationCanceledException 不應被當成一般 API Error。
    // Middleware 不應另外寫入錯誤 JSON，也不應記錄一般錯誤分類 Log。
    public async Task RequestCancellation_DoesNotBecomeValidationOrCustomStatus()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var context = new DefaultHttpContext { RequestAborted = cancellation.Token };
        context.Response.Body = new MemoryStream();
        var logger = new RecordingLogger<ApiExceptionMiddleware>();
        await new ApiExceptionMiddleware(_ => throw new OperationCanceledException(cancellation.Token), logger)
            .InvokeAsync(context);
        Assert.Equal(0, context.Response.Body.Length);
        Assert.Empty(logger.Entries);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    // 直接呼叫 Controller，驗證 multipart 資料會正確轉成 Application 的 UploadRequest，
    // CancellationToken 會往下傳，成功結果會包裝成 202 ApiSuccessResponse。
    // 同時驗證 FormUploadFile 開啟的 Stream會在 Controller action 結束後被正確 Dispose。
    public async Task Controller_MapsFilesAndForwardsToken(int count)
    {
        // 建立測試用 CancellationTokenSource，
        // 等一下用來確認 Controller 有把同一個 token 傳給 Service。
        using var cancellation = new CancellationTokenSource();

        // 建立假的 JPEG 內容。
        // FF D8 FF 是測試用 JPEG signature。
        using var content = new MemoryStream([0xff, 0xd8, 0xff]);

        // 建立 ASP.NET Core 的上傳檔案集合。
        var files = new FormFileCollection();

        // 根據 count 建立 1 或 3 個 FormFile。
        for (var i = 0; i < count; i++)
            files.Add(new FormFile(content, 0, content.Length, "files", $"photo{i}.jpg")
            { Headers = new HeaderDictionary(), ContentType = "image/jpeg" });

        // 建立假的 HttpContext，模擬 Controller 收到的 HTTP Request。
        var context = new DefaultHttpContext();

        // 設定成 multipart/form-data。
        context.Request.ContentType = "multipart/form-data; boundary=test";

        // 手動放入 workflow 與 files，模擬解析完成的 multipart 表單。
        context.Request.Form = new FormCollection(new Dictionary<string, StringValues> { ["workflow"] = "Full" }, files);

        // 用來確認 Service 是否真的有被呼叫。
        var called = false;

       // 記錄 Service 開啟過的 Stream，最後確認 Controller action 結束後有被 Dispose。
        var opened = new List<Stream>();
        var service = new StubUpload(async (request, token) =>
        {
            called = true;
            Assert.Equal(cancellation.Token, token);
            Assert.Equal(WorkflowType.Full, request.Workflow);
            Assert.Equal(count, request.Files.Count);
            foreach (var file in request.Files)
            {
                Assert.Equal("image/jpeg", file.MimeType);
                Assert.Equal(3, file.Length);
                var stream = file.OpenReadStream();
                opened.Add(stream);
                Assert.Equal(0xff, stream.ReadByte());
                Assert.Equal(0xff, file.OpenReadStream().ReadByte());
            }
            await Task.Yield();
            return new(Guid.NewGuid(), count, "Pending");
        });

        // 建立 Controller，
        // 並把前面準備好的 HttpContext 放進 ControllerContext。
        var controller = new UploadController(service) { ControllerContext = new ControllerContext { HttpContext = context } };

        // 直接呼叫 Controller.Upload()。
        // 預期成功時回 AcceptedResult。
        var accepted = Assert.IsType<AcceptedResult>(await controller.Upload(cancellation.Token));

        // 確認 Stub Service 有被執行。
        Assert.True(called);

        Assert.Equal(202, accepted.StatusCode);

        // 確認 Controller 回傳的是：ApiSuccessResponse<UploadResult>
        var envelope = Assert.IsType<ApiSuccessResponse<UploadResult>>(accepted.Value);
        Assert.True(envelope.Success);
        Assert.Equal(count, envelope.Data.TotalCount);
        Assert.Equal("Pending", envelope.Data.Status);

        // FormUploadFile Dispose 後，不應該把 ASP.NET Core 原本擁有的底層 content 一起關掉。
        Assert.True(content.CanRead);

        // 但由 FormUploadFile 自己開出去的 Stream，在 Controller action 結束後必須全部被 Dispose。
        Assert.All(opened, stream => Assert.Throws<ObjectDisposedException>(() => stream.ReadByte()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("unknown")]
    [InlineData("0")]
    [InlineData("Naming,Full")]
    // 在轉成 enum 前拒絕缺值、空白與非法表示法，避免誤用預設 workflow。
    public async Task InvalidWorkflow_IsRejectedBeforeService(string? workflow)
    {
        // 建立假的 HttpContext。
        var context = new DefaultHttpContext();

        // 告訴 Request：這是一個 multipart/form-data。
        context.Request.ContentType = "multipart/form-data; boundary=test";
        context.Request.Form = new FormCollection(workflow is null ? new() : new() { ["workflow"] = workflow });
        var error = await Assert.ThrowsAsync<UploadValidationException>(() => UploadHttpRequest.ReadAsync(context.Request, default));
        Assert.Equal("INVALID_WORKFLOW", error.ErrorCode);
    }

    [Fact]
    // 建立兩個 DI Scope，不真的連 SQL Server。
    // 驗證 Scoped 服務彼此隔離，Singleton Queue 則跨 Scope 共用。
    public void Composition_IsolatesPersistenceAndSharesQueue()
    {
        // 建立測試用 Configuration。
        // 這裡只是讓 AddUploadApi() 有完整設定可以註冊服務，
        // 測試本身不會真的執行 SQL 查詢。
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:PhotoPlatform"] = "Server=localhost;Database=CompositionTest;Integrated Security=true;TrustServerCertificate=true",
            ["Upload:StorageRoot"] = Path.GetTempPath(),
            ["Upload:MaxFileSizeBytes"] = "1024"
        }).Build();

        // 建立空的 DI ServiceCollection。
        var services = new ServiceCollection();

        // 加入 Logging。
        services.AddLogging();

        // 呼叫正式的 AddUploadApi()，
        // 把專案真正使用的 Upload 相關服務全部註冊進 DI。
        services.AddUploadApi(configuration);

        // 根據上面的註冊建立 ServiceProvider。
        // ValidateScopes = true 會幫忙檢查 Scoped / Singleton 的生命週期使用是否有明顯錯誤。
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        // 建立第一個 DI Scope。
        // 可以把它想成第一個 HTTP Request 的生命週期。
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        // PhotoPlatformDbContext 是 Scoped。
        // 所以不同 Scope 取得的 DbContext
        // 必須是不同 instance。
        Assert.NotSame(first.ServiceProvider.GetRequiredService<PhotoPlatformDbContext>(),
            second.ServiceProvider.GetRequiredService<PhotoPlatformDbContext>());

        // IUploadPersistence 也是 Scoped。
        // 不同 Request 不應共用同一個 Persistence instance。
        Assert.NotSame(first.ServiceProvider.GetRequiredService<IUploadPersistence>(),
            second.ServiceProvider.GetRequiredService<IUploadPersistence>());

        // IProcessingQueue 是 Singleton。
        // 所以不管從哪個 Scope 取得，都應該是同一個 Queue instance。
        Assert.Same(first.ServiceProvider.GetRequiredService<IProcessingQueue>(),
            second.ServiceProvider.GetRequiredService<IProcessingQueue>());

        // 最後確認 IUploadService 可以正常從 DI 解析出來，
        // 代表 AddUploadApi() 的主要依賴註冊是完整的。
        Assert.NotNull(first.ServiceProvider.GetRequiredService<IUploadService>());
    }

    // 注入測試行為，單獨觀察 HTTP 層責任，不模擬完整上傳交易。
    private sealed class StubUpload(Func<UploadRequest, CancellationToken, Task<UploadResult>> execute) : IUploadService
    {
        public Task<UploadResult> UploadAsync(UploadRequest request, CancellationToken token) => execute(request, token);
    }
}
