using PhotoPlatform.Application.Processing;

namespace PhotoPlatform.Application.Interfaces;

// 只重建已提交的原輸入，核對 Job、Step、來源與版本；不選最新版或建立 Binding。
// 不做 Assessment、Claim 或呼叫 Adapter；成功回傳也不是執行授權。
// 缺資料與來源不可用須分開回報，取消仍向外傳遞，不關閉既有 Invocation。
public interface IFrozenInputProvider<TInput> where TInput : class, IProcessingStepInput
{
    Task<ProcessingContext<TInput>> LoadFrozenAsync(long jobId, CancellationToken cancellationToken);
}
