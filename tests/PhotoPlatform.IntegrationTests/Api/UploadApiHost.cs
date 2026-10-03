using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PhotoPlatform.Api;

namespace PhotoPlatform.IntegrationTests.Api;

// 啟動測試用的 Kestrel Server，讓 Integration Test 透過真實 HTTP 呼叫 API。
internal sealed class UploadApiHost(WebApplication app, HttpClient client) : IAsyncDisposable
{
    public HttpClient Client { get; } = client;

    // 測試可傳入自己的 DI 與 Configuration，不影響正式 API。
    public static async Task<UploadApiHost> StartAsync(Action<IServiceCollection> configure,
        Dictionary<string, string?>? settings = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });

        // 只監聽本機，並由系統自動分配 Port，避免測試互相衝突。
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        // 關閉測試 Host 的預設日誌輸出。
        builder.Logging.ClearProviders();

        // 將測試提供的設定加入 Configuration。
        builder.Configuration.AddInMemoryCollection(settings ?? []);

        // 使用正式 API 的 DI 設定。
        builder.Services.AddUploadApi(builder.Configuration);

        // 允許個別測試增加或替換 Service。
        configure(builder.Services);
        var app = builder.Build();

        // 使用與正式 API 相同的 Middleware 與 Controller 路由。
        app.UseUploadApi();
        try
        {
            await app.StartAsync();
            return new(app, new HttpClient { BaseAddress = new Uri(app.Urls.Single()), Timeout = TimeSpan.FromSeconds(15) });
        }
        // 啟動失敗仍釋放 host，避免資源殘留影響後續。
        catch
        {
            await app.DisposeAsync();
            throw;
        }
    }

    // 測試結束後關閉 Server，並釋放 HttpClient 與 Host。
    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await app.StopAsync();
        await app.DisposeAsync();
    }
}
