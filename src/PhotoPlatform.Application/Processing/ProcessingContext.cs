namespace PhotoPlatform.Application.Processing;

// 只關聯 Job 與具名輸入，不持有資源，也不代表 Claim、Owner 或恢復授權。
public sealed class ProcessingContext<TInput> where TInput : class, IProcessingStepInput
{
    public ProcessingContext(long jobId, TInput input)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(jobId);
        ArgumentNullException.ThrowIfNull(input);
        JobId = jobId;
        Input = input;
    }

    public long JobId { get; }
    public TInput Input { get; }
}
