using System.Text;
using PhotoPlatform.Application.Processing;

namespace PhotoPlatform.UnitTests.Processing;

public sealed class GeoQualityStepInputTests
{
    private static readonly string Content = new('a', 64);
    private static readonly string Metadata = new('b', 64);

    private static GeoLookupInput Geo(string validity = "Valid", decimal? latitude = 25.5m, decimal? longitude = 121.25m) =>
        new(7, Content, "workflow-mvp-v1", "geo-v1", 11, Metadata, "metadata-v1", validity, latitude, longitude);

    // 測試目的：確認 GEO 與 QUALITY 使用正式欄位順序與獨立固定的指紋。
    // 測試情境：比較固定 JSON 的 UTF-8 bytes 與事先獨立計算的 SHA256。
    // 預期結果：包含明確 null 的 GEO 與 QUALITY 都精確相符，不使用被測序列化器建立預期值。
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Canonical_MatchesIndependentGoldenVectors(int vector)
    {
        const string valid = """{"contentVersion":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","gpsValidity":"Valid","imageId":7,"latitude":25.5,"longitude":121.25,"metadataInputFingerprint":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","metadataResultId":11,"metadataRuleVersion":"metadata-v1","ruleVersion":"geo-v1","schemaVersion":1,"step":"GEO_LOOKUP","workflowVersion":"workflow-mvp-v1"}""";
        const string missing = """{"contentVersion":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","gpsValidity":"Missing","imageId":7,"latitude":null,"longitude":null,"metadataInputFingerprint":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","metadataResultId":11,"metadataRuleVersion":"metadata-v1","ruleVersion":"geo-v1","schemaVersion":1,"step":"GEO_LOOKUP","workflowVersion":"workflow-mvp-v1"}""";
        const string quality = """{"contentLength":1024,"contentVersion":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","imageId":7,"metadataInputFingerprint":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","metadataResultId":11,"metadataRuleVersion":"metadata-v1","ruleVersion":"quality-v1","schemaVersion":1,"step":"QUALITY_ANALYSIS","workflowVersion":"workflow-mvp-v1"}""";
        var expectedJson = new[] { valid, missing, quality }[vector];
        var expectedHash = new[]
        {
            "445801c2cc6966b54e8d5b10bb4d81c7b6cf9d1a7385e51dd387f42ff6435dd4",
            "8ed58503b770aa0af57c68716d83b6136f7dfa03e978254c2975ffe08fe89cd8",
            "8ae18190f5b9954bd06190a99be8c4cd993a316b28d019d9c7b7f12c0f159ea8"
        }[vector];
        IProcessingStepInput input = vector switch
        {
            0 => Geo(),
            1 => Geo("Missing", null, null),
            _ => new QualityAnalysisInput(7, Content, 1024, "workflow-mvp-v1", "quality-v1", 11, Metadata, "metadata-v1")
        };
        Assert.Equal(Encoding.UTF8.GetBytes(expectedJson), input.ToCanonicalInput().ToUtf8Bytes());
        Assert.Equal(expectedHash, input.Identity.InputFingerprint);
        Assert.Equal(expectedHash, InputFingerprint.Compute(input.ToCanonicalInput()));
        Assert.Equal(vector == 2 ? "QUALITY_ANALYSIS" : "GEO_LOOKUP", input.Identity.Step);
        Assert.Equal(7, input.Identity.ImageId);
        Assert.Equal(Content, input.Identity.ContentVersion);
        Assert.Equal("workflow-mvp-v1", input.Identity.WorkflowVersion);
        Assert.Equal(vector == 2 ? "quality-v1" : "geo-v1", input.Identity.RuleVersion);
    }

    // 測試目的：確認有效 GPS 接受包含端點的合法座標。
    // 測試情境：使用緯度正負90、經度正負180及零。
    // 預期結果：座標原值保留，不轉換成 Missing 或 Invalid。
    [Theory]
    [InlineData(-90, -180)]
    [InlineData(90, 180)]
    [InlineData(0, 0)]
    public void ValidGps_AcceptsInclusiveBoundaries(int latitude, int longitude)
    {
        var input = Geo("Valid", latitude, longitude);
        Assert.Equal("Valid", input.GpsValidity);
        Assert.Equal((decimal)latitude, input.Latitude);
        Assert.Equal((decimal)longitude, input.Longitude);
    }

