using System.Text;
using PhotoPlatform.Application.Interfaces;
using PhotoPlatform.Application.Processing;

namespace PhotoPlatform.UnitTests.Processing;

public sealed class FileRenameInputTests
{
    private static readonly string Hash = new('a', 64);
    private static readonly string NamingHash = new('b', 64);
    private static readonly Guid Operation = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static FileRenameInput Create(Guid? operation = null, string provider = "local",
        string space = "images", string targetPolicy = "target-v1", string key = "local/images/photo.jpg",
        string name = "photo.jpg", string rule = "rename-v1") =>
        FileRenameInput.Create(1, Hash, "workflow-mvp-v1", rule, 9, NamingHash,
            operation ?? Operation, provider, space, targetPolicy, key, name);
    private static FileRenameInput Skip() =>
        FileRenameInput.Skip(1, Hash, "workflow-mvp-v1", "rename-v1", 9, NamingHash);

    // 測試目的：確認 Create 與 Skip 的固定格式。
    // 測試情境：分別使用兩個 Factory。
    // 預期結果：bytes 與獨立指紋一致且 Skip 明確輸出 null。

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Canonical_MatchesGoldenVectors(bool skip)
    {
        const string create = """{"contentVersion":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","imageId":1,"itemId":9,"mode":"Create","namespaceId":"images","namingInputFingerprint":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","operationId":"11111111-2222-3333-4444-555555555555","providerId":"local","ruleVersion":"rename-v1","schemaVersion":1,"step":"FILE_RENAME","targetKey":"local/images/photo.jpg","targetName":"photo.jpg","targetPolicyVersion":"target-v1","workflowVersion":"workflow-mvp-v1"}""";
        const string skipped = """{"contentVersion":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","imageId":1,"itemId":9,"mode":"Skip","namespaceId":null,"namingInputFingerprint":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","operationId":null,"providerId":null,"ruleVersion":"rename-v1","schemaVersion":1,"step":"FILE_RENAME","targetKey":null,"targetName":null,"targetPolicyVersion":null,"workflowVersion":"workflow-mvp-v1"}""";
        var input = skip ? Skip() : Create();
        Assert.Equal(Encoding.UTF8.GetBytes(skip ? skipped : create), input.ToCanonicalInput().ToUtf8Bytes());
        Assert.Equal(skip
            ? "31c9b8977e8c1597f0a65d5319336e0df5bd85879ef2356ab5a027da28ff9bed"
            : "b042b1fa31bd1fac4bbf757ba9c081c406cdbd5bb4c191fa1d5e7c98fa3eb047", input.Identity.InputFingerprint);
        Assert.Equal("FILE_RENAME", input.Identity.Step);
        Assert.Equal(skip ? "Skip" : "Create", input.Mode);
        object?[] branch = [input.OperationId, input.ProviderId, input.NamespaceId,
            input.TargetPolicyVersion, input.TargetKey, input.TargetName];
        if (skip)
            Assert.All(branch, value => Assert.Null(value));
        else
            Assert.All(branch, value => Assert.NotNull(value));
    }

    // 測試目的：拒絕 Create 缺少必要分支值。
    // 測試情境：提供空 GUID 與缺失的五個文字來源。
    // 預期結果：各問題參數被拒絕而不產生部分 Create。

    [Fact]
    public void Create_RequiresEveryBranchField()
    {
        Assert.Equal("operationId", Assert.Throws<ArgumentException>(() => Create(Guid.Empty)).ParamName);
        Assert.Equal("providerId", Assert.Throws<ArgumentNullException>(() => Create(provider: null!)).ParamName);
        Assert.Equal("namespaceId", Assert.Throws<ArgumentException>(() => Create(space: "")).ParamName);
        Assert.Equal("targetPolicyVersion", Assert.Throws<ArgumentException>(() => Create(targetPolicy: " ")).ParamName);
        Assert.Equal("targetKey", Assert.Throws<ArgumentException>(() => Create(key: "")).ParamName);
        Assert.Equal("targetName", Assert.Throws<ArgumentException>(() => Create(name: "")).ParamName);
    }

