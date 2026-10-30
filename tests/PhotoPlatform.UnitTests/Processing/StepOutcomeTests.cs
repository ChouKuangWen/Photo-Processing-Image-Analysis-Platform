using System.Reflection;
using PhotoPlatform.Application.Processing;
using PhotoPlatform.Domain.Enums;

namespace PhotoPlatform.UnitTests.Processing;

// 只驗證純型別的防誤用規則，不代表結果已提交或已通過資料庫來源核對。
public sealed class StepOutcomeTests
{
    private static readonly string Hash = new('a', 64);
    private sealed record Payload(string Value);
    private sealed record Evidence(StepEvidenceIdentity Identity) : IStepEvidence;

    private static StepEvidenceIdentity Identity() => new(1, "GEO_LOOKUP", Hash, "workflow-mvp-v1", "geo-v1", Hash);
    private static CompletionProposal<Payload, Evidence> Proposal(ProcessingResultStatus status) =>
        new(status, new ResultMaterial<Payload>.NoNewResult(), new Evidence(Identity()));

    private static StepOutcome<Payload, Evidence>.Returned Return(ProcessingResultStatus status,
        CompletionProposal<Payload, Evidence> proposal) => status switch
        {
            ProcessingResultStatus.Success => StepOutcome<Payload, Evidence>.Success(proposal),
            ProcessingResultStatus.Warning => StepOutcome<Payload, Evidence>.Warning(proposal),
            ProcessingResultStatus.Skipped => StepOutcome<Payload, Evidence>.Skipped(proposal),
            _ => throw new ArgumentOutOfRangeException(nameof(status))
        };

    // 測試目的：確認共通識別保留輸入值，而不是替呼叫端選版本。
    // 測試情境：使用最大 ImageId、長度上限的文字和包含全部合法字元的雜湊。
    // 預期結果：所有公開欄位原樣保留。
    [Fact]
    public void Identity_PreservesValidBoundaries()
    {
        var text = new string('x', 50);
        var hash = string.Concat(Enumerable.Repeat("0123456789abcdef", 4));
        var identity = new StepEvidenceIdentity(long.MaxValue, text, hash, text, text, hash);
        Assert.Equal(long.MaxValue, identity.ImageId);
        Assert.Equal(text, identity.Step);
        Assert.Equal(text, identity.WorkflowVersion);
        Assert.Equal(text, identity.RuleVersion);
        Assert.Equal(hash, identity.ContentVersion);
        Assert.Equal(hash, identity.InputFingerprint);
    }

