namespace PhotoPlatform.Application.Interfaces;

public interface IFileStorageService
{
    // 回傳儲存位置，供 Image.StoredPath 使用。
    Task<string> SaveAsync(
        IUploadFile file,
        CancellationToken cancellationToken);

    Task DeleteAsync(
        string storedPath,
        CancellationToken cancellationToken);
}
