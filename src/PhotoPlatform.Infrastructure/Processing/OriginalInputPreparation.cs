using PhotoPlatform.Application.Interfaces;
using PhotoPlatform.Application.Processing;
using PhotoPlatform.Domain.Entities;

namespace PhotoPlatform.Infrastructure.Processing;

// Read-only T1 candidate preparation, not a Claim or a substitute for transactional relationship checks.
internal static class OriginalInputPreparation
{
    internal static async Task<PreparedOriginalInput> PrepareAsync(long jobId, long imageId, long fileSize,
        DateTimeOffset uploadCreatedAt, ResolvedRuleManifest manifest, IImageContentReader reader,
        ProcessingOriginalInput? frozen, IReadOnlyList<ProcessingOriginalStepInput>? frozenSteps,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var content = await reader.OpenOriginalAsync(imageId, cancellationToken);
        try
        {
            if (content.Length != fileSize || (frozen is not null &&
                (frozen.JobId != jobId || frozen.ContentLength != content.Length ||
                 frozen.ContentVersion != content.ContentVersion || frozen.WorkflowVersion != manifest.WorkflowVersion)))
                throw new FrozenInputUnavailableException(jobId, FrozenInputFailureReason.Mismatch);
            var inputs = new List<ProcessingOriginalStepInput>();
            foreach (var step in manifest.Steps)
            {
                if (step.Step == "EXIF_PARSING")
                {
                    var input = new ExifParsingInput(imageId, content.ContentVersion, content.Length,
                        manifest.WorkflowVersion, step.RuleVersion, uploadCreatedAt);
                    inputs.Add(new(jobId, step.Step, step.RuleVersion, uploadCreatedAt,
                        input.Identity.InputFingerprint));
                }
                else if (step.Step == "SHA256_CALCULATION")
                {
                    var input = new Sha256CalculationInput(imageId, content.ContentVersion, content.Length,
                        manifest.WorkflowVersion, step.RuleVersion);
                    inputs.Add(new(jobId, step.Step, step.RuleVersion, null, input.Identity.InputFingerprint));
                }
            }
            if (frozen is not null)
            {
                if (frozenSteps is null || frozenSteps.Count != inputs.Count)
                    throw new FrozenInputUnavailableException(jobId, FrozenInputFailureReason.Missing);
                foreach (var input in inputs)
                {
                    var matches = frozenSteps.Where(value => value.Step == input.Step).ToArray();
                    if (matches.Length != 1 || matches[0].JobId != jobId || matches[0].RuleVersion != input.RuleVersion ||
                        matches[0].UploadCreatedAt != input.UploadCreatedAt || matches[0].InputFingerprint != input.InputFingerprint)
                        throw new FrozenInputUnavailableException(jobId, FrozenInputFailureReason.Mismatch);
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            return new PreparedOriginalInput(content, new(jobId, content.ContentVersion, content.Length,
                manifest.WorkflowVersion), inputs.AsReadOnly());
        }
        catch
        {
            await content.DisposeAsync();
            throw;
        }
    }
}

internal sealed record PreparedOriginalInput(ContentHandle Content, ProcessingOriginalInput Original,
    IReadOnlyList<ProcessingOriginalStepInput> Steps) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Content.DisposeAsync();
}
