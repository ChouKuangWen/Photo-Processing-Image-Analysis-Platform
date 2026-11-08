using System.Text;
using PhotoPlatform.Application.Processing;

namespace PhotoPlatform.UnitTests.Processing;

public sealed class ExactDuplicateCheckInputTests
{
    private static readonly string Hash = new('a', 64);
    private static readonly Guid Snapshot = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static ExactDuplicateCandidate Candidate(long imageId = 2, long size = 10, string? sha = null) =>
        new(imageId, Hash, 12, size, sha ?? Hash, "hash-v1");
    private static ExactDuplicateCheckInput Input(IEnumerable<ExactDuplicateCandidate>? candidates = null,
        long jobId = 7, Guid? snapshot = null, string rule = "exact-v1") =>
        new(jobId, 1, Hash, 10, "workflow-mvp-v1", rule, 11, "hash-v1", Hash,
            snapshot ?? Snapshot, "scope-v1", "selection-v1", candidates ?? []);

    // 測試目的：確認主輸入的固定 bytes 與指紋。
    // 測試情境：分別建立空與單一候選快照。
    // 預期結果：與獨立固定向量完全一致。

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Canonical_MatchesGoldenVectors(bool populated)
    {
        const string empty = """{"candidateFingerprints":[],"candidateSelectionVersion":"selection-v1","contentLength":10,"contentVersion":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","hashResultId":11,"hashRuleVersion":"hash-v1","imageId":1,"jobId":7,"ruleVersion":"exact-v1","schemaVersion":1,"scopeVersion":"scope-v1","sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","snapshotId":"11111111-2222-3333-4444-555555555555","step":"EXACT_DUPLICATE_CHECK","workflowVersion":"workflow-mvp-v1"}""";
        const string nonempty = """{"candidateFingerprints":["2fce82136494fb87da63565dda8ab9e038029f9fa3fa2c2faade75765f102486"],"candidateSelectionVersion":"selection-v1","contentLength":10,"contentVersion":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","hashResultId":11,"hashRuleVersion":"hash-v1","imageId":1,"jobId":7,"ruleVersion":"exact-v1","schemaVersion":1,"scopeVersion":"scope-v1","sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","snapshotId":"11111111-2222-3333-4444-555555555555","step":"EXACT_DUPLICATE_CHECK","workflowVersion":"workflow-mvp-v1"}""";
        var input = Input(populated ? [Candidate()] : []);
        Assert.Equal(Encoding.UTF8.GetBytes(populated ? nonempty : empty), input.ToCanonicalInput().ToUtf8Bytes());
        Assert.Equal(populated
            ? "185adacf2941dee6cc29f5636fbba0be6c4bab3bb0e3f48573a125b01494aaef"
            : "d8f55f8b7e85e4d3b889d62099bd83ad61341825c49f1b6b8e059e37f146da19", input.Identity.InputFingerprint);
        Assert.Equal("EXACT_DUPLICATE_CHECK", input.Identity.Step);
    }

    // 測試目的：確認候選子格式可獨立重建。
    // 測試情境：使用固定來源建立候選。
    // 預期結果：bytes 與獨立 SHA256 相符。

    [Fact]
    public void Candidate_MatchesGoldenVector()
    {
        const string expected = """{"contentVersion":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","fileSize":10,"hashResultId":12,"hashRuleVersion":"hash-v1","imageId":2,"schemaVersion":1,"sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}""";
        var candidate = Candidate();
        Assert.Equal(Encoding.UTF8.GetBytes(expected), candidate.ToCanonicalInput().ToUtf8Bytes());
        Assert.Equal("2fce82136494fb87da63565dda8ab9e038029f9fa3fa2c2faade75765f102486", candidate.Fingerprint);
    }

    // 測試目的：確認排序與集合不受外部變更影響。
    // 測試情境：建立亂序清單後清空原清單並嘗試改動公開集合。
    // 預期結果：維持原排序與指紋且拒絕寫入。

