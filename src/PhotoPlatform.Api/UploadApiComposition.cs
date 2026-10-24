using Microsoft.EntityFrameworkCore;
using PhotoPlatform.Api.Controllers;
using PhotoPlatform.Api.Http;
using PhotoPlatform.Application.Interfaces;
using PhotoPlatform.Application.Services;
using PhotoPlatform.Infrastructure.Persistence;
using PhotoPlatform.Infrastructure.Processing;
using PhotoPlatform.Infrastructure.Storage;

namespace PhotoPlatform.Api;


// 註冊 Upload API 需要的服務，集中設定 Upload API 所需的 DI 與 HTTP pipeline。
public static class UploadApiComposition
{
    public static IServiceCollection AddUploadApi(this IServiceCollection services, IConfiguration configuration)
    {
        // 關閉 EF Core 日誌，避免 SQL 與資料庫診斷資訊出現在 API 日誌中。
        // 注意：EF Core 的警告與錯誤也會一起被關閉。
        services.AddLogging(logging => logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.None));

        // 啟用 Controller，並加入 UploadController 所在的組件。
        services.AddControllers().AddApplicationPart(typeof(UploadController).Assembly);

        // UploadService 與 Persistence 使用 Scoped。
        // 同一個 HTTP Request 會共用同一組實例，不同 Request 則彼此隔離。
        services.AddScoped<IUploadService, UploadService>();
        services.AddScoped<IUploadPersistence, UploadPersistence>();

        // 註冊 Batch 狀態查詢服務與唯讀持久化實作，由 DI 依 Interface 注入對應實例。
        services.AddScoped<IBatchStatusQuery, BatchStatusQuery>();
        services.AddScoped<IBatchStatusPersistence, BatchStatusPersistence>();

        // 註冊 EF Core DbContext，並使用設定中的 SQL Server 連線字串。
        services.AddDbContext<PhotoPlatformDbContext>(options => options.UseSqlServer(
            Required(configuration, "ConnectionStrings:PhotoPlatform")));

        // Storage 與檔案驗證不保存 Request 專屬狀態，因此使用 Singleton。
        services.AddSingleton<IFileStorageService>(_ => new LocalFileStorageService(
            Required(configuration, "Upload:StorageRoot")));
        services.AddSingleton<IFileValidationService>(_ => new FileValidationService(
            configuration.GetValue<long>("Upload:MaxFileSizeBytes")));

        // Queue 使用 Singleton，讓所有 Request 與未來的 Worker 共用同一個佇列。
        // 目前只註冊 Queue，本 TASK 不負責啟動背景 Worker。
        var queueOptions = new ProcessingQueueOptions();
        configuration.GetSection("ProcessingQueue").Bind(queueOptions);
        services.AddSingleton<IProcessingQueue>(new ChannelProcessingQueue(queueOptions));
        return services;
    }

    // 正式 API 與測試 Host 共用相同的 Middleware 和 Controller 路由設定。
    public static WebApplication UseUploadApi(this WebApplication app)
    {
        app.UseMiddleware<ApiExceptionMiddleware>();
        app.MapControllers();
        return app;
    }

    // 讀取必填設定；若設定不存在則在啟動階段直接失敗。
    private static string Required(IConfiguration configuration, string key) =>
        !string.IsNullOrWhiteSpace(configuration[key]) ? configuration[key]!
            : throw new InvalidOperationException($"Missing configuration: {key}.");
}
