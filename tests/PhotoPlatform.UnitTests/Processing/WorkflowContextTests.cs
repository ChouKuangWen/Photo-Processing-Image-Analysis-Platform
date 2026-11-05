using PhotoPlatform.Application.Processing;
using PhotoPlatform.Domain.Enums;

namespace PhotoPlatform.UnitTests.Processing;

public sealed class WorkflowContextTests
{
    private static Sha256CalculationInput Input() =>
        new(7, new string('a', 64), 1, "workflow-mvp-v1", "hash-v1");

    // 測試目的：確認 Context 不接受不存在的 Job 身分或缺少輸入。
    // 測試情境：使用零、負數 JobId 與 null 輸入。
    // 預期結果：拒絕建立 Context，不提供預設輸入。
    [Fact]
    public void Context_RejectsInvalidArguments()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProcessingContext<Sha256CalculationInput>(0, Input()));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProcessingContext<Sha256CalculationInput>(-1, Input()));
        Assert.Throws<ArgumentNullException>(() => new ProcessingContext<Sha256CalculationInput>(1, null!));
    }

    // 測試目的：確認 Job 關聯不會污染原圖業務指紋。
    // 測試情境：讓兩個 Context 引用同一份具名輸入。
    // 預期結果：保留各自 JobId、同一輸入與原指紋，公開屬性沒有 setter。
    [Fact]
    public void Context_PreservesTypedInputWithoutChangingFingerprint()
    {
        var input = Input();
        var fingerprint = input.Identity.InputFingerprint;
        var first = new ProcessingContext<Sha256CalculationInput>(1, input);
        var second = new ProcessingContext<Sha256CalculationInput>(long.MaxValue, input);
        Assert.Equal(1, first.JobId);
        Assert.Equal(long.MaxValue, second.JobId);
        Assert.Same(input, first.Input);
        Assert.Same(input, second.Input);
        Assert.Equal(fingerprint, InputFingerprint.Compute(second.Input.ToCanonicalInput()));
        Assert.All(first.GetType().GetProperties(), property => Assert.Null(property.SetMethod));
    }

    // 測試目的：確認具名原圖輸入對應正式流程的第一個步驟。
    // 測試情境：解析 Naming 與 DuplicateDetection，對照各自的 EXIF 或 SHA 輸入。
    // 預期結果：Step 與版本一致；此測試只核對定義，不宣稱取得執行權。
    [Fact]
    public void InputIdentity_MatchesApprovedWorkflowStep()
    {
        var resolver = new WorkflowDefinitionResolver();
        var exif = new ExifParsingInput(7, new string('a', 64), 1, "workflow-mvp-v1", "metadata-v1", DateTimeOffset.UnixEpoch);
        foreach (var pair in new[] { (WorkflowType.Naming, (IProcessingStepInput)exif), (WorkflowType.DuplicateDetection, (IProcessingStepInput)Input()) })
        {
            var definition = resolver.Resolve(pair.Item1, pair.Item2.Identity.WorkflowVersion);
            Assert.Equal(definition.Steps[0].Step, pair.Item2.Identity.Step);
            Assert.Equal(definition.WorkflowVersion, pair.Item2.Identity.WorkflowVersion);
        }
    }
}
