using System.Text;
using PhotoPlatform.Application.Processing;

namespace PhotoPlatform.UnitTests.Processing;

public sealed class OriginalStepInputTests
{
    private static readonly string Content = new('a', 64);
    private static readonly DateTimeOffset Uploaded = new(2026, 10, 26, 8, 0, 0, TimeSpan.FromHours(8));

    // 測試目的：確認具名輸入使用精確的欄位、字元組與指紋。
    // 測試情境：以固定原圖資料建立兩種輸入，比對獨立字串及預先計算的 SHA256。
    // 預期結果：完整 bytes 與固定 hash 相符，Identity 也指向相同業務輸入。
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Canonical_MatchesIndependentGoldenVectors(bool exif)
    {
        IProcessingStepInput input = exif
            ? new ExifParsingInput(7, Content, 1024, "workflow-mvp-v1", "rule-v1", Uploaded)
            : new Sha256CalculationInput(7, Content, 1024, "workflow-mvp-v1", "rule-v1");
        const string exifJson = """{"contentLength":1024,"contentVersion":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","imageId":7,"ruleVersion":"rule-v1","schemaVersion":1,"step":"EXIF_PARSING","uploadCreatedAt":"2026-10-26T00:00:00.0000000+00:00","workflowVersion":"workflow-mvp-v1"}""";
        const string shaJson = """{"contentLength":1024,"contentVersion":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","imageId":7,"ruleVersion":"rule-v1","schemaVersion":1,"step":"SHA256_CALCULATION","workflowVersion":"workflow-mvp-v1"}""";
        var expectedHash = exif
            ? "81ca2c3467694fb6cc965efb793854c2dc8e111add51d6b2b7e97849e22450b8"
            : "6b0262d10abe520f7b49766e7c0362d51453339423f4da44a67685e1994ff5e5";
        Assert.Equal(Encoding.UTF8.GetBytes(exif ? exifJson : shaJson), input.ToCanonicalInput().ToUtf8Bytes());
        Assert.Equal(expectedHash, input.Identity.InputFingerprint);
        Assert.Equal(expectedHash, InputFingerprint.Compute(input.ToCanonicalInput()));
        Assert.Equal(exif ? "EXIF_PARSING" : "SHA256_CALCULATION", input.Identity.Step);
        Assert.Equal(7, input.Identity.ImageId);
        Assert.Equal(Content, input.Identity.ContentVersion);
        Assert.Equal("workflow-mvp-v1", input.Identity.WorkflowVersion);
        Assert.Equal("rule-v1", input.Identity.RuleVersion);
    }

    // 測試目的：確認 EXIF 業務時間會影響指紋，但相同瞬間不受 offset 表示影響。
    // 測試情境：使用同一時間的 UTC 表示，並另外增加一個 tick。
    // 預期結果：相同瞬間指紋一致，真正改時間時指紋不同，SHA 沒有時間欄位。
    [Fact]
    public void UploadTime_IsBusinessInputOnlyForExif()
    {
        var original = new ExifParsingInput(7, Content, 0, "workflow-mvp-v1", "rule-v1", Uploaded);
        var utc = new ExifParsingInput(7, Content, 0, "workflow-mvp-v1", "rule-v1", Uploaded.ToUniversalTime());
        var later = new ExifParsingInput(7, Content, 0, "workflow-mvp-v1", "rule-v1", Uploaded.AddTicks(1));
        Assert.Equal(Uploaded, original.UploadCreatedAt);
        Assert.Equal(original.Identity.InputFingerprint, utc.Identity.InputFingerprint);
        Assert.NotEqual(original.Identity.InputFingerprint, later.Identity.InputFingerprint);
        var sha = new Sha256CalculationInput(7, Content, 0, "workflow-mvp-v1", "rule-v1");
        Assert.DoesNotContain("uploadCreatedAt", Encoding.UTF8.GetString(sha.ToCanonicalInput().ToUtf8Bytes()));
    }

    // 測試目的：確認兩個輸入都拒絕非法身分、版本及內容長度。
    // 測試情境：逐項傳入空值、超長版本、非小寫 hash、錯誤 hash 長度與負數。
    // 預期結果：每項無效參數都拋出參數例外，不能建立有效輸入。
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Construction_RejectsInvalidFields(bool exif)
    {
        IProcessingStepInput Create(long imageId, string hash, long length, string workflow, string rule) =>
            exif ? new ExifParsingInput(imageId, hash, length, workflow, rule, Uploaded)
                : new Sha256CalculationInput(imageId, hash, length, workflow, rule);
        foreach (var imageId in new[] { 0L, -1L })
            Assert.Throws<ArgumentOutOfRangeException>(() => Create(imageId, Content, 0, "workflow-mvp-v1", "v1"));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(1, Content, -1, "workflow-mvp-v1", "v1"));
        foreach (var hash in new[] { null, "", new string('a', 63), new string('a', 65), new string('A', 64), new string('g', 64) })
            Assert.ThrowsAny<ArgumentException>(() => Create(1, hash!, 0, "workflow-mvp-v1", "v1"));
        foreach (var version in new[] { null, "", " ", new string('v', 51) })
        {
            Assert.ThrowsAny<ArgumentException>(() => Create(1, Content, 0, version!, "v1"));
            Assert.ThrowsAny<ArgumentException>(() => Create(1, Content, 0, "workflow-mvp-v1", version!));
        }
    }

    // 測試目的：確認合法數值邊界可保存，外部不能改寫已建立的輸入。
    // 測試情境：建立零及最大長度輸入，檢查屬性並修改取回的 byte 副本。
    // 預期結果：數值保留、沒有 setter，修改副本不影響原指紋。
    [Theory]
    [InlineData(0L)]
    [InlineData(long.MaxValue)]
    public void Inputs_PreserveBoundariesAndImmutableSurface(long length)
    {
        var exif = new ExifParsingInput(long.MaxValue, Content, length, new string('w', 50), new string('r', 50), Uploaded);
        var sha = new Sha256CalculationInput(long.MaxValue, Content, length, new string('w', 50), new string('r', 50));
        Assert.Equal(length, exif.ContentLength);
        Assert.Equal(length, sha.ContentLength);
        foreach (var input in new IProcessingStepInput[] { exif, sha })
        {
            Assert.True(input.GetType().IsSealed);
            Assert.All(input.GetType().GetProperties(), property => Assert.Null(property.SetMethod));
            var bytes = input.ToCanonicalInput().ToUtf8Bytes();
            bytes[0] = 0;
            Assert.Equal((byte)'{', input.ToCanonicalInput().ToUtf8Bytes()[0]);
            Assert.Equal(input.Identity.InputFingerprint, InputFingerprint.Compute(input.ToCanonicalInput()));
        }
    }
}
