namespace PhotoPlatform.Application.Processing;

// 持有一次原圖讀取的資源。內容雜湊由 Reader 核對，本型別只檢查格式，不假裝已讀過檔案。
public sealed class ContentHandle : IDisposable, IAsyncDisposable
{
    private Stream? stream;

    public ContentHandle(Stream stream, long length, string contentVersion)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead)
            throw new ArgumentException("A readable stream is required.", nameof(stream));
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        if (contentVersion is null || contentVersion.Length != 64 ||
            contentVersion.Any(character => character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new ArgumentException("Content version must be a lowercase SHA-256 value.", nameof(contentVersion));

        // 不讀取 Stream.Length 或 Position，讓不能 Seek 的來源也能使用；建構失敗時仍由原呼叫端持有資源。
        this.stream = stream;
        Length = length;
        ContentVersion = contentVersion;
    }

    public Stream Stream => stream ?? throw new ObjectDisposedException(nameof(ContentHandle));
    public long Length { get; }
    public string ContentVersion { get; }

    public void Dispose() => Interlocked.Exchange(ref stream, null)?.Dispose();

    public async ValueTask DisposeAsync()
    {
        // 同步與非同步釋放共用同一份所有權，避免兩種清理方式重複關閉底層 Stream。
        var owned = Interlocked.Exchange(ref stream, null);
        if (owned is not null)
            await owned.DisposeAsync().ConfigureAwait(false);
    }
}
