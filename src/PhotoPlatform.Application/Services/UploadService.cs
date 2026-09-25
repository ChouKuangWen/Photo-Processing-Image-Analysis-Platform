using Microsoft.Extensions.Logging;
using PhotoPlatform.Application.DTOs;
using PhotoPlatform.Application.Exceptions;
using PhotoPlatform.Application.Interfaces;
using PhotoPlatform.Domain.Entities;

namespace PhotoPlatform.Application.Services;

// 負責協調完整 Upload 流程。
// 只使用 Application 定義的介面，不直接操作 EF Core、HTTP、實體檔案系統或 Channel。
public sealed class UploadService(
    IFileValidationService validation,
    IFileStorageService storage,
    IUploadPersistence persistence,
    IProcessingQueue queue,
    ILogger<UploadService> logger) : IUploadService
{
    public async Task<UploadResult> UploadAsync(UploadRequest request, CancellationToken cancellationToken)
    {
        // 記錄「這次 Upload 已成功存下來的檔案」。
        // 如果 Commit 前失敗，就只刪除這些檔案。
        var paths = new List<string>();

        // Storage 完成後才建立 DB Transaction，所以一開始是 null。
        IUploadTransaction? transaction = null;

        // 記錄資料庫是否已正式 Commit。
        // false：失敗時需要 Rollback / 刪檔。
        // true ：資料已正式成立，不能再 Rollback 或刪檔。
        var committed = false;

        // 記錄 Upload 原本是否已經發生錯誤。
        // 用來避免 finally 的 Dispose 錯誤蓋掉真正的 Upload 錯誤。
        Exception? failure = null;

        // 記住本次 Validation Exception。
        // catch 時可辨識這是不是「使用者輸入驗證錯誤」。
        UploadValidationException? validationFailure = null;

        // 以下 ID 主要提供安全 Logging 使用。
        Guid? batchId = null;
        long? imageId = null;
        long? jobId = null;

        // 記錄目前執行到哪一個階段。
        // 如果下一步發生錯誤，就知道錯在哪裡。
        var stage = UploadFailureStage.RequestValidation;

        try
        {
            // Request 已取消就立即停止。
            cancellationToken.ThrowIfCancellationRequested();
            // Request 本身不能是 null。
            ArgumentNullException.ThrowIfNull(request);
            // 固定本次 Upload 的檔案清單。
            var files = request.Files?.ToArray() ?? [];
            // 至少需要一個檔案。
            if (files.Length == 0)
                throw validationFailure = new UploadValidationException("INVALID_FILE", "The uploaded file is invalid.");
            // Workflow 必須是系統支援的 enum。
            if (!Enum.IsDefined(request.Workflow))
                throw validationFailure = new UploadValidationException("INVALID_WORKFLOW", "The specified workflow is not supported.");

            // 1. 驗證所有檔案
            // 先驗證全部檔案。
            // 只有全部合法，才進入 Storage，避免留下垃圾檔案。
            stage = UploadFailureStage.FileValidation;
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await validation.ValidateAsync(file, cancellationToken);
                if (!result.IsValid)
                    throw validationFailure = new UploadValidationException(result.ErrorCode!, result.ErrorMessage!, stage);
            }

            // 2. 儲存原始圖片
            // TASK-08 的順序維持不變
            // 全部 Storage 完成後，才開始 Database Transaction。
            stage = UploadFailureStage.StorageSave;
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // SaveAsync 成功後才把路徑記下來。
                // 如果後面失敗，就知道要刪哪些檔案。
                paths.Add(await storage.SaveAsync(file, cancellationToken));
            }

            // 3. 開始 Database Transaction
            stage = UploadFailureStage.DatabaseBegin;
            cancellationToken.ThrowIfCancellationRequested();
            transaction = await persistence.BeginTransactionAsync(cancellationToken);

            // 4. 建立 Batch / Image
            // 這段主要是在記憶體建立 Entity，還沒真正寫入 Database。
            stage = UploadFailureStage.Unexpected;
            var batch = new Batch(Guid.NewGuid(), files.Length, DateTimeOffset.UtcNow);
            batchId = batch.Id;
            var images = files.Select((file, index) => new Image(batch.Id, file.FileName,
                paths[index], file.Length, file.MimeType, batch.CreatedAt)).ToArray();

            // 5. 第一階段 Database Save
            stage = UploadFailureStage.DatabaseSave;
            persistence.AddBatch(batch);
            persistence.AddImages(images);
            // 第一次 SaveChanges 的重要目的：讓 Database 產生 Image Identity，
            // 並把正式 Image.Id 回填到 Entity。此時還沒有 Commit，所以仍然可以 Rollback。
            await persistence.SaveChangesAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            // 6. 建立 ProcessingJob
            // ProcessingJob 需要真正的 Image.Id，所以必須等第一次 SaveChanges 完成後才能建立。
            stage = UploadFailureStage.Unexpected;
            var jobs = images.Select(image => new ProcessingJob(image.Id, batch.Id,
                request.Workflow, batch.CreatedAt)).ToArray();

            // 7. 第二階段 Database Save
            stage = UploadFailureStage.DatabaseSave;
            persistence.AddProcessingJobs(jobs);
            await persistence.SaveChangesAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            // 8. Commit Database
            stage = UploadFailureStage.DatabaseCommit;
            await transaction.CommitAsync(cancellationToken);
            // 這是最重要的界線。
            // Commit 成功後，Batch / Image / Job 已正式成立。
            // 後面即使 Queue 或 Cancellation 出錯，也不能再 Rollback 或刪除原始圖片。
            committed = true;

            // 9. 加入 Processing Queue
            stage = UploadFailureStage.QueueEnqueue;
            foreach (var job in jobs)
            {
                // 保存目前正在處理的 ID，供 Log 使用。
                imageId = job.ImageId;
                jobId = job.Id;
                cancellationToken.ThrowIfCancellationRequested();
                await queue.EnqueueAsync(job, cancellationToken);
            }
            // Upload 接受流程完成。
            // 不代表後續圖片分析已經完成。
            return new UploadResult(batch.Id, batch.TotalCount, batch.Status);
        }
        catch (Exception exception)
        {
            // 保存原始錯誤。
            // finally 的 Dispose 如果又失敗，不能把這個錯誤蓋掉。
            failure = exception;

            // 判斷錯誤大分類。
            // Validation → 使用者輸入問題
            // Storage    → 儲存檔案失敗
            // Internal   → DB、Transaction、Queue 等其他系統錯誤
            var category = ReferenceEquals(exception, validationFailure)
                ? UploadFailureCategory.Validation
                : stage == UploadFailureStage.StorageSave
                    ? UploadFailureCategory.Storage : UploadFailureCategory.Internal;
            // 安全記錄錯誤。
            // 不把原始 Exception Message / Stack Trace 直接寫進 Log。
            LogFailure(stage, exception is OperationCanceledException ? "Canceled" : category.ToString(),
                batchId, imageId, jobId);

            // Commit 前失敗才需要善後
            if (!committed)
            {
                // 如果 Transaction 已經建立，嘗試 Rollback。
                // 如果 Transaction 還沒建立，就不能假裝執行 Rollback。
                if (transaction is not null)
                {
                    try
                    {   // Cleanup 不使用原 Request token。
                        // 即使使用者已取消 Request，也要盡量完成 Rollback。
                        await transaction.RollbackAsync(CancellationToken.None); }
                    catch (Exception)
                    {
                        // Rollback 自己失敗只記錄，不能蓋掉原本真正造成 Upload 失敗的錯誤。
                        LogFailure(UploadFailureStage.DatabaseRollback, "Internal", batchId);
                    }
                }
                // 刪除本次 Upload 已經成功存下來的檔案。
                foreach (var path in paths)
                {
                    try { await storage.DeleteAsync(path, CancellationToken.None); }
                    catch (Exception)
                    {
                        // 一個檔案刪除失敗，還是要繼續刪其他檔案。
                        // Compensation 錯誤也不能蓋掉原始錯誤。
                        LogFailure(UploadFailureStage.StorageCompensation, "Storage", batchId, storageKey: path);
                    }
                }
            }

            // Cancellation 保持原本的 OperationCanceledException。
            // Validation 也保持 UploadValidationException， 讓 TASK-10 API Layer 可以讀 ErrorCode / ErrorMessage。
            if (exception is OperationCanceledException || ReferenceEquals(exception, validationFailure))
                throw;

            // 其他 Storage / DB / Queue 錯誤
            // 統一包成 UploadFailureException，並記錄當時失敗的 Stage。
            throw new UploadFailureException(stage, exception);
        }

        // 最後一定要釋放 Transaction
        finally
        {
            if (transaction is not null)
            {
                try { await transaction.DisposeAsync(); }
                catch (Exception exception)
                {
                    // Transaction Dispose 失敗也需要記錄。
                    LogFailure(UploadFailureStage.TransactionDispose, "Internal", batchId);
                    // 如果 Upload 原本已經有錯誤，保留原本錯誤，不讓 Dispose 錯誤蓋掉它。
                    if (failure is null)
                    {
                        // 如果 Dispose 本身是取消，保留 Cancellation 原本語意。
                        if (exception is OperationCanceledException)
                            throw;

                        // 如果 Upload 本來成功，只有 Dispose 自己失敗，才把 Dispose Failure 往上丟。
                        throw new UploadFailureException(UploadFailureStage.TransactionDispose, exception);
                    }
                }
            }
        }
    }
    // 安全 Failure Logging
    private void LogFailure(UploadFailureStage stage, string code, Guid? batchId,
        long? imageId = null, long? jobId = null, string? storageKey = null)
    {
        // Storage Log 只允許系統定義的 logical key  original/{32字元 GUID}
        // 如果收到 OS 路徑或其他奇怪內容，就不寫進 Log。
        var safeKey = storageKey is { Length: 41 } && storageKey.StartsWith("original/", StringComparison.Ordinal)
            && Guid.TryParseExact(storageKey.AsSpan(9), "N", out _) ? storageKey : null;
        try
        {
            // 只記錄安全的結構化資訊。
            // 不傳原始 Exception，避免 Stack Trace、SQL Detail、實體路徑等資訊進入 Log。
            logger.LogError(new EventId(9001, "UploadFailure"),
                "Upload failure at {FailureStage}; code {ErrorCode}; batch {BatchId}; image {ImageId}; job {JobId}; storage {StorageKey}",
                stage, code, batchId, imageId, jobId, safeKey);
        }
        catch (Exception)
        {
            // Logger 自己如果壞掉，也不能影響 Upload cleanup，更不能把原本真正的 Upload 錯誤蓋掉。
        }
    }
}
