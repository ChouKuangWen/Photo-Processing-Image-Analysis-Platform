namespace PhotoPlatform.Application.Processing;

// 固定原圖型輸入的核准欄位；不查 Storage 或規則 Registry，也不替代持久來源核對。
public sealed class Sha256CalculationInput : IProcessingStepInput
{
    private static readonly CanonicalInputSchema Schema = new(
        new("contentLength", CanonicalValueKind.Integer),
        new("contentVersion", CanonicalValueKind.String),
        new("imageId", CanonicalValueKind.Integer),
        new("ruleVersion", CanonicalValueKind.String),
        new("step", CanonicalValueKind.String),
        new("workflowVersion", CanonicalValueKind.String));
    private readonly CanonicalInput canonicalInput;

    public Sha256CalculationInput(long imageId, string contentVersion, long contentLength,
        string workflowVersion, string ruleVersion)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(contentLength);
        canonicalInput = Schema.Serialize(
            new("contentLength", CanonicalValue.Integer(contentLength)),
            new("contentVersion", CanonicalValue.String(contentVersion)),
            new("imageId", CanonicalValue.Integer(imageId)),
            new("ruleVersion", CanonicalValue.String(ruleVersion)),
            new("step", CanonicalValue.String("SHA256_CALCULATION")),
            new("workflowVersion", CanonicalValue.String(workflowVersion)));
        // 身分與指紋只從同一份輸入建立，不能另傳一個與內容不符的 fingerprint。
        Identity = new StepEvidenceIdentity(imageId, "SHA256_CALCULATION", contentVersion,
            workflowVersion, ruleVersion, InputFingerprint.Compute(canonicalInput));
        ContentLength = contentLength;
    }

    public StepEvidenceIdentity Identity { get; }
    public long ContentLength { get; }

    public CanonicalInput ToCanonicalInput() => canonicalInput;
}