    // 測試目的：防止無效圖片識別進入後續核對。
    // 測試情境：傳入零或負數。
    // 預期結果：建構時拒絕，不產生有效識別。
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Identity_RejectsInvalidImageId(long imageId) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new StepEvidenceIdentity(imageId, "STEP", Hash, "v1", "v1", Hash));

    // 測試目的：確認步驟及兩種版本文字都有必要欄位與長度限制。
    // 測試情境：逐欄代入 NULL、空白、空字串及超過五十字元的文字。
    // 預期結果：每種缺漏與超長輸入都被拒絕。
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx")]
    public void Identity_RejectsInvalidText(string? text)
    {
        Assert.ThrowsAny<ArgumentException>(() => new StepEvidenceIdentity(1, text!, Hash, "v1", "v1", Hash));
        Assert.ThrowsAny<ArgumentException>(() => new StepEvidenceIdentity(1, "STEP", Hash, text!, "v1", Hash));
        Assert.ThrowsAny<ArgumentException>(() => new StepEvidenceIdentity(1, "STEP", Hash, "v1", text!, Hash));
    }

    // 測試目的：確認內容版本及輸入指紋都限定為小寫 SHA256 文字。
    // 測試情境：逐欄測試缺值、長度六十三或六十五，以及大寫與非十六進位字元。
    // 預期結果：兩個欄位採用相同拒絕規則，不自動轉換輸入。
    [Fact]
    public void Identity_RejectsInvalidHashes()
    {
        foreach (var hash in new[] { null, "", new string('a', 63), new string('a', 65), new string('A', 64), new string('g', 64) })
        {
            Assert.Throws<ArgumentException>(() => new StepEvidenceIdentity(1, "STEP", hash!, "v1", "v1", Hash));
            Assert.Throws<ArgumentException>(() => new StepEvidenceIdentity(1, "STEP", Hash, "v1", "v1", hash!));
        }
    }

    // 測試目的：確認失敗只帶分類與受控代碼，不附帶例外訊息。
    // 測試情境：使用兩種既有分類及一到五十字元的代碼。
    // 預期結果：保留原值；此處不把測試代碼視為業務白名單。
    [Theory]
    [InlineData(ProcessingErrorClassification.Transient)]
    [InlineData(ProcessingErrorClassification.Permanent)]
    public void Failure_AcceptsDefinedClassification(ProcessingErrorClassification classification)
    {
        foreach (var code in new[] { "X", "TEST_ONLY_CODE", new string('X', 50) })
        {
            var failure = new StepFailure(classification, code);
            Assert.Equal(classification, failure.Classification);
            Assert.Equal(code, failure.ErrorCode);
        }
    }

    // 測試目的：防止缺漏或含不受控字元的錯誤代碼流入公開契約。
    // 測試情境：提供空值、空白、控制字元、非 ASCII 及超長代碼。
    // 預期結果：全部拒絕，非法分類也不能建立失敗物件。
    [Fact]
    public void Failure_RejectsInvalidValues()
    {
        foreach (var code in new[] { null, "", " ", "A B", "A\n", "\u007f", "錯誤", new string('X', 51) })
            Assert.ThrowsAny<ArgumentException>(() => new StepFailure(ProcessingErrorClassification.Transient, code!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StepFailure((ProcessingErrorClassification)99, "ERROR"));
    }

    // 測試目的：區分準備好的資料與已提交結果，並禁止空 Payload。
    // 測試情境：建立 Prepared 後再嘗試傳入 NULL。
    // 預期結果：有效資料保留同一物件，NULL 被拒絕。
    [Fact]
    public void Prepared_RequiresPayload()
    {
        var payload = new Payload("prepared");
        Assert.Same(payload, new ResultMaterial<Payload>.Prepared(payload).Payload);
        Assert.Throws<ArgumentNullException>(() => new ResultMaterial<Payload>.Prepared(null!));
    }

    // 測試目的：確認候選引用的文字邊界，不假裝已核對資料庫結果。
    // 測試情境：傳入一般引用及長度上限，再提供缺值或不合法文字。
    // 預期結果：有效文字原樣保留，無效輸入拒絕；真正來源驗證仍由後續負責。
    [Fact]
    public void ExistingCandidate_ValidatesReference()
    {
        foreach (var reference in new[] { "metadata:1:v1", new string('x', 500) })
            Assert.Equal(reference, new ResultMaterial<Payload>.ExistingCandidate(reference).ResultReference);
        foreach (var reference in new[] { null, "", " ", "a b", "a\n", "\u007f", "資料", new string('x', 501) })
            Assert.ThrowsAny<ArgumentException>(() => new ResultMaterial<Payload>.ExistingCandidate(reference!));
    }

    // 測試目的：允許提出沒有新結果的合法情境，但不在純型別內授予完成權。
    // 測試情境：三種完成狀態各自搭配 NoNewResult 與具名證據。
    // 預期結果：提案保留狀態、資料分支與證據，仍需後續步驟規則核對。
    [Theory]
    [InlineData(ProcessingResultStatus.Success)]
    [InlineData(ProcessingResultStatus.Warning)]
    [InlineData(ProcessingResultStatus.Skipped)]
    public void Proposal_PreservesRequiredParts(ProcessingResultStatus status)
    {
        var material = new ResultMaterial<Payload>.NoNewResult();
        var evidence = new Evidence(Identity());
        var proposal = new CompletionProposal<Payload, Evidence>(status, material, evidence);
        Assert.Equal(status, proposal.ResultStatus);
        Assert.Same(material, proposal.Material);
        Assert.Same(evidence, proposal.Evidence);
    }

    // 測試目的：完成提案不能省略資料分支或共通輸入證據。
    // 測試情境：分別傳入非法狀態、空 Material、空 Evidence 及空 Identity。
    // 預期結果：所有不完整提案在建構時拒絕。
    [Fact]
    public void Proposal_RejectsIncompleteValues()
    {
        var material = new ResultMaterial<Payload>.NoNewResult();
        var evidence = new Evidence(Identity());
        Assert.Throws<ArgumentOutOfRangeException>(() => new CompletionProposal<Payload, Evidence>((ProcessingResultStatus)99, material, evidence));
        Assert.Throws<ArgumentNullException>(() => new CompletionProposal<Payload, Evidence>(ProcessingResultStatus.Success, null!, evidence));
        Assert.Throws<ArgumentNullException>(() => new CompletionProposal<Payload, Evidence>(ProcessingResultStatus.Success, material, null!));
        Assert.Throws<ArgumentNullException>(() => new CompletionProposal<Payload, Evidence>(ProcessingResultStatus.Success, material, new Evidence(null!)));
    }

    // 測試目的：Returned 只允許執行結果與完成提案同名配對。
    // 測試情境：對每個 Factory 逐一提供三種提案，以及 NULL。
    // 預期結果：同名配對成功，其餘六種錯配與空提案全部拒絕。
    [Theory]
    [InlineData(ProcessingResultStatus.Success, ProcessingLogStatus.Success)]
    [InlineData(ProcessingResultStatus.Warning, ProcessingLogStatus.Warning)]
    [InlineData(ProcessingResultStatus.Skipped, ProcessingLogStatus.Skipped)]
    public void Returned_EnforcesPairing(ProcessingResultStatus factoryStatus, ProcessingLogStatus logStatus)
    {
        foreach (var result in Enum.GetValues<ProcessingResultStatus>())
        {
            var proposal = Proposal(result);
            if (result == factoryStatus)
            {
                var outcome = Return(factoryStatus, proposal);
                Assert.Equal(logStatus, outcome.InvocationStatus);
                Assert.Same(proposal, outcome.Completion);
            }
            else
                Assert.Throws<ArgumentException>(() => Return(factoryStatus, proposal));
        }
        Assert.Throws<ArgumentNullException>(() => Return(factoryStatus, null!));
    }

    // 測試目的：讀取失敗等情況沒有模組證據時，仍可回報失敗觀察。
    // 測試情境：以相同失敗分類分別提供及省略 Evidence。
    // 預期結果：兩者皆保留 Failure，不憑空產生完成提案或持久執行識別。
    [Fact]
    public void Failed_AllowsOptionalEvidence()
    {
        var failure = new StepFailure(ProcessingErrorClassification.Permanent, "READ_FAILED");
        var evidence = new Evidence(Identity());
        var withEvidence = new StepOutcome<Payload, Evidence>.Failed(failure, evidence);
        var withoutEvidence = new StepOutcome<Payload, Evidence>.Failed(failure);
        Assert.Same(failure, withEvidence.Failure);
        Assert.Same(evidence, withEvidence.Evidence);
        Assert.Same(failure, withoutEvidence.Failure);
        Assert.Null(withoutEvidence.Evidence);
        Assert.Throws<ArgumentNullException>(() => new StepOutcome<Payload, Evidence>.Failed(null!));
        Assert.Throws<ArgumentNullException>(() => new StepOutcome<Payload, Evidence>.Failed(failure, new Evidence(null!)));
    }

    // 測試目的：結果未明時必須留下證據，不能直接當成失敗或可重試。
    // 測試情境：提供有效證據，再嘗試缺少 Evidence 或 Identity。
    // 預期結果：只接受完整的共通證據，不新增終態或重試授權。
    [Fact]
    public void Reconciliation_RequiresEvidence()
    {
        var evidence = new Evidence(Identity());
        Assert.Same(evidence, new StepOutcome<Payload, Evidence>.NeedsReconciliation(evidence).Evidence);
        Assert.Throws<ArgumentNullException>(() => new StepOutcome<Payload, Evidence>.NeedsReconciliation(null!));
        Assert.Throws<ArgumentNullException>(() => new StepOutcome<Payload, Evidence>.NeedsReconciliation(new Evidence(null!)));
    }

    // 測試目的：防止呼叫端繞過 Factory 建立錯誤 Returned 或自行新增分支。
    // 測試情境：檢查公開建構入口、基底建構子及各分支的繼承限制。
    // 預期結果：Returned 無公開建構子，基底只允許同組件衍生，既有分支全部封閉。
    [Fact]
    public void PublicConstruction_DoesNotExposeBypass()
    {
        Assert.Empty(typeof(StepOutcome<Payload, Evidence>.Returned).GetConstructors());
        foreach (var type in new[] { typeof(StepOutcome<Payload, Evidence>), typeof(ResultMaterial<Payload>) })
        {
            Assert.True(type.IsAbstract);
            Assert.Empty(type.GetConstructors(BindingFlags.Instance | BindingFlags.Public));
            var constructor = Assert.Single(type.GetConstructors(
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
            Assert.True(constructor.IsFamilyAndAssembly);
        }
        foreach (var type in new[] { typeof(StepOutcome<Payload, Evidence>.Returned), typeof(StepOutcome<Payload, Evidence>.Failed),
            typeof(StepOutcome<Payload, Evidence>.NeedsReconciliation), typeof(ResultMaterial<Payload>.Prepared),
            typeof(ResultMaterial<Payload>.ExistingCandidate), typeof(ResultMaterial<Payload>.NoNewResult) })
            Assert.True(type.IsSealed);
        Assert.Equal(new[] { "Skipped", "Success", "Warning" }, typeof(StepOutcome<Payload, Evidence>)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly).Select(method => method.Name).OrderBy(name => name));
    }

    // 測試目的：建立後不能重新指定共通識別、失敗或完成結果。
    // 測試情境：檢查純契約公開屬性及欄位，不測試任意模組 Payload 的內部可變性。
    // 預期結果：所有公開屬性沒有 setter，也沒有可公開寫入的欄位。
    [Fact]
    public void PublicProperties_CannotBeReassigned()
    {
        foreach (var type in new[] { typeof(StepEvidenceIdentity), typeof(StepFailure), typeof(IStepEvidence),
            typeof(CompletionProposal<Payload, Evidence>), typeof(ResultMaterial<Payload>.Prepared),
            typeof(ResultMaterial<Payload>.ExistingCandidate), typeof(StepOutcome<Payload, Evidence>.Returned),
            typeof(StepOutcome<Payload, Evidence>.Failed), typeof(StepOutcome<Payload, Evidence>.NeedsReconciliation) })
        {
            Assert.All(type.GetProperties(), property => Assert.Null(property.SetMethod));
            Assert.Empty(type.GetFields(BindingFlags.Public | BindingFlags.Instance));
        }
    }
}
