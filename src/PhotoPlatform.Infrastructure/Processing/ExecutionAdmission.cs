using PhotoPlatform.Application.Processing;
using PhotoPlatform.Domain.Entities;
using PhotoPlatform.Domain.Enums;

namespace PhotoPlatform.Infrastructure.Processing;

// Infrastructure-only handoff from admission. No API/Worker can construct this capability.
// The production issuer remains closed until publications/first-admission evidence are delivered.
internal sealed class ExecutionAdmission
{
    internal ExecutionAdmission(long jobId, long imageId, Guid batchId, string storedPath,
        WorkflowType workflow, ResolvedRuleManifest manifest, ProcessingOriginalInput original,
        IEnumerable<ProcessingOriginalStepInput> originalSteps, StepEvidenceIdentity input,
        bool permitsFirstClaim, IEnumerable<CompletedStepSource>? sources = null)
    {
        JobId = jobId;
        ImageId = imageId;
        BatchId = batchId;
        StoredPath = storedPath;
        Workflow = workflow;
        Manifest = manifest;
        Original = original;
        OriginalSteps = Array.AsReadOnly(originalSteps.ToArray());
        Input = input;
        PermitsFirstClaim = permitsFirstClaim;
        Sources = Array.AsReadOnly((sources ?? []).ToArray());
    }
    internal long JobId { get; }
    internal long ImageId { get; }
    internal Guid BatchId { get; }
    internal string StoredPath { get; }
    internal WorkflowType Workflow { get; }
    internal ResolvedRuleManifest Manifest { get; }
    internal ProcessingOriginalInput Original { get; }
    internal IReadOnlyList<ProcessingOriginalStepInput> OriginalSteps { get; }
    internal StepEvidenceIdentity Input { get; }
    internal bool PermitsFirstClaim { get; }
    internal IReadOnlyList<CompletedStepSource> Sources { get; }
}

internal sealed record CompletedStepSource(long CheckpointId, string Step, string RuleVersion,
    string InputFingerprint, string? ResultReference, ProcessingResultStatus ResultStatus);

internal sealed record StartedProcessingInvocation(long JobId, long RunId, long CheckpointId,
    long InvocationId, int AttemptNo, int RetryOrdinal, int ResumeNo);
