namespace PhotoPlatform.Application.Processing;

// 候選只保存已選定的輸入；是否已提交、版本是否相容仍須由持久來源核對。
public sealed class ExactDuplicateCandidate
{
    private static readonly CanonicalInputSchema Schema = new(
        new("contentVersion", CanonicalValueKind.String),
        new("fileSize", CanonicalValueKind.Integer),
        new("hashResultId", CanonicalValueKind.Integer),
        new("hashRuleVersion", CanonicalValueKind.String),
        new("imageId", CanonicalValueKind.Integer),
        new("sha256", CanonicalValueKind.String));
    private readonly CanonicalInput canonicalInput;

    public ExactDuplicateCandidate(long imageId, string contentVersion, long hashResultId,
        long fileSize, string sha256, string hashRuleVersion)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(imageId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(hashResultId);
        ArgumentOutOfRangeException.ThrowIfNegative(fileSize);
        ValidateHash(contentVersion, nameof(contentVersion));
        ValidateHash(sha256, nameof(sha256));
        ValidateVersion(hashRuleVersion, nameof(hashRuleVersion));
        ImageId = imageId;
        ContentVersion = contentVersion;
        HashResultId = hashResultId;
        FileSize = fileSize;
        Sha256 = sha256;
        HashRuleVersion = hashRuleVersion;
        canonicalInput = Schema.Serialize(
            new("contentVersion", CanonicalValue.String(contentVersion)),
            new("fileSize", CanonicalValue.Integer(fileSize)),
            new("hashResultId", CanonicalValue.Integer(hashResultId)),
            new("hashRuleVersion", CanonicalValue.String(hashRuleVersion)),
            new("imageId", CanonicalValue.Integer(imageId)),
            new("sha256", CanonicalValue.String(sha256)));
        Fingerprint = InputFingerprint.Compute(canonicalInput);
    }

    public long ImageId { get; }
    public string ContentVersion { get; }
    public long HashResultId { get; }
    public long FileSize { get; }
    public string Sha256 { get; }
    public string HashRuleVersion { get; }
    public string Fingerprint { get; }
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
