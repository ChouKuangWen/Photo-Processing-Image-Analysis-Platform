using PhotoPlatform.Application.Interfaces;
using PhotoPlatform.Application.Processing;

namespace PhotoPlatform.UnitTests.Processing;

public sealed class FrozenInputProviderContractTests
{
    private static readonly Sha256CalculationInput Input = new(7, new string('a', 64), 1, "workflow-mvp-v1", "hash-v1");

    // 測試目的：確認正式介面能交接原身分及取消 token，不擴充寫入入口。
    // 測試情境：用只回傳固定值的替身，透過泛型介面呼叫並檢查公開方法。
    // 預期結果：原 Context／Evidence 保留，只有核准方法；不代表 SQL 來源驗證成功。
    [Fact]
    public async Task Contract_PreservesTypedHandoff()
    {
        var context = new ProcessingContext<Sha256CalculationInput>(9, Input);
        var stub = new Stub(context);
        IFrozenInputProvider<Sha256CalculationInput> port = stub;
        using var cancellation = new CancellationTokenSource();
        var result = await port.LoadFrozenAsync(9, cancellation.Token);
        Assert.Same(context, result);
        Assert.Same(Input.Identity, result.Input.Identity);
        Assert.Equal(cancellation.Token, stub.Token);
        Assert.Equal(9, stub.JobId);
        Assert.Equal(9, result.JobId);
        var method = Assert.Single(typeof(IFrozenInputProvider<Sha256CalculationInput>).GetMethods());
        Assert.Equal("LoadFrozenAsync", method.Name);
        Assert.Equal(typeof(Task<ProcessingContext<Sha256CalculationInput>>), method.ReturnType);
        Assert.Equal(new[] { typeof(long), typeof(CancellationToken) },
            method.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.Empty(typeof(IFrozenInputProvider<Sha256CalculationInput>).GetProperties());
    }

    // 測試目的：確認每個核准失敗原因可以安全回報，不能混入任意錯誤訊息。
    // 測試情境：逐一建立失敗例外，並透過替身將原例外交回呼叫端。
    // 預期結果：原因不變、訊息固定且沒有內層例外，公開建構子只接受核准參數。
    [Fact]
    public async Task Failure_PreservesAllApprovedReasonsSafely()
    {
        Assert.Equal(4, Enum.GetValues<FrozenInputFailureReason>().Length);
        foreach (var reason in Enum.GetValues<FrozenInputFailureReason>())
        {
            var failure = new FrozenInputUnavailableException(9, reason);
            Assert.Equal(reason, failure.Reason);
            Assert.Equal(9, failure.JobId);
            Assert.Equal("Frozen input is unavailable.", failure.Message);
            Assert.Null(failure.InnerException);
            var context = new ProcessingContext<Sha256CalculationInput>(9, Input);
            var stub = new Stub(context, failure);
            var thrown = await Assert.ThrowsAsync<FrozenInputUnavailableException>(() =>
                stub.LoadFrozenAsync(9, CancellationToken.None));
            Assert.Same(failure, thrown);
        }
        var constructor = Assert.Single(typeof(FrozenInputUnavailableException).GetConstructors());
        Assert.Equal(new[] { typeof(long), typeof(FrozenInputFailureReason) },
            constructor.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.All(typeof(FrozenInputUnavailableException).GetProperties()
            .Where(property => property.DeclaringType == typeof(FrozenInputUnavailableException)),
            property => Assert.Null(property.SetMethod));
    }

    // 測試目的：確認例外本身不接受未定義分類或非法 JobId。
    // 測試情境：傳入範圍外 enum及零或負數識別。
    // 預期結果：拒絕建構，不產生看似合法的受控失敗。
    [Fact]
    public void Failure_RejectsInvalidArguments()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FrozenInputUnavailableException(9, (FrozenInputFailureReason)(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FrozenInputUnavailableException(9, (FrozenInputFailureReason)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FrozenInputUnavailableException(0, FrozenInputFailureReason.Missing));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FrozenInputUnavailableException(-1, FrozenInputFailureReason.Missing));
    }

    // 測試目的：確認取消不被包裝成來源缺失或驗證失敗。
    // 測試情境：使用已取消 token 呼叫替身，並同時準備來源失敗例外。
    // 預期結果：保留原 token 的取消例外先傳出；不宣稱真實外部操作已停止。
    [Fact]
    public async Task Cancellation_RemainsCancellation()
    {
        var context = new ProcessingContext<Sha256CalculationInput>(9, Input);
        var stub = new Stub(context,
            new FrozenInputUnavailableException(9, FrozenInputFailureReason.SourceUnavailable));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            stub.LoadFrozenAsync(9, cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
    }


    // 替身只記錄交接，不假裝驗證 Completed、Owner、History 或持久來源。
    private sealed class Stub(ProcessingContext<Sha256CalculationInput> value, FrozenInputUnavailableException? failure = null) : IFrozenInputProvider<Sha256CalculationInput>
    {
        public CancellationToken Token { get; private set; }
        public long JobId { get; private set; }

        public Task<ProcessingContext<Sha256CalculationInput>> LoadFrozenAsync(long jobId, CancellationToken cancellationToken)
        {
            Token = cancellationToken;
            JobId = jobId;
            cancellationToken.ThrowIfCancellationRequested();
            return failure is null ? Task.FromResult(value) : Task.FromException<ProcessingContext<Sha256CalculationInput>>(failure);
        }
    }
}
