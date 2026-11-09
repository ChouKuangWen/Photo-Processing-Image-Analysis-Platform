using System.Security.Cryptography;
using PhotoPlatform.Infrastructure.Storage;

namespace PhotoPlatform.UnitTests.Infrastructure;

public sealed class StorageReadFoundationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "PhotoPlatform-read-" + Guid.NewGuid().ToString("N"));
    private const string Id = "550e8400e29b41d4a716446655440000";
    private const string Reference = "original/" + Id;
    private string FilePath => Path.Combine(root, "original", Id);

    public StorageReadFoundationTests() => Directory.CreateDirectory(root);

    private Task<PhotoPlatform.Application.Processing.ContentHandle> CreateSnapshotAsync(Stream source,
        long expected, long limit, string? version, CancellationToken token) =>
        VerifiedContentSnapshot.CreateAsync(source, expected, limit, version, token, root);

    private async Task<LocalFileStorageService> PrepareAsync(byte[] bytes)
    {
        Directory.CreateDirectory(Path.Combine(root, "original"));
        await File.WriteAllBytesAsync(FilePath, bytes);
        return new LocalFileStorageService(root);
    }

    [LocalStorageRaceTests.WindowsFact]
    public async Task OpenRead_ReturnsOwnedReadOnlyStreamFromBeginningAndReleasesFile()
    {
        var bytes = new byte[] { 1, 2, 3, 4 };
        var storage = await PrepareAsync(bytes);
        var stream = await storage.OpenReadAsync(Reference, default);
        Assert.False(stream.CanWrite);
        using var actual = new MemoryStream();
        await stream.CopyToAsync(actual);
        Assert.Equal(bytes, actual.ToArray());
        await stream.DisposeAsync();
        Assert.Throws<ObjectDisposedException>(() => stream.ReadByte());
        await storage.DeleteAsync(Reference, default);
        Assert.False(File.Exists(FilePath));
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("original/../outside")]
    [InlineData("original/550e8400e29b41d4a716446655440000/extra")]
    [InlineData("C:\\outside")]
    [InlineData("/outside")]
    [InlineData("")]
    public async Task OpenRead_RejectsArbitraryOrEscapingReferences(string reference)
    {
        var storage = await PrepareAsync([1]);
        await Assert.ThrowsAnyAsync<ArgumentException>(() => storage.OpenReadAsync(reference, default));
    }

    [Fact]
    public async Task OpenRead_CancelledBeforeOpeningDoesNotAcquireFile()
    {
        var storage = await PrepareAsync([1]);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            storage.OpenReadAsync(Reference, new CancellationToken(true)));
        using var exclusive = new FileStream(FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [LocalStorageRaceTests.WindowsFact]
    public async Task OpenRead_MissingReferenceFailsWithoutCreatingFile()
    {
        var storage = await PrepareAsync([1]);
        File.Delete(FilePath);
        await Assert.ThrowsAsync<FileNotFoundException>(() => storage.OpenReadAsync(Reference, default));
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public async Task Snapshot_HashesNonSeekableBytesAndKeepsConsumerBytesStable()
    {
        byte[] bytes = [1, 2, 3, 4, 5];
        using var source = new NonSeekableStream(bytes);
        await using var handle = await CreateSnapshotAsync(source, bytes.Length, 10, null, default);
        Assert.False(source.Disposed);
        Assert.Equal(bytes.Length, handle.Length);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), handle.ContentVersion);
        Array.Fill(bytes, (byte)9);
        using var actual = new MemoryStream();
        await handle.Stream.CopyToAsync(actual);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, actual.ToArray());
        Assert.False(handle.Stream.CanWrite);
        Assert.Throws<NotSupportedException>(() => handle.Stream.WriteByte(0));
        var snapshotPath = Assert.Single(Directory.GetFiles(root, "PhotoPlatform-snapshot-*"));
        await handle.DisposeAsync();
        Assert.False(File.Exists(snapshotPath));
    }

    [Fact]
    public async Task Snapshot_RejectsChangedVersionLengthAndCapacityAndCleansItsFiles()
    {
        var before = Directory.GetFiles(root, "PhotoPlatform-snapshot-*").Order().ToArray();
        foreach (var (expected, limit, version) in new (long, long, string?)[]
        {
            (2, 10, null), (4, 10, null), (3, 2, null), (3, 10, new string('a', 64))
        })
        {
            using var source = new NonSeekableStream([1, 2, 3]);
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                CreateSnapshotAsync(source, expected, limit, version, default));
            Assert.False(source.Disposed);
        }
        Assert.Equal(before, Directory.GetFiles(root, "PhotoPlatform-snapshot-*").Order().ToArray());
    }

    [Fact]
    public async Task Snapshot_LargeGeneratedSourceUsesBoundedReadsAndCancellationCleansSnapshot()
    {
        using var source = new GeneratedStream(16 * 1024 * 1024);
        await using (var handle = await CreateSnapshotAsync(source, source.Remaining, source.Remaining, null, default))
        {
            Assert.InRange(source.LargestRequest, 1, 81920);
            using var hash = SHA256.Create();
            Assert.Equal(handle.ContentVersion, Convert.ToHexStringLower(await hash.ComputeHashAsync(handle.Stream)));
        }
        var before = Directory.GetFiles(root, "PhotoPlatform-snapshot-*").Order().ToArray();
        using var cancellation = new CancellationTokenSource();
        using var cancelled = new GeneratedStream(1024 * 1024, cancellation);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateSnapshotAsync(cancelled, cancelled.Remaining, cancelled.Remaining, null, cancellation.Token));
        Assert.Equal(before, Directory.GetFiles(root, "PhotoPlatform-snapshot-*").Order().ToArray());
    }

    private sealed class NonSeekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public bool Disposed { get; private set; }
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }

    [LocalStorageRaceTests.WindowsFact]
    public async Task StorageSnapshot_RemainsStableAndReopeningChangedOriginalRejectsFrozenVersion()
    {
        byte[] original = [1, 2, 3];
        var storage = await PrepareAsync(original);
        PhotoPlatform.Application.Processing.ContentHandle handle;
        await using (var source = await storage.OpenReadAsync(Reference, default))
            handle = await CreateSnapshotAsync(source, 3, 10, null, default);
        await using (handle)
        {
            await File.WriteAllBytesAsync(FilePath, new byte[] { 4, 5, 6 });
            using var consumed = new MemoryStream();
            await handle.Stream.CopyToAsync(consumed);
            Assert.Equal(original, consumed.ToArray());
            await using var changed = await storage.OpenReadAsync(Reference, default);
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                CreateSnapshotAsync(changed, 3, 10, handle.ContentVersion, default));
        }
        Assert.Empty(Directory.GetFiles(root, "PhotoPlatform-snapshot-*"));
    }

    [Fact]
    public async Task Snapshot_SourceReadFailureCleansSnapshotAndPreservesBorrowedSource()
    {
        using var source = new FailingStream();
        await Assert.ThrowsAsync<IOException>(() => CreateSnapshotAsync(source, 3, 10, null, default));
        Assert.True(source.CanRead);
        Assert.Empty(Directory.GetFiles(root, "PhotoPlatform-snapshot-*"));
    }

    private sealed class FailingStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) =>
            ValueTask.FromException<int>(new IOException("Injected source read failure."));
    }

    private sealed class GeneratedStream(long length, CancellationTokenSource? cancellation = null) : Stream
    {
        public long Remaining { get; private set; } = length;
        public int LargestRequest { get; private set; }
        public override bool CanRead => true;
        public override bool CanWrite => false;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            LargestRequest = Math.Max(LargestRequest, buffer.Length);
            var count = (int)Math.Min(Remaining, buffer.Length);
            buffer.Span[..count].Fill(7);
            Remaining -= count;
            cancellation?.Cancel();
            return ValueTask.FromResult(count);
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
