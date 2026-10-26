using PhotoPlatform.Domain.Entities;
using PhotoPlatform.Domain.Enums;
using PhotoPlatform.Domain.Exceptions;
using PhotoPlatform.Domain.Policies;

namespace PhotoPlatform.UnitTests.Domain;

/// <summary>
/// 驗證單次 invocation 的識別與不可變終態，保留原始時間並區分未知耗時和實測耗時。
/// </summary>
public sealed class ProcessingLogTests
{
    private static readonly DateTimeOffset StartedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Constructor_StoresInvocationIdentityAndOriginalTimestamp()
    {
        // (4,1,2) 表示初次執行後進入重試槽位 1，再於該槽位恢復兩次，三個數值不可混用。
        var log = new ProcessingLog(10, 20, 30, new ProcessingAttemptIdentity(4, 1, 2), StartedAt, "trace");

        Assert.Equal(10, log.StepCheckpointId);
        Assert.Equal(20, log.JobId);
        Assert.Equal(30, log.ImageId);
        Assert.Equal(4, log.AttemptNo);
        Assert.Equal(1, log.RetryOrdinal);
        Assert.Equal(2, log.ResumeNo);
        Assert.Equal("trace", log.TraceId);
        Assert.Equal(ProcessingLogStatus.Running, log.Status);
        Assert.Equal(StartedAt, log.StartedAt);
        Assert.Equal(StartedAt, log.Timestamp);
        Assert.Null(log.CompletedAt);
        Assert.Null(log.DurationMs);
        Assert.Null(log.ErrorCode);
        Assert.Null(log.ErrorMessage);
    }

    [Theory]
    [InlineData(ProcessingLogStatus.Success)]
    [InlineData(ProcessingLogStatus.Warning)]
    [InlineData(ProcessingLogStatus.Failed)]
    [InlineData(ProcessingLogStatus.Interrupted)]
    [InlineData(ProcessingLogStatus.Skipped)]
    public void Finish_AllTerminalStatesAreImmutable(ProcessingLogStatus terminal)
    {
        // 對每種終態嘗試所有後續狀態，確保連相同終態的重寫也不能改變時間或耗時。
        var log = Create();
        log.Finish(terminal, StartedAt.AddSeconds(1), 1000, terminal == ProcessingLogStatus.Failed ? "TIMEOUT" : null);

        foreach (var target in Enum.GetValues<ProcessingLogStatus>())
            Assert.Throws<InvalidProcessingStateException>(() => log.Finish(target, StartedAt.AddSeconds(2), 2000, "OTHER"));

        Assert.Equal(terminal, log.Status);
        Assert.Equal(StartedAt, log.StartedAt);
        Assert.Equal(StartedAt, log.Timestamp);
        Assert.Equal(StartedAt.AddSeconds(1), log.CompletedAt);
        Assert.Equal(1000, log.DurationMs);
    }

    [Fact]
    public void InterruptedAttempt_UsesUnknownDurationAndNewResumeLog()
    {
        // 已知中斷收尾時間不等於已量測耗時，因此保留 null；Resume 另建 Log 並使用新的 Timestamp。
        var attempt = ProcessingAttemptIdentity.Initial();
        var original = Create();
        original.Finish(ProcessingLogStatus.Interrupted, StartedAt.AddSeconds(5), null);
        var resumed = new ProcessingLog(1, 2, 3, attempt.Resume(), StartedAt.AddSeconds(6));

        Assert.Null(original.DurationMs);
        Assert.Equal(ProcessingLogStatus.Interrupted, original.Status);
        Assert.Equal(ProcessingLogStatus.Running, resumed.Status);
        Assert.Equal(2, resumed.AttemptNo);
        Assert.Equal(0, resumed.RetryOrdinal);
        Assert.Equal(1, resumed.ResumeNo);
        Assert.Equal(StartedAt.AddSeconds(6), resumed.Timestamp);
    }

    [Fact]
    public void CheckpointWarning_DoesNotRewriteFailedInvocation()
    {
        // 只驗證兩種結果可並存：降級後的步驟為 Warning，實際失敗呼叫仍為 Failed；不測 Geo 降級政策。
        var log = Create();
        log.Finish(ProcessingLogStatus.Failed, StartedAt.AddSeconds(1), 1000, "GEO_UNAVAILABLE", "Geo service unavailable.");
        var checkpoint = new ProcessingStepCheckpoint(1, "Geo", 1, "v1", new string('a', 64));
        checkpoint.Start(ProcessingAttemptIdentity.Initial(), StartedAt);
        checkpoint.Complete(ProcessingResultStatus.Warning, null, StartedAt.AddSeconds(1));

        Assert.Equal(ProcessingLogStatus.Failed, log.Status);
        Assert.Equal("GEO_UNAVAILABLE", log.ErrorCode);
        Assert.Equal(ProcessingResultStatus.Warning, checkpoint.ResultStatus);
    }

    [Fact]
    public void InvalidFinish_DoesNotMutateRunningLog()
    {
        var log = Create();
        Assert.Throws<InvalidProcessingStateException>(() => log.Finish(ProcessingLogStatus.Running, StartedAt, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => log.Finish((ProcessingLogStatus)99, StartedAt, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => log.Finish(ProcessingLogStatus.Success, StartedAt.AddTicks(-1), null));
        Assert.Throws<ArgumentOutOfRangeException>(() => log.Finish(ProcessingLogStatus.Success, StartedAt, -1));
        Assert.Throws<ArgumentNullException>(() => log.Finish(ProcessingLogStatus.Failed, StartedAt, null));
        Assert.Throws<ArgumentException>(() => log.Finish(ProcessingLogStatus.Failed, StartedAt, null, ""));
        Assert.Throws<ArgumentOutOfRangeException>(() => log.Finish(ProcessingLogStatus.Warning, StartedAt, null, new string('a', 51)));
        Assert.Equal(ProcessingLogStatus.Running, log.Status);
        Assert.Null(log.CompletedAt);
        Assert.Null(log.DurationMs);
        Assert.Null(log.ErrorCode);
        Assert.Equal(StartedAt, log.Timestamp);
    }

    [Fact]
    public void Constructor_RejectsInvalidIdsAttemptAndTrace()
    {
        var attempt = ProcessingAttemptIdentity.Initial();
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProcessingLog(0, 2, 3, attempt, StartedAt));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProcessingLog(1, 0, 3, attempt, StartedAt));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProcessingLog(1, 2, 0, attempt, StartedAt));
        Assert.Throws<ArgumentNullException>(() => new ProcessingLog(1, 2, 3, null!, StartedAt));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProcessingLog(1, 2, 3, attempt, StartedAt, new string('a', 101)));
    }

    private static ProcessingLog Create() => new(1, 2, 3, ProcessingAttemptIdentity.Initial(), StartedAt);
}
