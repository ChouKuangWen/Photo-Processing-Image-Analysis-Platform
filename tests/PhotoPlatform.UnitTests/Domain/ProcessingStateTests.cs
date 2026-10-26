using PhotoPlatform.Domain.Entities;
using PhotoPlatform.Domain.Enums;
using PhotoPlatform.Domain.Exceptions;
using PhotoPlatform.Domain.Policies;

namespace PhotoPlatform.UnitTests.Domain;

/// <summary>
/// 驗證 Job 的生命週期、人工重試與受控恢復，並保護 Upload 識別及累計重試投影。
/// 僅驗證領域前提，不代表 SQL 執行權或持久化去重已驗收。
/// </summary>
public class ProcessingStateTests
{
    private static readonly DateTimeOffset Created = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static ProcessingJob Create() => new(10, Guid.NewGuid(), WorkflowType.Naming, Created);

    // 透過合法轉換建立各起點，避免直接設定狀態而跳過必要的時間與錯誤欄位。
    private static ProcessingJob InState(ProcessingJobStatus status)
    {
        var job = Create();
        if (status != ProcessingJobStatus.Pending)
            job.Start(Created.AddSeconds(1));
        if (status == ProcessingJobStatus.Completed)
            job.Complete(Created.AddSeconds(2));
        if (status == ProcessingJobStatus.Failed)
            job.Fail("TEMPORARY_FAILURE", "Temporary failure.", Created.AddSeconds(2));
        return job;
    }

    [Fact]
    public void Job_CompletesWithoutChangingUploadIdentity()
    {
        var job = Create();
        var batchId = job.BatchId;
        job.Start(Created.AddSeconds(1));
        job.Complete(Created.AddSeconds(2));

        Assert.Equal(ProcessingJobStatus.Completed, job.Status);
        Assert.Equal(Created, job.CreatedAt);
        Assert.Equal(Created.AddSeconds(1), job.StartedAt);
        Assert.Equal(Created.AddSeconds(2), job.CompletedAt);
        Assert.Equal(batchId, job.BatchId);
        Assert.Equal(10, job.ImageId);
        Assert.Equal(WorkflowType.Naming, job.Workflow);
        Assert.Null(job.ErrorCode);
        Assert.Null(job.ErrorMessage);
    }

    [Theory]
    [InlineData(ProcessingJobStatus.Processing)]
    [InlineData(ProcessingJobStatus.Completed)]
    [InlineData(ProcessingJobStatus.Failed)]
    public void Job_StartRejectsNonPending(ProcessingJobStatus status)
    {
        var job = InState(status);
        Assert.Throws<InvalidProcessingStateException>(() => job.Start(Created.AddSeconds(3)));
        Assert.Equal(status, job.Status);
    }

    [Theory]
    [InlineData(ProcessingJobStatus.Pending)]
    [InlineData(ProcessingJobStatus.Completed)]
    [InlineData(ProcessingJobStatus.Failed)]
    public void Job_TerminalOperationsRequireProcessing(ProcessingJobStatus status)
    {
        // 未開始的工作不能結束；已結束的工作也不能藉再次結束覆寫原始結果。
        var job = InState(status);
        var completedAt = job.CompletedAt;
        var error = job.ErrorCode;
        Assert.Throws<InvalidProcessingStateException>(() => job.Complete(Created.AddSeconds(4)));
        Assert.Throws<InvalidProcessingStateException>(() => job.Fail("ERROR", "Failure.", Created.AddSeconds(4)));
        Assert.Equal(status, job.Status);
        Assert.Equal(completedAt, job.CompletedAt);
        Assert.Equal(error, job.ErrorCode);
    }

    [Theory]
    [InlineData(ProcessingJobStatus.Pending)]
    [InlineData(ProcessingJobStatus.Processing)]
    [InlineData(ProcessingJobStatus.Completed)]
    public void Job_ManualRetryRequiresFailed(ProcessingJobStatus status)
    {
        var job = InState(status);
        Assert.Throws<InvalidProcessingStateException>(() =>
            job.PrepareManualRetry(ProcessingErrorClassification.Transient, 2));
        Assert.Equal(status, job.Status);
    }

