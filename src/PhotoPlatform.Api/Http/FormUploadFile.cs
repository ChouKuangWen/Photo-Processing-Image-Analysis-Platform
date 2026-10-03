using PhotoPlatform.Application.Interfaces;

namespace PhotoPlatform.Api.Http;

// 將 ASP.NET Core 的 IFormFile 包裝成 Application 層使用的 IUploadFile，
// 避免 Application 直接依賴 HTTP Framework 型別。
public sealed class FormUploadFile(IFormFile file) : IUploadFile, IDisposable
{
    // 記住這個檔案曾開啟過的所有讀取 Stream，方便最後統一釋放。
    private readonly List<Stream> streams = [];

    // 將 IFormFile 的基本檔案資訊提供給 Application 層。
    public string FileName => file.FileName;
    public string MimeType => file.ContentType;
    public long Length => file.Length;

    // 每次呼叫都開啟一個新的讀取 Stream，
    // 讓 Validation、Storage 可以各自從檔案起點讀取完整內容。
    public Stream OpenReadStream()
    {
        var stream = file.OpenReadStream();
        // 保存 Stream，之後由 Dispose 統一關閉。
        streams.Add(stream);
        return stream;
    }

    // 釋放這個 adapter 曾經開啟的所有檔案讀取 Stream。
    public void Dispose()
    {
        foreach (var stream in streams) stream.Dispose();
        streams.Clear();
    }
}
