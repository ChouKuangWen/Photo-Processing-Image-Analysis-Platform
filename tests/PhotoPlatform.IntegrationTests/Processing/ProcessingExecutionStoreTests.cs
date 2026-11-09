using System.Data.Common;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using PhotoPlatform.Application.Interfaces;
using PhotoPlatform.Application.Processing;
using PhotoPlatform.Domain.Entities;
using PhotoPlatform.Domain.Enums;
using PhotoPlatform.Domain.Policies;
using PhotoPlatform.Infrastructure.Persistence;
using PhotoPlatform.Infrastructure.Processing;
using PhotoPlatform.IntegrationTests.Fixtures;

namespace PhotoPlatform.IntegrationTests.Processing;

// These are isolated, explicitly trusted test admissions, never production rule publications.
// Test-only completion/recovery setup below simulates already-committed inputs; it does not implement T3/T4/T5.
public sealed class ProcessingExecutionStoreTests : IAsyncLifetime
{
    private readonly ProcessingSchemaFixture fixture = new();
    private Factory factory = null!;
    public async Task InitializeAsync()
    {
        await fixture.InitializeAsync();
        await using var db = fixture.CreateContext();
        await db.Database.MigrateAsync();
        factory = new Factory(fixture);
    }
    public Task DisposeAsync() => fixture.DisposeAsync();

    [Fact]
    public async Task Claim_AtomicallyCreatesBindingsRunCheckpointInvocationAndPrimaryProjection()
    {
        var seed = await SeedAsync(WorkflowType.Full);
        var admission = await AdmissionAsync(seed);
        var result = await new ProcessingExecutionStore(factory).ClaimAsync(admission, default);
        Assert.NotNull(result);
        await using var db = fixture.CreateContext();
        Assert.Equal(ProcessingJobStatus.Processing, (await db.ProcessingJobs.SingleAsync()).Status);
        Assert.Equal("Processing", (await db.Images.SingleAsync()).Status);
        Assert.Equal("Processing", (await db.Batches.SingleAsync()).Status);
        Assert.Equal(0, (await db.Batches.SingleAsync()).ProcessedCount);
        Assert.Equal(ProcessingJobStatus.Processing, (await db.ImageProcessingBindings.SingleAsync()).PrimaryStatus);
        Assert.Single(await db.ProcessingRuleManifestBindings.ToListAsync());
        Assert.Single(await db.ProcessingOriginalInputs.ToListAsync());
        Assert.Equal(2, await db.ProcessingOriginalStepInputs.CountAsync());
        Assert.Equal(ProcessingRunStatus.Active, (await db.ProcessingRuns.SingleAsync()).Status);
        Assert.Equal("EXIF_PARSING", (await db.ProcessingStepCheckpoints.SingleAsync()).Step);
        var log = await db.ProcessingLogs.SingleAsync();
        Assert.Equal(ProcessingLogStatus.Running, log.Status);
        Assert.Equal(result.InvocationId, log.Id);
        Assert.Equal((1, 0, 0), (log.AttemptNo, log.RetryOrdinal, log.ResumeNo));
        Assert.Null(await new ProcessingExecutionStore(factory).ClaimAsync(admission, default));
        Assert.Equal(1, await db.ProcessingLogs.CountAsync());
    }

    [Fact]
    public async Task Claim_ConcurrentWorkersHaveExactlyOneWinner()
    {
        var admission = await AdmissionAsync(await SeedAsync());
        var results = await RaceAsync(6, () => new ProcessingExecutionStore(factory).ClaimAsync(admission, default));
        Assert.Single(results, value => value is not null);
        await using var db = fixture.CreateContext();
        Assert.Equal(1, await db.ProcessingRuns.CountAsync());
        Assert.Equal(1, await db.ProcessingLogs.CountAsync());
    }

