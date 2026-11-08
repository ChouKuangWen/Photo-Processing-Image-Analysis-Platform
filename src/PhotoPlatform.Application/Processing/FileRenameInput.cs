namespace PhotoPlatform.Application.Processing;

// Factory 只區分輸入形狀；不建立檔案，也不核准原 NAMING 的 Skip 證據。
public sealed class FileRenameInput : IProcessingStepInput
{
    private static readonly CanonicalInputSchema Schema = new(
        new("contentVersion", CanonicalValueKind.String),
        new("imageId", CanonicalValueKind.Integer),
        new("itemId", CanonicalValueKind.Integer),
        new("mode", CanonicalValueKind.String),
        new("namespaceId", CanonicalValueKind.String, nullable: true),
        new("namingInputFingerprint", CanonicalValueKind.String),
        new("operationId", CanonicalValueKind.String, nullable: true),
        new("providerId", CanonicalValueKind.String, nullable: true),
        new("ruleVersion", CanonicalValueKind.String),
        new("step", CanonicalValueKind.String),
        new("targetKey", CanonicalValueKind.String, nullable: true),
        new("targetName", CanonicalValueKind.String, nullable: true),
        new("targetPolicyVersion", CanonicalValueKind.String, nullable: true),
        new("workflowVersion", CanonicalValueKind.String));
    private readonly CanonicalInput canonicalInput;

    private FileRenameInput(long imageId, string contentVersion, string workflowVersion, string ruleVersion,
        long itemId, string namingInputFingerprint, string mode, Guid? operationId, string? providerId,
        string? namespaceId, string? targetPolicyVersion, string? targetKey, string? targetName)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(imageId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(itemId);
        ValidateHash(contentVersion, nameof(contentVersion));
        ValidateHash(namingInputFingerprint, nameof(namingInputFingerprint));
        ValidateVersion(workflowVersion, nameof(workflowVersion));
        ValidateVersion(ruleVersion, nameof(ruleVersion));
        if (mode == "Create")
        {
            if (operationId is null || operationId == Guid.Empty)
                throw new ArgumentException("An operation identifier is required.", nameof(operationId));
            ValidateText(providerId!, nameof(providerId));
            ValidateText(namespaceId!, nameof(namespaceId), 100);
            ValidateVersion(targetPolicyVersion!, nameof(targetPolicyVersion));
            ValidateText(targetKey!, nameof(targetKey), 500);
            ValidateText(targetName!, nameof(targetName), 255);
        }
        ItemId = itemId;
        NamingInputFingerprint = namingInputFingerprint;
        Mode = mode;
        OperationId = operationId;
        ProviderId = providerId;
        NamespaceId = namespaceId;
        TargetPolicyVersion = targetPolicyVersion;
        TargetKey = targetKey;
        TargetName = targetName;
        canonicalInput = Schema.Serialize(
            new("contentVersion", CanonicalValue.String(contentVersion)),
            new("imageId", CanonicalValue.Integer(imageId)),
            new("itemId", CanonicalValue.Integer(itemId)),
            new("mode", CanonicalValue.String(mode)),
            new("namespaceId", namespaceId is null ? CanonicalValue.Null : CanonicalValue.String(namespaceId)),
            new("namingInputFingerprint", CanonicalValue.String(namingInputFingerprint)),
            new("operationId", operationId is null ? CanonicalValue.Null : CanonicalValue.String(operationId.Value.ToString("D"))),
            new("providerId", providerId is null ? CanonicalValue.Null : CanonicalValue.String(providerId)),
            new("ruleVersion", CanonicalValue.String(ruleVersion)),
            new("step", CanonicalValue.String("FILE_RENAME")),
            new("targetKey", targetKey is null ? CanonicalValue.Null : CanonicalValue.String(targetKey)),
            new("targetName", targetName is null ? CanonicalValue.Null : CanonicalValue.String(targetName)),
            new("targetPolicyVersion", targetPolicyVersion is null ? CanonicalValue.Null : CanonicalValue.String(targetPolicyVersion)),
            new("workflowVersion", CanonicalValue.String(workflowVersion)));
        Identity = new StepEvidenceIdentity(imageId, "FILE_RENAME", contentVersion,
            workflowVersion, ruleVersion, InputFingerprint.Compute(canonicalInput));
    }

    public static FileRenameInput Create(long imageId, string contentVersion, string workflowVersion,
        string ruleVersion, long itemId, string namingInputFingerprint, Guid operationId,
        string providerId, string namespaceId, string targetPolicyVersion, string targetKey, string targetName) =>
        new(imageId, contentVersion, workflowVersion, ruleVersion, itemId, namingInputFingerprint,
            "Create", operationId, providerId, namespaceId, targetPolicyVersion, targetKey, targetName);

    // 前段 Skip 沒有 Operation；後發碰撞仍須沿用原 Create 輸入，不走這個 Factory。
    public static FileRenameInput Skip(long imageId, string contentVersion, string workflowVersion,
        string ruleVersion, long itemId, string namingInputFingerprint) =>
        new(imageId, contentVersion, workflowVersion, ruleVersion, itemId, namingInputFingerprint,
            "Skip", null, null, null, null, null, null);

    public StepEvidenceIdentity Identity { get; }
    public long ItemId { get; }
    public string NamingInputFingerprint { get; }
    public string Mode { get; }
    public Guid? OperationId { get; }
    public string? ProviderId { get; }
    public string? NamespaceId { get; }
    public string? TargetPolicyVersion { get; }
    public string? TargetKey { get; }
    public string? TargetName { get; }
    public CanonicalInput ToCanonicalInput() => canonicalInput;

    private static void ValidateText(string value, string name, int maximum = 50)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        if (value.Length > maximum)
            throw new ArgumentOutOfRangeException(name);
    }

    private static void ValidateVersion(string value, string name)
    {
        ValidateText(value, name);
        if (value is "latest" or "default")
            throw new ArgumentException("An explicit frozen version is required.", name);
    }

    private static void ValidateHash(string value, string name)
    {
        if (value is null || value.Length != 64 ||
            value.Any(character => character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new ArgumentException("A lowercase SHA-256 value is required.", name);
    }

}
