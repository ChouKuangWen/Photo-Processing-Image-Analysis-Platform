using System.Data.Common;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using PhotoPlatform.Application.Interfaces;
using PhotoPlatform.Application.Processing;
using PhotoPlatform.Infrastructure.Persistence;

namespace PhotoPlatform.Infrastructure.Processing;

// Read-only admission. A deployment snapshot and successful parsing never constitute execution rights.
public sealed class RuleManifestResolver : IRuleManifestResolver
{
    private readonly PhotoPlatformDbContext db;
    private readonly RuleManifestDeployment deployment;
    private readonly IReadOnlyList<IManifestStepImplementation> implementations;

    public RuleManifestResolver(PhotoPlatformDbContext db, RuleManifestDeployment deployment)
        : this(db, deployment, []) { }

    internal RuleManifestResolver(PhotoPlatformDbContext db, RuleManifestDeployment deployment,
        IEnumerable<IManifestStepImplementation> implementations)
    {
        this.db = db;
        this.deployment = deployment;
        this.implementations = Array.AsReadOnly(implementations.ToArray());
    }

    public async Task<ResolvedRuleManifest> ResolveAsync(long jobId, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(jobId);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var job = await db.ProcessingJobs.AsNoTracking().SingleOrDefaultAsync(job => job.Id == jobId, cancellationToken)
                ?? throw new FrozenInputUnavailableException(jobId, FrozenInputFailureReason.Missing);
            var binding = await db.ProcessingRuleManifestBindings.AsNoTracking()
                .SingleOrDefaultAsync(binding => binding.JobId == jobId, cancellationToken);
            if (binding is null)
            {
                // Pending + no binding cannot distinguish a new Job from legacy data. No trusted
                // deployment activation evidence has been delivered; do not consume the selection.
                var selection = deployment.Selection(job.Workflow);
                throw new FrozenInputUnavailableException(jobId, selection is null
                    ? FrozenInputFailureReason.Missing : FrozenInputFailureReason.VersionUnavailable);
            }
            var bytes = deployment.Root(binding.ManifestId)
                ?? throw new FrozenInputUnavailableException(jobId, FrozenInputFailureReason.Missing);
            var manifest = RuleManifestParser.Parse(jobId, bytes, binding.ManifestId, binding.ManifestChecksum, cancellationToken);
            if (manifest.Workflow != job.Workflow.ToString() || manifest.WorkflowVersion != binding.WorkflowVersion)
                throw new FrozenInputUnavailableException(jobId, FrozenInputFailureReason.Mismatch);

            var resources = new Dictionary<ResourceReference, ReadOnlyMemory<byte>>();
            foreach (var step in manifest.Steps)
            {
                foreach (var reference in step.Resources)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var resource = deployment.Resource(reference)
                        ?? throw new FrozenInputUnavailableException(jobId, FrozenInputFailureReason.Missing);
                    if (Convert.ToHexStringLower(SHA256.HashData(resource.Span)) != reference.Checksum)
                        throw new FrozenInputUnavailableException(jobId, FrozenInputFailureReason.Mismatch);
                    resources[reference] = resource;
                }
            }
            var verifiedResources = new System.Collections.ObjectModel.ReadOnlyDictionary<ResourceReference, ReadOnlyMemory<byte>>(resources);
            foreach (var step in manifest.Steps)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var matches = implementations.Where(implementation => implementation.Workflow == manifest.Workflow
                    && implementation.WorkflowVersion == manifest.WorkflowVersion && implementation.Step == step.Step
                    && implementation.RuleVersion == step.RuleVersion).ToArray();
                if (matches.Length != 1)
                    throw new FrozenInputUnavailableException(jobId, FrozenInputFailureReason.VersionUnavailable);
                var implementation = matches[0];
                var required = implementation.RequiredResources;
                if (required.Count == 0 || required.Distinct().Count() != required.Count
                    || required.Count != step.Resources.Count || !required.ToHashSet().SetEquals(step.Resources))
                    throw new FrozenInputUnavailableException(jobId, FrozenInputFailureReason.VersionUnavailable);
                implementation.ValidateCompatibility(jobId, manifest, step, verifiedResources, cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return manifest; // Parsed compatibility is not an ExecutionAdmission or a SQL execution grant.
        }
        catch (DbException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new FrozenInputUnavailableException(jobId, FrozenInputFailureReason.SourceUnavailable);
        }
    }
}
