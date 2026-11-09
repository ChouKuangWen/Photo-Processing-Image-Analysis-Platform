using PhotoPlatform.Application.Processing;

namespace PhotoPlatform.Application.Interfaces;

public interface IRuleManifestResolver
{
    Task<ResolvedRuleManifest> ResolveAsync(long jobId, CancellationToken cancellationToken);
}
