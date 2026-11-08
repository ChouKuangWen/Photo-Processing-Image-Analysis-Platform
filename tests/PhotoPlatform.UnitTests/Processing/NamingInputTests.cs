using System.Globalization;
using System.Text;
using PhotoPlatform.Application.Processing;

namespace PhotoPlatform.UnitTests.Processing;

public sealed class NamingInputTests
{
    private static readonly string Hash = new('a', 64);
    private static readonly Guid Request = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly DateTimeOffset Instant = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
    private static NamingInput Input(string policy = "Skip", string template = "{OriginalFileName}",
        string fileName = "photo", string extension = ".jpg", int offset = 480,
        DateTimeOffset? takenAt = null, long metadata = 11, long geo = 13, Guid? request = null,
        string rule = "naming-v1") =>
        new(1, Hash, "workflow-mvp-v1", rule, request ?? Request, Hash, 9, 0, 0,
            fileName, extension, template, policy, metadata, new string('b', 64), "metadata-v1",
            geo, new string('c', 64), "geo-v1", "Camera", "Place", takenAt ?? Instant, offset, "UploadCreatedAt");

    // 測試目的：確認正式命名格式與指紋。
    // 測試情境：提供完整已選來源。
    // 預期結果：與獨立固定 bytes 及 SHA256 完全一致。

    [Fact]
    public void Canonical_MatchesGoldenVector()
    {
        const string expected = """{"cameraModel":"Camera","collisionPolicy":"Skip","contentVersion":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","geoInputFingerprint":"cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc","geoResultId":13,"geoRuleVersion":"geo-v1","imageId":1,"itemId":9,"locationName":"Place","metadataInputFingerprint":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","metadataResultId":11,"metadataRuleVersion":"metadata-v1","ordinal":0,"originalExtension":".jpg","originalFileName":"photo","requestId":"11111111-2222-3333-4444-555555555555","requestInputFingerprint":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","ruleVersion":"naming-v1","schemaVersion":1,"sequence":0,"step":"NAMING","takenAt":"2026-01-02T03:04:05.0000000+00:00","takenAtOffsetMinutes":480,"template":"{OriginalFileName}","timeSource":"UploadCreatedAt","workflowVersion":"workflow-mvp-v1"}""";
        var input = Input();
        Assert.Equal(Encoding.UTF8.GetBytes(expected), input.ToCanonicalInput().ToUtf8Bytes());
        Assert.Equal("4053e0e7b3303977e80d288f2155cfd19e48d4cebb690e43de78882f1410d66b", input.Identity.InputFingerprint);
        Assert.Equal("NAMING", input.Identity.Step);
    }

    // 測試目的：確認碰撞政策不提前決定命名結果。
    // 測試情境：使用四種核准政策建立相同來源。
    // 預期結果：保留政策但不存在目標名稱或結果欄位。

    [Theory]
    [InlineData("NumericSuffix")]
    [InlineData("Sequence")]
    [InlineData("Skip")]
    [InlineData("Fail")]
    public void CollisionPolicy_IsInputNotOutcome(string policy)
    {
        var input = Input(policy);
        Assert.Equal(policy, input.CollisionPolicy);
        foreach (var name in new[] { "TargetName", "OperationId", "Receipt", "Outcome", "Mode" })
            Assert.Null(typeof(NamingInput).GetProperty(name));
    }

    // 測試目的：確認不同來源不共用指紋。
    // 測試情境：分別改變 Request、Metadata 與 GEO 引用。
    // 預期結果：三種變更都改變指紋。

    [Fact]
    public void SourceReferences_ChangeFingerprint()
    {
        var fingerprint = Input().Identity.InputFingerprint;
        Assert.NotEqual(fingerprint, Input(metadata: 12).Identity.InputFingerprint);
        Assert.NotEqual(fingerprint, Input(geo: 14).Identity.InputFingerprint);
        Assert.NotEqual(fingerprint, Input(request: Guid.Parse("21111111-2222-3333-4444-555555555555")).Identity.InputFingerprint);
        Assert.NotEqual(fingerprint, Input(rule: "naming-v2").Identity.InputFingerprint);
    }

    // 測試目的：確認 UTC 指紋與來源時區分開保存。
    // 測試情境：相同瞬間使用不同 offset 表達並切換 Culture。
    // 預期結果：相同瞬間不變但來源 offset 改變會改變指紋。

    [Fact]
    public void Time_PreservesSourceOffsetWithoutHostCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            var baseline = Input();
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Assert.Equal(baseline.Identity.InputFingerprint,
                Input(takenAt: Instant.ToOffset(TimeSpan.FromHours(8))).Identity.InputFingerprint);
            Assert.NotEqual(baseline.Identity.InputFingerprint, Input(offset: 0).Identity.InputFingerprint);
            Assert.Equal(480, baseline.TakenAtOffsetMinutes);
            Assert.Equal(-840, Input(offset: -840).TakenAtOffsetMinutes);
            Assert.Equal(840, Input(offset: 840).TakenAtOffsetMinutes);
            Assert.Throws<ArgumentOutOfRangeException>(() => Input(offset: 841));
            Assert.Throws<ArgumentOutOfRangeException>(() => Input(offset: -841));
            Assert.Throws<ArgumentOutOfRangeException>(() => Input(takenAt: DateTimeOffset.MinValue, offset: -840));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    // 測試目的：確認文字上限與必要身分。
    // 測試情境：使用上限、超長、空白、非法政策及來源。
    // 預期結果：合法上限保留原值而非法輸入拒絕。

    [Fact]
    public void Input_ValidatesTextAndIdentityBoundaries()
    {
        Assert.Equal(1000, Input(template: new string('x', 1000)).Template.Length);
        Assert.Equal(255, Input(fileName: new string('x', 255)).OriginalFileName.Length);
        Assert.Equal(20, Input(extension: new string('x', 20)).OriginalExtension.Length);
        Assert.Throws<ArgumentOutOfRangeException>(() => Input(template: new string('x', 1001)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Input(fileName: new string('x', 256)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Input(extension: new string('x', 21)));
        Assert.Throws<ArgumentException>(() => Input(template: " "));
        Assert.Throws<ArgumentNullException>(() => Input(fileName: null!));
        Assert.Throws<ArgumentException>(() => Input(extension: ""));
        Assert.Throws<ArgumentException>(() => Input(policy: "skip"));
        Assert.Throws<ArgumentException>(() => Input(request: Guid.Empty));
        Assert.Throws<ArgumentOutOfRangeException>(() => Input(metadata: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Input(geo: 0));
        Assert.Throws<ArgumentException>(() => Input(rule: "latest"));
        Assert.Throws<ArgumentOutOfRangeException>(() => Input(rule: new string('v', 51)));
        Assert.All(typeof(NamingInput).GetProperties(), property => Assert.Null(property.SetMethod));
        Assert.True(typeof(NamingInput).IsSealed);
    }
}
