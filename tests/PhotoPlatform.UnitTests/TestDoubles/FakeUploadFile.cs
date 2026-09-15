using PhotoPlatform.Application.Interfaces;

namespace PhotoPlatform.UnitTests.TestDoubles;

// 測試用的 IUploadFile 實作，不需要真正透過 HTTP 上傳檔案。
// internal 限定在測試組件內使用；sealed 表示不允許其他類別繼承。
// openReadStream 是無參數、回傳 Stream 的函式，例如 () => stream，供測試控制資料來源或模擬開啟失敗。
internal sealed class FakeUploadFile(
    string fileName, string mimeType, long length, Func<Stream> openReadStream) : IUploadFile
{
    public string FileName { get; } = fileName;
    public string MimeType { get; } = mimeType;
    // 宣告的檔案大小由測試指定，不會自動調整或檢查 Stream 的實際長度。
    public long Length { get; } = length;

    // 外部只能讀取次數，用來確認前置驗證失敗時，Validator 沒有繼續開啟 Stream。
    public int OpenCount { get; private set; }

    public Stream OpenReadStream()
    {
        // 記錄呼叫次數，包含下方函式拋出例外、未能取得 Stream 的情況。
        OpenCount++;
        // 執行測試提供的函式；Stream 的生命週期由測試管理，此處不負責 Dispose。
        return openReadStream();
    }
}
