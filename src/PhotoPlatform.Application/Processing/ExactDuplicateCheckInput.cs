namespace PhotoPlatform.Application.Processing;

// 此集合是原快照的資料，不會查詢目前群組，也不代表取得執行權。
public sealed class ExactDuplicateCheckInput : IProcessingStepInput
{
    private static readonly CanonicalInputSchema Schema = new(
        new("candidateFingerprints", CanonicalValueKind.Array, elementKind: CanonicalValueKind.String),
        new("candidateSelectionVersion", CanonicalValueKind.String),
        new("contentLength", CanonicalValueKind.Integer),
        new("contentVersion", CanonicalValueKind.String),
        new("hashResultId", CanonicalValueKind.Integer),
        new("hashRuleVersion", CanonicalValueKind.String),
        new("imageId", CanonicalValueKind.Integer),
        new("jobId", CanonicalValueKind.Integer),
        new("ruleVersion", CanonicalValueKind.String),
        new("scopeVersion", CanonicalValueKind.String),
        new("sha256", CanonicalValueKind.String),
        new("snapshotId", CanonicalValueKind.String),
        new("step", CanonicalValueKind.String),
        new("workflowVersion", CanonicalValueKind.String));
    private readonly CanonicalInput canonicalInput;

    public ExactDuplicateCheckInput(long jobId, long imageId, string contentVersion, long contentLength,
        string workflowVersion, string ruleVersion, long hashResultId, string hashRuleVersion,
        string sha256, Guid snapshotId, string scopeVersion, string candidateSelectionVersion,
        IEnumerable<ExactDuplicateCandidate> candidates)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(jobId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(imageId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(hashResultId);
        ArgumentOutOfRangeException.ThrowIfNegative(contentLength);
        if (snapshotId == Guid.Empty)
            throw new ArgumentException("A snapshot identifier is required.", nameof(snapshotId));
        ValidateHash(contentVersion, nameof(contentVersion));
        ValidateHash(sha256, nameof(sha256));
        ValidateVersion(workflowVersion, nameof(workflowVersion));
        ValidateVersion(ruleVersion, nameof(ruleVersion));
        ValidateVersion(hashRuleVersion, nameof(hashRuleVersion));
        ValidateVersion(scopeVersion, nameof(scopeVersion));
        ValidateVersion(candidateSelectionVersion, nameof(candidateSelectionVersion));
        ArgumentNullException.ThrowIfNull(candidates);
        var copied = candidates.ToArray();
        if (copied.Any(candidate => candidate is null))
            throw new ArgumentException("Candidates cannot contain null.", nameof(candidates));
        if (copied.Any(candidate => candidate.ImageId == imageId ||
                candidate.FileSize != contentLength || candidate.Sha256 != sha256) ||
            copied.Select(candidate => candidate.ImageId).Distinct().Count() != copied.Length)
            throw new ArgumentException("Candidates must be distinct matching images.", nameof(candidates));
        // 複製、排序再封裝，避免呼叫端改動清單後改變同一份輸入。
        Candidates = Array.AsReadOnly(copied.OrderBy(candidate => candidate.ImageId).ToArray());
        JobId = jobId;
        ContentLength = contentLength;
        HashResultId = hashResultId;
        HashRuleVersion = hashRuleVersion;
        Sha256 = sha256;
        SnapshotId = snapshotId;
        ScopeVersion = scopeVersion;
        CandidateSelectionVersion = candidateSelectionVersion;
        canonicalInput = Schema.Serialize(
            new("candidateFingerprints", CanonicalValue.Array(Candidates.Select(candidate => CanonicalValue.String(candidate.Fingerprint)).ToArray())),
            new("candidateSelectionVersion", CanonicalValue.String(candidateSelectionVersion)),
            new("contentLength", CanonicalValue.Integer(contentLength)),
            new("contentVersion", CanonicalValue.String(contentVersion)),
            new("hashResultId", CanonicalValue.Integer(hashResultId)),
            new("hashRuleVersion", CanonicalValue.String(hashRuleVersion)),
            new("imageId", CanonicalValue.Integer(imageId)),
            new("jobId", CanonicalValue.Integer(jobId)),
            new("ruleVersion", CanonicalValue.String(ruleVersion)),
            new("scopeVersion", CanonicalValue.String(scopeVersion)),
            new("sha256", CanonicalValue.String(sha256)),
            new("snapshotId", CanonicalValue.String(snapshotId.ToString("D"))),
            new("step", CanonicalValue.String("EXACT_DUPLICATE_CHECK")),
            new("workflowVersion", CanonicalValue.String(workflowVersion)));
        Identity = new StepEvidenceIdentity(imageId, "EXACT_DUPLICATE_CHECK", contentVersion,
            workflowVersion, ruleVersion, InputFingerprint.Compute(canonicalInput));
    }

    public StepEvidenceIdentity Identity { get; }
    public long JobId { get; }
    public long ContentLength { get; }
    public long HashResultId { get; }
    public string HashRuleVersion { get; }
    public string Sha256 { get; }
    public Guid SnapshotId { get; }
    public string ScopeVersion { get; }
    public string CandidateSelectionVersion { get; }
    public IReadOnlyList<ExactDuplicateCandidate> Candidates { get; }
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
