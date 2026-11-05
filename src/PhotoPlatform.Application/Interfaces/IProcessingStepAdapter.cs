using PhotoPlatform.Application.Processing;

namespace PhotoPlatform.Application.Interfaces;

// 只回傳單次觀察；Retry、Fallback、History 與交易由協調層負責。
// 實作須向下傳遞取消，不把取消當成 Failed 或副作用已停止的證明。
public interface IProcessingStepAdapter<TInput, TPayload, TEvidence>
    where TInput : class, IProcessingStepInput
    where TPayload : notnull
    where TEvidence : class, IStepEvidence
{
    Task<StepOutcome<TPayload, TEvidence>> ExecuteAsync(
        ProcessingContext<TInput> context,
        CancellationToken cancellationToken);
}
