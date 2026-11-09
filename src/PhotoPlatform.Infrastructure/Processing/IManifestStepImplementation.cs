using PhotoPlatform.Application.Processing;

namespace PhotoPlatform.Infrastructure.Processing;

// Implemented by delivered module code, not publication metadata. No production implementations
// are registered yet. Implementations must validate their child schemas and cross-resource semantics
// against the actual adapter/provider capabilities; identity matching alone is insufficient.
internal interface IManifestStepImplementation
{
    string Workflow { get; }
    string WorkflowVersion { get; }
    string Step { get; }
    string RuleVersion { get; }
    IReadOnlyList<ResourceReference> RequiredResources { get; }

    void ValidateCompatibility(long jobId, ResolvedRuleManifest manifest, ManifestStep step,
        IReadOnlyDictionary<ResourceReference, ReadOnlyMemory<byte>> resources, CancellationToken cancellationToken);
}
