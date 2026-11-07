namespace PhotoPlatform.Application.Processing;

// 只驗證核准輸入的結構與 Canonical；不證明 EXIF 已提交、版本可執行或取得執行權。
public sealed class GeoLookupInput : IProcessingStepInput
{
    private static readonly CanonicalInputSchema Schema = new(
        new("contentVersion", CanonicalValueKind.String),
        new("gpsValidity", CanonicalValueKind.String),
        new("imageId", CanonicalValueKind.Integer),
        new("latitude", CanonicalValueKind.Decimal, nullable: true),
        new("longitude", CanonicalValueKind.Decimal, nullable: true),
        new("metadataInputFingerprint", CanonicalValueKind.String),
        new("metadataResultId", CanonicalValueKind.Integer),
        new("metadataRuleVersion", CanonicalValueKind.String),
        new("ruleVersion", CanonicalValueKind.String),
        new("step", CanonicalValueKind.String),
        new("workflowVersion", CanonicalValueKind.String));
    private readonly CanonicalInput canonicalInput;

    public GeoLookupInput(long imageId, string contentVersion, string workflowVersion,
        string ruleVersion, long metadataResultId, string metadataInputFingerprint,
        string metadataRuleVersion, string gpsValidity, decimal? latitude, decimal? longitude)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(metadataResultId);
        // Valid 必須成對且在範圍內；Missing／Invalid 不把原始壞值當成正規化座標。
        if (gpsValidity is not ("Valid" or "Missing" or "Invalid"))
            throw new ArgumentException("Unknown GPS validity.", nameof(gpsValidity));
        if (gpsValidity == "Valid")
        {
            if (latitude is null || latitude < -90 || latitude > 90)
                throw new ArgumentOutOfRangeException(nameof(latitude));
            if (longitude is null || longitude < -180 || longitude > 180)
                throw new ArgumentOutOfRangeException(nameof(longitude));
        }
        else if (latitude is not null || longitude is not null)
            throw new ArgumentException("Non-valid GPS requires null coordinates.", nameof(gpsValidity));
        ArgumentException.ThrowIfNullOrWhiteSpace(metadataRuleVersion);
        if (metadataRuleVersion.Length > 50)
            throw new ArgumentOutOfRangeException(nameof(metadataRuleVersion));
        if (metadataInputFingerprint is null || metadataInputFingerprint.Length != 64 ||
            metadataInputFingerprint.Any(character => character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new ArgumentException("A lowercase SHA-256 value is required.", nameof(metadataInputFingerprint));

        canonicalInput = Schema.Serialize(
            new("contentVersion", CanonicalValue.String(contentVersion)),
            new("gpsValidity", CanonicalValue.String(gpsValidity)),
            new("imageId", CanonicalValue.Integer(imageId)),
            new("latitude", latitude is { } latitudeValue ? CanonicalValue.Decimal(latitudeValue) : CanonicalValue.Null),
            new("longitude", longitude is { } longitudeValue ? CanonicalValue.Decimal(longitudeValue) : CanonicalValue.Null),
            new("metadataInputFingerprint", CanonicalValue.String(metadataInputFingerprint)),
            new("metadataResultId", CanonicalValue.Integer(metadataResultId)),
            new("metadataRuleVersion", CanonicalValue.String(metadataRuleVersion)),
            new("ruleVersion", CanonicalValue.String(ruleVersion)),
            new("step", CanonicalValue.String("GEO_LOOKUP")),
            new("workflowVersion", CanonicalValue.String(workflowVersion)));
        Identity = new StepEvidenceIdentity(imageId, "GEO_LOOKUP", contentVersion,
            workflowVersion, ruleVersion, InputFingerprint.Compute(canonicalInput));
        MetadataResultId = metadataResultId;
        MetadataInputFingerprint = metadataInputFingerprint;
        MetadataRuleVersion = metadataRuleVersion;
        GpsValidity = gpsValidity;
        Latitude = latitude;
        Longitude = longitude;
    }

    public StepEvidenceIdentity Identity { get; }
    public long MetadataResultId { get; }
    public string MetadataInputFingerprint { get; }
    public string MetadataRuleVersion { get; }
    public string GpsValidity { get; }
    public decimal? Latitude { get; }
    public decimal? Longitude { get; }

    public CanonicalInput ToCanonicalInput() => canonicalInput;
}
