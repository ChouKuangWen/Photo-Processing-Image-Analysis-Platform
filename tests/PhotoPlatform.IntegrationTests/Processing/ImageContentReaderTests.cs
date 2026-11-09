using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using PhotoPlatform.Application.Interfaces;
using PhotoPlatform.Domain.Entities;
using PhotoPlatform.Infrastructure.Storage;
using PhotoPlatform.IntegrationTests.Fixtures;

namespace PhotoPlatform.IntegrationTests.Processing;

public sealed class ImageContentReaderTests : IAsyncLifetime
{
    private readonly ProcessingSchemaFixture fixture = new();
    public Task InitializeAsync() => fixture.InitializeAsync();
    public Task DisposeAsync() => fixture.DisposeAsync();

    [Fact]
    public async Task Read_PersistedReferenceProducesOwnedSnapshotOfActualBytes()
    {
        var imageId = await SeedAsync(3);
        await using var db = fixture.CreateContext();
        var storage = new Storage([1, 2, 3]);
        var reader = new ImageContentReader(db, storage, 100);
        var handle = await reader.OpenOriginalAsync(imageId, default);
        Assert.Equal("original/persisted-reference", storage.Reference);
        Assert.True(storage.LastStream!.Disposed);
        Assert.Equal(3, handle.Length);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(new byte[] { 1, 2, 3 })), handle.ContentVersion);
        storage.Bytes = [4, 5, 6];
        Assert.False(handle.Stream.CanWrite);
        using var consumed = new MemoryStream();
        await handle.Stream.CopyToAsync(consumed);
        Assert.Equal(new byte[] { 1, 2, 3 }, consumed.ToArray());
        await using var reopened = await reader.OpenOriginalAsync(imageId, default);
        Assert.NotEqual(handle.ContentVersion, reopened.ContentVersion);
        // Frozen-version equality remains the caller's admission check; reopening cannot silently prove it.
        await handle.DisposeAsync();
        Assert.Throws<ObjectDisposedException>(() => handle.Stream);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public async Task Read_LengthMismatchReleasesNonSeekableSource(long length)
    {
        var imageId = await SeedAsync(length);
        await using var db = fixture.CreateContext();
        var storage = new Storage([1, 2, 3]);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new ImageContentReader(db, storage, 100).OpenOriginalAsync(imageId, default));
        Assert.True(storage.LastStream!.Disposed);
    }

    [Fact]
    public async Task Read_MissingImageOrOverCapacityNeverOpensStorage()
    {
        var imageId = await SeedAsync(101);
        await using var db = fixture.CreateContext();
        var storage = new Storage([]);
        var reader = new ImageContentReader(db, storage, 100);
        await Assert.ThrowsAsync<FileNotFoundException>(() => reader.OpenOriginalAsync(long.MaxValue, default));
        await Assert.ThrowsAsync<InvalidDataException>(() => reader.OpenOriginalAsync(imageId, default));
        Assert.Null(storage.Reference);
    }

    [Fact]
    public async Task Read_CancellationDuringReadPreservesTokenAndReleasesSource()
    {
        var imageId = await SeedAsync(3);
        await using var db = fixture.CreateContext();
        using var cancellation = new CancellationTokenSource();
        var storage = new Storage([1, 2, 3]) { BeforeRead = cancellation.Cancel };
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ImageContentReader(db, storage, 100).OpenOriginalAsync(imageId, cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.True(storage.LastStream!.Disposed);
    }

    private async Task<long> SeedAsync(long length)
    {
        await using var db = fixture.CreateContext();
        await db.Database.MigrateAsync();
        var batch = new Batch(Guid.NewGuid(), 1, DateTimeOffset.UtcNow);
        db.Batches.Add(batch);
        var image = new Image(batch.Id, "photo.jpg", "original/persisted-reference", length,
            "image/jpeg", DateTimeOffset.UtcNow);
        db.Images.Add(image);
        await db.SaveChangesAsync();
        return image.Id;
    }

    private sealed class Storage(byte[] bytes) : IFileStorageService
    {
        public byte[] Bytes { get; set; } = bytes;
        public string? Reference { get; private set; }
        public Source? LastStream { get; private set; }
        public Action? BeforeRead { get; init; }
        public Task<Stream> OpenReadAsync(string storedPath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Reference = storedPath;
            LastStream = new Source(Bytes, BeforeRead);
            return Task.FromResult<Stream>(LastStream);
        }
        public Task<string> SaveAsync(IUploadFile file, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteAsync(string storedPath, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class Source(byte[] bytes, Action? beforeRead) : MemoryStream(bytes)
    {
        public bool Disposed { get; private set; }
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            beforeRead?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            return base.ReadAsync(buffer, cancellationToken);
        }
        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