    // 測試目的：確認無有效 GPS 時只能保存明確的空座標。
    // 測試情境：分別建立 Missing 與 Invalid，並嘗試混入單邊或成對座標。
    // 預期結果：只有雙 null 合法，且兩種 validity 不會產生相同指紋。
    [Theory]
    [InlineData("Missing")]
    [InlineData("Invalid")]
    public void NonValidGps_RequiresNullCoordinates(string validity)
    {
        var input = Geo(validity, null, null);
        Assert.Equal(validity, input.GpsValidity);
        Assert.Null(input.Latitude);
        Assert.Null(input.Longitude);
        Assert.NotEqual(Geo("Missing", null, null).Identity.InputFingerprint,
            Geo("Invalid", null, null).Identity.InputFingerprint);
        Assert.Throws<ArgumentException>(() => Geo(validity, 0, null));
        Assert.Throws<ArgumentException>(() => Geo(validity, null, 0));
        Assert.Throws<ArgumentException>(() => Geo(validity, 0, 0));
    }

    // 測試目的：確認未知 validity、半邊座標與超界值不能進入有效輸入。
    // 測試情境：傳入空值、大小寫別名、缺少座標及剛超出界線的數值。
    // 預期結果：每個非法輸入被對應參數檢查拒絕，不自動修正或補零。
    [Fact]
    public void Gps_RejectsUnknownAndInvalidCoordinates()
    {
        foreach (var validity in new[] { null, "", " ", "valid", "Unknown" })
            Assert.Throws<ArgumentException>(() => Geo(validity!, null, null));
        foreach (var latitude in new decimal?[] { null, -90.0001m, 90.0001m })
            Assert.Equal("latitude", Assert.Throws<ArgumentOutOfRangeException>(() => Geo("Valid", latitude, 0)).ParamName);
        foreach (var longitude in new decimal?[] { null, -180.0001m, 180.0001m })
            Assert.Equal("longitude", Assert.Throws<ArgumentOutOfRangeException>(() => Geo("Valid", 0, longitude)).ParamName);
    }

    // 測試目的：確認兩個輸入都拒絕非法原圖及 Metadata 身分。
    // 測試情境：一次只改一個 ID、hash 或版本，其他參數保持合法。
    // 預期結果：拒絕非正值、空值、超長版本、大寫或錯誤長度 hash。
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Inputs_RejectInvalidSourceFields(bool geo)
    {
        IProcessingStepInput Create(long imageId, string content, string workflow, string rule,
            long metadataId, string fingerprint, string metadataRule) => geo
            ? new GeoLookupInput(imageId, content, workflow, rule, metadataId, fingerprint, metadataRule, "Missing", null, null)
            : new QualityAnalysisInput(imageId, content, 0, workflow, rule, metadataId, fingerprint, metadataRule);
        foreach (var invalidId in new[] { 0L, -1L })
        {
            Assert.Equal("imageId", Assert.Throws<ArgumentOutOfRangeException>(() =>
                Create(invalidId, Content, "workflow-mvp-v1", "rule-v1", 11, Metadata, "metadata-v1")).ParamName);
            Assert.Equal("metadataResultId", Assert.Throws<ArgumentOutOfRangeException>(() =>
                Create(7, Content, "workflow-mvp-v1", "rule-v1", invalidId, Metadata, "metadata-v1")).ParamName);
        }
        foreach (var hash in new[] { null, "", new string('a', 63), new string('a', 65), new string('A', 64), new string('g', 64) })
        {
            Assert.ThrowsAny<ArgumentException>(() => Create(7, hash!, "workflow-mvp-v1", "rule-v1", 11, Metadata, "metadata-v1"));
            Assert.Equal("metadataInputFingerprint", Assert.Throws<ArgumentException>(() =>
                Create(7, Content, "workflow-mvp-v1", "rule-v1", 11, hash!, "metadata-v1")).ParamName);
        }
        foreach (var version in new[] { null, "", " ", new string('v', 51) })
        {
            Assert.ThrowsAny<ArgumentException>(() => Create(7, Content, version!, "rule-v1", 11, Metadata, "metadata-v1"));
            Assert.ThrowsAny<ArgumentException>(() => Create(7, Content, "workflow-mvp-v1", version!, 11, Metadata, "metadata-v1"));
            Assert.ThrowsAny<ArgumentException>(() => Create(7, Content, "workflow-mvp-v1", "rule-v1", 11, Metadata, version!));
        }
    }