    [Fact]
    public void Candidates_AreSortedAndDefensivelyCopied()
    {
        var source = new List<ExactDuplicateCandidate> { Candidate(3), Candidate(2) };
        var input = Input(source);
        var fingerprint = input.Identity.InputFingerprint;
        source.Clear();
        Assert.Equal(new long[] { 2, 3 }, input.Candidates.Select(candidate => candidate.ImageId));
        Assert.Throws<NotSupportedException>(() => ((IList<ExactDuplicateCandidate>)input.Candidates).Clear());
        Assert.Equal(Input([Candidate(2), Candidate(3)]).Identity.InputFingerprint, fingerprint);
        Assert.Equal(fingerprint, input.Identity.InputFingerprint);
    }

    // 測試目的：拒絕不合法的匹配集合。
    // 測試情境：提供重複、自身、空項目、大小或雜湊不符的候選。
    // 預期結果：以 candidates 參數錯誤拒絕。

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Candidates_RejectInvalidSets(int scenario)
    {
        ExactDuplicateCandidate[] candidates = scenario switch
        {
            0 => [Candidate(), Candidate()],
            1 => [Candidate(1)],
            2 => [null!],
            3 => [Candidate(size: 11)],
            _ => [Candidate(sha: new string('b', 64))]
        };
        var error = Assert.Throws<ArgumentException>(() => Input(candidates));
        Assert.Equal("candidates", error.ParamName);
    }

    // 測試目的：確認業務身分參與主指紋。
    // 測試情境：分別更換 Job、Snapshot、規則及候選。
    // 預期結果：每次變更都得到不同指紋。

    [Fact]
    public void BusinessIdentity_ChangesFingerprint()
    {
        var original = Input().Identity.InputFingerprint;
        Assert.NotEqual(original, Input(jobId: 8).Identity.InputFingerprint);
        Assert.NotEqual(original, Input(snapshot: Guid.Parse("21111111-2222-3333-4444-555555555555")).Identity.InputFingerprint);
        Assert.NotEqual(original, Input(rule: "exact-v2").Identity.InputFingerprint);
        Assert.NotEqual(original, Input([Candidate()]).Identity.InputFingerprint);
    }

    // 測試目的：拒絕非法 Hash 與必要數值。
    // 測試情境：提供大寫、非法字元、錯誤長度與零 ID。
    // 預期結果：拒絕正確的問題參數並接受零大小。

    [Fact]
    public void Candidate_ValidatesBoundaries()
    {
        foreach (var invalid in new[] { "", new string('A', 64), new string('g', 64), new string('a', 63), new string('a', 65) })
        {
            Assert.Equal("contentVersion", Assert.Throws<ArgumentException>(() =>
                new ExactDuplicateCandidate(2, invalid, 12, 10, Hash, "hash-v1")).ParamName);
            Assert.Equal("sha256", Assert.Throws<ArgumentException>(() => Candidate(sha: invalid)).ParamName);
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => Candidate(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Candidate(size: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExactDuplicateCandidate(2, Hash, 0, 10, Hash, "hash-v1"));
        Assert.Equal(0, Candidate(size: 0).FileSize);
        Assert.Throws<ArgumentException>(() => new ExactDuplicateCandidate(2, Hash, 12, 10, Hash, "latest"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExactDuplicateCandidate(2, Hash, 12, 10, Hash, new string('v', 51)));
    }

    // 測試目的：拒絕主輸入缺少快照或來源。
    // 測試情境：傳入空集合參考、空 GUID 與非法版本。
    // 預期結果：不產生有效輸入且公開表面不可重新指定。

    [Fact]
    public void Input_ValidatesRequiredIdentityAndReadonlySurface()
    {
        Assert.Throws<ArgumentException>(() => Input(snapshot: Guid.Empty));
        Assert.Throws<ArgumentOutOfRangeException>(() => Input(jobId: 0));
        Assert.Throws<ArgumentException>(() => Input(rule: "default"));
        Assert.Throws<ArgumentNullException>(() => new ExactDuplicateCheckInput(7, 1, Hash, 10,
            "workflow-mvp-v1", "exact-v1", 11, "hash-v1", Hash, Snapshot, "scope-v1", "selection-v1", null!));
        foreach (var type in new[] { typeof(ExactDuplicateCandidate), typeof(ExactDuplicateCheckInput) })
        {
            Assert.True(type.IsSealed);
            Assert.All(type.GetProperties(), property => Assert.Null(property.SetMethod));
        }
        var input = Input();
        Assert.Same(input, new ProcessingContext<ExactDuplicateCheckInput>(input.JobId, input).Input);
    }
}
