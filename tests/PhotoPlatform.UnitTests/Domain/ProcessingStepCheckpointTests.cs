using PhotoPlatform.Domain.Entities;
using PhotoPlatform.Domain.Enums;
using PhotoPlatform.Domain.Exceptions;
using PhotoPlatform.Domain.Policies;

namespace PhotoPlatform.UnitTests.Domain;

/// <summary>
/// 驗證步驟進度、重試期限與結果終態，區分新重試槽位及同槽位恢復。
/// 使用固定時間檢查邊界，不依賴實際等待或背景排程。
/// </summary>
public sealed class ProcessingStepCheckpointTests
{
    private static readonly DateTimeOffset StartedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Constructor_CreatesPendingCheckpoint()
    {
        var checkpoint = Create();

        Assert.Equal(10, checkpoint.RunId);
        Assert.Equal("Metadata", checkpoint.Step);
        Assert.Equal(1, checkpoint.StepOrder);
        Assert.Equal("v1", checkpoint.RuleVersion);
        Assert.Equal(new string('a', 64), checkpoint.InputFingerprint);
        Assert.Equal(ProcessingCheckpointStatus.Pending, checkpoint.Status);
        Assert.Equal(0, checkpoint.RetryOrdinal);
        Assert.Null(checkpoint.StartedAt);
        Assert.Null(checkpoint.CompletedAt);
        Assert.Null(checkpoint.NextRetryAt);
        Assert.Null(checkpoint.ResultStatus);
        Assert.Null(checkpoint.ResultReference);
        Assert.Null(checkpoint.ErrorCode);
        Assert.Null(checkpoint.ErrorClassification);
    }

