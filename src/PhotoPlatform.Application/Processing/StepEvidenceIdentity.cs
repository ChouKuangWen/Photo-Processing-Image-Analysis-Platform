namespace PhotoPlatform.Application.Processing;

// 只綁定目前步驟的業務輸入；不是執行權，也不能證明整個候選集合的來源完整。
public sealed class StepEvidenceIdentity
{
    public StepEvidenceIdentity(long imageId, string step, string contentVersion,
        string workflowVersion, string ruleVersion, string inputFingerprint)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(imageId);
        ValidateText(step, nameof(step));
        ValidateText(workflowVersion, nameof(workflowVersion));
        ValidateText(ruleVersion, nameof(ruleVersion));
        ValidateHash(contentVersion, nameof(contentVersion));
        ValidateHash(inputFingerprint, nameof(inputFingerprint));
        ImageId = imageId;
        Step = step;
        ContentVersion = contentVersion;
        WorkflowVersion = workflowVersion;
        RuleVersion = ruleVersion;
        InputFingerprint = inputFingerprint;
    }

    public long ImageId { get; }
    public string Step { get; }
    public string ContentVersion { get; }
    public string WorkflowVersion { get; }
    public string RuleVersion { get; }
    public string InputFingerprint { get; }

    private static void ValidateText(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > 50)
            throw new ArgumentOutOfRangeException(parameterName);
    }

    private static void ValidateHash(string value, string parameterName)
    {
        if (value is null || value.Length != 64 ||
            value.Any(character => character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new ArgumentException("A lowercase SHA-256 value is required.", parameterName);
    }
}
