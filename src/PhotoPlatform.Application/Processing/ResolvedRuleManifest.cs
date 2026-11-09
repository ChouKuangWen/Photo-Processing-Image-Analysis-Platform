namespace PhotoPlatform.Application.Processing;

// Parsed rule identity is not a SQL execution grant. Collections are copied at construction.
public sealed class ResolvedRuleManifest
{
    public ResolvedRuleManifest(string manifestId, string checksum, string workflow, string workflowVersion,
        IEnumerable<ManifestStep> steps)
    {
        ManifestId = manifestId;
        Checksum = checksum;
        Workflow = workflow;
        WorkflowVersion = workflowVersion;
        Steps = Array.AsReadOnly(steps.ToArray());
    }

    public int FormatVersion => 1;
    public string ManifestId { get; }
    public string Checksum { get; }
    public string Workflow { get; }
    public string WorkflowVersion { get; }
    public IReadOnlyList<ManifestStep> Steps { get; }
}

public sealed class ManifestStep
{
    public ManifestStep(string step, int stepOrder, string ruleVersion, IEnumerable<ResourceReference> resources)
    {
        Step = step;
        StepOrder = stepOrder;
        RuleVersion = ruleVersion;
        Resources = Array.AsReadOnly(resources.ToArray());
    }
    public string Step { get; }
    public int StepOrder { get; }
    public string RuleVersion { get; }
    public IReadOnlyList<ResourceReference> Resources { get; }
}

public sealed record ResourceReference(string Kind, string Id, string Version, string Checksum);
