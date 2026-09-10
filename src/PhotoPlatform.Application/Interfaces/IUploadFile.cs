namespace PhotoPlatform.Application.Interfaces;

public interface IUploadFile
{
    string FileName { get; }
    string MimeType { get; }
    long Length { get; }

    // 回傳的 Stream 與檔案來源生命週期由呼叫端負責。
    // Application Service 不得釋放由呼叫端擁有的資源。
    Stream OpenReadStream();
}
