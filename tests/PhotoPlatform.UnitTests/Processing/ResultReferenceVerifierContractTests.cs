using PhotoPlatform.Application.Interfaces;
using PhotoPlatform.Application.Processing;

namespace PhotoPlatform.UnitTests.Processing;

public sealed class ResultReferenceVerifierContractTests
{
    private static readonly Sha256CalculationInput Input = new(7, new string('a', 64), 1, "workflow-mvp-v1", "hash-v1");

    // 測試目的：確認正式介面能交接原身分及取消 token，不擴充寫入入口。
    // 測試情境：用只回傳固定值的替身，透過泛型介面呼叫並檢查公開方法。
    // 預期結果：原 Context／Evidence 保留，只有核准方法；不代表 SQL 來源驗證成功。
    [Fact]
    public async Task Contract_PreservesTypedHandoff()
    {
        var context = new ProcessingContext<Sha256CalculationInput>(9, Input);
        var stub = new Stub(new Evidence(Input.Identity));
        IResultReferenceVerifier<Sha256CalculationInput, Evidence> port = stub;
        using var cancellation = new CancellationTokenSource();
        var result = await port.VerifyAsync(context, "image_hash_results/1/hash-v1", cancellation.Token);
        Assert.Same(stub.Evidence, result);
        Assert.Same(Input.Identity, result.Identity);
        Assert.Equal(cancellation.Token, stub.Token);
        Assert.Same(context, stub.Context);
        Assert.Equal("image_hash_results/1/hash-v1", stub.Reference);
        var method = Assert.Single(typeof(IResultReferenceVerifier<Sha256CalculationInput, Evidence>).GetMethods());
        Assert.Equal("VerifyAsync", method.Name);
        Assert.Equal(typeof(Task<Evidence>), method.ReturnType);
        Assert.Equal(new[] { typeof(ProcessingContext<Sha256CalculationInput>), typeof(string), typeof(CancellationToken) },
            method.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.Empty(typeof(IResultReferenceVerifier<Sha256CalculationInput, Evidence>).GetProperties());
    }

    // 測試目的：確認每個核准失敗原因可以安全回報，不能混入任意錯誤訊息。
    // 測試情境：逐一建立失敗例外，並透過替身將原例外交回呼叫端。
    // 預期結果：原因不變、訊息固定且沒有內層例外，公開建構子只接受核准參數。
    [Fact]
    public async Task Failure_PreservesAllApprovedReasonsSafely()
    {
        Assert.Equal(7, Enum.GetValues<ResultReferenceVerificationFailureReason>().Length);
        foreach (var reason in Enum.GetValues<ResultReferenceVerificationFailureReason>())
        {
            var failure = new ResultReferenceVerificationException(reason);
            Assert.Equal(reason, failure.Reason);
            Assert.Equal("Result reference verification failed.", failure.Message);
            Assert.Null(failure.InnerException);
            var context = new ProcessingContext<Sha256CalculationInput>(9, Input);
            var stub = new Stub(new Evidence(Input.Identity), failure);
            var thrown = await Assert.ThrowsAsync<ResultReferenceVerificationException>(() =>
                stub.VerifyAsync(context, "image_hash_results/1/hash-v1", CancellationToken.None));
            Assert.Same(failure, thrown);
        }
        var constructor = Assert.Single(typeof(ResultReferenceVerificationException).GetConstructors());
        Assert.Equal(new[] { typeof(ResultReferenceVerificationFailureReason) },
            constructor.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.All(typeof(ResultReferenceVerificationException).GetProperties()
            .Where(property => property.DeclaringType == typeof(ResultReferenceVerificationException)),
            property => Assert.Null(property.SetMethod));
    }

    // 測試目的：確認例外本身不接受未定義分類。
    // 測試情境：傳入範圍外 enum。
    // 預期結果：拒絕建構，不產生看似合法的受控失敗。
    [Fact]
    public void Failure_RejectsInvalidArguments()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ResultReferenceVerificationException((ResultReferenceVerificationFailureReason)(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ResultReferenceVerificationException((ResultReferenceVerificationFailureReason)99));

    }

    // 測試目的：確認取消不被包裝成來源缺失或驗證失敗。
    // 測試情境：使用已取消 token 呼叫替身，並同時準備來源失敗例外。
    // 預期結果：保留原 token 的取消例外先傳出；不宣稱真實外部操作已停止。
    [Fact]
    public async Task Cancellation_RemainsCancellation()
    {
        var context = new ProcessingContext<Sha256CalculationInput>(9, Input);
        var stub = new Stub(new Evidence(Input.Identity),
            new ResultReferenceVerificationException(ResultReferenceVerificationFailureReason.SourceUnavailable));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            stub.VerifyAsync(context, "image_hash_results/1/hash-v1", cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
    }

    private sealed record Evidence(StepEvidenceIdentity Identity) : IStepEvidence;

    // 替身只記錄交接，不假裝驗證 Completed、Owner、History 或持久來源。
    private sealed class Stub(Evidence value, ResultReferenceVerificationException? failure = null) : IResultReferenceVerifier<Sha256CalculationInput, Evidence>
    {
        public CancellationToken Token { get; private set; }
        public Evidence Evidence => value;
        public ProcessingContext<Sha256CalculationInput>? Context { get; private set; }
        public string? Reference { get; private set; }

        public Task<Evidence> VerifyAsync(ProcessingContext<Sha256CalculationInput> context, string resultReference, CancellationToken cancellationToken)
        {
            Token = cancellationToken;
            Context = context;
            Reference = resultReference;
            cancellationToken.ThrowIfCancellationRequested();
            return failure is null ? Task.FromResult(value) : Task.FromException<Evidence>(failure);
        }
    }
}
