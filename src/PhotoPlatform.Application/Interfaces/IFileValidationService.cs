using PhotoPlatform.Application.DTOs;

namespace PhotoPlatform.Application.Interfaces;

public interface IFileValidationService
{
    Task<FileValidationResult> ValidateAsync(
        IUploadFile file,
        CancellationToken cancellationToken);
}
