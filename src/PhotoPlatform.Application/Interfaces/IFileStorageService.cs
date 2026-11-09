namespace PhotoPlatform.Application.Interfaces;

public interface IFileStorageService
{
    // Reference comes from persisted system data; the caller owns and disposes the returned stream.
    Task<Stream> OpenReadAsync(string storedPath, CancellationToken cancellationToken);

    // 回傳儲存位置，供 Image.StoredPath 使用。
    Task<string> SaveAsync(
        IUploadFile file,
        CancellationToken cancellationToken);

    Task DeleteAsync(
        string storedPath,
        CancellationToken cancellationToken);
}
