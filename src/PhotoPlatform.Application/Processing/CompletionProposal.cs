using PhotoPlatform.Domain.Enums;

namespace PhotoPlatform.Application.Processing;

// 完成提案只保護結構；來源、集合版本及 ownership 必須在後續交易核對，不能由此宣稱完成。
public sealed class CompletionProposal<TPayload, TEvidence>
    where TPayload : notnull
    where TEvidence : class, IStepEvidence
{
    public CompletionProposal(ProcessingResultStatus resultStatus,
        ResultMaterial<TPayload> material, TEvidence evidence)
    {
        if (!Enum.IsDefined(resultStatus))
            throw new ArgumentOutOfRangeException(nameof(resultStatus));
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(evidence.Identity);
        ResultStatus = resultStatus;
        Material = material;
        Evidence = evidence;
    }

    public ProcessingResultStatus ResultStatus { get; }
    public ResultMaterial<TPayload> Material { get; }
    public TEvidence Evidence { get; }
}