    [Theory]
    [InlineData("manifest-binding")]
    [InlineData("original-binding")]
    [InlineData("step-binding:EXIF_PARSING")]
    [InlineData("step-binding:SHA256_CALCULATION")]
    [InlineData("step-bindings")]
    [InlineData("claim")]
    [InlineData("run")]
    [InlineData("checkpoint-created")]
    [InlineData("checkpoint")]
    [InlineData("invocation")]
    [InlineData("primary-projection")]
    [InlineData("image-projection")]
    [InlineData("aggregate")]
    [InlineData("before-commit")]
    public async Task Claim_FaultAtEveryWritePointRollsBackEverything(string point)
    {
        var admission = await AdmissionAsync(await SeedAsync(WorkflowType.Full));
        var writer = new ProcessingExecutionStore(factory, phase =>
            phase == point ? Task.FromException(new InjectedSqlFault()) : Task.CompletedTask);
        var error = await Assert.ThrowsAsync<FrozenInputUnavailableException>(() => writer.ClaimAsync(admission, default));
        Assert.Equal(FrozenInputFailureReason.SourceUnavailable, error.Reason);
        await AssertPristineAsync();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Claim_CrossJobChecksumCompetitionAcceptsOnlyConsistentBindings(bool sameChecksum)
    {
        var first = await AdmissionAsync(await SeedAsync());
        var second = await AdmissionAsync(await SeedAsync(), checksum: sameChecksum ? new string('a', 64) : new string('b', 64));
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> Claim(ExecutionAdmission admission)
        {
            await gate.Task;
            try { return await new ProcessingExecutionStore(factory).ClaimAsync(admission, default) is not null; }
            catch (FrozenInputUnavailableException error) when (error.Reason == FrozenInputFailureReason.Mismatch) { return false; }
        }
        var one = Claim(first);
        var two = Claim(second);
        gate.SetResult();
        var results = await Task.WhenAll(one, two);
        Assert.Equal(sameChecksum ? 2 : 1, results.Count(value => value));
        await using var db = fixture.CreateContext();
        var bindings = await db.ProcessingRuleManifestBindings.ToListAsync();
        Assert.Equal(sameChecksum ? 2 : 1, bindings.Count);
        Assert.Single(bindings.Select(value => value.ManifestChecksum).Distinct());
        Assert.Equal(bindings.Count, await db.ProcessingLogs.CountAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CommitUnknown_ConfirmsDurableIdentityWithoutReplayingWriter(bool committed)
    {
        var admission = await AdmissionAsync(await SeedAsync());
        var writes = 0;
        var writer = new ProcessingExecutionStore(factory, point =>
        {
            if (point == "invocation") writes++;
            return point == (committed ? "after-commit" : "before-commit")
                ? Task.FromException(new InjectedSqlFault()) : Task.CompletedTask;
        });
        if (committed)
        {
            var started = await writer.ClaimAsync(admission, default);
            Assert.NotNull(started);
            await using var db = fixture.CreateContext();
            Assert.Equal(started.InvocationId, (await db.ProcessingLogs.SingleAsync()).Id);
            Assert.Equal(0, (await db.ProcessingJobs.SingleAsync()).RetryCount);
        }
        else
        {
            await Assert.ThrowsAsync<FrozenInputUnavailableException>(() => writer.ClaimAsync(admission, default));
            await AssertPristineAsync();
        }
        Assert.Equal(1, writes);
        Assert.Equal(2, factory.Created); // Original transaction + fresh durable confirmation, never a replay.
    }

    [Fact]
    public async Task T2_CompetingNextStepStartsOneInvocationAfterPredecessorCompletion()
    {
        var seed = await SeedAsync();
        var initial = await AdmissionAsync(seed);
        var started = (await new ProcessingExecutionStore(factory).ClaimAsync(initial, default))!;
        CompletedStepSource source;
        await using (var db = fixture.CreateContext())
        {
            var checkpoint = await db.ProcessingStepCheckpoints.SingleAsync();
            var log = await db.ProcessingLogs.SingleAsync();
            checkpoint.Complete(ProcessingResultStatus.Success, "test-only-hash-result:1", log.StartedAt);
            log.Finish(ProcessingLogStatus.Success, log.StartedAt, 0);
            await db.SaveChangesAsync();
            source = new(checkpoint.Id, checkpoint.Step, checkpoint.RuleVersion, checkpoint.InputFingerprint,
                checkpoint.ResultReference, checkpoint.ResultStatus!.Value);
        }
        var next = await AdmissionAsync(seed, first: false, stepOrder: 2, sources: [source]);
        var results = await RaceAsync(5, () => new ProcessingExecutionStore(factory).StartNextAsync(next, default));
        var winner = Assert.Single(results, value => value is not null)!;
        Assert.Equal(started.RunId, winner.RunId);
        Assert.Equal((1, 0, 0), (winner.AttemptNo, winner.RetryOrdinal, winner.ResumeNo));
        await using var read = fixture.CreateContext();
        Assert.Equal(2, await read.ProcessingStepCheckpoints.CountAsync());
        Assert.Equal(2, await read.ProcessingLogs.CountAsync());
        Assert.Equal(1, await read.ProcessingLogs.CountAsync(value => value.Status == ProcessingLogStatus.Running));
    }

    [Fact]
    public async Task T2_RetryDeadlineAndResumeAllocateIdentityWithoutDoubleCounting()
    {
        var seed = await SeedAsync();
        await new ProcessingExecutionStore(factory).ClaimAsync(await AdmissionAsync(seed), default);
        await using (var db = fixture.CreateContext())
        {
            // Model a failure that already occurred, independently of sub-second SQL clock adjustments.
            await db.Database.ExecuteSqlRawAsync("UPDATE processing_logs SET StartedAt=DATEADD(minute,-1,StartedAt),Timestamp=DATEADD(minute,-1,Timestamp); UPDATE processing_step_checkpoints SET StartedAt=DATEADD(minute,-1,StartedAt); UPDATE processing_runs SET StartedAt=DATEADD(minute,-1,StartedAt); UPDATE processing_jobs SET StartedAt=DATEADD(minute,-1,StartedAt)");
            var checkpoint = await db.ProcessingStepCheckpoints.SingleAsync();
            var log = await db.ProcessingLogs.SingleAsync();
            checkpoint.WaitForRetry("TEST_ONLY_TRANSIENT", log.StartedAt);
            log.Finish(ProcessingLogStatus.Failed, log.StartedAt, 0, "TEST_ONLY_TRANSIENT");
            await db.SaveChangesAsync();
            // Keep a deterministic not-yet-due deadline, without sleeping.
            await db.Database.ExecuteSqlRawAsync("UPDATE processing_step_checkpoints SET NextRetryAt=DATEADD(hour,1,SYSUTCDATETIME())");
        }
        var retry = await AdmissionAsync(seed, first: false);
        Assert.Null(await new ProcessingExecutionStore(factory).StartNextAsync(retry, default));
        await using (var db = fixture.CreateContext())
            await db.Database.ExecuteSqlRawAsync("UPDATE processing_step_checkpoints SET NextRetryAt=DATEADD(second,-1,SYSUTCDATETIME())");
        var retried = (await new ProcessingExecutionStore(factory).StartNextAsync(retry, default))!;
        Assert.Equal((2, 1, 0), (retried.AttemptNo, retried.RetryOrdinal, retried.ResumeNo));
        await SimulateSafeRecoveryAsync();
        var resumed = (await new ProcessingExecutionStore(factory).ClaimAsync(retry, default))!;
        Assert.Equal((3, 1, 1), (resumed.AttemptNo, resumed.RetryOrdinal, resumed.ResumeNo));
        await using var read = fixture.CreateContext();
        Assert.Equal(1, (await read.ProcessingJobs.SingleAsync()).RetryCount);
        Assert.Equal(3, await read.ProcessingLogs.CountAsync());
        Assert.Equal(1, await read.ProcessingRuns.CountAsync());
    }

    [Fact]
    public async Task ExistingBindingsCannotBeReboundAndMissingAdmissionHasNoSideEffects()
    {
        var seed = await SeedAsync();
        var admission = await AdmissionAsync(seed);
        await Assert.ThrowsAsync<ArgumentNullException>(() => new ProcessingExecutionStore(factory).ClaimAsync(null!, default));
        await AssertPristineAsync();
        var legacy = await AdmissionAsync(seed, first: false);
        await Assert.ThrowsAsync<FrozenInputUnavailableException>(() => new ProcessingExecutionStore(factory).ClaimAsync(legacy, default));
        await AssertPristineAsync();
        await new ProcessingExecutionStore(factory).ClaimAsync(admission, default);
        await SimulateSafeRecoveryAsync();
        var changed = await AdmissionAsync(seed, first: false, checksum: new string('b', 64));
        await Assert.ThrowsAsync<FrozenInputUnavailableException>(() => new ProcessingExecutionStore(factory).ClaimAsync(changed, default));
        await using var read = fixture.CreateContext();
        Assert.Equal(new string('a', 64), (await read.ProcessingRuleManifestBindings.SingleAsync()).ManifestChecksum);
        Assert.Equal(1, await read.ProcessingLogs.CountAsync());
        Assert.Equal(ProcessingJobStatus.Pending, (await read.ProcessingJobs.SingleAsync()).Status);
    }

    [Fact]
    public async Task CancellationBeforeCommitRollsBackAndNeverReturnsExecutionGrant()
    {
        var admission = await AdmissionAsync(await SeedAsync());
        using var cancellation = new CancellationTokenSource();
        var writer = new ProcessingExecutionStore(factory, point =>
        {
            if (point == "invocation") cancellation.Cancel();
            return Task.CompletedTask;
        });
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writer.ClaimAsync(admission, cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        await AssertPristineAsync();
    }

    [Fact]
    public async Task DifferentManifestIdsCanCommitWithoutLockConversionDeadlock()
    {
        var one = await AdmissionAsync(await SeedAsync(), manifestId: "test-only-one");
        var two = await AdmissionAsync(await SeedAsync(), manifestId: "test-only-two");
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<StartedProcessingInvocation?> Start(ExecutionAdmission admission)
        {
            await gate.Task;
            return await new ProcessingExecutionStore(factory).ClaimAsync(admission, default);
        }
        var first = Start(one);
        var second = Start(two);
        gate.SetResult();
        Assert.All(await Task.WhenAll(first, second), invocation => Assert.NotNull(invocation));
    }

    [Fact]
    public async Task SecondaryClaimPreservesPrimaryImageAndBatchProjection()
    {
        var primary = await SeedAsync(WorkflowType.Naming);
        Seed secondary;
        await using (var db = fixture.CreateContext())
        {
            var job = new ProcessingJob(primary.ImageId, primary.BatchId, WorkflowType.DuplicateDetection, primary.CreatedAt);
            db.ProcessingJobs.Add(job);
            await db.SaveChangesAsync();
            secondary = primary with { JobId = job.Id, Workflow = WorkflowType.DuplicateDetection };
        }
        Assert.NotNull(await new ProcessingExecutionStore(factory).ClaimAsync(await AdmissionAsync(secondary), default));
        await using var read = fixture.CreateContext();
        Assert.Equal("Pending", (await read.Images.SingleAsync()).Status);
        Assert.Equal("Pending", (await read.Batches.SingleAsync()).Status);
        Assert.Equal(ProcessingJobStatus.Pending, (await read.ImageProcessingBindings.SingleAsync()).PrimaryStatus);
        Assert.Equal(ProcessingJobStatus.Pending, (await read.ProcessingJobs.SingleAsync(job => job.Id == primary.JobId)).Status);
        Assert.Equal(ProcessingJobStatus.Processing, (await read.ProcessingJobs.SingleAsync(job => job.Id == secondary.JobId)).Status);
    }

    [Theory]
    [InlineData("Completed")]
    [InlineData("Failed")]
    [InlineData("Processing")]
    public async Task Claim_NonPendingNotificationCannotStartInvocation(string status)
    {
        var admission = await AdmissionAsync(await SeedAsync());
        await using (var db = fixture.CreateContext())
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE processing_jobs SET Status={status}");
        Assert.Null(await new ProcessingExecutionStore(factory).ClaimAsync(admission, default));
        await using var read = fixture.CreateContext();
        Assert.Empty(await read.ProcessingLogs.ToListAsync());
        Assert.Empty(await read.ProcessingRuns.ToListAsync());
        Assert.Empty(await read.ProcessingRuleManifestBindings.ToListAsync());
    }

    [Fact]
    public async Task CancellationAfterCommitDoesNotInvokeAdapterOrReclaimDurableInvocation()
    {
        var admission = await AdmissionAsync(await SeedAsync());
        using var cancellation = new CancellationTokenSource();
        var writer = new ProcessingExecutionStore(factory, point =>
        {
            if (point == "after-commit") cancellation.Cancel();
            return Task.CompletedTask;
        });
        var calls = 0;
        var coordinator = new ProcessingInvocationCoordinator(writer);
        Task Adapter(StartedProcessingInvocation identity, CancellationToken token) { calls++; return Task.CompletedTask; }
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            coordinator.ExecuteAsync(admission, true, Adapter, cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(0, calls);
        await using var read = fixture.CreateContext();
        Assert.Equal(ProcessingLogStatus.Running, (await read.ProcessingLogs.SingleAsync()).Status);
        Assert.Equal(ProcessingJobStatus.Processing, (await read.ProcessingJobs.SingleAsync()).Status);
        Assert.Null(await new ProcessingExecutionStore(factory).ClaimAsync(admission, default));
        Assert.Equal(1, await read.ProcessingLogs.CountAsync());
    }

    [Fact]
    public async Task CommitUnknown_ConflictingDurableEvidenceNeverGrantsAdapterExecution()
    {
        var admission = await AdmissionAsync(await SeedAsync());
        var writer = new ProcessingExecutionStore(factory, async point =>
        {
            if (point != "after-commit") return;
            await using var db = fixture.CreateContext();
            // Deliberately corrupt the test row to simulate contradictory durable evidence.
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE processing_rule_manifest_bindings SET ManifestChecksum={new string('b', 64)}");
            throw new InjectedSqlFault();
        });
        var calls = 0;
        var coordinator = new ProcessingInvocationCoordinator(writer);
        Task Adapter(StartedProcessingInvocation identity, CancellationToken token) { calls++; return Task.CompletedTask; }
        await Assert.ThrowsAsync<FrozenInputUnavailableException>(() => coordinator.ExecuteAsync(admission, true, Adapter, default));
        Assert.Equal(0, calls);
        await using var read = fixture.CreateContext();
        Assert.Equal(1, await read.ProcessingLogs.CountAsync());
        Assert.Equal(ProcessingLogStatus.Running, (await read.ProcessingLogs.SingleAsync()).Status);
    }

    [Fact]
    public async Task T2_UnknownHistoryRelationIsRejectedWithoutAddingInvocation()
    {
        var seed = await SeedAsync();
        await new ProcessingExecutionStore(factory).ClaimAsync(await AdmissionAsync(seed), default);
        await SimulateSafeRecoveryAsync();
        await using (var db = fixture.CreateContext())
            await db.Database.ExecuteSqlRawAsync("UPDATE processing_logs SET ResumeNo=7");
        var admission = await AdmissionAsync(seed, first: false);
        await Assert.ThrowsAsync<FrozenInputUnavailableException>(() => new ProcessingExecutionStore(factory).ClaimAsync(admission, default));
        await using var read = fixture.CreateContext();
        Assert.Equal(1, await read.ProcessingLogs.CountAsync());
        Assert.Equal(ProcessingJobStatus.Pending, (await read.ProcessingJobs.SingleAsync()).Status);
    }

    [Theory]
    [InlineData("storage-reference")]
    [InlineData("file-length")]
    [InlineData("primary-binding")]
    [InlineData("batch-projection")]
    public async Task Claim_RechecksDurableAdmissionSourcesInsideTransaction(string changed)
    {
        var admission = await AdmissionAsync(await SeedAsync());
        await using (var db = fixture.CreateContext())
        {
            var sql = changed switch
            {
                "storage-reference" => "UPDATE images SET StoredPath='original/test-only-changed'",
                "file-length" => "UPDATE images SET FileSize=4",
                "primary-binding" => "DELETE FROM image_processing_bindings",
                _ => "UPDATE batches SET Status='Processing'"
            };
            await db.Database.ExecuteSqlRawAsync(sql);
        }
        await Assert.ThrowsAsync<FrozenInputUnavailableException>(() => new ProcessingExecutionStore(factory).ClaimAsync(admission, default));
        await using var read = fixture.CreateContext();
        Assert.Equal(ProcessingJobStatus.Pending, (await read.ProcessingJobs.SingleAsync()).Status);
        Assert.Empty(await read.ProcessingRuleManifestBindings.ToListAsync());
        Assert.Empty(await read.ProcessingRuns.ToListAsync());
        Assert.Empty(await read.ProcessingLogs.ToListAsync());
    }

    [Fact]
    public async Task Resume_FailedNewInvocationRollsBackToOriginalPendingRunAndHistory()
    {
        var seed = await SeedAsync();
        await new ProcessingExecutionStore(factory).ClaimAsync(await AdmissionAsync(seed), default);
        await SimulateSafeRecoveryAsync();
        var writer = new ProcessingExecutionStore(factory, point => point == "invocation"
            ? Task.FromException(new InjectedSqlFault()) : Task.CompletedTask);
        var admission = await AdmissionAsync(seed, first: false);
        await Assert.ThrowsAsync<FrozenInputUnavailableException>(() => writer.ClaimAsync(admission, default));
        await using var read = fixture.CreateContext();
        Assert.Equal(ProcessingJobStatus.Pending, (await read.ProcessingJobs.SingleAsync()).Status);
        Assert.Equal(ProcessingRunStatus.Pending, (await read.ProcessingRuns.SingleAsync()).Status);
        Assert.Equal(ProcessingCheckpointStatus.Pending, (await read.ProcessingStepCheckpoints.SingleAsync()).Status);
        Assert.Equal(ProcessingLogStatus.Interrupted, (await read.ProcessingLogs.SingleAsync()).Status);
        Assert.Equal(0, (await read.ProcessingJobs.SingleAsync()).RetryCount);
    }

    [Fact]
    public async Task T2_PermanentFailureCannotConsumeTransientRetrySlot()
    {
        var seed = await SeedAsync();
        await new ProcessingExecutionStore(factory).ClaimAsync(await AdmissionAsync(seed), default);
        await using (var db = fixture.CreateContext())
        {
            var checkpoint = await db.ProcessingStepCheckpoints.SingleAsync();
            var log = await db.ProcessingLogs.SingleAsync();
            checkpoint.WaitForRetry("TEST_ONLY_FAILURE", log.StartedAt);
            log.Finish(ProcessingLogStatus.Failed, log.StartedAt, 0, "TEST_ONLY_FAILURE");
            await db.SaveChangesAsync();
            // Contradictory persisted fixture: a permanent classification must never be retried.
            await db.Database.ExecuteSqlRawAsync("UPDATE processing_step_checkpoints SET ErrorClassification='Permanent',NextRetryAt=DATEADD(second,-1,SYSUTCDATETIME())");
        }
        var admission = await AdmissionAsync(seed, first: false);
        await Assert.ThrowsAsync<FrozenInputUnavailableException>(() => new ProcessingExecutionStore(factory).StartNextAsync(admission, default));
        await using var read = fixture.CreateContext();
        Assert.Equal(1, await read.ProcessingLogs.CountAsync());
        Assert.Equal(0, (await read.ProcessingJobs.SingleAsync()).RetryCount);
    }

    private async Task SimulateSafeRecoveryAsync()
    {
        await using var db = fixture.CreateContext();
        var checkpoint = await db.ProcessingStepCheckpoints.SingleAsync(value => value.Status == ProcessingCheckpointStatus.Processing);
        var log = await db.ProcessingLogs.SingleAsync(value => value.Status == ProcessingLogStatus.Running);
        var job = await db.ProcessingJobs.SingleAsync();
        var run = await db.ProcessingRuns.SingleAsync();
        var authorization = ProcessingRecoveryAuthorization.Confirm(true, true); // Test fixture only.
        log.Finish(ProcessingLogStatus.Interrupted, log.StartedAt, 0);
        checkpoint.Recover(authorization);
        run.Recover(authorization);
        job.Recover(authorization);
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("UPDATE images SET Status='Pending'; UPDATE image_processing_bindings SET PrimaryStatus='Pending'; UPDATE batches SET Status='Pending'");
    }

    [Fact]
    public async Task Coordinator_InvokesAdapterOnlyForConfirmedCommittedWinner()
    {
        var admission = await AdmissionAsync(await SeedAsync());
        var coordinator = new ProcessingInvocationCoordinator(new ProcessingExecutionStore(factory));
        var calls = 0;
        async Task Adapter(StartedProcessingInvocation identity, CancellationToken token)
        {
            calls++;
            await using var db = fixture.CreateContext();
            Assert.Equal(identity.InvocationId, (await db.ProcessingLogs.SingleAsync(token)).Id);
            Assert.Equal(ProcessingRunStatus.Active, (await db.ProcessingRuns.SingleAsync(token)).Status);
            // A separate connection can read the committed record while the adapter is executing.
        }
        Assert.False(await coordinator.ExecuteAsync(null, true, Adapter, default));
        await AssertPristineAsync();
        var rejected = await AdmissionAsync(await LoadSeedAsync(admission), first: false);
        await Assert.ThrowsAsync<FrozenInputUnavailableException>(() => coordinator.ExecuteAsync(rejected, true, Adapter, default));
        Assert.Equal(0, calls);
        Assert.True(await coordinator.ExecuteAsync(admission, true, Adapter, default));
        Assert.False(await coordinator.ExecuteAsync(admission, true, Adapter, default));
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData("checkpoint")]
    [InlineData("invocation")]
    [InlineData("aggregate")]
    [InlineData("before-commit")]
    [InlineData("after-commit")]
    public async Task T2_FaultsRollbackOrConfirmWithoutDuplicateInvocation(string point)
    {
        var seed = await SeedAsync();
        await new ProcessingExecutionStore(factory).ClaimAsync(await AdmissionAsync(seed), default);
        var source = await CompleteFirstStepAsync();
        var admission = await AdmissionAsync(seed, first: false, stepOrder: 2, sources: [source]);
        var writes = 0;
        var calls = 0;
        var writer = new ProcessingExecutionStore(factory, phase =>
        {
            if (phase == "invocation") writes++;
            return phase == point ? Task.FromException(new InjectedSqlFault()) : Task.CompletedTask;
        });
        var coordinator = new ProcessingInvocationCoordinator(writer);
        Task Adapter(StartedProcessingInvocation identity, CancellationToken token) { calls++; return Task.CompletedTask; }
        if (point == "after-commit")
            Assert.True(await coordinator.ExecuteAsync(admission, false, Adapter, default));
        else
            await Assert.ThrowsAsync<FrozenInputUnavailableException>(() => coordinator.ExecuteAsync(admission, false, Adapter, default));
        await using var read = fixture.CreateContext();
        Assert.Equal(point == "after-commit" ? 2 : 1, await read.ProcessingLogs.CountAsync());
        Assert.Equal(point == "after-commit" ? 2 : 1, await read.ProcessingStepCheckpoints.CountAsync());
        Assert.Equal(point == "after-commit" ? 1 : 0, calls);
        Assert.True(writes <= 1);
        Assert.Equal(0, (await read.ProcessingJobs.SingleAsync()).RetryCount);
    }

    [Fact]
    public async Task Resume_PreservesFirstStartTimesAndOriginalBindings()
    {
        var seed = await SeedAsync();
        var started = (await new ProcessingExecutionStore(factory).ClaimAsync(await AdmissionAsync(seed), default))!;
        await using var before = fixture.CreateContext();
        var jobStart = (await before.ProcessingJobs.SingleAsync()).StartedAt;
        var runStart = (await before.ProcessingRuns.SingleAsync()).StartedAt;
        var checkpointStart = (await before.ProcessingStepCheckpoints.SingleAsync()).StartedAt;
        await SimulateSafeRecoveryAsync();
        var resumed = (await new ProcessingExecutionStore(factory).ClaimAsync(await AdmissionAsync(seed, first: false), default))!;
        Assert.Equal(started.RunId, resumed.RunId);
        Assert.Equal((2, 0, 1), (resumed.AttemptNo, resumed.RetryOrdinal, resumed.ResumeNo));
        await using var after = fixture.CreateContext();
        Assert.Equal(jobStart, (await after.ProcessingJobs.SingleAsync()).StartedAt);
        Assert.Equal(runStart, (await after.ProcessingRuns.SingleAsync()).StartedAt);
        Assert.Equal(checkpointStart, (await after.ProcessingStepCheckpoints.SingleAsync()).StartedAt);
        Assert.Equal(0, (await after.ProcessingJobs.SingleAsync()).RetryCount);
        Assert.Equal(1, await after.ProcessingOriginalInputs.CountAsync());
    }

    [Fact]
    public async Task Claim_ActivatesApprovedPendingManualRunWithoutRebinding()
    {
        var seed = await SeedAsync();
        await new ProcessingExecutionStore(factory).ClaimAsync(await AdmissionAsync(seed), default);
        await using (var db = fixture.CreateContext())
        {
            // Simulate an exhausted transient run in the past, with all four actual invocation records.
            await db.Database.ExecuteSqlRawAsync("UPDATE processing_logs SET StartedAt=DATEADD(minute,-1,StartedAt),Timestamp=DATEADD(minute,-1,Timestamp); UPDATE processing_step_checkpoints SET StartedAt=DATEADD(minute,-1,StartedAt); UPDATE processing_runs SET StartedAt=DATEADD(minute,-1,StartedAt); UPDATE processing_jobs SET StartedAt=DATEADD(minute,-1,StartedAt)");
            var checkpoint = await db.ProcessingStepCheckpoints.SingleAsync();
            var log = await db.ProcessingLogs.SingleAsync();
            var job = await db.ProcessingJobs.SingleAsync();
            var run = await db.ProcessingRuns.SingleAsync();
            var attempt = ProcessingAttemptIdentity.Initial();
            for (var index = 0; index < 3; index++)
            {
                log.Finish(ProcessingLogStatus.Failed, log.StartedAt, 0, "TEST_ONLY_TRANSIENT");
                checkpoint.WaitForRetry("TEST_ONLY_TRANSIENT", log.StartedAt);
                attempt = attempt.NextRetry();
                var time = checkpoint.NextRetryAt!.Value;
                checkpoint.Start(attempt, time);
                job.RecordRetryStart(attempt);
                log = new ProcessingLog(checkpoint.Id, job.Id, seed.ImageId, attempt, time);
                db.ProcessingLogs.Add(log);
                await db.SaveChangesAsync();
            }
            log.Finish(ProcessingLogStatus.Failed, log.StartedAt, 0, "TEST_ONLY_TRANSIENT");
            checkpoint.Fail("TEST_ONLY_TRANSIENT", ProcessingErrorClassification.Transient, log.StartedAt);
            run.Fail(log.StartedAt);
            job.Fail("TEST_ONLY_TRANSIENT", null, log.StartedAt);
            job.PrepareManualRetry(ProcessingErrorClassification.Transient, 2);
            db.ProcessingRuns.Add(new(job.Id, 2, ProcessingRunReason.ManualRetry, "workflow-mvp-v1", log.StartedAt));
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlRawAsync("UPDATE images SET Status='Pending'; UPDATE image_processing_bindings SET PrimaryStatus='Pending'; UPDATE batches SET Status='Pending'");
        }
        var started = (await new ProcessingExecutionStore(factory).ClaimAsync(await AdmissionAsync(seed, first: false), default))!;
        await using var read = fixture.CreateContext();
        var runs = await read.ProcessingRuns.OrderBy(run => run.RunNo).ToListAsync();
        Assert.Equal(ProcessingRunStatus.Failed, runs[0].Status);
        Assert.Equal(ProcessingRunStatus.Active, runs[1].Status);
        Assert.Equal(runs[1].Id, started.RunId);
        Assert.Equal((1, 0, 0), (started.AttemptNo, started.RetryOrdinal, started.ResumeNo));
        Assert.Equal(3, (await read.ProcessingJobs.SingleAsync()).RetryCount);
        Assert.Equal(1, await read.ProcessingOriginalInputs.CountAsync());
        Assert.Equal(1, await read.ProcessingRuleManifestBindings.CountAsync());
    }

    [Fact]
    public async Task T2_RejectsUncompletedOrChangedPredecessorWithoutCreatingCheckpoint()
    {
        var seed = await SeedAsync();
        await new ProcessingExecutionStore(factory).ClaimAsync(await AdmissionAsync(seed), default);
        var premature = await AdmissionAsync(seed, first: false, stepOrder: 2);
        await Assert.ThrowsAsync<FrozenInputUnavailableException>(() => new ProcessingExecutionStore(factory).StartNextAsync(premature, default));
        var source = await CompleteFirstStepAsync();
        var stale = source with { ResultReference = "test-only-wrong-result" };
        var next = await AdmissionAsync(seed, first: false, stepOrder: 2, sources: [stale]);
        await Assert.ThrowsAsync<FrozenInputUnavailableException>(() => new ProcessingExecutionStore(factory).StartNextAsync(next, default));
        await using var read = fixture.CreateContext();
        Assert.Equal(1, await read.ProcessingLogs.CountAsync());
        Assert.Equal(1, await read.ProcessingStepCheckpoints.CountAsync());
    }

    private async Task<CompletedStepSource> CompleteFirstStepAsync()
    {
        await using var db = fixture.CreateContext();
        var checkpoint = await db.ProcessingStepCheckpoints.SingleAsync();
        var log = await db.ProcessingLogs.SingleAsync();
        checkpoint.Complete(ProcessingResultStatus.Success, "test-only-hash-result:1", log.StartedAt);
        log.Finish(ProcessingLogStatus.Success, log.StartedAt, 0);
        await db.SaveChangesAsync();
        return new(checkpoint.Id, checkpoint.Step, checkpoint.RuleVersion, checkpoint.InputFingerprint,
            checkpoint.ResultReference, checkpoint.ResultStatus!.Value);
    }

    private async Task<Seed> LoadSeedAsync(ExecutionAdmission admission)
    {
        await using var db = fixture.CreateContext();
        var image = await db.Images.SingleAsync();
        return new(admission.JobId, admission.ImageId, admission.BatchId, image.StoredPath, admission.Workflow, image.CreatedAt);
    }

    private async Task AssertPristineAsync()
    {
        await using var db = fixture.CreateContext();
        Assert.Empty(await db.ProcessingRuleManifestBindings.ToListAsync());
        Assert.Empty(await db.ProcessingOriginalInputs.ToListAsync());
        Assert.Empty(await db.ProcessingOriginalStepInputs.ToListAsync());
        Assert.Empty(await db.ProcessingRuns.ToListAsync());
        Assert.Empty(await db.ProcessingStepCheckpoints.ToListAsync());
        Assert.Empty(await db.ProcessingLogs.ToListAsync());
        Assert.All(await db.ProcessingJobs.ToListAsync(), job =>
        {
            Assert.Equal(ProcessingJobStatus.Pending, job.Status);
            Assert.Null(job.StartedAt);
            Assert.Equal(0, job.RetryCount);
        });
        Assert.All(await db.Images.ToListAsync(), image => Assert.Equal("Pending", image.Status));
        Assert.All(await db.Batches.ToListAsync(), batch => Assert.Equal("Pending", batch.Status));
    }

    private async Task<Seed> SeedAsync(WorkflowType workflow = WorkflowType.DuplicateDetection)
    {
        await using var db = fixture.CreateContext();
        var time = DateTimeOffset.UtcNow.AddMinutes(-1);
        var batch = new Batch(Guid.NewGuid(), 1, time);
        db.Batches.Add(batch);
        var image = new Image(batch.Id, "test-only.jpg", "original/test-only-" + Guid.NewGuid().ToString("N"), 3, "image/jpeg", time);
        db.Images.Add(image);
        await db.SaveChangesAsync();
        var job = new ProcessingJob(image.Id, batch.Id, workflow, time);
        db.ProcessingJobs.Add(job);
        await db.SaveChangesAsync();
        db.ImageProcessingBindings.Add(new(image.Id, batch.Id, job.Id, workflow, ProcessingJobStatus.Pending, time));
        await db.SaveChangesAsync();
        return new(job.Id, image.Id, batch.Id, image.StoredPath, workflow, time);
    }

    private static async Task<ExecutionAdmission> AdmissionAsync(Seed seed, bool first = true, string? checksum = null,
        int stepOrder = 1, CompletedStepSource[]? sources = null, string manifestId = "test-only-shared")
    {
        var definition = new WorkflowDefinitionResolver().Resolve(seed.Workflow, "workflow-mvp-v1");
        var manifest = new ResolvedRuleManifest(manifestId, checksum ?? new string('a', 64), seed.Workflow.ToString(),
            "workflow-mvp-v1", definition.Steps.Select(step => new ManifestStep(step.Step, step.StepOrder, "test-only-" + step.Step, [])));
        await using var prepared = await OriginalInputPreparation.PrepareAsync(seed.JobId, seed.ImageId, 3,
            seed.CreatedAt, manifest, new TestReader(), null, null, default);
        var current = manifest.Steps[stepOrder - 1];
        var fingerprint = prepared.Steps.SingleOrDefault(step => step.Step == current.Step)?.InputFingerprint ?? new string('c', 64);
        return new(seed.JobId, seed.ImageId, seed.BatchId, seed.Path, seed.Workflow, manifest, prepared.Original,
            prepared.Steps, new(seed.ImageId, current.Step, prepared.Original.ContentVersion, manifest.WorkflowVersion,
                current.RuleVersion, fingerprint), first, sources);
    }

    private static async Task<T[]> RaceAsync<T>(int count, Func<Task<T>> operation)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, count).Select(async _ => { await gate.Task; return await operation(); }).ToArray();
        gate.SetResult();
        return await Task.WhenAll(tasks);
    }
    private sealed record Seed(long JobId, long ImageId, Guid BatchId, string Path, WorkflowType Workflow, DateTimeOffset CreatedAt);
    private sealed class InjectedSqlFault : DbException { }
    private sealed class TestReader : IImageContentReader
    {
        public Task<ContentHandle> OpenOriginalAsync(long imageId, CancellationToken cancellationToken)
        {
            byte[] bytes = [1, 2, 3];
            return Task.FromResult(new ContentHandle(new MemoryStream(bytes), bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes))));
        }
    }
    private sealed class Factory(ProcessingSchemaFixture fixture) : IDbContextFactory<PhotoPlatformDbContext>
    {
        private int created;
        internal int Created => created;
        public PhotoPlatformDbContext CreateDbContext()
        {
            Interlocked.Increment(ref created);
            return fixture.CreateContext();
        }
    }
}
