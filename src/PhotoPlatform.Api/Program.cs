using PhotoPlatform.Api;

var builder = WebApplication.CreateBuilder(args);

// 集中註冊 Upload 所需的服務與設定，包含驗證、儲存、資料庫及共用 Queue。
builder.Services.AddUploadApi(builder.Configuration);
var app = builder.Build();

// 加入共用例外處理 Middleware 並對應 Controller 路由，
// 讓 Controller 執行時拋出的例外交由 Middleware 統一處理。
app.UseUploadApi();

// 啟動應用程式，開始接收 HTTP 請求。
app.Run();