    // 測試目的：確認來源引用與版本都實際參與指紋。
    // 測試情境：逐項改變 Metadata ID、指紋、規則及原圖內容或流程版本。
    // 預期結果：任一業務來源變動都改變指紋，不依目前資料庫或設定補值。
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Fingerprint_ChangesWithSourceAndVersions(bool geo)
    {
        IProcessingStepInput Create(string content = "", long metadataId = 11, string fingerprint = "",
            string metadataRule = "metadata-v1", string rule = "rule-v1", string workflow = "workflow-mvp-v1") => geo
            ? new GeoLookupInput(7, content == "" ? Content : content, workflow, rule, metadataId,
                fingerprint == "" ? Metadata : fingerprint, metadataRule, "Missing", null, null)
            : new QualityAnalysisInput(7, content == "" ? Content : content, 1024, workflow, rule, metadataId,
                fingerprint == "" ? Metadata : fingerprint, metadataRule);
        var original = Create().Identity.InputFingerprint;
        foreach (var changed in new[]
        {
            Create(content: new string('c', 64)), Create(metadataId: 12),
            Create(fingerprint: new string('d', 64)), Create(metadataRule: "metadata-v2"),
            Create(rule: "rule-v2"), Create(workflow: "workflow-test-v2")
        })
            Assert.NotEqual(original, changed.Identity.InputFingerprint);
    }

    // 測試目的：確認合法來源邊界可保存且公開表面不能改寫。
    // 測試情境：使用最大 ID、50字元版本、零或最大長度，並修改取得的 byte 副本。
    // 預期結果：屬性保留原值且無 setter，原指紋不變，負長度拒絕。
    [Theory]
    [InlineData(0L)]
    [InlineData(long.MaxValue)]
    public void Inputs_PreserveImmutableSourceAndLengthBoundaries(long length)
    {
        var version = new string('v', 50);
        var quality = new QualityAnalysisInput(long.MaxValue, Content, length, version, version, long.MaxValue, Metadata, version);
        var geo = new GeoLookupInput(long.MaxValue, Content, version, version, long.MaxValue, Metadata, version, "Missing", null, null);
        Assert.Equal(length, quality.ContentLength);
        Assert.Equal(long.MaxValue, quality.MetadataResultId);
        Assert.Equal(long.MaxValue, geo.MetadataResultId);
        Assert.Equal(Metadata, quality.MetadataInputFingerprint);
        Assert.Equal(Metadata, geo.MetadataInputFingerprint);
        Assert.Equal(version, quality.MetadataRuleVersion);
        Assert.Equal(version, geo.MetadataRuleVersion);
        foreach (var input in new IProcessingStepInput[] { quality, geo })
        {
            Assert.True(input.GetType().IsSealed);
            Assert.All(input.GetType().GetProperties(), property => Assert.Null(property.SetMethod));
            var copy = input.ToCanonicalInput().ToUtf8Bytes();
            copy[0] = 0;
            Assert.Equal((byte)'{', input.ToCanonicalInput().ToUtf8Bytes()[0]);
            Assert.Equal(input.Identity.InputFingerprint, InputFingerprint.Compute(input.ToCanonicalInput()));
        }
        Assert.Equal("contentLength", Assert.Throws<ArgumentOutOfRangeException>(() =>
            new QualityAnalysisInput(7, Content, -1, version, version, 11, Metadata, version)).ParamName);
    }
}
