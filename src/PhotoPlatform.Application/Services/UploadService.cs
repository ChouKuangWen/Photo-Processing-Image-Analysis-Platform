using PhotoPlatform.Application.DTOs;
using PhotoPlatform.Application.Exceptions;
using PhotoPlatform.Application.Interfaces;
using PhotoPlatform.Domain.Entities;

namespace PhotoPlatform.Application.Services;

// UploadService 負責安排一次 Upload 的完整流程。
// 它只透過 Application Interfaces 使用 Validation、Storage、Database 與 Queue，不直接操作 EF Core、SQL Server、Channel 或實體檔案系統。
public sealed class UploadService(
    IFileValidationService validation,
    IFileStorageService storage,
    IUploadPersistence persistence,
    IProcessingQueue queue) : IUploadService
{
    // 執行一次完整 Upload。
    // 回傳 UploadResult 只代表圖片已成功建立並排入背景處理，不代表後續圖片分析工作已經完成。
    public async Task<UploadResult> UploadAsync(UploadRequest request, CancellationToken cancellationToken)
    {
        // 如果呼叫端已經要求取消，就直接停止後續 Upload 流程。
        cancellationToken.ThrowIfCancellationRequested();

        // 先檢查整個 Upload Request 是否合理。
        // 檔案本身的格式、大小、內容等規則，則交給 FileValidationService 處理。
        ArgumentNullException.ThrowIfNull(request);

        // 先把本次 Upload 的檔案固定成陣列，後續 Validation、Storage 和建立 Image 都使用同一份資料。
        // 取得 Request 裡的檔案並轉成陣列；如果 Files 是 null，就建立一個空陣列。
        var files = request.Files?.ToArray() ?? [];
        if (files.Length == 0)
            throw new UploadValidationException("INVALID_FILE", "The uploaded file is invalid.");
        if (!Enum.IsDefined(request.Workflow))
            throw new UploadValidationException("INVALID_WORKFLOW", "The specified workflow is not supported.");

        // 必須先把所有檔案驗證完成。
        // 只要有一個檔案不合法，就不應先留下任何實體檔案或 Database 資料。
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await validation.ValidateAsync(file, cancellationToken);

            // ErrorCode / ErrorMessage 已由 Validator 決定，
            // UploadService 只負責把驗證錯誤往上層傳遞。
            if (!result.IsValid)
                throw new UploadValidationException(result.ErrorCode!, result.ErrorMessage!);
        }

        // 記錄已經成功存下來的檔案路徑。
        // 如果 Commit 前發生錯誤，後面需要靠這份清單把檔案刪除。
        var paths = new List<string>();

        // Transaction 要等 Storage 完成後才建立，所以一開始可能還是 null。
        IUploadTransaction? transaction = null;

        // 用來記錄 Database 是否已經正式 Commit。
        // Commit 後就不能再用 Rollback 或刪除來源檔案的方式復原。
        var committed = false;

        // 記錄真正造成 Upload 失敗的主要 Exception。
        // 如果 finally 裡 Dispose 又失敗，不要讓 Dispose Exception 蓋掉原始錯誤。
        Exception? failure = null;
        try
        {
            // 先把所有來源檔案存進 Storage。
            // Storage 不屬於 Database Transaction，所以後續若 DB 失敗，
            // 必須另外呼叫 DeleteAsync 清理已經成功儲存的檔案。
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                paths.Add(await storage.SaveAsync(file, cancellationToken));
            }

            cancellationToken.ThrowIfCancellationRequested();

            // 檔案全部存好後才開始 Database Transaction，
            // 避免檔案 I/O 期間一直占用資料庫交易。
            transaction = await persistence.BeginTransactionAsync(cancellationToken);

            // 一次 Upload 共用同一個 Batch，可以用來管理這一批圖片後續的處理狀態。
            var batch = new Batch(Guid.NewGuid(), files.Length, DateTimeOffset.UtcNow);

            // 每個上傳檔案建立一個 Image Entity。
            // paths[index] 是前面 Storage 回傳的實際儲存位置。
            var images = files.Select((file, index) => new Image(batch.Id, file.FileName,
                paths[index], file.Length, file.MimeType, batch.CreatedAt)).ToArray();

            // Add 只是把 Batch / Images 加入 EF Core Tracking，
            // 此時還沒有真正 INSERT 到 SQL Server。
            persistence.AddBatch(batch);
            persistence.AddImages(images);


            /*第一次 SaveChanges：
              1.把 Batch / Images 寫進目前的 Transaction。
              2.SQL Server 會在這時產生 Image.Id，EF Core 再把正式 Id 回填到原本的 Image 物件。
              注意：這裡只是 SaveChanges，Transaction 還沒有 Commit。*/
            await persistence.SaveChangesAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            // ProcessingJob 必須知道自己屬於哪一張 Image，
            // 所以一定要等第一次 SaveChanges 取得正式 Image.Id 後才能建立。
            var jobs = images.Select(image => new ProcessingJob(image.Id, batch.Id,
                request.Workflow, batch.CreatedAt)).ToArray();

            // 將 Jobs 加入 EF Core Tracking。
            persistence.AddProcessingJobs(jobs);

            // 第二次 SaveChanges：
            // 把 ProcessingJobs 寫進和 Batch / Images 相同的 Transaction。
            // 此時雖然 SQL 已經執行，但整筆 Transaction 仍然可以 Rollback。
            await persistence.SaveChangesAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            // 正式 Commit Database Transaction。
            // 成功後 Batch、Images、ProcessingJobs 才正式成立，其他 DbContext 也能查到。
            await transaction.CommitAsync(cancellationToken);

            // 一定要在 Commit 成功後立刻記錄。後面如果 Queue 或 Cancellation 發生錯誤，就不能再當成「尚未 Commit」處理。
            committed = true;

             // Database 已 Commit，現在才把 Jobs 放進背景處理 Queue。
            foreach (var job in jobs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await queue.EnqueueAsync(job, cancellationToken);
            }
            // Upload 已完成建立與入列，
            // 回傳 Batch 資訊給上一層。
            return new UploadResult(batch.Id, batch.TotalCount, batch.Status);
        }
        catch (Exception exception)
        {
            failure = exception;

            // 只有 Database 尚未 Commit 時，才做 Rollback 與 Storage Cleanup。
            // 如果已經 Commit，代表資料已正式成立，不能因為後面的 Queue 失敗又把來源檔案刪除。
            if (!committed)
            {

                // 如果 Transaction 已經建立，就嘗試 Rollback。
                // Cleanup 使用 CancellationToken.None， 因為即使原本 Request 已取消，仍希望盡量完成必要清理。
                if (transaction is not null)
                {
                    try { await transaction.RollbackAsync(CancellationToken.None); }
                    catch (Exception)
                    {
                        // Rollback cleanup 失敗不能蓋掉原本真正的 Upload Exception。
                        // 繼續嘗試清理 Storage。
                    }
                }
                foreach (var path in paths)
                {
                    try { await storage.DeleteAsync(path, CancellationToken.None); }
                    catch (Exception)
                    {
                        // 單一檔案刪除失敗時繼續處理其他檔案， 並保留原本真正造成 Upload 失敗的 Exception。
                    }
                }
            }
            throw;
        }
        finally
        {
            // 不論 Upload 成功或失敗，只要建立過 Transaction，最後都要釋放 Transaction resource。
            // UploadTransaction.DisposeAsync 只處理 Transaction，不會 Dispose 外部 DI Scope 管理的 DbContext。
            if (transaction is not null)
            {
                try { await transaction.DisposeAsync(); }
                catch (Exception) when (failure is not null)
                {
                    // 如果 Upload 本來就已經失敗，
                    // Dispose 的次要錯誤不能取代真正造成 Upload 失敗的 Exception。
                }
            }
        }
    }
}
