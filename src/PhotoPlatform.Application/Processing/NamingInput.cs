namespace PhotoPlatform.Application.Processing;

// 命名結果尚未產生；來源解析與合法 fallback 必須由上游提供，不在這裡猜測。
public sealed class NamingInput : IProcessingStepInput
{
    private static readonly CanonicalInputSchema Schema = new(
        new("cameraModel", CanonicalValueKind.String),
        new("collisionPolicy", CanonicalValueKind.String),
        new("contentVersion", CanonicalValueKind.String),
        new("geoInputFingerprint", CanonicalValueKind.String),
        new("geoResultId", CanonicalValueKind.Integer),
        new("geoRuleVersion", CanonicalValueKind.String),
        new("imageId", CanonicalValueKind.Integer),
        new("itemId", CanonicalValueKind.Integer),
        new("locationName", CanonicalValueKind.String),
        new("metadataInputFingerprint", CanonicalValueKind.String),
        new("metadataResultId", CanonicalValueKind.Integer),
        new("metadataRuleVersion", CanonicalValueKind.String),
        new("ordinal", CanonicalValueKind.Integer),
        new("originalExtension", CanonicalValueKind.String),
        new("originalFileName", CanonicalValueKind.String),
        new("requestId", CanonicalValueKind.String),
        new("requestInputFingerprint", CanonicalValueKind.String),
        new("ruleVersion", CanonicalValueKind.String),
        new("sequence", CanonicalValueKind.Integer),
        new("step", CanonicalValueKind.String),
        new("takenAt", CanonicalValueKind.UtcInstant),
        new("takenAtOffsetMinutes", CanonicalValueKind.Integer),
        new("template", CanonicalValueKind.String),
        new("timeSource", CanonicalValueKind.String),
        new("workflowVersion", CanonicalValueKind.String));
    private readonly CanonicalInput canonicalInput;

