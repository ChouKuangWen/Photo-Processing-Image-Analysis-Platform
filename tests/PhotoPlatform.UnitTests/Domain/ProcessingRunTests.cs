using PhotoPlatform.Domain.Entities;
using PhotoPlatform.Domain.Enums;
using PhotoPlatform.Domain.Exceptions;
using PhotoPlatform.Domain.Policies;

namespace PhotoPlatform.UnitTests.Domain;

/// <summary>
/// 驗證 RunNo 與建立原因配對、Initial／ManualRetry 的起始狀態，以及恢復與終態時間不變量。
/// </summary>
public class ProcessingRunTests
{
    private static readonly DateTimeOffset Created = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static ProcessingRun Initial() => new(10, 1, ProcessingRunReason.Initial, "workflow-mvp-v1", Created);

    [Fact]
    public void InitialRun_IsActiveWithFirstStart()
    {
        // 初始 Run 隨首次 Claim 建立為 Active，不經人工重試的等待階段。
        var run = Initial();
        Assert.Equal(0, run.Id);
        Assert.Equal(10, run.JobId);
        Assert.Equal(1, run.RunNo);
        Assert.Equal(ProcessingRunReason.Initial, run.Reason);
        Assert.Equal(ProcessingRunStatus.Active, run.Status);
        Assert.Equal(Created, run.StartedAt);
        Assert.Null(run.CompletedAt);
        Assert.Null(run.TraceId);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void ManualRun_IsPendingUntilActivation(int runNo)
    {
        // 2～4 是三次人工重試的 Run 編號，不是 Step 的 RetryOrdinal；Claim 前不得有開始時間。
        var run = new ProcessingRun(10, runNo, ProcessingRunReason.ManualRetry, "workflow-mvp-v1", Created, "trace");
        Assert.Equal(ProcessingRunStatus.Pending, run.Status);
        Assert.Null(run.StartedAt);
        Assert.Throws<InvalidProcessingStateException>(() => run.Complete(Created));
        Assert.Throws<InvalidProcessingStateException>(() => run.Fail(Created));
        Assert.Throws<InvalidProcessingStateException>(() => run.Recover(ProcessingRecoveryAuthorization.Confirm(true, true)));
        run.Activate(Created.AddSeconds(1));
        Assert.Equal(Created.AddSeconds(1), run.StartedAt);
        Assert.Equal("trace", run.TraceId);
    }

    [Theory]
    // 0 與 5 分別位於合法 RunNo 1～4 的兩側。
    [InlineData(0)]
    [InlineData(5)]
    public void Run_RejectsOutOfRangeRunNo(int runNo)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ProcessingRun(10, runNo, ProcessingRunReason.Initial, "workflow-mvp-v1", Created));
    }

    [Theory]
    [InlineData(1, ProcessingRunReason.ManualRetry)]
    [InlineData(2, ProcessingRunReason.Initial)]
    [InlineData(4, ProcessingRunReason.Initial)]
    public void Run_RejectsReasonMismatch(int runNo, ProcessingRunReason reason)
    {
        Assert.Throws<InvalidProcessingStateException>(() =>
            new ProcessingRun(10, runNo, reason, "workflow-mvp-v1", Created));
    }

    [Fact]
    public void Run_RejectsInvalidConstruction()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProcessingRun(0, 1, ProcessingRunReason.Initial, "v1", Created));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProcessingRun(10, 1, (ProcessingRunReason)99, "v1", Created));
        Assert.Throws<ArgumentException>(() => new ProcessingRun(10, 1, ProcessingRunReason.Initial, "", Created));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProcessingRun(10, 1, ProcessingRunReason.Initial, new string('v', 51), Created));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProcessingRun(10, 1, ProcessingRunReason.Initial, "v1", Created, new string('t', 101)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TerminalRun_CannotBeOverwrittenOrRecovered(bool success)
    {
        // 成功與失敗都屬不可變歷史；人工重試須另建 Run，不能重開或改寫這一筆。
        var run = Initial();
        if (success)
            run.Complete(Created.AddSeconds(2));
        else
            run.Fail(Created.AddSeconds(2));
        var status = run.Status;

        Assert.Throws<InvalidProcessingStateException>(() => run.Complete(Created.AddSeconds(3)));
        Assert.Throws<InvalidProcessingStateException>(() => run.Fail(Created.AddSeconds(3)));
        Assert.Throws<InvalidProcessingStateException>(() => run.Activate(Created.AddSeconds(3)));
        Assert.Throws<InvalidProcessingStateException>(() => run.Recover(ProcessingRecoveryAuthorization.Confirm(true, true)));
        Assert.Equal(status, run.Status);
        Assert.Equal(Created.AddSeconds(2), run.CompletedAt);
    }

    [Fact]
    public void Recovery_PreservesRunIdentityAndInitialTime()
    {
        // 刻意延後一小時再啟動，確保恢復不會以新的啟動時間取代首次 StartedAt。
        var run = Initial();
        Assert.Throws<ArgumentNullException>(() => run.Recover(null!));
        run.Recover(ProcessingRecoveryAuthorization.Confirm(true, true));
        Assert.Equal(ProcessingRunStatus.Pending, run.Status);
        Assert.Equal(1, run.RunNo);
        Assert.Equal(ProcessingRunReason.Initial, run.Reason);
        run.Activate(Created.AddHours(1));
        Assert.Equal(Created, run.StartedAt);
        Assert.Null(run.CompletedAt);
        Assert.Throws<InvalidProcessingStateException>(() => run.Activate(Created.AddHours(2)));
    }

    [Fact]
    public void InvalidTime_DoesNotMutateRun()
    {
        var run = Initial();
        Assert.Throws<ArgumentOutOfRangeException>(() => run.Complete(Created.AddTicks(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => run.Fail(Created.AddTicks(-1)));
        Assert.Equal(ProcessingRunStatus.Active, run.Status);
        Assert.Null(run.CompletedAt);

        var manual = new ProcessingRun(10, 2, ProcessingRunReason.ManualRetry, "v1", Created);
        Assert.Throws<ArgumentOutOfRangeException>(() => manual.Activate(Created.AddTicks(-1)));
        Assert.Equal(ProcessingRunStatus.Pending, manual.Status);
        Assert.Null(manual.StartedAt);
    }
}
