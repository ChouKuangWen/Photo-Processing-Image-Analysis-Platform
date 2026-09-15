using PhotoPlatform.Application.DTOs;
using PhotoPlatform.Application.Services;
using PhotoPlatform.UnitTests.TestDoubles;

namespace PhotoPlatform.UnitTests.Application;

public class FileValidationServiceTests
{
    // 100 bytes 僅為測試上限，不代表正式環境設定；下方 Signature 也不是完整圖片。
    private const long Maximum = 100;
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private readonly FileValidationService service = new(Maximum);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    // 情境：分別將 0、-1 當成服務的檔案大小上限。
    // 預期：建構子拋出 ArgumentOutOfRangeException，ParamName 為 maxFileSizeBytes。
    // 目的：拒絕不合法的服務設定；這不是上傳檔案本身的驗證錯誤。
    public void Constructor_RejectsNonPositiveMaximum(long maximum)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new FileValidationService(maximum));
        Assert.Equal("maxFileSizeBytes", exception.ParamName);
    }

    [Theory]
    [InlineData("photo.jpg", "image/jpeg")]
    [InlineData("photo.jpeg", "image/jpeg")]
    [InlineData("photo.png", "image/png")]
    [InlineData("photo.JPG", "image/jpeg")]
    [InlineData("photo.JPEG", "image/jpeg")]
    [InlineData("photo.PNG", "image/png")]
    [InlineData("photo.backup.jpg", "image/jpeg")]
    [InlineData("photo.jpg", "IMAGE/JPEG")]
    [InlineData("photo.png", " \tIMAGE/PNG\r\n")]
    // 情境：提供 JPG／JPEG／PNG，以及大寫副檔名、多重副檔名、大寫或前後帶空白的 MIME。
    // 預期：每組資料都得到 IsValid = true，ErrorCode 與 ErrorMessage 都是 null。
    // 目的：確認規格允許的表示方式不會被誤拒絕；僅準備 Signature，不測完整圖片解碼。
    public async Task ValidateAsync_AcceptsSupportedFormats(string name, string mime)
    {
        // 只準備對應的 Signature；using 讓測試在方法結束時負責釋放 Stream。
        using var stream = new MemoryStream(name.EndsWith("png", StringComparison.OrdinalIgnoreCase) ? Png : Jpeg);
        // 將檔案資訊與回傳 Stream 的函式交給 Fake；10 是宣告大小，不會改變實際內容。
        var file = new FakeUploadFile(name, mime, 10, () => stream);
        // record 依屬性值比較：必須成功且沒有錯誤；default 表示不提供取消訊號。
        Assert.Equal(new FileValidationResult(true, null, null), await service.ValidateAsync(file, default));
    }

    [Theory]
    [InlineData("photo.exe")]
    [InlineData("photo")]
    [InlineData("photo.")]
    [InlineData("photo.jpg.exe")]
    // 情境：使用 .exe、無副檔名、結尾只有句點，或以 .exe 結尾的多重副檔名。
    // 預期：回傳 UNSUPPORTED_FORMAT 與核准訊息，OpenCount = 0。
    // 目的：即使宣告 image/jpeg，也不能讓不合法的最後副檔名通過；此時不應讀取內容。
    public async Task ValidateAsync_RejectsUnsupportedOrMissingExtension(string name)
    {
        await AssertFailure(name, "image/jpeg", 10, "UNSUPPORTED_FORMAT");
    }

    [Theory]
    [InlineData("photo.jpg", "image/png")]
    [InlineData("photo.png", "image/jpeg")]
    [InlineData("photo.jpg", "image/jpeg; charset=utf-8")]
    [InlineData("photo.jpg", "image/jpg")]
    [InlineData("photo.jpg", "")]
    [InlineData("photo.jpg", null)]
    // 情境：副檔名與大小合法，但 MIME 格式錯配、含參數、使用 image/jpg 別名，或為空／null。
    // 預期：回傳 INVALID_FILE 與核准訊息，不開啟 Stream。
    // 目的：副檔名合法仍必須檢查 MIME，不能只靠檔名接受檔案。
    public async Task ValidateAsync_RejectsInvalidMime(string name, string? mime)
    {
        await AssertFailure(name, mime!, 10, "INVALID_FILE");
    }

    [Theory]
    [InlineData(-1, "INVALID_FILE")]
    [InlineData(0, "INVALID_FILE")]
    [InlineData(101, "FILE_TOO_LARGE")]
    // 情境：宣告長度為 -1、0，或比測試上限 100 多一個 byte 的 101。
    // 預期：-1 與 0 回傳 INVALID_FILE；101 回傳 FILE_TOO_LARGE；均不開啟 Stream。
    // 目的：區分無效長度與超過上限，並驗證上限外的第一個值。
    public async Task ValidateAsync_RejectsInvalidSize(long length, string code)
    {
        await AssertFailure("photo.jpg", "image/jpeg", length, code);
    }

    [Theory]
    [InlineData(99)]
    [InlineData(100)]
    // 情境：其他資料合法，宣告長度分別為 99 與 100 bytes。
    // 預期：兩個案例的 IsValid 都是 true。
    // 目的：確認上限包含等號，不能誤用大於等於來拒絕；本 Task 不比對 Stream 總長度。
    public async Task ValidateAsync_AcceptsSizeAtOrBelowMaximum(long length)
    {
        using var stream = new MemoryStream(Jpeg);
        var file = new FakeUploadFile("photo.jpg", "image/jpeg", length, () => stream);
        Assert.True((await service.ValidateAsync(file, default)).IsValid);
    }

    // MemberData 的資料來源：每次 yield return 提供一組檔名測試參數。
    public static IEnumerable<object?[]> InvalidNames()
    {
        foreach (var name in new string?[] { null, "", " \t", ".", "..", "../photo.jpg", "..\\photo.jpg",
                     "folder/photo.jpg", "folder\\photo.jpg", "/photo.jpg", "C:\\photo.jpg", "\\\\server\\photo.jpg",
                     new string('a', 252) + ".jpg" })
            yield return [name];
        foreach (var character in "<>:\"/\\|?*\0\n\u007f")
            yield return [$"photo{character}.jpg"];
    }

    [Theory]
    [MemberData(nameof(InvalidNames))]
    // 情境：由 InvalidNames 提供空值、空白、點號、256 字元檔名、非法字元與各種路徑。
    // 預期：一律回傳 INVALID_FILE 與核准訊息，且不開啟 Stream。
    // 目的：在讀取內容前拒絕不合法檔名，包括 Windows 與 Unix 形式的路徑。
    public async Task ValidateAsync_RejectsInvalidFilename(string? name)
    {
        await AssertFailure(name!, "image/jpeg", 10, "INVALID_FILE");
    }

    [Fact]
    // 情境：建立長度剛好 255 的檔名，並以同一檔名進行兩次驗證。
    // 預期：兩次都成功，且 FakeUploadFile.FileName 保持原值。
    // 目的：確認長度上限可接受、Validator 不拒絕重複檔名，也不進行重新命名。
    public async Task ValidateAsync_Accepts255CharacterAndDuplicateFilenames()
    {
        using var stream = new MemoryStream(Jpeg);
        var name = new string('a', 251) + ".jpg";
        for (var i = 0; i < 2; i++)
        {
            var file = new FakeUploadFile(name, "image/jpeg", 10, () => stream);
            Assert.True((await service.ValidateAsync(file, default)).IsValid);
            Assert.Equal(name, file.FileName);
        }
    }

    // 產生格式錯配、所有不足長度，以及逐一破壞各 byte 的 Signature 案例。
    public static IEnumerable<object[]> InvalidSignatures()
    {
        yield return ["photo.jpg", "image/jpeg", Png];
        yield return ["photo.png", "image/png", Jpeg];
        foreach (var (name, mime, signature) in new[] { ("photo.jpg", "image/jpeg", Jpeg), ("photo.png", "image/png", Png) })
        {
            for (var length = 0; length < signature.Length; length++)
                yield return [name, mime, signature[..length]];
            for (var index = 0; index < signature.Length; index++)
            {
                var corrupted = signature.ToArray();
                corrupted[index] ^= 0xFF;
                yield return [name, mime, corrupted];
            }
        }
    }

    [Theory]
    [MemberData(nameof(InvalidSignatures))]
    // 情境：檔名與 MIME 一致，但內容是另一格式、Signature 被截短，或其中一個 byte 被修改。
    // 預期：回傳 INVALID_FILE；Stream.Position 回到原本的 0，且 CanRead 仍為 true。
    // 目的：必須檢查實際起始 bytes；即使驗證失敗，也要恢復位置並保留呼叫端的 Stream。
    public async Task ValidateAsync_RejectsInvalidOrInsufficientSignature(string name, string mime, byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        // 將檔案資訊與回傳 Stream 的函式交給 Fake；10 是宣告大小，不會改變實際內容。
        var file = new FakeUploadFile(name, mime, 10, () => stream);
        AssertError(await service.ValidateAsync(file, default), "INVALID_FILE");
        Assert.Equal(0, stream.Position);
        Assert.True(stream.CanRead);
    }

    [Theory]
    [InlineData("../photo.exe", 101, "bad", "INVALID_FILE")]
    [InlineData("photo.exe", 101, "bad", "UNSUPPORTED_FORMAT")]
    [InlineData("photo.jpg", 101, "bad", "FILE_TOO_LARGE")]
    [InlineData("photo.jpg", 0, "bad", "INVALID_FILE")]
    // 情境：同一檔案同時有檔名、副檔名、大小或 MIME 等多項錯誤。
    // 預期：依資料列得到檔名錯誤、格式錯誤或大小錯誤，且不開啟 Stream。
    // 目的：以可區分的錯誤代碼檢查驗證優先順序；相同代碼的規則僅能確認回傳與未讀取。
    public async Task ValidateAsync_ReturnsFirstFailure(string name, long length, string mime, string code)
    {
        await AssertFailure(name, mime, length, code);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    // 情境：交叉測試兩種格式與 Seek 能力；Signature 後加額外 bytes，且每次只允許讀 1 byte。
    // 預期：驗證成功、總讀取量恰為 Signature 長度、傳入相同 Token，且 Stream 未被 Dispose。
    // 目的：確認短讀取會被累積、不多讀後續內容；可 Seek 時恢復原位，不可 Seek 時不存取 Position。
    public async Task ValidateAsync_ReadsOnlySignatureAndPreservesCallerOwnership(bool seekable, bool png)
    {
        var signature = png ? Png : Jpeg;
        using var stream = new ObservedStream([.. signature, 1, 2, 3, 4], seekable);
        if (seekable)
            // 原本位置不在開頭，確認 Validator 先從 0 讀取 Signature，最後再恢復到 2。
            stream.Position = 2;
        using var cancellation = new CancellationTokenSource();
        var file = new FakeUploadFile(png ? "photo.png" : "photo.jpg", png ? "image/png" : "image/jpeg", 10, () => stream);
        Assert.True((await service.ValidateAsync(file, cancellation.Token)).IsValid);
        Assert.Equal(signature.Length, stream.BytesRead);
        Assert.Equal(cancellation.Token, stream.LastToken);
        Assert.False(stream.Disposed);
        if (seekable)
            Assert.Equal(2, stream.Position);
    }

    [Fact]
    // 情境：先 Cancel Token，再提供同時具有多種錯誤的檔案。
    // 預期：拋出 OperationCanceledException 或衍生例外，OpenCount = 0。
    // 目的：取消必須先於檔案驗證，不能被轉成 INVALID_FILE，也不能繼續開啟 Stream。
    public async Task ValidateAsync_ChecksCancellationBeforeFileValidation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var file = new FakeUploadFile("../bad.exe", "bad", -1, () => throw new InvalidOperationException());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ValidateAsync(file, cancellation.Token));
        Assert.Equal(0, file.OpenCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    // 情境：檔案資訊合法，但 OpenReadStream 分別拋出 IOException 或 OperationCanceledException。
    // 預期：I/O 失敗回傳 INVALID_FILE 的核准訊息；取消例外則向外傳遞。
    // 目的：區分讀取來源失敗與取消操作，並避免把 Internal details 暴露在結果中。
    public async Task ValidateAsync_MapsOpenFailureButPropagatesCancellation(bool canceled)
    {
        var file = new FakeUploadFile("photo.jpg", "image/jpeg", 10,
            () => throw (canceled ? new OperationCanceledException() : new IOException("Internal details")));
        if (canceled)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ValidateAsync(file, default));
        else
            AssertError(await service.ValidateAsync(file, default), "INVALID_FILE");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    // 情境：Stream 原始位置設為 1，並讓 ReadAsync 拋出 IOException 或取消例外。
    // 預期：I/O 失敗回傳 INVALID_FILE，取消例外向外傳遞；兩者都恢復 Position = 1 且未 Dispose。
    // 目的：驗證 finally 在異常路徑仍恢復呼叫端位置；只測原始位置 0 無法辨識錯誤地固定歸零。
    public async Task ValidateAsync_RestoresPositionOnReadFailureOrCancellation(bool canceled)
    {
        using var stream = new ObservedStream(Jpeg, true)
        {
            // 刻意模擬呼叫端已移到第 2 個 byte；驗證後應回到 1，而不是停在 0。
            Position = 1,
            ReadFailure = canceled ? new OperationCanceledException() : new IOException("Internal details")
        };
        var file = new FakeUploadFile("photo.jpg", "image/jpeg", 10, () => stream);
        if (canceled)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ValidateAsync(file, default));
        else
            AssertError(await service.ValidateAsync(file, default), "INVALID_FILE");
        Assert.Equal(1, stream.Position);
        Assert.False(stream.Disposed);
    }

    // 前置驗證失敗的共用檢查：若意外開啟 Stream 就拋錯，並確認呼叫次數為 0。
    private async Task AssertFailure(string name, string mime, long length, string code)
    {
        var file = new FakeUploadFile(name, mime, length, () => throw new InvalidOperationException("Must not open"));
        AssertError(await service.ValidateAsync(file, default), code);
        Assert.Equal(0, file.OpenCount);
    }

    // 同時檢查失敗狀態、錯誤代碼與核准的完整訊息，避免洩漏內部例外內容。
    private static void AssertError(FileValidationResult result, string code)
    {
        var message = code switch
        {
            "UNSUPPORTED_FORMAT" => "The uploaded file format is not supported.",
            "FILE_TOO_LARGE" => "The uploaded file exceeds the maximum allowed size.",
            _ => "The uploaded file is invalid."
        };
        Assert.Equal(new FileValidationResult(false, code, message), result);
    }

    // 測試專用 Stream：記錄讀取量、取消 Token 與 Dispose，並可模擬讀取失敗。
    private sealed class ObservedStream(byte[] content, bool seekable) : MemoryStream(content)
    {
        public override bool CanSeek => seekable;
        public int BytesRead { get; private set; }
        public bool Disposed { get; private set; }
        public CancellationToken LastToken { get; private set; }
        public Exception? ReadFailure { get; init; }
        // 不可 Seek 時存取 Position 就拋錯，用來偵測 Validator 是否誤用位置操作。
        public override long Position
        {
            get => seekable ? base.Position : throw new NotSupportedException();
            set
            {
                if (!seekable) throw new NotSupportedException();
                base.Position = value;
            }
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            LastToken = cancellationToken;
            cancellationToken.ThrowIfCancellationRequested();
            if (ReadFailure is not null) throw ReadFailure;
            // 每次最多讀取 1 byte，確認 Validator 會累積短讀取結果，而不讀取後續內容。
            var read = await base.ReadAsync(buffer[..Math.Min(1, buffer.Length)], cancellationToken);
            BytesRead += read;
            return read;
        }

        // 記錄是否被釋放，讓測試確認 Validator 沒有提早關閉呼叫端資源。
        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
