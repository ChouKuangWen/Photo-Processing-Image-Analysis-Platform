using PhotoPlatform.Domain.Enums;

namespace PhotoPlatform.Application.Processing;

// 一次執行的觀察，不是 T3 提交結果；不在此決定 Retry、Fallback 或關閉持久狀態。
public abstract class StepOutcome<TPayload, TEvidence>
    where TPayload : notnull
    where TEvidence : class, IStepEvidence
{
    private protected StepOutcome() { }

    public static Returned Success(CompletionProposal<TPayload, TEvidence> completion) =>
        Returned.Create(ProcessingLogStatus.Success, completion);

    public static Returned Warning(CompletionProposal<TPayload, TEvidence> completion) =>
        Returned.Create(ProcessingLogStatus.Warning, completion);

    public static Returned Skipped(CompletionProposal<TPayload, TEvidence> completion) =>
        Returned.Create(ProcessingLogStatus.Skipped, completion);

    public sealed class Returned : StepOutcome<TPayload, TEvidence>
    {
        private Returned(ProcessingLogStatus invocationStatus,
            CompletionProposal<TPayload, TEvidence> completion)
        {
            ArgumentNullException.ThrowIfNull(completion);
            var expected = invocationStatus switch
            {
                ProcessingLogStatus.Success => ProcessingResultStatus.Success,
                ProcessingLogStatus.Warning => ProcessingResultStatus.Warning,
                ProcessingLogStatus.Skipped => ProcessingResultStatus.Skipped,
                _ => throw new ArgumentOutOfRangeException(nameof(invocationStatus))
            };
            if (completion.ResultStatus != expected)
                throw new ArgumentException("Invocation and completion statuses must match.", nameof(completion));
            InvocationStatus = invocationStatus;
            Completion = completion;
        }

        // 非公開協作解決外層無法呼叫巢狀 private constructor 的限制；所有路徑仍通過同一建構驗證。
        internal static Returned Create(ProcessingLogStatus invocationStatus,
            CompletionProposal<TPayload, TEvidence> completion) => new(invocationStatus, completion);

        public ProcessingLogStatus InvocationStatus { get; }
        public CompletionProposal<TPayload, TEvidence> Completion { get; }
    }

    public sealed class Failed : StepOutcome<TPayload, TEvidence>
    {
        public Failed(StepFailure failure, TEvidence? evidence = null)
        {
            ArgumentNullException.ThrowIfNull(failure);
            if (evidence is not null)
                ArgumentNullException.ThrowIfNull(evidence.Identity);
            Failure = failure;
            Evidence = evidence;
        }

        // 讀檔或解析失敗可能還沒有模組證據；協調層仍須核對原 Invocation 與 Frozen Input。
        public StepFailure Failure { get; }
        public TEvidence? Evidence { get; }
    }

    public sealed class NeedsReconciliation : StepOutcome<TPayload, TEvidence>
    {
        public NeedsReconciliation(TEvidence evidence)
        {
            ArgumentNullException.ThrowIfNull(evidence);
            ArgumentNullException.ThrowIfNull(evidence.Identity);
            Evidence = evidence;
        }

        // 未確認副作用不代表 Failed、Interrupted 或可重試；此證據也不能取代持久操作識別。
        public TEvidence Evidence { get; }
    }
}
