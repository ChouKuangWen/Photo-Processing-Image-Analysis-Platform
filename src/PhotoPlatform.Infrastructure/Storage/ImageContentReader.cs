using Microsoft.EntityFrameworkCore;
using PhotoPlatform.Application.Interfaces;
using PhotoPlatform.Application.Processing;
using PhotoPlatform.Infrastructure.Persistence;

namespace PhotoPlatform.Infrastructure.Storage;

public sealed class ImageContentReader : IImageContentReader
{
    private readonly PhotoPlatformDbContext db;
    private readonly IFileStorageService storage;
    private readonly long maximumLength;

    public ImageContentReader(PhotoPlatformDbContext db, IFileStorageService storage, long maximumLength)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumLength);
        this.db = db;
        this.storage = storage;
        this.maximumLength = maximumLength;
    }

    public async Task<ContentHandle> OpenOriginalAsync(long imageId, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(imageId);
        cancellationToken.ThrowIfCancellationRequested();
        var image = await db.Images.AsNoTracking().Where(image => image.Id == imageId)
            .Select(image => new { image.StoredPath, image.FileSize })
            .SingleOrDefaultAsync(cancellationToken);
        if (image is null)
            throw new FileNotFoundException("Original image was not found.");
        if (image.FileSize < 0 || image.FileSize > maximumLength)
            throw new InvalidDataException("Original content exceeds the snapshot limit.");

        // Only the persisted reference reaches storage. Image.SHA256 is not content authority.
        await using var source = await storage.OpenReadAsync(image.StoredPath, cancellationToken);
        return await VerifiedContentSnapshot.CreateAsync(source, image.FileSize, maximumLength,
            expectedContentVersion: null, cancellationToken);
    }
}
