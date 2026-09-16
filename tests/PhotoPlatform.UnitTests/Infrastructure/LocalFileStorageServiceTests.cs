using PhotoPlatform.Infrastructure.Storage;
using PhotoPlatform.UnitTests.TestDoubles;

namespace PhotoPlatform.UnitTests.Infrastructure;

// 以隔離的暫存目錄驗證實際檔案內容、失敗清理與資源所有權，不依賴正式儲存環境。
public sealed class LocalFileStorageServiceTests : IDisposable
{
    private const string Identifier = "550e8400e29b41d4a716446655440000";
    private const string StoredPath = "original/" + Identifier;
    // 每個測試隔離暫存目錄，避免並行執行互相干擾或衝突實際上傳資料。
    private readonly string root = Path.Combine(Path.GetTempPath(), "PhotoPlatform-StorageTests", Guid.NewGuid().ToString("N"));
    private string Destination => Path.Combine(root, "original", Identifier);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t")]
    // 目的：拒絕未指定儲存位置的設定，避免服務在不明確的位置建立檔案。
    public void Constructor_RejectsMissingRoot(string? value)
    {
        Assert.ThrowsAny<ArgumentException>(() => new LocalFileStorageService(value!));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    // 目的：目錄存在與否皆能保存完整內容，並回傳 GUID 相對路徑、還原來源位置及保留來源可用性。
    public async Task SaveAsync_CreatesDirectoriesAndPreservesCompleteContent(bool existingDirectory)
    {
        if (existingDirectory) Directory.CreateDirectory(Path.Combine(root, "original"));
        // 超過複製緩衝區的內容，確認不只儲存 Signature 或第一段資料。
        var bytes = Enumerable.Range(0, 200000).Select(i => (byte)i).ToArray();
        // 非零起點可同時抓出「只複製剩餘內容」與「固定恢復成 0」的錯誤。
        using var source = new TestStream(bytes) { Position = 7 };
        var file = File(source);
        var service = new LocalFileStorageService(root);

        var path = await service.SaveAsync(file, default);

        Assert.StartsWith("original/", path);
        Assert.Equal(41, path.Length);
        Assert.True(Guid.TryParseExact(path[9..], "N", out _));
        Assert.Equal(bytes, await System.IO.File.ReadAllBytesAsync(Path.Combine(root, "original", path[9..])));
        Assert.Equal(7, source.Position);
        Assert.False(source.Disposed);
        Assert.Equal(1, file.OpenCount);
    }

    [Fact]
    // 目的：不可回捲的來源仍可完整保存，且複製操作收到呼叫端的取消 Token、不被服務關閉。
    public async Task SaveAsync_AcceptsNonSeekableSourceAndPassesCancellationToken()
    {
        byte[] bytes = [1, 2, 3, 4, 5];
        using var source = new TestStream(bytes, false);
        using var cancellation = new CancellationTokenSource();
        var service = FixedService();

        Assert.Equal(StoredPath, await service.SaveAsync(File(source), cancellation.Token));
        Assert.Equal(bytes, await System.IO.File.ReadAllBytesAsync(Destination));
        Assert.Equal(cancellation.Token, source.LastToken);
        Assert.False(source.Disposed);
    }

    [Fact]
    // 目的：證明檔名無法控制儲存位置，Storage 也不重複承擔圖片格式驗證責任。
    public async Task SaveAsync_DoesNotUseOriginalFilenameOrValidateContent()
    {
        using var source = new MemoryStream([1, 2, 3]);
        // 不可信檔名不應參與路徑；內容驗證屬 TASK-04，Storage 不重複驗證。
        var file = new FakeUploadFile("../../outside.exe", "not-an-image", 3, () => source);
        Assert.Equal(StoredPath, await FixedService().SaveAsync(file, default));
        Assert.Equal(new byte[] { 1, 2, 3 }, await System.IO.File.ReadAllBytesAsync(Destination));
    }

    [Fact]
    // 目的：固定識別碼衝突時直接傳遞 IOException，不覆寫、誤刪既存檔案或再次產生識別碼。
    public async Task SaveAsync_CollisionPreservesExistingFileWithoutRetry()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Destination)!);
        byte[] existing = [9, 8, 7];
        await System.IO.File.WriteAllBytesAsync(Destination, existing);
        // 固定 identifier 並預建同名檔案，穩定製造衝突；計數用來偵測重新產生名稱。
        var calls = 0;
        var service = new LocalFileStorageService(root, () => { calls++; return Identifier; });
        using var source = new TestStream([1, 2, 3]) { Position = 1 };

        await Assert.ThrowsAsync<IOException>(() => service.SaveAsync(File(source), default));

        Assert.Equal(1, calls);
        Assert.Equal(existing, await System.IO.File.ReadAllBytesAsync(Destination));
        Assert.Single(Directory.GetFiles(Path.Combine(root, "original")));
        Assert.Equal(1, source.Position);
        Assert.False(source.Disposed);
    }

    [Fact]
    // 目的：已取消的請求應在任何來源讀取或檔案系統寫入前停止。
    public async Task SaveAsync_AlreadyCanceledDoesNotOpenSourceOrCreateFiles()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var file = new FakeUploadFile("photo.jpg", "image/jpeg", 3, () => throw new InvalidOperationException());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => FixedService().SaveAsync(file, cancellation.Token));
        Assert.Equal(0, file.OpenCount);
        Assert.False(Directory.Exists(root));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    // 目的：已寫入部分內容後發生讀取失敗或取消，必須清除本次檔案、恢復位置並保留原始例外。
    public async Task SaveAsync_ReadFailureOrCancellationRemovesPartialFile(bool canceled)
    {
        using var cancellation = new CancellationTokenSource();
        var error = new IOException("Read failed");
        using var source = new TestStream([1, 2, 3, 4])
        {
            Position = 1,
            // 等實際寫入第一段才觸發失敗，才能驗證 partial file 清理而非只有事前取消。
            AfterFirstWrite = () =>
            {
                Assert.True(System.IO.File.Exists(Destination));
                if (canceled)
                {
                    cancellation.Cancel();
                    cancellation.Token.ThrowIfCancellationRequested();
                }
                throw error;
            }
        };

        if (canceled)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => FixedService().SaveAsync(File(source), cancellation.Token));
        else
            Assert.Same(error, await Assert.ThrowsAsync<IOException>(() => FixedService().SaveAsync(File(source), cancellation.Token)));

        Assert.False(System.IO.File.Exists(Destination));
        Assert.Equal(1, source.Position);
        Assert.False(source.Disposed);
    }

    [Fact]
    // 目的：建目錄失敗時保留既存內容、傳遞 I/O 例外，並還原借用的來源串流。
    public async Task SaveAsync_DirectoryFailurePropagatesAndRestoresSource()
    {
        Directory.CreateDirectory(root);
        // 用一般檔案佔據目錄位置，穩定製造 I/O 失敗，不依賴機器權限設定。
        await System.IO.File.WriteAllTextAsync(Path.Combine(root, "original"), "occupied");
        using var source = new TestStream([1, 2, 3]) { Position = 1 };
        await Assert.ThrowsAnyAsync<IOException>(() => FixedService().SaveAsync(File(source), default));
        Assert.Equal(1, source.Position);
        Assert.False(source.Disposed);
        Assert.Equal("occupied", await System.IO.File.ReadAllTextAsync(Path.Combine(root, "original")));
    }

    [Fact]
    // 目的：來源尚未取得就失敗時，不建立儲存目錄，也不包裝原始例外。
    public async Task SaveAsync_OpenFailurePropagatesOriginalException()
    {
        var error = new IOException("Open failed");
        var file = new FakeUploadFile("photo.jpg", "image/jpeg", 3, () => throw error);
        Assert.Same(error, await Assert.ThrowsAsync<IOException>(() => FixedService().SaveAsync(file, default)));
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    // 目的：寫入與位置恢復同時失敗時，呼叫端仍收到最初的寫入例外，且目的檔案已清除。
    public async Task SaveAsync_RestoreFailureDoesNotMaskOriginalWriteFailure()
    {
        var error = new IOException("Original failure");
        using var source = new TestStream([1, 2, 3])
        {
            Position = 1,
            // 同時製造寫入與恢復失敗，確認次要例外不會取代原始例外。
            FailRestore = true,
            AfterFirstWrite = () => throw error
        };
        Assert.Same(error, await Assert.ThrowsAsync<IOException>(() => FixedService().SaveAsync(File(source), default)));
        Assert.False(System.IO.File.Exists(Destination));
        Assert.False(source.Disposed);
    }

    [Fact]
    // 目的：即使複製完成，恢復位置失敗仍屬 Save 失敗，應清理本次目的檔案並保留來源所有權。
    public async Task SaveAsync_RestoreFailureAfterCopyRemovesDestination()
    {
        using var source = new TestStream([1, 2, 3]) { Position = 1, FailRestore = true };
        await Assert.ThrowsAsync<IOException>(() => FixedService().SaveAsync(File(source), default));
        Assert.False(System.IO.File.Exists(Destination));
        Assert.False(source.Disposed);
    }

    [Fact]
    // 目的：可刪除已保存的檔案，再次刪除同一合法路徑也成功，方便補償流程重複執行。
    public async Task DeleteAsync_DeletesSavedFileAndAllowsRepeatedDelete()
    {
        using var source = new MemoryStream([1, 2, 3]);
        var service = FixedService();
        var path = await service.SaveAsync(File(source), default);
        await service.DeleteAsync(path, default);
        Assert.False(System.IO.File.Exists(Destination));
        await service.DeleteAsync(path, default);
    }

    [Fact]
    // 目的：根目錄不存在代表目標也不存在，刪除應成功且不為此建立目錄。
    public async Task DeleteAsync_MissingRootIsSuccessful()
    {
        await FixedService().DeleteAsync(StoredPath, default);
        Assert.False(Directory.Exists(root));
    }

    // 集中列出空值、非允許目錄、錯誤 GUID 格式與逃逸路徑，供同一組安全性斷言逐一驗證。
    public static IEnumerable<object?[]> InvalidPaths()
    {
        foreach (var path in new string?[] { null, "", " \t", "temp/" + Identifier, "processed/" + Identifier,
            "foo/bar", "original/hello", "original/550e8400-e29b-41d4-a716-446655440000", "original/file.jpg",
            "../" + StoredPath, "original/../" + Identifier, "/" + StoredPath, "C:/" + StoredPath,
            "C:" + StoredPath, "\\\\server\\share\\" + Identifier, "original\\" + Identifier,
            StoredPath + "/extra", StoredPath + ".jpg", "original/" + new string('g', 32),
            "original/" + new string('a', 31), "original/" + new string('a', 33) })
            yield return [path];
    }

    [Theory]
    [MemberData(nameof(InvalidPaths))]
    // 目的：非法路徑必須被當成參數錯誤拒絕，同時確認既有合法檔案未被波及。
    public async Task DeleteAsync_RejectsInvalidPathsWithoutDeletingValidFile(string? path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Destination)!);
        await System.IO.File.WriteAllTextAsync(Destination, "keep");
        await Assert.ThrowsAnyAsync<ArgumentException>(() => FixedService().DeleteAsync(path!, default));
        Assert.Equal("keep", await System.IO.File.ReadAllTextAsync(Destination));
    }

    [Fact]
    // 目的：已取消的刪除請求必須傳遞取消例外，原檔案應保持存在。
    public async Task DeleteAsync_CancellationPreservesFile()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Destination)!);
        await System.IO.File.WriteAllTextAsync(Destination, "keep");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => FixedService().DeleteAsync(StoredPath, cancellation.Token));
        Assert.True(System.IO.File.Exists(Destination));
    }

    [Fact]
    // 目的：目標其實是目錄時，不能當作檔案不存在而吞掉 I/O 失敗，也不能刪除該目錄。
    public async Task DeleteAsync_DirectoryAtFilePathPropagatesIoFailure()
    {
        Directory.CreateDirectory(Destination);
        var error = await Record.ExceptionAsync(() => FixedService().DeleteAsync(StoredPath, default));
        // 各 OS 對刪除目錄路徑的例外可能不同，但都不能被誤判為檔案不存在。
        Assert.True(error is IOException or UnauthorizedAccessException);
        Assert.True(Directory.Exists(Destination));
    }

    // 使用核准的 internal seam 固定儲存位置，讓測試能精確檢查本次目的檔案。
    private LocalFileStorageService FixedService() => new(root, () => Identifier);
    private static FakeUploadFile File(Stream source) => new("photo.jpg", "image/jpeg", 3, () => source);

    public void Dispose()
    {
        // 僅清理本測試建立的 GUID 目錄，先確認仍位於專用暫存根目錄內。
        var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "PhotoPlatform-StorageTests"));
        var target = Path.GetFullPath(root);
        if (Path.GetDirectoryName(target) != parent) throw new InvalidOperationException("Unexpected test directory.");
        if (Directory.Exists(target)) Directory.Delete(target, true);
    }

    // 只在測試端控制來源行為，避免為了模擬失敗而新增正式 FileSystem abstraction。
    private sealed class TestStream(byte[] bytes, bool seekable = true) : MemoryStream(bytes)
    {
        public override bool CanSeek => seekable;
        public bool Disposed { get; private set; }
        public bool FailRestore { get; init; }
        public Action? AfterFirstWrite { get; init; }
        public CancellationToken LastToken { get; private set; }
        private bool rewound;

        public override long Position
        {
            get => seekable ? base.Position : throw new NotSupportedException();
            set
            {
                if (!seekable) throw new NotSupportedException();
                if (FailRestore && rewound && value != 0) throw new IOException("Restore failed");
                if (value == 0) rewound = true;
                base.Position = value;
            }
        }

        public override async Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            LastToken = cancellationToken;
            // 刻意分成小段寫入，使測試能在已有 partial file 時觸發失敗或取消。
            var buffer = new byte[2];
            var first = true;
            int read;
            while ((read = await base.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                if (first) AfterFirstWrite?.Invoke();
                first = false;
            }
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
