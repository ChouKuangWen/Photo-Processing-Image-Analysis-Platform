using PhotoPlatform.Domain.Exceptions;
using PhotoPlatform.Domain.Policies;

namespace PhotoPlatform.UnitTests.Domain;

/// <summary>
/// 驗證執行序號、重試槽位與恢復次數的獨立語意，以及 F-01 初始槽位限制和整數邊界。
/// </summary>
public class ProcessingAttemptIdentityTests
{
    [Fact]
    public void ResumeAndRetry_HaveDifferentIdentities()
    {
        // 先 Resume 再 Retry，證明新槽位沿用累積 AttemptNo，但 ResumeNo 必須從零開始。
        var initial = ProcessingAttemptIdentity.Initial();
        Assert.Equal(new ProcessingAttemptIdentity(1, 0, 0), initial);
        Assert.False(initial.StartsRetrySlot);
        var resumed = initial.Resume();
        Assert.Equal(new ProcessingAttemptIdentity(2, 0, 1), resumed);
        Assert.False(resumed.StartsRetrySlot);
        var retry = resumed.NextRetry();
        Assert.Equal(new ProcessingAttemptIdentity(3, 1, 0), retry);
        Assert.True(retry.StartsRetrySlot);
        Assert.Equal(new ProcessingAttemptIdentity(4, 1, 1), retry.Resume());
        Assert.False(retry.Resume().StartsRetrySlot);
        Assert.Equal(new ProcessingAttemptIdentity(1, 0, 0), initial);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(-1, 0, 0)]
    [InlineData(1, -1, 0)]
    [InlineData(5, 4, 0)]
    [InlineData(1, 0, -1)]
    [InlineData(1, 1, 0)]
    [InlineData(1, 0, 1)]
    [InlineData(2, 0, 0)]
    [InlineData(3, 0, 1)]
    [InlineData(int.MaxValue, 0, int.MaxValue)]
    public void InvalidIdentity_IsRejected(int attemptNo, int retryOrdinal, int resumeNo)
    {
        // F-01 的 (2,0,0)／(3,0,1) 數值各自合法，組合卻不符合初始槽位等式。
        // MaxValue／MaxValue 案例要求以較寬型別比較，不能讓「恢復次數加一」溢位後繞過驗證。
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProcessingAttemptIdentity(attemptNo, retryOrdinal, resumeNo));
    }

    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(2, 0, 1)]
    [InlineData(3, 0, 2)]
    [InlineData(int.MaxValue, 0, int.MaxValue - 1)]
    [InlineData(2, 1, 0)]
    [InlineData(3, 1, 0)]
    [InlineData(4, 1, 1)]
    [InlineData(6, 2, 1)]
    [InlineData(8, 3, 1)]
    public void ValidIdentity_PreservesAllComponents(int attemptNo, int retryOrdinal, int resumeNo)
    {
        // (6,2,1)／(8,3,1) 可包含先前槽位的恢復次數，不能將初始槽位的等式套用到所有槽位。
        var identity = new ProcessingAttemptIdentity(attemptNo, retryOrdinal, resumeNo);

        Assert.Equal(attemptNo, identity.AttemptNo);
        Assert.Equal(retryOrdinal, identity.RetryOrdinal);
        Assert.Equal(resumeNo, identity.ResumeNo);
    }

    [Fact]
    public void FourthSlot_CannotRetryButCanResume()
    {
        // 槽位 0～3 包含一次初始執行與三次重試；到達上限仍可恢復同槽位，不等於允許第四次重試。
        var last = ProcessingAttemptIdentity.Initial().NextRetry().NextRetry().NextRetry();
        Assert.Equal(3, last.RetryOrdinal);
        Assert.Throws<InvalidProcessingStateException>(() => last.NextRetry());
        Assert.Equal(3, last.Resume().RetryOrdinal);
        Assert.Equal(1, last.Resume().ResumeNo);
    }

    [Fact]
    public void AttemptOverflow_DoesNotWrap()
    {
        // 先建立符合初始槽位不變量的極限識別，再驗證遞增拒絕溢位，而非因非法測試資料提早失敗。
        var identity = new ProcessingAttemptIdentity(int.MaxValue, 0, int.MaxValue - 1);
        Assert.Throws<OverflowException>(() => identity.Resume());
        Assert.Throws<OverflowException>(() => identity.NextRetry());
        Assert.Equal(int.MaxValue, identity.AttemptNo);
    }
}
