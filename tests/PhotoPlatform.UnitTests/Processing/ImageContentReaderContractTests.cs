using PhotoPlatform.Application.Interfaces;
using PhotoPlatform.Application.Processing;

namespace PhotoPlatform.UnitTests.Processing;

public sealed class ImageContentReaderContractTests
{
    private const string ContentVersion = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

    // 測試目的：確認取得原圖資源後，由呼叫端負責關閉，且不需要可 Seek 的來源。
    // 測試情境：測試 Reader 回傳不能讀取 Length／Position 的 Stream，呼叫端讀完後釋放。
    // 預期結果：成功讀到內容與明確長度，釋放前仍開啟，重複清理只關閉一次。
    [Fact]
    public async Task Handle_OwnsNonSeekableStreamAndDisposesOnce()
    {
        var stream = new NonSeekableStream();
        var reader = new TestReader(stream);
        using var cancellation = new CancellationTokenSource();
        var handle = await reader.OpenOriginalAsync(7, cancellation.Token);
        Assert.Equal(7, reader.ImageId);
        Assert.Equal(cancellation.Token, reader.Token);
        Assert.Same(stream, handle.Stream);
        Assert.Equal(3, handle.Length);
        Assert.Equal(ContentVersion, handle.ContentVersion);
        Assert.False(handle.Stream.CanSeek);
        var bytes = new byte[3];
        await handle.Stream.ReadExactlyAsync(bytes, cancellation.Token);
        Assert.Equal("abc"u8.ToArray(), bytes);
        Assert.Equal(0, stream.Disposals);
        handle.Dispose();
        await handle.DisposeAsync();
        Assert.Equal(1, stream.Disposals);
        Assert.Throws<ObjectDisposedException>(() => handle.Stream);
    }

    // 測試目的：確認非同步清理會交給底層 Stream，不被換成同步清理。
    // 測試情境：先 DisposeAsync，再重複呼叫同步與非同步 Dispose。
    // 預期結果：只呼叫一次底層非同步清理，沒有重複釋放。
    [Fact]
    public async Task Handle_UsesAsyncDisposalWhenRequested()
    {
        var stream = new NonSeekableStream();
        var handle = new ContentHandle(stream, 3, ContentVersion);
        await handle.DisposeAsync();
        handle.Dispose();
        await handle.DisposeAsync();
        Assert.Equal(1, stream.AsyncDisposals);
        Assert.Equal(1, stream.Disposals);
    }

    // 測試目的：確認建構失敗不會偷走呼叫端的 Stream 所有權。
    // 測試情境：長度為負數、Hash 大寫／長度錯誤／非法字元及不可讀 Stream。
    // 預期結果：非法資料拒絕，原資源沒有被自動 Dispose，仍由原呼叫端清理。
    [Fact]
    public void Handle_RejectsInvalidMetadataWithoutDisposingCallerResource()
    {
        using var stream = new NonSeekableStream();
        Assert.Throws<ArgumentOutOfRangeException>(() => new ContentHandle(stream, -1, ContentVersion));
        foreach (var value in new[] { ContentVersion.ToUpperInvariant(), "abc", new string('g', 64), null })
            Assert.Throws<ArgumentException>(() => new ContentHandle(stream, 3, value!));
        Assert.Throws<ArgumentNullException>(() => new ContentHandle(null!, 3, ContentVersion));
        Assert.Equal(0, stream.Disposals);
        using var unreadable = new MemoryStream();
        unreadable.Dispose();
        Assert.Throws<ArgumentException>(() => new ContentHandle(unreadable, 0, ContentVersion));
    }

    // 測試目的：確認 Reader Port 能傳遞取消，不需要把取消偽裝成處理結果。
    // 測試情境：以已取消的 Token 呼叫測試替身，尚未交出 ContentHandle。
    // 預期結果：取消直接傳回，Stream 未被取得或關閉；這不代表具體 Reader 已交付。
    [Fact]
    public async Task ReaderPort_PropagatesCancellationBeforeTransferringOwnership()
    {
        using var stream = new NonSeekableStream();
        IImageContentReader reader = new TestReader(stream);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.OpenOriginalAsync(7, new CancellationToken(true)));
        Assert.Equal(0, stream.Disposals);
    }

    private sealed class TestReader(Stream stream) : IImageContentReader
    {
        public long ImageId { get; private set; }
        public CancellationToken Token { get; private set; }

        public Task<ContentHandle> OpenOriginalAsync(long imageId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ImageId = imageId;
            Token = cancellationToken;
            return Task.FromResult(new ContentHandle(stream, 3, ContentVersion));
        }
    }

    private sealed class NonSeekableStream : Stream
    {
        private readonly MemoryStream content = new("abc"u8.ToArray());
        public int Disposals { get; private set; }
        public int AsyncDisposals { get; private set; }
        public override bool CanRead => content.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => content.Read(buffer, offset, count);
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Disposals++;
                content.Dispose();
            }
            base.Dispose(disposing);
        }

        public override ValueTask DisposeAsync()
        {
            AsyncDisposals++;
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
