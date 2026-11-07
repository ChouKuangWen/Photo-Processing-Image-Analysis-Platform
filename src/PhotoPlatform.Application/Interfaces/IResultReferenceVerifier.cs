using PhotoPlatform.Application.Processing;

namespace PhotoPlatform.Application.Interfaces;

// 實作須核對受控 table/id/version 引用（非空且無空白的可列印 ASCII、至多500字元），不能只看格式或 row 存在。
// 必須核對原輸入的內容版本、規則、指紋、Completed、原 Owner Checkpoint／History 與必要來源。
// IStepEvidence 只有共通身分；實作不能僅憑型別就宣稱上述持久核對已完成。
// 此介面唯讀，不換 Owner、不寫 History、不做 Claim／T3／Recovery；成功仍不能取代後續交易核對。
// 取消保持取消語意；來源驗證失敗使用獨立受控例外，不攜帶敏感引用。
public interface IResultReferenceVerifier<TInput, TEvidence>
    where TInput : class, IProcessingStepInput
    where TEvidence : class, IStepEvidence
{
    Task<TEvidence> VerifyAsync(
        ProcessingContext<TInput> context,
        string resultReference,
        CancellationToken cancellationToken);
}
