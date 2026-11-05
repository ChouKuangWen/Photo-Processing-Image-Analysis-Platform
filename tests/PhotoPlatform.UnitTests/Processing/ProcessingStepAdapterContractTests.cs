using PhotoPlatform.Application.Interfaces;
using PhotoPlatform.Application.Processing;
using PhotoPlatform.Domain.Enums;

namespace PhotoPlatform.UnitTests.Processing;

public sealed class ProcessingStepAdapterContractTests
{
    // 測試目的：確認強型別 Adapter 可以原樣交接各種 R3 觀察結果。
    // 測試情境：測試替身接收具名 Context 與 token，回傳配對終態、失敗或待核對證據。
    // 預期結果：Context、token 與 Outcome 都不被替換，不宣稱模組或 SQL 已執行。
    [Fact]
    public async Task Adapter_PassesContextTokenAndR3Outcomes()
    {
        var input = new Sha256CalculationInput(7, new string('a', 64), 1, "workflow-mvp-v1", "hash-v1");
        var context = new ProcessingContext<Sha256CalculationInput>(1, input);
        var evidence = new Evidence(input.Identity);
        using var cancellation = new CancellationTokenSource();
        var outcomes = new StepOutcome<Payload, Evidence>[]
        {
            StepOutcome<Payload, Evidence>.Success(new(ProcessingResultStatus.Success, new ResultMaterial<Payload>.Prepared(new Payload()), evidence)),
            StepOutcome<Payload, Evidence>.Warning(new(ProcessingResultStatus.Warning, new ResultMaterial<Payload>.Prepared(new Payload()), evidence)),
            StepOutcome<Payload, Evidence>.Skipped(new(ProcessingResultStatus.Skipped, new ResultMaterial<Payload>.NoNewResult(), evidence)),
            new StepOutcome<Payload, Evidence>.Failed(new StepFailure(ProcessingErrorClassification.Transient, "TEST_ONLY")),
            new StepOutcome<Payload, Evidence>.NeedsReconciliation(evidence)
        };
        foreach (var outcome in outcomes)
        {
            var adapter = new TestAdapter(outcome);
            IProcessingStepAdapter<Sha256CalculationInput, Payload, Evidence> port = adapter;
            Assert.Same(outcome, await port.ExecuteAsync(context, cancellation.Token));
            Assert.Same(context, adapter.ReceivedContext);
            Assert.Equal(cancellation.Token, adapter.ReceivedToken);
        }
    }

    // 測試目的：確認測試替身遵守取消傳遞，不把取消包裝為失敗結果。
    // 測試情境：使用已取消 token 呼叫正式介面。
    // 預期結果：拋出帶原 token 的取消例外；不代表真實外部副作用已停止。
    [Fact]
    public async Task Adapter_PropagatesCancellationWithoutOutcomeConversion()
    {
        var input = new Sha256CalculationInput(7, new string('a', 64), 1, "workflow-mvp-v1", "hash-v1");
        var outcome = new StepOutcome<Payload, Evidence>.NeedsReconciliation(new Evidence(input.Identity));
        IProcessingStepAdapter<Sha256CalculationInput, Payload, Evidence> adapter = new TestAdapter(outcome);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            adapter.ExecuteAsync(new ProcessingContext<Sha256CalculationInput>(1, input), cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
    }

    // 測試目的：確認介面只有執行與 R3 交接，不新增交易或重試入口。
    // 測試情境：檢查正式介面的公開方法與參數型別。
    // 預期結果：只有 ExecuteAsync，接收具名 Context 與 CancellationToken。
    [Fact]
    public void Adapter_ExposesOnlyApprovedSignature()
    {
        var contract = typeof(IProcessingStepAdapter<Sha256CalculationInput, Payload, Evidence>);
        var method = Assert.Single(contract.GetMethods());
        Assert.Equal("ExecuteAsync", method.Name);
        Assert.Equal(typeof(Task<StepOutcome<Payload, Evidence>>), method.ReturnType);
        Assert.Equal(new[] { typeof(ProcessingContext<Sha256CalculationInput>), typeof(CancellationToken) },
            method.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.Empty(contract.GetProperties());
    }

    private sealed record Payload;
    private sealed record Evidence(StepEvidenceIdentity Identity) : IStepEvidence;

    // 只測介面交接，不模擬 SQL 完成、實際 Hash 演算法或來源核准。
    private sealed class TestAdapter(StepOutcome<Payload, Evidence> outcome)
        : IProcessingStepAdapter<Sha256CalculationInput, Payload, Evidence>
    {
        public ProcessingContext<Sha256CalculationInput>? ReceivedContext { get; private set; }
        public CancellationToken ReceivedToken { get; private set; }

        public Task<StepOutcome<Payload, Evidence>> ExecuteAsync(
            ProcessingContext<Sha256CalculationInput> context, CancellationToken cancellationToken)
        {
            ReceivedContext = context;
            ReceivedToken = cancellationToken;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(outcome);
        }
    }
}
