using PhotoPlatform.Application.DTOs;

namespace PhotoPlatform.Application.Interfaces;

public interface IUploadService
{
    Task<UploadResult> UploadAsync(
        UploadRequest request,
        CancellationToken cancellationToken);
}
