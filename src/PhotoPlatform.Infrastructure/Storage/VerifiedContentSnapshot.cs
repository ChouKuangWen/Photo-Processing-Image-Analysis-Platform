using System.Security.Cryptography;
using PhotoPlatform.Application.Processing;

namespace PhotoPlatform.Infrastructure.Storage;

// Reader foundation only: the caller supplies a persisted length and an explicit capacity policy.
// It borrows source; the returned handle owns only this operation's private snapshot.
internal static class VerifiedContentSnapshot
{
    internal static async Task<ContentHandle> CreateAsync(Stream source, long expectedLength,
        long maximumLength, string? expectedContentVersion, CancellationToken cancellationToken,
        string? snapshotDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedLength);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumLength);
        cancellationToken.ThrowIfCancellationRequested();
        if (expectedLength > maximumLength)
            throw new InvalidDataException("Original content exceeds the snapshot limit.");

        var path = Path.Combine(snapshotDirectory ?? Path.GetTempPath(), "PhotoPlatform-snapshot-" + Guid.NewGuid().ToString("N"));
        var snapshot = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
            81920, FileOptions.Asynchronous | FileOptions.DeleteOnClose | FileOptions.SequentialScan);
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            long length = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) != 0)
            {
                if (read > maximumLength - length || read > expectedLength - length)
                    throw new InvalidDataException("Original content length does not match.");
                length += read;
                hash.AppendData(buffer, 0, read);
                await snapshot.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
            if (length != expectedLength)
                throw new InvalidDataException("Original content length does not match.");
            var version = Convert.ToHexStringLower(hash.GetHashAndReset());
            if (expectedContentVersion is not null &&
                !string.Equals(version, expectedContentVersion, StringComparison.Ordinal))
                throw new InvalidDataException("Original content version does not match.");
            await snapshot.FlushAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            snapshot.Position = 0;
            return new ContentHandle(new ReadOnlySnapshot(snapshot), length, version);
        }
        catch
        {
            await snapshot.DisposeAsync();
            throw;
        }
    }

    // Keep the writable FileStream private, so the consumer cannot change the verified bytes.
    private sealed class ReadOnlySnapshot(FileStream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) => inner.ReadAsync(buffer, token);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token) => inner.ReadAsync(buffer, offset, count, token);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void Flush() => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
        public override async ValueTask DisposeAsync()
        {
            await inner.DisposeAsync();
            GC.SuppressFinalize(this);
        }
    }
}
