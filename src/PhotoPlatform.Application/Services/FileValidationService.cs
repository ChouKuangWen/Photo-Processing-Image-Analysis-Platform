using PhotoPlatform.Application.DTOs;
using PhotoPlatform.Application.Interfaces;

namespace PhotoPlatform.Application.Services;

public sealed class FileValidationService : IFileValidationService
{
    private readonly long maxFileSizeBytes;

    public FileValidationService(long maxFileSizeBytes)
    {
        // 上限由呼叫端設定；非正數屬於設定錯誤，不是上傳檔案的驗證失敗。
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFileSizeBytes);
        this.maxFileSizeBytes = maxFileSizeBytes;
    }

    public async Task<FileValidationResult> ValidateAsync(
        IUploadFile file, CancellationToken cancellationToken)
    {
        // 依 TASK-04 順序驗證，遇到第一個失敗就回傳；取消操作優先傳遞。
        cancellationToken.ThrowIfCancellationRequested();

        // 拒絕不合法檔名與路徑字元，不在此清理或改寫呼叫端提供的檔名。
        var name = file.FileName;
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.Length > 255 ||
            name.Any(c => char.IsControl(c) || "<>:\"/\\|?*".Contains(c)))
            return InvalidFile();

        // 只依最後一個副檔名判斷格式，並忽略大小寫。
        var extension = Path.GetExtension(name);
        var isJpeg = extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
        var isPng = extension.Equals(".png", StringComparison.OrdinalIgnoreCase);
        if (!isJpeg && !isPng)
            return new(false, "UNSUPPORTED_FORMAT", "The uploaded file format is not supported.");

        // 空檔案與負數長度無效；大小等於上限時允許通過。
        if (file.Length <= 0)
            return InvalidFile();
        if (file.Length > maxFileSizeBytes)
            return new(false, "FILE_TOO_LARGE", "The uploaded file exceeds the maximum allowed size.");

        // MIME 必須與副檔名一致；允許前後空白及大小寫差異，不接受額外參數或別名。
        if (!string.Equals(file.MimeType?.Trim(), isJpeg ? "image/jpeg" : "image/png",
                StringComparison.OrdinalIgnoreCase))
            return InvalidFile();

        // 僅檢查 JPG 的 3 bytes 或 PNG 的 8 bytes 起始標記，不代表圖片完整可解碼。
        byte[] signature = isJpeg ? [0xFF, 0xD8, 0xFF] : [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        try
        {
            // Stream 由呼叫端管理生命週期，因此此處不使用 using 或 Dispose。
            var stream = file.OpenReadStream();
            long? originalPosition = stream.CanSeek ? stream.Position : null;
            try
            {
                // 支援 Seek 時從檔案開頭驗證，並記住位置供稍後恢復。
                if (originalPosition.HasValue)
                    stream.Position = 0;

                // 只配置 Signature 所需空間，不把整個檔案載入記憶體。
                var buffer = new byte[signature.Length];
                var totalRead = 0;
                while (totalRead < buffer.Length)
                {
                    // 單次讀取可能不足；從尚未填入的位置繼續讀取，避免覆蓋已讀資料。
                    var read = await stream.ReadAsync(buffer.AsMemory(totalRead), cancellationToken);
                    // 尚未湊足 Signature 就讀到結尾，視為檔案不合法。
                    if (read == 0)
                        return InvalidFile();
                    totalRead += read;
                }

                return buffer.AsSpan().SequenceEqual(signature)
                    ? new(true, null, null)
                    : InvalidFile();
            }
            finally
            {
                // 即使 return 或發生例外，也嘗試恢復原始位置，避免影響呼叫端後續讀取。
                // 不支援 Seek 的 Stream 依規格允許消耗 Signature bytes，不要求恢復。
                if (originalPosition.HasValue)
                    stream.Position = originalPosition.Value;
            }
        }
        catch (OperationCanceledException)
        {
            // 取消不代表檔案無效，保留原例外向外傳遞。
            throw;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or ObjectDisposedException
            or NotSupportedException)
        {
            // 僅將指定的 Stream／I/O 例外轉成核准錯誤；其他程式例外繼續向外傳遞。
            return InvalidFile();
        }
    }

    private static FileValidationResult InvalidFile() =>
        new(false, "INVALID_FILE", "The uploaded file is invalid.");
}
