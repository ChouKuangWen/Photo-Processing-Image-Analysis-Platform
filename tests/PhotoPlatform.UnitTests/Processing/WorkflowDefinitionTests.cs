using PhotoPlatform.Application.Processing;
using PhotoPlatform.Domain.Enums;

namespace PhotoPlatform.UnitTests.Processing;

public sealed class WorkflowDefinitionTests
{
    private readonly WorkflowDefinitionResolver resolver = new();

    // 測試目的：確認四種流程只有正式核准的步驟，且順序不因實作方便而改動。
    // 測試情境：分別解析 Naming、DuplicateDetection、Analysis 和 Full 的固定版本。
    // 預期結果：名稱與序號逐項相符，沒有 VALIDATION／COMPLETED 假步驟；解析不代表允許執行。
    [Theory]
    [InlineData(WorkflowType.Naming, "EXIF_PARSING,GEO_LOOKUP,NAMING,FILE_RENAME")]
    [InlineData(WorkflowType.DuplicateDetection, "SHA256_CALCULATION,EXACT_DUPLICATE_CHECK")]
    [InlineData(WorkflowType.Analysis, "EXIF_PARSING,GEO_LOOKUP,SHA256_CALCULATION,EXACT_DUPLICATE_CHECK,PHASH_GENERATION,VISUAL_SIMILARITY,QUALITY_ANALYSIS,VERSION_ANALYSIS")]
    [InlineData(WorkflowType.Full, "EXIF_PARSING,GEO_LOOKUP,SHA256_CALCULATION,EXACT_DUPLICATE_CHECK,PHASH_GENERATION,VISUAL_SIMILARITY,QUALITY_ANALYSIS,VERSION_ANALYSIS,NAMING,FILE_RENAME")]
    public void Resolve_ReturnsExactApprovedDefinition(WorkflowType workflow, string expected)
    {
        var definition = resolver.Resolve(workflow, "workflow-mvp-v1");
        Assert.Equal(workflow, definition.Workflow);
        Assert.Equal("workflow-mvp-v1", definition.WorkflowVersion);
        Assert.Equal(expected.Split(','), definition.Steps.Select(step => step.Step));
        Assert.Equal(Enumerable.Range(1, definition.Steps.Count), definition.Steps.Select(step => step.StepOrder));
        Assert.DoesNotContain(definition.Steps, step => step.Step is "VALIDATION" or "COMPLETED");
        Assert.Same(definition, resolver.Resolve(workflow.ToString(), "workflow-mvp-v1"));
    }

    // 測試目的：確認流程名稱不能透過數字、空白或大小寫別名繞過檢查。
    // 測試情境：送入未知名稱、Enum 數字及看似相近的字串。
    // 預期結果：全部拒絕，不默認成 Naming 或其他合法流程。
    [Theory]
    [InlineData("0")]
    [InlineData("naming")]
    [InlineData(" Naming")]
    [InlineData("Naming,Full")]
    [InlineData("Unknown")]
    [InlineData("")]
    [InlineData(null)]
    public void Resolve_RejectsUnknownNames(string? workflow)
        => Assert.Throws<ArgumentException>(() => resolver.Resolve(workflow!, "workflow-mvp-v1"));

    // 測試目的：確認舊 Run 不會被解析成目前最新或其他流程版本。
    // 測試情境：傳入 latest、未知版本、大小寫不同的版本或缺值。
    // 預期結果：拒絕解析；呼叫端必須提供原本已知的版本。
    [Theory]
    [InlineData("latest")]
    [InlineData("workflow-mvp-v2")]
    [InlineData("WORKFLOW-MVP-V1")]
    [InlineData("")]
    [InlineData(null)]
    public void Resolve_RejectsUnknownVersions(string? version)
        => Assert.Throws<ArgumentException>(() => resolver.Resolve(WorkflowType.Naming, version!));

    // 測試目的：確認非法 Enum 與可變集合都不能改變已凍結的流程。
    // 測試情境：傳入未定義 Enum，並嘗試透過 ICollection 修改回傳的步驟。
    // 預期結果：非法值拒絕，集合禁止增刪，下一次解析仍取得原始定義。
    [Fact]
    public void Definition_IsImmutableAndRejectsInvalidEnum()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => resolver.Resolve((WorkflowType)99, "workflow-mvp-v1"));
        var definition = resolver.Resolve(WorkflowType.Naming, "workflow-mvp-v1");
        var collection = Assert.IsAssignableFrom<ICollection<WorkflowStepDefinition>>(definition.Steps);
        Assert.True(collection.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => collection.Clear());
        Assert.Throws<NotSupportedException>(() => collection.Add(definition.Steps[0]));
        Assert.Equal(4, resolver.Resolve(WorkflowType.Naming, "workflow-mvp-v1").Steps.Count);
        Assert.All(typeof(WorkflowStepDefinition).GetProperties(), property => Assert.Null(property.SetMethod));
        Assert.All(typeof(WorkflowDefinition).GetProperties(), property => Assert.Null(property.SetMethod));
    }
}
