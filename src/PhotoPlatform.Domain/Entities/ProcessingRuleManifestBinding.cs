namespace PhotoPlatform.Domain.Entities;

// 保存已選定的引用；不證明發布核准、跨 Job checksum 一致或執行權。
public sealed class ProcessingRuleManifestBinding
{
    public ProcessingRuleManifestBinding(long jobId, string workflowVersion, string manifestId, string manifestChecksum)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(jobId);
        ProcessingBindingValueGuard.Text(workflowVersion, 50, nameof(workflowVersion));
        ProcessingBindingValueGuard.Text(manifestId, 100, nameof(manifestId));
        ProcessingBindingValueGuard.Hash(manifestChecksum, nameof(manifestChecksum));
        JobId = jobId;
        WorkflowVersion = workflowVersion;
        ManifestId = manifestId;
        ManifestChecksum = manifestChecksum;
    }

    public long JobId { get; private set; }
    public string WorkflowVersion { get; private set; }
    public string ManifestId { get; private set; }
    public string ManifestChecksum { get; private set; }
}
