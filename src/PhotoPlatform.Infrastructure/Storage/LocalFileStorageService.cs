using PhotoPlatform.Application.Interfaces;

namespace PhotoPlatform.Infrastructure.Storage;

// 將上傳內容保存至本機，讓 Application 透過既有介面使用 Storage，而不依賴實體路徑。
// 此類別只負責保存與刪除檔案；不驗證圖片格式、不建立資料庫紀錄，也不執行後續圖片分析。
public sealed class LocalFileStorageService : IFileStorageService
{
    // 保存正規化後的根目錄絕對路徑，後續目的檔案都由這個位置組合而成。
    private readonly string _storageRoot;
    // 保存「產生字串的函式」，不是已產生的識別碼；每次 Save 呼叫它時才取得識別碼。
    private readonly Func<string> _identifierFactory;

    // 正式使用時只需提供儲存根目錄；檔案識別碼由服務產生，避免受使用者檔名控制。
    // : this(...) 將初始化交給下方的雙參數建構子，兩個建構子共同初始化同一個物件。
    // Lambda 此時只被傳入；執行 _identifierFactory() 時才產生 32 個十六進位字元、無連字號的 GUID。
    public LocalFileStorageService(string storageRoot)
        : this(storageRoot, () => Guid.NewGuid().ToString("N"))
    {
    }