    public NamingInput(long imageId, string contentVersion, string workflowVersion, string ruleVersion,
        Guid requestId, string requestInputFingerprint, long itemId, int ordinal, long sequence,
        string originalFileName, string originalExtension, string template, string collisionPolicy,
        long metadataResultId, string metadataInputFingerprint, string metadataRuleVersion,
        long geoResultId, string geoInputFingerprint, string geoRuleVersion, string cameraModel,
        string locationName, DateTimeOffset takenAt, int takenAtOffsetMinutes, string timeSource)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(imageId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(itemId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(metadataResultId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(geoResultId);
        ArgumentOutOfRangeException.ThrowIfNegative(ordinal);
        ArgumentOutOfRangeException.ThrowIfNegative(sequence);
        if (requestId == Guid.Empty)
            throw new ArgumentException("A request identifier is required.", nameof(requestId));
        ValidateHash(contentVersion, nameof(contentVersion));
        ValidateHash(requestInputFingerprint, nameof(requestInputFingerprint));
        ValidateHash(metadataInputFingerprint, nameof(metadataInputFingerprint));
        ValidateHash(geoInputFingerprint, nameof(geoInputFingerprint));
        ValidateVersion(workflowVersion, nameof(workflowVersion));
        ValidateVersion(ruleVersion, nameof(ruleVersion));
        ValidateVersion(metadataRuleVersion, nameof(metadataRuleVersion));
        ValidateVersion(geoRuleVersion, nameof(geoRuleVersion));
        ValidateText(originalFileName, nameof(originalFileName), 255);
        ValidateText(originalExtension, nameof(originalExtension), 20);
        ValidateText(template, nameof(template), 1000);
        ValidateText(cameraModel, nameof(cameraModel), int.MaxValue);
        ValidateText(locationName, nameof(locationName), 150);
        ValidateText(timeSource, nameof(timeSource), 30);
        if (collisionPolicy is not ("NumericSuffix" or "Sequence" or "Skip" or "Fail"))
            throw new ArgumentException("An approved collision policy is required.", nameof(collisionPolicy));
        if (takenAtOffsetMinutes is < -840 or > 840)
            throw new ArgumentOutOfRangeException(nameof(takenAtOffsetMinutes));
        // UTC 用於指紋；原來源 offset 另存，不能拿主機時區決定命名日期。
        _ = takenAt.ToOffset(TimeSpan.FromMinutes(takenAtOffsetMinutes));
        RequestId = requestId;
        RequestInputFingerprint = requestInputFingerprint;
        ItemId = itemId;
        Ordinal = ordinal;
        Sequence = sequence;
        OriginalFileName = originalFileName;
        OriginalExtension = originalExtension;
        Template = template;
        CollisionPolicy = collisionPolicy;
        MetadataResultId = metadataResultId;
        MetadataInputFingerprint = metadataInputFingerprint;
        MetadataRuleVersion = metadataRuleVersion;
        GeoResultId = geoResultId;
        GeoInputFingerprint = geoInputFingerprint;
        GeoRuleVersion = geoRuleVersion;
        CameraModel = cameraModel;
        LocationName = locationName;
        TakenAt = takenAt;
        TakenAtOffsetMinutes = takenAtOffsetMinutes;
        TimeSource = timeSource;
        canonicalInput = Schema.Serialize(
            new("cameraModel", CanonicalValue.String(cameraModel)),
            new("collisionPolicy", CanonicalValue.String(collisionPolicy)),
            new("contentVersion", CanonicalValue.String(contentVersion)),
            new("geoInputFingerprint", CanonicalValue.String(geoInputFingerprint)),
            new("geoResultId", CanonicalValue.Integer(geoResultId)),
            new("geoRuleVersion", CanonicalValue.String(geoRuleVersion)),
            new("imageId", CanonicalValue.Integer(imageId)),
            new("itemId", CanonicalValue.Integer(itemId)),
            new("locationName", CanonicalValue.String(locationName)),
            new("metadataInputFingerprint", CanonicalValue.String(metadataInputFingerprint)),
            new("metadataResultId", CanonicalValue.Integer(metadataResultId)),
            new("metadataRuleVersion", CanonicalValue.String(metadataRuleVersion)),
            new("ordinal", CanonicalValue.Integer(ordinal)),
            new("originalExtension", CanonicalValue.String(originalExtension)),
            new("originalFileName", CanonicalValue.String(originalFileName)),
            new("requestId", CanonicalValue.String(requestId.ToString("D"))),
            new("requestInputFingerprint", CanonicalValue.String(requestInputFingerprint)),
            new("ruleVersion", CanonicalValue.String(ruleVersion)),
            new("sequence", CanonicalValue.Integer(sequence)),
            new("step", CanonicalValue.String("NAMING")),
            new("takenAt", CanonicalValue.UtcInstant(takenAt)),
            new("takenAtOffsetMinutes", CanonicalValue.Integer(takenAtOffsetMinutes)),
            new("template", CanonicalValue.String(template)),
            new("timeSource", CanonicalValue.String(timeSource)),
            new("workflowVersion", CanonicalValue.String(workflowVersion)));
        Identity = new StepEvidenceIdentity(imageId, "NAMING", contentVersion,
            workflowVersion, ruleVersion, InputFingerprint.Compute(canonicalInput));
    }

    public StepEvidenceIdentity Identity { get; }
    public Guid RequestId { get; }
    public string RequestInputFingerprint { get; }
    public long ItemId { get; }
    public int Ordinal { get; }
    public long Sequence { get; }
    public string OriginalFileName { get; }
    public string OriginalExtension { get; }
    public string Template { get; }
    public string CollisionPolicy { get; }
    public long MetadataResultId { get; }
    public string MetadataInputFingerprint { get; }
    public string MetadataRuleVersion { get; }
    public long GeoResultId { get; }
    public string GeoInputFingerprint { get; }
    public string GeoRuleVersion { get; }
    public string CameraModel { get; }
    public string LocationName { get; }
    public DateTimeOffset TakenAt { get; }
    public int TakenAtOffsetMinutes { get; }
    public string TimeSource { get; }
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
