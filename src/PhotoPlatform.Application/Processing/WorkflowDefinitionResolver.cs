using PhotoPlatform.Domain.Enums;

namespace PhotoPlatform.Application.Processing;

// 不讀取可變設定。舊 Run 必須仍解析到原版本，不能偷偷換成最新流程。
public sealed class WorkflowDefinitionResolver
{
    private static readonly WorkflowDefinition Naming = new(WorkflowType.Naming,
        "EXIF_PARSING", "GEO_LOOKUP", "NAMING", "FILE_RENAME");
    private static readonly WorkflowDefinition DuplicateDetection = new(WorkflowType.DuplicateDetection,
        "SHA256_CALCULATION", "EXACT_DUPLICATE_CHECK");
    private static readonly WorkflowDefinition Analysis = new(WorkflowType.Analysis,
        "EXIF_PARSING", "GEO_LOOKUP", "SHA256_CALCULATION", "EXACT_DUPLICATE_CHECK",
        "PHASH_GENERATION", "VISUAL_SIMILARITY", "QUALITY_ANALYSIS", "VERSION_ANALYSIS");
    private static readonly WorkflowDefinition Full = new(WorkflowType.Full,
        "EXIF_PARSING", "GEO_LOOKUP", "SHA256_CALCULATION", "EXACT_DUPLICATE_CHECK",
        "PHASH_GENERATION", "VISUAL_SIMILARITY", "QUALITY_ANALYSIS", "VERSION_ANALYSIS",
        "NAMING", "FILE_RENAME");

    public WorkflowDefinition Resolve(WorkflowType workflow, string workflowVersion)
    {
        if (!string.Equals(workflowVersion, WorkflowDefinition.SupportedVersion, StringComparison.Ordinal))
            throw new ArgumentException("Unknown workflow version.", nameof(workflowVersion));

        // Analysis／Full 可供檢視定義，但 N-01／N-02 的執行限制仍由後續准入流程保留。
        return workflow switch
        {
            WorkflowType.Naming => Naming,
            WorkflowType.DuplicateDetection => DuplicateDetection,
            WorkflowType.Analysis => Analysis,
            WorkflowType.Full => Full,
            _ => throw new ArgumentOutOfRangeException(nameof(workflow))
        };
    }

    public WorkflowDefinition Resolve(string workflow, string workflowVersion)
    {
        // 不接受 Enum.TryParse 能解析的數字、逗號組合或大小寫別名，避免未知輸入被當成正式名稱。
        var type = workflow switch
        {
            "Naming" => WorkflowType.Naming,
            "DuplicateDetection" => WorkflowType.DuplicateDetection,
            "Analysis" => WorkflowType.Analysis,
            "Full" => WorkflowType.Full,
            _ => throw new ArgumentException("Unknown workflow name.", nameof(workflow))
        };
        return Resolve(type, workflowVersion);
    }
}
