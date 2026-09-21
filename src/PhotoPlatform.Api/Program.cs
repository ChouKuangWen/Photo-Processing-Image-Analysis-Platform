var builder = WebApplication.CreateBuilder(args);
// 先建立含預設容量的設定物件，再以組態中的 ProcessingQueue 區段覆寫。
// 可由 appsettings 或環境變數 ProcessingQueue__Capacity 提供容量。
var queueOptions = new PhotoPlatform.Infrastructure.Processing.ProcessingQueueOptions();
builder.Configuration.GetSection("ProcessingQueue").Bind(queueOptions);
// 啟動時建立 Queue 並驗證容量；Singleton 確保同一個應用程式內共用同一個 instance。
// 若 Producer / Consumer 各用不同 Queue，Consumer 就無法取得 Producer 加入的工作。
builder.Services.AddSingleton<PhotoPlatform.Application.Interfaces.IProcessingQueue>(
    new PhotoPlatform.Infrastructure.Processing.ChannelProcessingQueue(queueOptions));
var app = builder.Build();

app.Run();
