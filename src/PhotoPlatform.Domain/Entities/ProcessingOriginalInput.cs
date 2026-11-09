namespace PhotoPlatform.Domain.Entities;

public sealed class ProcessingOriginalInput
{
    public ProcessingOriginalInput(long jobId, string contentVersion, long contentLength, string workflowVersion)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(jobId);
        ArgumentOutOfRangeException.ThrowIfNegative(contentLength);
        ProcessingBindingValueGuard.Hash(contentVersion, nameof(contentVersion));
        ProcessingBindingValueGuard.Text(workflowVersion, 50, nameof(workflowVersion));
        JobId = jobId;
        ContentVersion = contentVersion;
        ContentLength = contentLength;
        WorkflowVersion = workflowVersion;
    }

    public long JobId { get; private set; }
    public string ContentVersion { get; private set; }
    public long ContentLength { get; private set; }
    public string WorkflowVersion { get; private set; }
}
