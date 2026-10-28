using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PhotoPlatform.Application.DTOs;
using PhotoPlatform.Application.Exceptions;
using PhotoPlatform.Application.Interfaces;
using PhotoPlatform.Domain.Enums;
using PhotoPlatform.Domain.Policies;

namespace PhotoPlatform.Infrastructure.Persistence;

// Binding 只由 Upload 建立，這裡不選主、不補資料，也不更新 aggregate。
public sealed class ProcessingBindingPersistence(PhotoPlatformDbContext db) : IProcessingPrerequisiteService
{
    public async Task<ProcessingPrerequisiteAssessment> AssessBatchAsync(Guid batchId, CancellationToken cancellationToken)
    {
        try
        {
            // 多次查詢要看到同一組資料。後續 writer 可在既有 Serializable 交易內呼叫，
            // 讓圖片集合的鎖保留到自己的 Commit；不接受較弱隔離或把舊 ready 結果當成許可。
            if (db.Database.CurrentTransaction is { } current)
            {
                if (current.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
                    throw new InvalidOperationException("Prerequisite checks require a serializable transaction.");
                return await AssessCoreAsync(batchId, cancellationToken);
            }
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var result = await AssessCoreAsync(batchId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (DbException)
        {
            throw new ProcessingPrerequisiteUnavailableException();
        }
    }

    private async Task<ProcessingPrerequisiteAssessment> AssessCoreAsync(Guid batchId, CancellationToken cancellationToken)
    {
        var reasons = new List<ProcessingPrerequisiteReason>();
        var batch = await db.Batches.AsNoTracking().SingleOrDefaultAsync(value => value.Id == batchId, cancellationToken);
        if (batch is null)
            return new(batchId, false, DateTimeOffset.UtcNow, false, [], 0, false, null);
        var images = await db.Images.AsNoTracking().Where(value => value.BatchId == batchId).ToListAsync(cancellationToken);
        // 同時抓出「聲稱屬於這個 Batch」及「實際屬於這批圖片」的綁定，避免錯誤 BatchId 被漏查。
        var imageQuery = db.Images.Where(value => value.BatchId == batchId).Select(value => value.Id);
        var bindings = await db.ImageProcessingBindings.AsNoTracking()
            .Where(value => value.BatchId == batchId || imageQuery.Contains(value.ImageId)).ToListAsync(cancellationToken);
        var bindingQuery = db.ImageProcessingBindings
            .Where(value => value.BatchId == batchId || imageQuery.Contains(value.ImageId)).Select(value => value.PrimaryJobId);
        var jobs = await db.ProcessingJobs.AsNoTracking().Where(value => bindingQuery.Contains(value.Id))
            .ToDictionaryAsync(value => value.Id, cancellationToken);
        var byImage = bindings.ToDictionary(value => value.ImageId);
        var imageIds = images.Select(value => value.Id).ToHashSet();
        if (images.Count != batch.TotalCount)
            reasons.Add(new("COLLECTION_UNCONFIRMED"));
        foreach (var binding in bindings.Where(value => !imageIds.Contains(value.ImageId)))
            reasons.Add(new("RELATIONSHIP_MISMATCH"));
        var statuses = new List<ProcessingJobStatus>();
        foreach (var image in images)
        {
            if (!byImage.TryGetValue(image.Id, out var binding))
            {
                // 即使只有一筆 Job，也不能用猜測補回歷史來源。
                reasons.Add(new("BINDING_MISSING", image.Id));
                continue;
            }
            if (!jobs.TryGetValue(binding.PrimaryJobId, out var job) || job.ImageId != image.Id ||
                job.BatchId != batchId || binding.BatchId != batchId || job.Workflow != binding.PrimaryWorkflow ||
                !Enum.IsDefined(job.Workflow) || !Enum.IsDefined(job.Status))
            {
                reasons.Add(new("RELATIONSHIP_MISMATCH", image.Id));
                continue;
            }
            statuses.Add(job.Status);
            if (binding.PrimaryStatus != job.Status || image.Status != job.Status.ToString())
                reasons.Add(new("AGGREGATE_INCONSISTENT", image.Id, job.Id));
        }
        if (statuses.Count == images.Count && images.Count == batch.TotalCount)
        {
            var expected = PrimaryAggregatePolicy.Calculate(batch.TotalCount, statuses);
            var terminal = expected.Status is "Completed" or "Failed";
            if (batch.ProcessedCount != expected.Processed || batch.SuccessCount != expected.Success ||
                batch.FailedCount != expected.Failed || batch.Status != expected.Status ||
                batch.CompletedAt.HasValue != terminal)
                reasons.Add(new("AGGREGATE_INCONSISTENT"));
        }
        // 完整核對之後才限制顯示筆數；後面第 101 張圖片有問題也不能被當成 ready。
        var display = reasons.OrderBy(value => value.ReasonCode, StringComparer.Ordinal)
            .ThenBy(value => value.ImageId).ThenBy(value => value.JobId).Take(100).ToArray();
        return new(batchId, true, DateTimeOffset.UtcNow, reasons.Count == 0, display, reasons.Count,
            reasons.Count > display.Length, reasons.Count == 0 ? null : "請確認交接資料與批次一致性；本查詢不修復資料。");
    }
}
