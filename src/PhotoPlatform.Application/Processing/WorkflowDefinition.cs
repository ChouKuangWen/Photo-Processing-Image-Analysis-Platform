using PhotoPlatform.Domain.Enums;

namespace PhotoPlatform.Application.Processing;

// 只描述已核准的步驟，不代表已取得 SQL 執行權或模組依賴已就緒。
public sealed class WorkflowDefinition
{
    public const string SupportedVersion = "workflow-mvp-v1";

    internal WorkflowDefinition(WorkflowType workflow, params string[] steps)
    {
        Workflow = workflow;
        Steps = Array.AsReadOnly(steps.Select((step, index) =>
            new WorkflowStepDefinition(step, index + 1)).ToArray());
    }

    public WorkflowType Workflow { get; }
    public string WorkflowVersion => SupportedVersion;
    public IReadOnlyList<WorkflowStepDefinition> Steps { get; }
}

public sealed class WorkflowStepDefinition
{
    internal WorkflowStepDefinition(string step, int stepOrder)
    {
        Step = step;
        StepOrder = stepOrder;
    }

    public string Step { get; }
    public int StepOrder { get; }
}