    // 測試目的：確認欄位長度與固定版本。
    // 測試情境：使用各欄位上限及超長值。
    // 預期結果：接受上限並拒絕超長或浮動版本。

    [Fact]
    public void Create_ValidatesLengthsAndVersions()
    {
        var input = Create(provider: new string('p', 50), space: new string('s', 100),
            targetPolicy: new string('v', 50), key: new string('k', 500), name: new string('n', 255));
        Assert.Equal(500, input.TargetKey!.Length);
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(provider: new string('p', 51)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(space: new string('s', 101)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(targetPolicy: new string('v', 51)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(key: new string('k', 501)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(name: new string('n', 256)));
        Assert.Throws<ArgumentException>(() => Create(rule: "default"));
        Assert.Throws<ArgumentException>(() => Create(targetPolicy: "latest"));
    }

    // 測試目的：確認共通來源不能缺失。
    // 測試情境：以非法 ID 與指紋呼叫 Skip。
    // 預期結果：拒絕錯誤且不將 Skip 當免驗證路徑。

    [Fact]
    public void Skip_ValidatesCommonSources()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            FileRenameInput.Skip(0, Hash, "workflow-mvp-v1", "rename-v1", 9, NamingHash));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            FileRenameInput.Skip(1, Hash, "workflow-mvp-v1", "rename-v1", 0, NamingHash));
        foreach (var invalid in new[] { "", new string('A', 64), new string('g', 64), new string('a', 63), new string('a', 65) })
        {
            Assert.Equal("contentVersion", Assert.Throws<ArgumentException>(() =>
                FileRenameInput.Skip(1, invalid, "workflow-mvp-v1", "rename-v1", 9, NamingHash)).ParamName);
            Assert.Equal("namingInputFingerprint", Assert.Throws<ArgumentException>(() =>
                FileRenameInput.Skip(1, Hash, "workflow-mvp-v1", "rename-v1", 9, invalid)).ParamName);
        }
    }

    // 測試目的：確認不能事後改寫 Create 成 Skip。
    // 測試情境：檢查公開入口並另建 Skip 輸入。
    // 預期結果：原 Operation 與指紋維持不變且無公開建構子或 setter。

    [Fact]
    public void Factories_PreserveImmutableCreateIdentity()
    {
        var input = Create();
        var fingerprint = input.Identity.InputFingerprint;
        var skipped = Skip();
        Assert.Equal("Create", input.Mode);
        Assert.Equal(Operation, input.OperationId);
        Assert.Equal(fingerprint, input.Identity.InputFingerprint);
        Assert.NotEqual(fingerprint, skipped.Identity.InputFingerprint);
        Assert.Empty(typeof(FileRenameInput).GetConstructors());
        Assert.True(typeof(FileRenameInput).IsSealed);
        Assert.All(typeof(FileRenameInput).GetProperties(), property => Assert.Null(property.SetMethod));
    }

    // 測試目的：確認既有協調泛型可接收新輸入。
    // 測試情境：以 FILE_RENAME 建立 Context 並使用原 Adapter 型別。
    // 預期結果：保留原輸入指紋而不假裝執行 Storage。

    [Fact]
    public void Input_IsCompatibleWithExistingContextAndAdapter()
    {
        var input = Create();
        var context = new ProcessingContext<FileRenameInput>(7, input);
        Assert.Same(input, context.Input);
        Assert.Equal(input.Identity.InputFingerprint, context.Input.Identity.InputFingerprint);
        Assert.Equal(7, context.JobId);
        var contract = typeof(IProcessingStepAdapter<FileRenameInput, string, Evidence>);
        Assert.True(contract.IsInterface);
        Assert.NotNull(contract.GetMethod("ExecuteAsync"));
    }

    private sealed class Evidence : IStepEvidence
    {
        public StepEvidenceIdentity Identity => Create().Identity;
    }
}
