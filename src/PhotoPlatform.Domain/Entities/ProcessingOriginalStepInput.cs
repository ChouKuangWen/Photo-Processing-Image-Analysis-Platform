namespace PhotoPlatform.Domain.Entities;

public sealed class ProcessingOriginalStepInput
{
    public ProcessingOriginalStepInput(long jobId, string step, string ruleVersion,
        DateTimeOffset? uploadCreatedAt, string inputFingerprint)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(jobId);
        if (step is not ("EXIF_PARSING" or "SHA256_CALCULATION"))
            throw new ArgumentException("An original-input step is required.", nameof(step));
        if ((step == "EXIF_PARSING") != uploadCreatedAt.HasValue)
            throw new ArgumentException("Upload time must be present only for EXIF.", nameof(uploadCreatedAt));
        ProcessingBindingValueGuard.Text(ruleVersion, 50, nameof(ruleVersion));
        ProcessingBindingValueGuard.Hash(inputFingerprint, nameof(inputFingerprint));
        JobId = jobId;
        Step = step;
        RuleVersion = ruleVersion;
        UploadCreatedAt = uploadCreatedAt;
        InputFingerprint = inputFingerprint;
    }

    public long JobId { get; private set; }
    public string Step { get; private set; }
    public string RuleVersion { get; private set; }
    public DateTimeOffset? UploadCreatedAt { get; private set; }
    public string InputFingerprint { get; private set; }
}
