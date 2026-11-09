using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using PhotoPlatform.Application.Processing;
using PhotoPlatform.Domain.Entities;
using PhotoPlatform.Domain.Enums;
using PhotoPlatform.Domain.Policies;
using PhotoPlatform.Infrastructure.Processing;

namespace PhotoPlatform.Infrastructure.Persistence;

// SQL persistence only. Never registered as an API service and never invokes an adapter.
internal sealed class ProcessingExecutionStore(IDbContextFactory<PhotoPlatformDbContext> factory,
    Func<string, Task>? fault = null)
{
    internal Task<StartedProcessingInvocation?> ClaimAsync(ExecutionAdmission admission, CancellationToken token) =>
        StartAsync(admission, true, token);
    internal Task<StartedProcessingInvocation?> StartNextAsync(ExecutionAdmission admission, CancellationToken token) =>
        StartAsync(admission, false, token);

    private async Task<StartedProcessingInvocation?> StartAsync(ExecutionAdmission admission, bool claim, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(admission);
        token.ThrowIfCancellationRequested();
        ValidateAdmission(admission);
        StartedProcessingInvocation? identity = null;
        var commitStarted = false;
        try
        {
            await using var db = await factory.CreateDbContextAsync(token);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            // One lock order for every writer: Manifest -> Batch -> Job -> Run -> checkpoint/log.
            await ManifestChecksumLock.VerifyAsync(db, admission.JobId, admission.Manifest.ManifestId,
                admission.Manifest.Checksum, 5000, token);
            var batch = await db.Batches.FromSqlInterpolated($"SELECT * FROM batches WITH (UPDLOCK,HOLDLOCK) WHERE Id={admission.BatchId}")
                .SingleOrDefaultAsync(token);
            var job = await db.ProcessingJobs.FromSqlInterpolated($"SELECT * FROM processing_jobs WITH (UPDLOCK,HOLDLOCK) WHERE Id={admission.JobId}")
                .SingleOrDefaultAsync(token);
            Require(batch is not null && job is not null, admission, FrozenInputFailureReason.Missing);
            Require(job!.ImageId == admission.ImageId && job.BatchId == admission.BatchId && job.Workflow == admission.Workflow, admission);
            if (job.Status != (claim ? ProcessingJobStatus.Pending : ProcessingJobStatus.Processing))
                return null;
            var image = await db.Images.SingleAsync(value => value.Id == job.ImageId, token);
            Require(image.BatchId == job.BatchId && image.StoredPath == admission.StoredPath &&
                image.FileSize == admission.Original.ContentLength, admission);
            var primary = await db.ImageProcessingBindings.SingleOrDefaultAsync(value => value.ImageId == image.Id, token);
            Require(primary is not null && primary.BatchId == job.BatchId, admission, FrozenInputFailureReason.Missing);
            var primaryJob = await db.ProcessingJobs.AsNoTracking().SingleOrDefaultAsync(value => value.Id == primary!.PrimaryJobId, token);
            Require(primaryJob is not null && primaryJob.ImageId == image.Id && primaryJob.BatchId == job.BatchId &&
                primaryJob.Workflow == primary!.PrimaryWorkflow && primaryJob.Status == primary.PrimaryStatus &&
                image.Status == primary.PrimaryStatus.ToString(), admission);
            var isPrimary = primary!.PrimaryJobId == job.Id;
            if (isPrimary)
                Require((await new ProcessingBindingPersistence(db).AssessBatchAsync(job.BatchId, token)).Ready, admission);

            var runs = await db.ProcessingRuns.Where(value => value.JobId == job.Id).OrderBy(value => value.RunNo).ToListAsync(token);
            Require(runs.Select((value, index) => value.RunNo == index + 1 && value.WorkflowVersion == admission.Manifest.WorkflowVersion)
                .All(value => value), admission);
            var unclosed = runs.Where(value => value.Status is ProcessingRunStatus.Pending or ProcessingRunStatus.Active).ToArray();
            Require(unclosed.Length <= 1, admission);
            var run = unclosed.SingleOrDefault();
            var first = runs.Count == 0;
            if (first)
            {
                Require(claim && admission.PermitsFirstClaim, admission, FrozenInputFailureReason.VersionUnavailable);
                Require(job.StartedAt is null && job.CompletedAt is null && job.RetryCount == 0 &&
                    job.ErrorCode is null && job.ErrorMessage is null, admission);
                Require(!await db.ProcessingRuleManifestBindings.AnyAsync(value => value.JobId == job.Id, token) &&
                    !await db.ProcessingOriginalInputs.AnyAsync(value => value.JobId == job.Id, token), admission);
            }
            else
            {
                Require(run is not null && run.Status == (claim ? ProcessingRunStatus.Pending : ProcessingRunStatus.Active) &&
                    run.WorkflowVersion == admission.Manifest.WorkflowVersion && run.CompletedAt is null && job.CompletedAt is null &&
                    (run.StartedAt.HasValue ? job.StartedAt == run.StartedAt : job.StartedAt is null) &&
                    (claim || run.StartedAt.HasValue), admission);
                Require(run!.RunNo == runs[^1].RunNo && runs.Where(value => value.Id != run.Id)
                    .All(value => value.Status == ProcessingRunStatus.Failed), admission);
                if (run.Reason == ProcessingRunReason.Initial)
                    Require(run.StartedAt.HasValue, admission);
                if (claim && run.Reason == ProcessingRunReason.ManualRetry && !run.StartedAt.HasValue)
                {
                    var previous = runs[run.RunNo - 2];
                    var failed = await db.ProcessingStepCheckpoints.Where(value => value.RunId == previous.Id &&
                        value.Status == ProcessingCheckpointStatus.Failed).OrderByDescending(value => value.StepOrder).FirstOrDefaultAsync(token);
                    Require(failed is not null && failed.CompletedAt.HasValue && failed.ErrorClassification == ProcessingErrorClassification.Transient &&
                        failed.RetryOrdinal == 3, admission);
                    var finalLog = await db.ProcessingLogs.Where(value => value.StepCheckpointId == failed!.Id)
                        .OrderByDescending(value => value.AttemptNo).FirstOrDefaultAsync(token);
                    Require(finalLog is not null && finalLog.Status == ProcessingLogStatus.Failed && finalLog.RetryOrdinal == 3 &&
                        finalLog.CompletedAt.HasValue && finalLog.JobId == job.Id && finalLog.ImageId == image.Id, admission);
                }
            }
            await BindAsync(db, admission, image, first, token);
            var now = await db.Database.SqlQueryRaw<DateTimeOffset>(
                "SELECT TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00') AS [Value]").SingleAsync(token);
            Require(now >= job.CreatedAt && (!job.StartedAt.HasValue || now >= job.StartedAt) &&
                (run is null || (now >= run.CreatedAt && (!run.StartedAt.HasValue || now >= run.StartedAt))),
                admission, FrozenInputFailureReason.SourceUnavailable);
            if (claim)
            {
                var affected = await db.ProcessingJobs.Where(value => value.Id == job.Id && value.Status == ProcessingJobStatus.Pending)
                    .ExecuteUpdateAsync(update => update.SetProperty(value => value.Status, ProcessingJobStatus.Processing)
                        .SetProperty(value => value.StartedAt, value => value.StartedAt ?? now), token);
                if (affected != 1) return null;
                await db.Entry(job).ReloadAsync(token);
                await FaultAsync("claim");
            }
            if (first)
            {
                run = new ProcessingRun(job.Id, 1, ProcessingRunReason.Initial, admission.Manifest.WorkflowVersion, now);
                db.ProcessingRuns.Add(run);
            }
            else if (claim) run!.Activate(now);
            await db.SaveChangesAsync(token);
            await FaultAsync("run");
            var checkpoints = await db.ProcessingStepCheckpoints.Where(value => value.RunId == run!.Id)
                .OrderBy(value => value.StepOrder).ToListAsync(token);
            Require(checkpoints.All(value => value.StepOrder <= admission.Manifest.Steps.Count && value.StepOrder > 0 &&
                admission.Manifest.Steps[value.StepOrder - 1].Step == value.Step &&
                admission.Manifest.Steps[value.StepOrder - 1].RuleVersion == value.RuleVersion), admission);
            var manifestStep = admission.Manifest.Steps.Single(value => value.Step == admission.Input.Step);
            var predecessors = checkpoints.Where(value => value.StepOrder < manifestStep.StepOrder).ToArray();
            Require(predecessors.Length == manifestStep.StepOrder - 1 && predecessors.All(value =>
                value.Status == ProcessingCheckpointStatus.Completed && value.ResultStatus.HasValue && value.CompletedAt.HasValue), admission);
            Require(admission.Sources.Count == predecessors.Length, admission);
            foreach (var previous in predecessors)
            {
                var expected = admission.Manifest.Steps[previous.StepOrder - 1];
                var sources = admission.Sources.Where(value => value.CheckpointId == previous.Id).ToArray();
                Require(previous.Step == expected.Step && previous.RuleVersion == expected.RuleVersion && sources.Length == 1 &&
                    sources[0].Step == previous.Step && sources[0].RuleVersion == previous.RuleVersion &&
                    sources[0].InputFingerprint == previous.InputFingerprint && sources[0].ResultReference == previous.ResultReference &&
                    sources[0].ResultStatus == previous.ResultStatus, admission);
            }
            Require(!checkpoints.Any(value => value.StepOrder > manifestStep.StepOrder && value.Status != ProcessingCheckpointStatus.Pending), admission);
            var checkpointIds = checkpoints.Select(value => value.Id).ToArray();
            if (await db.ProcessingLogs.AnyAsync(value => checkpointIds.Contains(value.StepCheckpointId) && value.Status == ProcessingLogStatus.Running, token))
                return null;
            var checkpoint = checkpoints.SingleOrDefault(value => value.Step == admission.Input.Step);
            if (checkpoint is null)
            {
                Require(!checkpoints.Any(value => value.StepOrder == manifestStep.StepOrder), admission);
                checkpoint = new ProcessingStepCheckpoint(run!.Id, manifestStep.Step, manifestStep.StepOrder,
                    manifestStep.RuleVersion, admission.Input.InputFingerprint);
                db.ProcessingStepCheckpoints.Add(checkpoint);
                await db.SaveChangesAsync(token);
                await FaultAsync("checkpoint-created");
            }
            Require(checkpoint.StepOrder == manifestStep.StepOrder && checkpoint.RuleVersion == manifestStep.RuleVersion &&
                checkpoint.InputFingerprint == admission.Input.InputFingerprint, admission);
            if (checkpoint.Status is not (ProcessingCheckpointStatus.Pending or ProcessingCheckpointStatus.RetryWaiting)) return null;
            Require(checkpoint.CompletedAt is null, admission);
            if (db.Entry(checkpoint).Property<string>("DependencyWaitStatus").CurrentValue is not null)
                throw new FrozenInputUnavailableException(admission.JobId, FrozenInputFailureReason.VersionUnavailable);
            if (checkpoint.NextRetryAt > now) return null;
            var logs = await db.ProcessingLogs.Where(value => value.StepCheckpointId == checkpoint.Id).OrderBy(value => value.AttemptNo).ToListAsync(token);
            Require((!checkpoint.StartedAt.HasValue || now >= checkpoint.StartedAt) &&
                logs.All(value => now >= value.StartedAt && (!value.CompletedAt.HasValue || now >= value.CompletedAt)),
                admission, FrozenInputFailureReason.SourceUnavailable);
            var attempt = AllocateAttempt(checkpoint, logs, admission);
            checkpoint.Start(attempt, now);
            Require(job.RetryCount >= 0 && (!attempt.StartsRetrySlot || job.RetryCount < int.MaxValue), admission);
            job.RecordRetryStart(attempt);
            await db.SaveChangesAsync(token);
            await FaultAsync("checkpoint");
            var log = new ProcessingLog(checkpoint.Id, job.Id, image.Id, attempt, now);
            db.ProcessingLogs.Add(log);
            await db.SaveChangesAsync(token);
            await FaultAsync("invocation");
            if (isPrimary)
            {
                db.Entry(primary).Property(value => value.PrimaryStatus).CurrentValue = job.Status;
                db.Entry(primary).Property(value => value.UpdatedAt).CurrentValue = now;
                await db.SaveChangesAsync(token);
                await FaultAsync("primary-projection");
                db.Entry(image).Property(value => value.Status).CurrentValue = job.Status.ToString();
                db.Entry(image).Property(value => value.UpdatedAt).CurrentValue = now;
                await db.SaveChangesAsync(token);
                await FaultAsync("image-projection");
                var ids = db.ImageProcessingBindings.Where(value => value.BatchId == batch!.Id).Select(value => value.PrimaryJobId);
                var statuses = await db.ProcessingJobs.Where(value => ids.Contains(value.Id)).Select(value => value.Status).ToArrayAsync(token);
                var aggregate = PrimaryAggregatePolicy.Calculate(batch!.TotalCount, statuses);
                db.Entry(batch).Property(value => value.Status).CurrentValue = aggregate.Status;
                db.Entry(batch).Property(value => value.ProcessedCount).CurrentValue = aggregate.Processed;
                db.Entry(batch).Property(value => value.SuccessCount).CurrentValue = aggregate.Success;
                db.Entry(batch).Property(value => value.FailedCount).CurrentValue = aggregate.Failed;
                db.Entry(batch).Property(value => value.CompletedAt).CurrentValue =
                    PrimaryAggregatePolicy.CompletionTime(batch.Status, batch.CompletedAt, aggregate, now);
                await db.SaveChangesAsync(token);
            }
            await FaultAsync("aggregate");
            identity = new(job.Id, run!.Id, checkpoint.Id, log.Id, attempt.AttemptNo, attempt.RetryOrdinal, attempt.ResumeNo);
            token.ThrowIfCancellationRequested();
            commitStarted = true;
            await FaultAsync("before-commit");
            await transaction.CommitAsync(token);
            await FaultAsync("after-commit");
            token.ThrowIfCancellationRequested();
            return identity;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw new OperationCanceledException(token);
        }
        catch (Exception error) when (error is DbException or DbUpdateException)
        {
            token.ThrowIfCancellationRequested();
            if (commitStarted && identity is not null && await ConfirmAsync(identity, admission, token)) return identity;
            throw new FrozenInputUnavailableException(admission.JobId, FrozenInputFailureReason.SourceUnavailable);
        }
    }

    private async Task BindAsync(PhotoPlatformDbContext db, ExecutionAdmission admission, Image image, bool first, CancellationToken token)
    {
        var manifest = await db.ProcessingRuleManifestBindings.SingleOrDefaultAsync(value => value.JobId == admission.JobId, token);
        var original = await db.ProcessingOriginalInputs.SingleOrDefaultAsync(value => value.JobId == admission.JobId, token);
        var steps = await db.ProcessingOriginalStepInputs.Where(value => value.JobId == admission.JobId).ToListAsync(token);
        if (first)
        {
            Require(manifest is null && original is null && steps.Count == 0, admission);
            db.ProcessingRuleManifestBindings.Add(new(admission.JobId, admission.Manifest.WorkflowVersion,
                admission.Manifest.ManifestId, admission.Manifest.Checksum));
            await db.SaveChangesAsync(token);
            await FaultAsync("manifest-binding");
            db.ProcessingOriginalInputs.Add(admission.Original);
            await db.SaveChangesAsync(token);
            await FaultAsync("original-binding");
        }
        else
        {
            Require(manifest is not null && original is not null, admission, FrozenInputFailureReason.Missing);
            Require(manifest!.ManifestId == admission.Manifest.ManifestId && manifest.ManifestChecksum == admission.Manifest.Checksum &&
                manifest.WorkflowVersion == admission.Manifest.WorkflowVersion && original!.ContentVersion == admission.Original.ContentVersion &&
                original.ContentLength == admission.Original.ContentLength && original.WorkflowVersion == admission.Original.WorkflowVersion, admission);
            Require(steps.Count == admission.OriginalSteps.Count, admission, FrozenInputFailureReason.Missing);
        }
        foreach (var input in admission.OriginalSteps)
        {
            if (first)
            {
                Require(input.Step != "EXIF_PARSING" || input.UploadCreatedAt == image.CreatedAt, admission);
                db.ProcessingOriginalStepInputs.Add(input);
                await db.SaveChangesAsync(token);
                await FaultAsync("step-binding:" + input.Step);
            }
            else
            {
                var saved = steps.SingleOrDefault(value => value.Step == input.Step);
                Require(saved is not null && saved.RuleVersion == input.RuleVersion && saved.UploadCreatedAt == input.UploadCreatedAt &&
                    saved.InputFingerprint == input.InputFingerprint, admission);
            }
        }
        await db.SaveChangesAsync(token);
        // VARCHAR persistence must round-trip exact identity; never accept lossy code-page conversion.
        var persistedManifest = await db.ProcessingRuleManifestBindings.AsNoTracking().SingleAsync(value => value.JobId == admission.JobId, token);
        var persistedOriginal = await db.ProcessingOriginalInputs.AsNoTracking().SingleAsync(value => value.JobId == admission.JobId, token);
        var persistedSteps = await db.ProcessingOriginalStepInputs.AsNoTracking().Where(value => value.JobId == admission.JobId).ToListAsync(token);
        Require(persistedManifest.ManifestId == admission.Manifest.ManifestId && persistedManifest.ManifestChecksum == admission.Manifest.Checksum &&
            persistedManifest.WorkflowVersion == admission.Manifest.WorkflowVersion && persistedOriginal.WorkflowVersion == admission.Original.WorkflowVersion &&
            persistedOriginal.ContentVersion == admission.Original.ContentVersion && persistedOriginal.ContentLength == admission.Original.ContentLength &&
            persistedSteps.Count == admission.OriginalSteps.Count && admission.OriginalSteps.All(expected => persistedSteps.Any(saved =>
                saved.Step == expected.Step && saved.RuleVersion == expected.RuleVersion && saved.InputFingerprint == expected.InputFingerprint &&
                saved.UploadCreatedAt == expected.UploadCreatedAt)), admission);
        await FaultAsync("step-bindings");
    }

    private static ProcessingAttemptIdentity AllocateAttempt(ProcessingStepCheckpoint checkpoint, List<ProcessingLog> logs, ExecutionAdmission admission)
    {
        if (logs.Count == 0)
        {
            Require(checkpoint.RetryOrdinal == 0 && checkpoint.Status == ProcessingCheckpointStatus.Pending, admission);
            return ProcessingAttemptIdentity.Initial();
        }
        var last = logs[^1];
        Require(logs.Select((value, index) => value.AttemptNo == index + 1 && value.JobId == admission.JobId &&
            value.ImageId == admission.ImageId && value.CompletedAt.HasValue && value.ResumeNo >= 0 && value.RetryOrdinal is >= 0 and <= 3).All(value => value), admission);
        Require(logs[0].RetryOrdinal == 0 && logs[0].ResumeNo == 0 && last.AttemptNo < int.MaxValue, admission);
        for (var index = 1; index < logs.Count; index++)
        {
            var previous = logs[index - 1];
            var current = logs[index];
            Require((current.RetryOrdinal == previous.RetryOrdinal && (long)current.ResumeNo == (long)previous.ResumeNo + 1 &&
                previous.Status == ProcessingLogStatus.Interrupted) ||
                (current.RetryOrdinal == previous.RetryOrdinal + 1 && current.ResumeNo == 0 && previous.Status == ProcessingLogStatus.Failed), admission);
        }
        var identity = new ProcessingAttemptIdentity(last.AttemptNo, last.RetryOrdinal, last.ResumeNo);
        if (last.RetryOrdinal == checkpoint.RetryOrdinal)
        {
            Require(last.Status == ProcessingLogStatus.Interrupted && checkpoint.Status == ProcessingCheckpointStatus.Pending, admission);
            Require(last.ResumeNo < int.MaxValue, admission);
            return identity.Resume();
        }
        Require(last.RetryOrdinal + 1 == checkpoint.RetryOrdinal && last.Status == ProcessingLogStatus.Failed &&
            checkpoint.Status == ProcessingCheckpointStatus.RetryWaiting && checkpoint.NextRetryAt.HasValue &&
            checkpoint.ErrorClassification == ProcessingErrorClassification.Transient && last.ErrorCode is not null &&
            checkpoint.ErrorCode == last.ErrorCode, admission);
        return identity.NextRetry();
    }

    private static void ValidateAdmission(ExecutionAdmission admission)
    {
        var definition = new WorkflowDefinitionResolver().Resolve(admission.Workflow, admission.Manifest.WorkflowVersion);
        Require(admission.Manifest.Workflow == admission.Workflow.ToString() && definition.Steps.Count == admission.Manifest.Steps.Count, admission);
        for (var index = 0; index < definition.Steps.Count; index++)
            Require(definition.Steps[index].Step == admission.Manifest.Steps[index].Step &&
                definition.Steps[index].StepOrder == admission.Manifest.Steps[index].StepOrder, admission);
        Require(admission.Original.JobId == admission.JobId && admission.Original.WorkflowVersion == admission.Manifest.WorkflowVersion &&
            admission.Input.ImageId == admission.ImageId && admission.Input.ContentVersion == admission.Original.ContentVersion &&
            admission.Input.WorkflowVersion == admission.Manifest.WorkflowVersion, admission);
        var current = admission.Manifest.Steps.Where(value => value.Step == admission.Input.Step).ToArray();
        Require(current.Length == 1 && current[0].RuleVersion == admission.Input.RuleVersion, admission);
        var originals = admission.Manifest.Steps.Where(value => value.Step is "EXIF_PARSING" or "SHA256_CALCULATION").ToArray();
        Require(originals.Length == admission.OriginalSteps.Count, admission);
        foreach (var step in originals)
        {
            var inputs = admission.OriginalSteps.Where(value => value.Step == step.Step).ToArray();
            Require(inputs.Length == 1 && inputs[0].JobId == admission.JobId && inputs[0].RuleVersion == step.RuleVersion, admission);
            var input = inputs[0];
            var fingerprint = step.Step == "EXIF_PARSING"
                ? new ExifParsingInput(admission.ImageId, admission.Original.ContentVersion, admission.Original.ContentLength,
                    admission.Manifest.WorkflowVersion, step.RuleVersion, input.UploadCreatedAt!.Value).Identity.InputFingerprint
                : new Sha256CalculationInput(admission.ImageId, admission.Original.ContentVersion, admission.Original.ContentLength,
                    admission.Manifest.WorkflowVersion, step.RuleVersion).Identity.InputFingerprint;
            Require(input.InputFingerprint == fingerprint && (current[0].Step != step.Step || admission.Input.InputFingerprint == fingerprint), admission);
        }
    }

    private async Task<bool> ConfirmAsync(StartedProcessingInvocation identity, ExecutionAdmission admission, CancellationToken token)
    {
        try
        {
        await using var db = await factory.CreateDbContextAsync(token);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        var evidence = await (from log in db.ProcessingLogs
            join checkpoint in db.ProcessingStepCheckpoints on log.StepCheckpointId equals checkpoint.Id
            join run in db.ProcessingRuns on checkpoint.RunId equals run.Id
            join job in db.ProcessingJobs on run.JobId equals job.Id
            join binding in db.ProcessingRuleManifestBindings on job.Id equals binding.JobId
            join original in db.ProcessingOriginalInputs on job.Id equals original.JobId
            where log.Id == identity.InvocationId && checkpoint.Id == identity.CheckpointId && run.Id == identity.RunId && job.Id == identity.JobId
            select new { log, checkpoint, run, job, binding, original }).AsNoTracking().SingleOrDefaultAsync(token);
        var confirmed = evidence is not null && evidence.log.Status == ProcessingLogStatus.Running && evidence.log.JobId == identity.JobId &&
            evidence.log.ImageId == admission.ImageId && evidence.log.AttemptNo == identity.AttemptNo &&
            evidence.log.RetryOrdinal == identity.RetryOrdinal && evidence.log.ResumeNo == identity.ResumeNo &&
            evidence.job.Status == ProcessingJobStatus.Processing && evidence.job.ImageId == admission.ImageId &&
            evidence.job.BatchId == admission.BatchId && evidence.job.Workflow == admission.Workflow && evidence.run.Status == ProcessingRunStatus.Active &&
            evidence.run.WorkflowVersion == admission.Manifest.WorkflowVersion && evidence.checkpoint.Status == ProcessingCheckpointStatus.Processing &&
            evidence.checkpoint.Step == admission.Input.Step && evidence.checkpoint.RetryOrdinal == identity.RetryOrdinal &&
            evidence.checkpoint.InputFingerprint == admission.Input.InputFingerprint && evidence.checkpoint.RuleVersion == admission.Input.RuleVersion &&
            evidence.binding.ManifestId == admission.Manifest.ManifestId && evidence.binding.ManifestChecksum == admission.Manifest.Checksum &&
            evidence.binding.WorkflowVersion == admission.Manifest.WorkflowVersion &&
            evidence.original.ContentVersion == admission.Original.ContentVersion && evidence.original.ContentLength == admission.Original.ContentLength &&
            evidence.original.WorkflowVersion == admission.Manifest.WorkflowVersion;
        if (!confirmed) return false;
        var steps = await db.ProcessingOriginalStepInputs.AsNoTracking().Where(value => value.JobId == admission.JobId).ToListAsync(token);
        if (steps.Count != admission.OriginalSteps.Count || admission.OriginalSteps.Any(expected => !steps.Any(saved =>
            saved.Step == expected.Step && saved.RuleVersion == expected.RuleVersion && saved.InputFingerprint == expected.InputFingerprint &&
            saved.UploadCreatedAt == expected.UploadCreatedAt))) return false;
        var image = await db.Images.AsNoTracking().SingleOrDefaultAsync(value => value.Id == admission.ImageId, token);
        var primary = await db.ImageProcessingBindings.AsNoTracking().SingleOrDefaultAsync(value => value.ImageId == admission.ImageId, token);
        if (image is null || primary is null || image.StoredPath != admission.StoredPath || image.FileSize != admission.Original.ContentLength ||
            image.BatchId != admission.BatchId || primary.BatchId != admission.BatchId) return false;
        if (primary.PrimaryJobId == admission.JobId && (primary.PrimaryStatus != ProcessingJobStatus.Processing || image.Status != "Processing")) return false;
        await transaction.CommitAsync(token);
        return true;
        }
        catch (DbException)
        {
            token.ThrowIfCancellationRequested();
            return false;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw new OperationCanceledException(token);
        }
    }
    private Task FaultAsync(string point) => fault?.Invoke(point) ?? Task.CompletedTask;
    private static void Require(bool condition, ExecutionAdmission admission, FrozenInputFailureReason reason = FrozenInputFailureReason.Mismatch)
    {
        if (!condition) throw new FrozenInputUnavailableException(admission.JobId, reason);
    }
}
