namespace PhotoPlatform.Application.Processing;

// 只驗證核准輸入的結構與 Canonical；不證明 EXIF 已提交、版本可執行或取得執行權。
public sealed class QualityAnalysisInput : IProcessingStepInput
{
    private static readonly CanonicalInputSchema Schema = new(
        new("contentLength", CanonicalValueKind.Integer),
        new("contentVersion", CanonicalValueKind.String),
        new("imageId", CanonicalValueKind.Integer),
        new("metadataInputFingerprint", CanonicalValueKind.String),
        new("metadataResultId", CanonicalValueKind.Integer),
        new("metadataRuleVersion", CanonicalValueKind.String),
        new("ruleVersion", CanonicalValueKind.String),
        new("step", CanonicalValueKind.String),
        new("workflowVersion", CanonicalValueKind.String));
    private readonly CanonicalInput canonicalInput;

    public QualityAnalysisInput(long imageId, string contentVersion, long contentLength, string workflowVersion,
        string ruleVersion, long metadataResultId, string metadataInputFingerprint,
        string metadataRuleVersion)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(metadataResultId);
        ArgumentOutOfRangeException.ThrowIfNegative(contentLength);
        ArgumentException.ThrowIfNullOrWhiteSpace(metadataRuleVersion);
        if (metadataRuleVersion.Length > 50)
            throw new ArgumentOutOfRangeException(nameof(metadataRuleVersion));
        if (metadataInputFingerprint is null || metadataInputFingerprint.Length != 64 ||
            metadataInputFingerprint.Any(character => character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new ArgumentException("A lowercase SHA-256 value is required.", nameof(metadataInputFingerprint));

        canonicalInput = Schema.Serialize(
            new("contentLength", CanonicalValue.Integer(contentLength)),
            new("contentVersion", CanonicalValue.String(contentVersion)),
            new("imageId", CanonicalValue.Integer(imageId)),
            new("metadataInputFingerprint", CanonicalValue.String(metadataInputFingerprint)),
            new("metadataResultId", CanonicalValue.Integer(metadataResultId)),
            new("metadataRuleVersion", CanonicalValue.String(metadataRuleVersion)),
            new("ruleVersion", CanonicalValue.String(ruleVersion)),
            new("step", CanonicalValue.String("QUALITY_ANALYSIS")),
            new("workflowVersion", CanonicalValue.String(workflowVersion)));
        Identity = new StepEvidenceIdentity(imageId, "QUALITY_ANALYSIS", contentVersion,
            workflowVersion, ruleVersion, InputFingerprint.Compute(canonicalInput));
        MetadataResultId = metadataResultId;
        MetadataInputFingerprint = metadataInputFingerprint;
        MetadataRuleVersion = metadataRuleVersion;
        ContentLength = contentLength;
    }

    public StepEvidenceIdentity Identity { get; }
    public long MetadataResultId { get; }
    public string MetadataInputFingerprint { get; }
    public string MetadataRuleVersion { get; }
    public long ContentLength { get; }

    public CanonicalInput ToCanonicalInput() => canonicalInput;
}