    // 固定 identifier 的 internal test seam，讓碰撞測試可重現，不擴充正式公開 API。
    // 正式呼叫傳入隨機 GUID 函式；測試可傳入回傳固定值的函式，穩定製造同名檔案。
    // UnitTests 透過 InternalsVisibleTo 存取此建構子，不必使用 Reflection 或新增公開測試入口。
    internal LocalFileStorageService(string storageRoot, Func<string> identifierFactory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageRoot);
        ArgumentNullException.ThrowIfNull(identifierFactory);
        // 在建立服務時把相對目錄轉成絕對路徑，避免後續工作目錄改變而指向不同位置。
        _storageRoot = Path.GetFullPath(storageRoot);
        _identifierFactory = identifierFactory;
    }

    // 只保存完整 bytes 並回傳相對參照；圖片是否合法由 FileValidationService 負責。
    // 成功才回傳 original/{guid}；失敗或取消則傳遞例外，並嘗試清除本次已建立的檔案。
    public async Task<string> SaveAsync(IUploadFile file, CancellationToken cancellationToken)
    {
        // 已取消的請求先停止，避免仍然開啟來源或建立目的檔案。
        cancellationToken.ThrowIfCancellationRequested();
        // Source 由呼叫端擁有，Storage 只借用，不負責 Dispose。
        // OpenReadStream 取得的是讀取內容的通道，不是把完整檔案載入記憶體，因此此處不使用 using。
        var source = file.OpenReadStream();
        // 完整檔案必須從頭複製，但結束後要還原位置，避免影響呼叫端後續使用。
        // CanSeek 代表能否調整讀取位置，不代表目前位置是 0；不能 Seek 時以 null 表示不做位置恢復。
        long? originalPosition = source.CanSeek ? source.Position : null;
        // 原始檔名不可信任，改用獨立 GUID；original/ 相對路徑不綁定機器或 OS 分隔符。
        // storedPath 是提供給上層保存的邏輯參照；destinationPath 才是本機實際使用的完整路徑。
        var storedPath = $"original/{_identifierFactory()}";
        var destinationPath = ResolvePath(storedPath);
        // 必須等 CreateNew 成功才設為 true，避免碰撞時把別人原本的檔案當成自己的檔案刪除。
        var created = false;
        // 記住最先發生的作業失敗，供後續清理與例外優先順序判斷使用。
        Exception? failure = null;

        try
        {
            // 可 Seek 的來源從頭複製；不可 Seek 的來源由呼叫端保證提供從頭開始的完整內容。
            if (originalPosition.HasValue)
                source.Position = 0;

            // 只建立目的檔案需要的父目錄；目錄已存在時也能正常使用。
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            cancellationToken.ThrowIfCancellationRequested();
            // CreateNew 避免先檢查再建立的競爭條件，保護既有內容不被覆寫。
            // 規格要求衝突直接失敗，不以 Retry 或重新命名隱藏此次衝突。
            // destination 是寫入檔案的 FileStream 物件，destinationPath 是它開啟的路徑。
            // 目的串流由本服務建立，所以用 await using 在成功或失敗離開區塊時釋放它。
            // FileShare.None 限制開啟期間的其他存取；81920 是緩衝區大小，不是檔案大小上限。
            await using (var destination = new FileStream(destinationPath, FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                created = true;
                // 分批從來源讀取 bytes 並寫入目的檔案，不需要把整份內容一次放進記憶體。
                // await 等待複製完成才往下執行；Token 讓非同步 I/O 能回應取消要求。
                await source.CopyToAsync(destination, cancellationToken);
                // 把目的串流尚未寫出的緩衝資料寫出；這不是關閉檔案，也不保證斷電後資料一定留存。
                await destination.FlushAsync(cancellationToken);
            }
        }
        catch (Exception exception)
        {
            // 僅記錄失敗以便清理，保留原例外，不包裝或轉成驗證結果。
            // 此 catch 不把失敗當成成功；throw; 會繼續傳遞同一例外，取消例外也包含在內。
            failure = exception;
            throw;
        }
        finally
        {
            // 寫入成功或拋出例外，都要嘗試還原借用的來源位置；目的串流此時已離開 await using。
            try
            {
                if (originalPosition.HasValue)
                    source.Position = originalPosition.Value;
            }
            catch (Exception) when (failure is not null)
            {
                // 恢復位置失敗不得取代原本的寫入失敗或取消例外。
                // when 條件限定「先前已經失敗」才忽略這個次要例外，保留真正的起始原因。
            }
            catch (Exception exception)
            {
                // 若先前寫入成功，恢復位置卻失敗，這就是本次 Save 的失敗原因，也必須觸發清理。
                failure = exception;
                throw;
            }
            finally
            {
                // 只有成功建立的檔案才屬於本次 Save；衝突留下的是既存檔案，不能刪除。
                if (created && failure is not null)
                {
                    try
                    {
                        File.Delete(destinationPath);
                    }
                    catch (Exception)
                    {
                        // 保留真正導致 Save 失敗的原因。
                        // 清理是盡力而為；刪除失敗可能留下檔案，但不能用清理例外蓋掉原本的失敗或取消。
                    }
                }
            }
        }

        return storedPath;
    }

    // 支援上傳失敗後的補償清理，重複刪除同一合法目標也應成功。
    // 先驗證 StoredPath，再刪除；即使檔案不存在，非法路徑仍不能被接受。
    public Task DeleteAsync(string storedPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolvePath(storedPath);
        try
        {
            File.Delete(path);
        }
        catch (DirectoryNotFoundException)
        {
            // 合法 StoredPath 的父目錄不存在時，目標檔案亦不存在，維持 Delete idempotency。
            // 只忽略此種不存在情況；權限不足等其他 I/O 例外仍原樣傳遞給呼叫端。
        }

        // File.Delete 是同步操作；執行完畢後回傳已完成的 Task，以符合既有非同步介面。
        return Task.CompletedTask;
    }

    // 限制只能操作 original/{Guid N}，避免合法根目錄內的其他檔案也被當成刪除目標。
    private string ResolvePath(string storedPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storedPath);
        const string prefix = "original/";
        // 必須是固定前綴加 32 字元 GUID N 格式，不允許副檔名、反斜線或額外路徑層級。
        // Ordinal 做精確字元比對；TryParseExact 驗證識別碼，out _ 表示不需要使用解析後的 GUID。
        if (storedPath.Length != prefix.Length + 32 ||
            !storedPath.StartsWith(prefix, StringComparison.Ordinal) ||
            !Guid.TryParseExact(storedPath.AsSpan(prefix.Length), "N", out _))
            throw new ArgumentException("Stored path must use original/{Guid N} format.", nameof(storedPath));

        // [prefix.Length..] 取得前綴後的識別碼，Path.Combine 負責組成本機作業系統使用的路徑。
        var fullPath = Path.GetFullPath(Path.Combine(_storageRoot, "original", storedPath[prefix.Length..]));
        // 再以相對路徑檢查目錄邊界；若需要往上一層（..）或仍是絕對路徑，就不在允許範圍內。
        var relative = Path.GetRelativePath(_storageRoot, fullPath);
        if (Path.IsPathRooted(relative) || relative == ".." ||
            relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            throw new ArgumentException("Stored path must remain within the storage root.", nameof(storedPath));

        return fullPath;
    }
}
