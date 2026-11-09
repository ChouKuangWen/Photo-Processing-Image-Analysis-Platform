using PhotoPlatform.Application.Processing;
using PhotoPlatform.Domain.Enums;

namespace PhotoPlatform.Infrastructure.Processing;

// A deployment-owned snapshot, not a mutable/latest registry. Copies retain historical raw bytes.
public sealed class RuleManifestDeployment
{
    private readonly IReadOnlyDictionary<WorkflowType, RootManifestReference> selections;
    private readonly IReadOnlyDictionary<string, byte[]> roots;
    private readonly IReadOnlyDictionary<ResourceReference, byte[]> resources;

    public RuleManifestDeployment(IReadOnlyDictionary<WorkflowType, RootManifestReference> selections,
        IReadOnlyDictionary<string, byte[]> roots, IReadOnlyDictionary<ResourceReference, byte[]> resources)
    {
        this.selections = new Dictionary<WorkflowType, RootManifestReference>(selections);
        this.roots = roots.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal);
        this.resources = resources.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
    }

    internal RootManifestReference? Selection(WorkflowType workflow) => selections.GetValueOrDefault(workflow);
    internal ReadOnlyMemory<byte>? Root(string id) => roots.TryGetValue(id, out var value)
        ? new ReadOnlyMemory<byte>(value) : (ReadOnlyMemory<byte>?)null;
    internal ReadOnlyMemory<byte>? Resource(ResourceReference reference) => resources.TryGetValue(reference, out var value)
        ? new ReadOnlyMemory<byte>(value) : (ReadOnlyMemory<byte>?)null;
}

public sealed record RootManifestReference(string ManifestId, string Checksum);