    [Fact]
    public void Constructor_RejectsInvalidInputs()
    {
        var fingerprint = new string('a', 64);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProcessingStepCheckpoint(0, "Metadata", 1, "v1", fingerprint));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProcessingStepCheckpoint(1, "Metadata", 0, "v1", fingerprint));
        Assert.Throws<ArgumentException>(() => new ProcessingStepCheckpoint(1, " ", 1, "v1", fingerprint));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProcessingStepCheckpoint(1, new string('a', 51), 1, "v1", fingerprint));
        Assert.Throws<ArgumentException>(() => new ProcessingStepCheckpoint(1, "Metadata", 1, "", fingerprint));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProcessingStepCheckpoint(1, "Metadata", 1, new string('a', 51), fingerprint));
    }

    [Theory]
    [InlineData(63, 'a')]
    [InlineData(65, 'a')]
    [InlineData(64, 'A')]
    [InlineData(64, 'g')]
    public void Constructor_RejectsInvalidFingerprint(int length, char character)
    {
        // 63／65 檢查固定長度的兩側；A 與 g 分別檢查大小寫及十六進位字元限制。
        Assert.Throws<ArgumentException>(() => new ProcessingStepCheckpoint(1, "Metadata", 1, "v1", new string(character, length)));
    }

    [Theory]
    [InlineData(ProcessingResultStatus.Success)]
    [InlineData(ProcessingResultStatus.Warning)]
    [InlineData(ProcessingResultStatus.Skipped)]
    public void Complete_StoresResultAndPreventsAllFurtherTransitions(ProcessingResultStatus result)
    {
        var checkpoint = Create();
        checkpoint.Start(ProcessingAttemptIdentity.Initial(), StartedAt);
        Assert.Throws<InvalidProcessingStateException>(() => checkpoint.Start(ProcessingAttemptIdentity.Initial(), StartedAt));
        checkpoint.Complete(result, result == ProcessingResultStatus.Skipped ? null : "metadata:1", StartedAt.AddSeconds(1));

        Assert.Equal(ProcessingCheckpointStatus.Completed, checkpoint.Status);
        Assert.Equal(result, checkpoint.ResultStatus);
        Assert.Equal(StartedAt.AddSeconds(1), checkpoint.CompletedAt);
        AssertTerminal(checkpoint);
    }

    [Fact]
    public void Retry_UsesOneTwoFourSecondsAndFailsOnlyAfterExhaustion()
    {
        // 從初始槽位走完三次重試：每次驗證到期前一個 tick 拒絕、到期當下允許。
        // RetryWaiting 尚無新 invocation，不得經 Recovery 清掉等待期限；耗盡後才能以暫時性失敗結束。
        var checkpoint = Create();
        var attempt = ProcessingAttemptIdentity.Initial();
        var invocationAt = StartedAt;
        checkpoint.Start(attempt, invocationAt);

        for (var ordinal = 0; ordinal < 3; ordinal++)
        {
            Assert.Throws<InvalidProcessingStateException>(() => checkpoint.Fail("TIMEOUT", ProcessingErrorClassification.Transient, invocationAt));
            checkpoint.WaitForRetry("TIMEOUT", invocationAt);
            var dueAt = invocationAt.AddSeconds(1 << ordinal);
            Assert.Equal(ordinal + 1, checkpoint.RetryOrdinal);
            Assert.Equal(dueAt, checkpoint.NextRetryAt);
            Assert.Equal(ProcessingCheckpointStatus.RetryWaiting, checkpoint.Status);
            Assert.Equal(ProcessingErrorClassification.Transient, checkpoint.ErrorClassification);
            Assert.Throws<InvalidProcessingStateException>(() => checkpoint.Recover(ProcessingRecoveryAuthorization.Confirm(true, true)));
            attempt = attempt.NextRetry();
            Assert.Throws<InvalidProcessingStateException>(() => checkpoint.Start(attempt, dueAt.AddTicks(-1)));
            Assert.Equal(dueAt, checkpoint.NextRetryAt);
            checkpoint.Start(attempt, dueAt);
            Assert.Null(checkpoint.NextRetryAt);
            Assert.Equal(StartedAt, checkpoint.StartedAt);
            invocationAt = dueAt;
        }

        Assert.Throws<InvalidProcessingStateException>(() => checkpoint.WaitForRetry("TIMEOUT", invocationAt));
        checkpoint.Fail("TIMEOUT", ProcessingErrorClassification.Transient, invocationAt);
        Assert.Equal(ProcessingCheckpointStatus.Failed, checkpoint.Status);
        Assert.Null(checkpoint.ResultStatus);
        AssertTerminal(checkpoint);
    }

    [Fact]
    public void PermanentFailure_DoesNotScheduleRetry()
    {
        var checkpoint = Create();
        checkpoint.Start(ProcessingAttemptIdentity.Initial(), StartedAt);
        checkpoint.Fail("INVALID_IMAGE", ProcessingErrorClassification.Permanent, StartedAt.AddSeconds(1));

        Assert.Equal(0, checkpoint.RetryOrdinal);
        Assert.Null(checkpoint.NextRetryAt);
        Assert.Equal("INVALID_IMAGE", checkpoint.ErrorCode);
        Assert.Equal(ProcessingErrorClassification.Permanent, checkpoint.ErrorClassification);
        AssertTerminal(checkpoint);
    }

    [Fact]
    public void Recovery_PreservesRetrySlotAndFirstStart()
    {
        // 先進入 ordinal 1 再恢復，避免只測 ordinal 0 而漏掉「恢復誤將槽位歸零」的問題。
        var checkpoint = Create();
        var attempt = ProcessingAttemptIdentity.Initial();
        checkpoint.Start(attempt, StartedAt);
        checkpoint.WaitForRetry("TIMEOUT", StartedAt);
        attempt = attempt.NextRetry();
        checkpoint.Start(attempt, StartedAt.AddSeconds(1));
        Assert.Throws<ArgumentNullException>(() => checkpoint.Recover(null!));
        checkpoint.Recover(ProcessingRecoveryAuthorization.Confirm(true, true));

        Assert.Equal(ProcessingCheckpointStatus.Pending, checkpoint.Status);
        Assert.Equal(1, checkpoint.RetryOrdinal);
        checkpoint.Start(attempt.Resume(), StartedAt.AddSeconds(2));
        checkpoint.Complete(ProcessingResultStatus.Success, "metadata:1", StartedAt.AddSeconds(3));
        Assert.Equal(StartedAt, checkpoint.StartedAt);
        Assert.Null(checkpoint.ErrorCode);
        Assert.Null(checkpoint.ErrorClassification);
    }

    [Fact]
    public void PendingCheckpoint_RejectsFinishRetryRecoveryAndWrongSlot()
    {
        var checkpoint = Create();
        Assert.Throws<InvalidProcessingStateException>(() => checkpoint.Complete(ProcessingResultStatus.Success, null, StartedAt));
        Assert.Throws<InvalidProcessingStateException>(() => checkpoint.Fail("ERROR", ProcessingErrorClassification.Permanent, StartedAt));
        Assert.Throws<InvalidProcessingStateException>(() => checkpoint.WaitForRetry("ERROR", StartedAt));
        Assert.Throws<InvalidProcessingStateException>(() => checkpoint.Recover(ProcessingRecoveryAuthorization.Confirm(true, true)));
        Assert.Throws<InvalidProcessingStateException>(() => checkpoint.Start(new ProcessingAttemptIdentity(2, 1, 0), StartedAt));
        Assert.Throws<ArgumentNullException>(() => checkpoint.Start(null!, StartedAt));
        Assert.Null(checkpoint.StartedAt);
        Assert.Equal(ProcessingCheckpointStatus.Pending, checkpoint.Status);
    }

    [Fact]
    public void InvalidCompletion_DoesNotMutateCheckpoint()
    {
        var checkpoint = Create();
        checkpoint.Start(ProcessingAttemptIdentity.Initial(), StartedAt);
        Assert.Throws<ArgumentOutOfRangeException>(() => checkpoint.Complete((ProcessingResultStatus)99, null, StartedAt));
        Assert.Throws<ArgumentOutOfRangeException>(() => checkpoint.Complete(ProcessingResultStatus.Success, new string('a', 501), StartedAt));
        Assert.Throws<ArgumentOutOfRangeException>(() => checkpoint.Complete(ProcessingResultStatus.Success, null, StartedAt.AddTicks(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => checkpoint.Fail("ERROR", (ProcessingErrorClassification)99, StartedAt));
        Assert.Throws<ArgumentOutOfRangeException>(() => checkpoint.Fail("ERROR", ProcessingErrorClassification.Permanent, StartedAt.AddTicks(-1)));
        Assert.Throws<ArgumentException>(() => checkpoint.WaitForRetry("", StartedAt));
        Assert.Throws<ArgumentOutOfRangeException>(() => checkpoint.WaitForRetry("ERROR", StartedAt.AddTicks(-1)));
        Assert.Equal(ProcessingCheckpointStatus.Processing, checkpoint.Status);
        Assert.Equal(0, checkpoint.RetryOrdinal);
        Assert.Null(checkpoint.CompletedAt);
        Assert.Null(checkpoint.ResultStatus);
        Assert.Null(checkpoint.ErrorCode);
    }

    private static ProcessingStepCheckpoint Create() => new(10, "Metadata", 1, "v1", new string('a', 64));

    private static void AssertTerminal(ProcessingStepCheckpoint checkpoint)
    {
        // 成功與失敗共用此防線，避免後續通知覆寫既有結果；新 Run 應使用新的 Checkpoint。
        var status = checkpoint.Status;
        var completedAt = checkpoint.CompletedAt;
        Assert.Throws<InvalidProcessingStateException>(() => checkpoint.Start(ProcessingAttemptIdentity.Initial(), StartedAt.AddMinutes(1)));
        Assert.Throws<InvalidProcessingStateException>(() => checkpoint.Complete(ProcessingResultStatus.Warning, null, StartedAt.AddMinutes(1)));
        Assert.Throws<InvalidProcessingStateException>(() => checkpoint.Fail("ERROR", ProcessingErrorClassification.Permanent, StartedAt.AddMinutes(1)));
        Assert.Throws<InvalidProcessingStateException>(() => checkpoint.WaitForRetry("ERROR", StartedAt.AddMinutes(1)));
        Assert.Throws<InvalidProcessingStateException>(() => checkpoint.Recover(ProcessingRecoveryAuthorization.Confirm(true, true)));
        Assert.Equal(status, checkpoint.Status);
        Assert.Equal(completedAt, checkpoint.CompletedAt);
    }
}