    [Theory]
    [InlineData(ProcessingErrorClassification.Permanent, 2)]
    [InlineData((ProcessingErrorClassification)99, 2)]
    [InlineData(ProcessingErrorClassification.Transient, 1)]
    [InlineData(ProcessingErrorClassification.Transient, 5)]
    public void Job_ManualRetryRejectsInvalidEligibility(ProcessingErrorClassification classification, int runNo)
    {
        var job = InState(ProcessingJobStatus.Failed);
        Assert.Throws<InvalidProcessingStateException>(() => job.PrepareManualRetry(classification, runNo));
        Assert.Equal(ProcessingJobStatus.Failed, job.Status);
        Assert.Equal("TEMPORARY_FAILURE", job.ErrorCode);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Job_ManualRetryClearsOnlyCurrentRunProjection(int nextRunNo)
    {
        // 人工重試開始新的 Run，應清空目前時間與錯誤，但不歸零跨 Run 累積的重試次數。
        var job = InState(ProcessingJobStatus.Processing);
        job.RecordRetryStart(ProcessingAttemptIdentity.Initial().NextRetry());
        job.Fail("TEMPORARY_FAILURE", "Temporary failure.", Created.AddSeconds(2));
        job.PrepareManualRetry(ProcessingErrorClassification.Transient, nextRunNo);

        Assert.Equal(ProcessingJobStatus.Pending, job.Status);
        Assert.Equal(Created, job.CreatedAt);
        Assert.Equal(1, job.RetryCount);
        Assert.Null(job.StartedAt);
        Assert.Null(job.CompletedAt);
        Assert.Null(job.ErrorCode);
        Assert.Null(job.ErrorMessage);

        job.Start(Created.AddSeconds(10));
        Assert.Equal(Created.AddSeconds(10), job.StartedAt);
    }

    [Fact]
    public void Job_RecoveryPreservesFirstStartAndRetryCount()
    {
        // 與人工重試相反，Recovery 延續原 Run；同槽位 Resume 不應再計一次 Retry。
        var job = InState(ProcessingJobStatus.Processing);
        var retry = ProcessingAttemptIdentity.Initial().NextRetry();
        job.RecordRetryStart(retry);
        job.Recover(ProcessingRecoveryAuthorization.Confirm(true, true));
        Assert.Equal(ProcessingJobStatus.Pending, job.Status);
        job.Start(Created.AddSeconds(10));
        job.RecordRetryStart(retry.Resume());
        Assert.Equal(Created.AddSeconds(1), job.StartedAt);
        Assert.Equal(1, job.RetryCount);
        Assert.Null(job.CompletedAt);
    }

    [Theory]
    [InlineData(ProcessingJobStatus.Pending)]
    [InlineData(ProcessingJobStatus.Completed)]
    [InlineData(ProcessingJobStatus.Failed)]
    public void Job_RecoveryRejectsNonProcessing(ProcessingJobStatus status)
    {
        var job = InState(status);
        Assert.Throws<InvalidProcessingStateException>(() =>
            job.Recover(ProcessingRecoveryAuthorization.Confirm(true, true)));
        Assert.Equal(status, job.Status);
    }

    [Fact]
    public void Job_RecoveryRequiresExplicitAuthorization()
    {
        var job = InState(ProcessingJobStatus.Processing);
        Assert.Throws<ArgumentNullException>(() => job.Recover(null!));
        Assert.Equal(ProcessingJobStatus.Processing, job.Status);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void RecoveryAuthorization_RequiresBothPrerequisites(bool stopped, bool reconciled)
    {
        // 三種不完整確認都必須拒絕；布林值只是外部核對結果，不在此測試程序存活偵測。
        Assert.Throws<InvalidProcessingStateException>(() =>
            ProcessingRecoveryAuthorization.Confirm(stopped, reconciled));
    }

    [Fact]
    public void Job_InvalidTimesAndErrorsLeaveStateUnchanged()
    {
        var job = Create();
        Assert.Throws<ArgumentOutOfRangeException>(() => job.Start(Created.AddTicks(-1)));
        Assert.Equal(ProcessingJobStatus.Pending, job.Status);
        Assert.Null(job.StartedAt);
        job.Start(Created.AddSeconds(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => job.Complete(Created));
        Assert.Throws<ArgumentException>(() => job.Fail("", "Failure.", Created.AddSeconds(2)));
        Assert.Throws<ArgumentOutOfRangeException>(() => job.Fail(new string('E', 51), null, Created.AddSeconds(2)));
        Assert.Equal(ProcessingJobStatus.Processing, job.Status);
        Assert.Null(job.CompletedAt);
        Assert.Null(job.ErrorCode);
    }

    [Fact]
    public void Job_RetryAccountingRequiresProcessingAndCountsOnlyNewRetrySlots()
    {
        var job = Create();
        Assert.Throws<InvalidProcessingStateException>(() => job.RecordRetryStart(ProcessingAttemptIdentity.Initial()));
        job.Start(Created);
        job.RecordRetryStart(ProcessingAttemptIdentity.Initial());
        job.RecordRetryStart(ProcessingAttemptIdentity.Initial().Resume());
        Assert.Equal(0, job.RetryCount);
        job.RecordRetryStart(ProcessingAttemptIdentity.Initial().NextRetry());
        Assert.Equal(1, job.RetryCount);
    }

    [Fact]
    public void Models_DoNotExposePublicSetters()
    {
        var models = new[] { typeof(ProcessingJob), typeof(ProcessingRun),
            typeof(ProcessingStepCheckpoint), typeof(ProcessingLog), typeof(ProcessingAttemptIdentity) };
        foreach (var model in models)
            Assert.All(model.GetProperties(), property => Assert.False(property.SetMethod?.IsPublic ?? false));
        Assert.Equal("INVALID_STATE_TRANSITION", InvalidProcessingStateException.ErrorCode);
    }
}
