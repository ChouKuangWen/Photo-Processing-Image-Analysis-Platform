using System.Security.Cryptography;
using System.Text;
using PhotoPlatform.Application.Processing;
using PhotoPlatform.Infrastructure.Processing;

namespace PhotoPlatform.UnitTests.Infrastructure;

public sealed class RuleManifestParserTests
{
    // Deliberately test-only identities; not published or executable module versions.
    private const string Document = """
        {"formatVersion":1,"manifestId":"test-only","workflow":"DuplicateDetection","workflowVersion":"workflow-mvp-v1","steps":[
        {"step":"SHA256_CALCULATION","stepOrder":1,"ruleVersion":"test-only-sha","resources":[{"kind":"Rule","id":"test-only-rule","version":"test-only-sha","checksum":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}]},
        {"step":"EXACT_DUPLICATE_CHECK","stepOrder":2,"ruleVersion":"test-only-exact","resources":[]}]}
        """;

    [Fact]
    public void Parse_PreservesRawByteIdentityWithoutGrantingExecution()
    {
        var first = Parse(Document);
        var second = Parse(Document + "\n");
        Assert.Equal("test-only", first.ManifestId);
        Assert.Equal(2, first.Steps.Count);
        Assert.Equal("test-only-sha", first.Steps[0].RuleVersion);
        Assert.NotEqual(first.Checksum, second.Checksum);
        Assert.Throws<NotSupportedException>(() => ((IList<ManifestStep>)first.Steps).Clear());
    }

    [Theory]
    [InlineData("\"formatVersion\":1", "\"formatVersion\":1,\"formatVersion\":1")]
    [InlineData("\"formatVersion\":1", "\"formatVersion\":\"1\"")]
    [InlineData("\"manifestId\":\"test-only\"", "\"manifestId\":null")]
    [InlineData("\"stepOrder\":1", "\"stepOrder\":2")]
    [InlineData("\"ruleVersion\":\"test-only-sha\"", "\"ruleVersion\":\"latest\"")]
    [InlineData("\"resources\":[", "\"unknown\":true,\"resources\":[")]
    [InlineData("\"id\":\"test-only-rule\"", "\"resourceId\":\"test-only-rule\"")]
    [InlineData("\"kind\":\"Rule\"", "\"role\":\"Rule\"")]
    [InlineData("\"version\":\"test-only-sha\"", "\"version\":null")]
    [InlineData("aaaaaaaa", "AAAAAAAA")]
    [InlineData("\"ruleVersion\":\"test-only-sha\"", "\"ruleVersion\":\" \"")]
    [InlineData("\"kind\":\"Rule\"", "\"kind\":\"Unexpected\"")]
    public void Parse_InvalidStructureIsSanitized(string oldValue, string newValue)
    {
        var error = Assert.Throws<FrozenInputUnavailableException>(() => Parse(Document.Replace(oldValue, newValue)));
        Assert.Equal(FrozenInputFailureReason.Mismatch, error.Reason);
        Assert.Equal("Frozen input is unavailable.", error.Message);
        Assert.Null(error.InnerException);
    }

    [Theory]
    [InlineData("\"formatVersion\":1", "\"formatVersion\":2")]
    [InlineData("workflow-mvp-v1", "workflow-unknown")]
    [InlineData("DuplicateDetection", "Unapproved")]
    public void Parse_UnknownVersionsAreUnavailable(string oldValue, string newValue)
    {
        var error = Assert.Throws<FrozenInputUnavailableException>(() => Parse(Document.Replace(oldValue, newValue)));
        Assert.Equal(FrozenInputFailureReason.VersionUnavailable, error.Reason);
    }

    [Fact]
    public void Parse_RejectsBomInvalidUtf8AndChecksumMismatch()
    {
        var bytes = Encoding.UTF8.GetBytes(Document);
        Reject([0xef, 0xbb, 0xbf, .. bytes]);
        Reject([0xff, .. bytes]);
        Assert.Throws<FrozenInputUnavailableException>(() =>
            RuleManifestParser.Parse(1, bytes, "test-only", new string('0', 64), default));
        Assert.Throws<FrozenInputUnavailableException>(() =>
            RuleManifestParser.Parse(1, bytes, "other-id", Checksum(bytes), default));
    }

    [Fact]
    public void Parse_CancellationPreservesToken()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var error = Assert.Throws<OperationCanceledException>(() =>
            RuleManifestParser.Parse(1, ReadOnlyMemory<byte>.Empty, "test-only", new string('0', 64), cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
    }

    private static ResolvedRuleManifest Parse(string document)
    {
        var bytes = Encoding.UTF8.GetBytes(document);
        return RuleManifestParser.Parse(1, bytes, "test-only", Checksum(bytes), default);
    }
    private static void Reject(byte[] bytes) => Assert.Throws<FrozenInputUnavailableException>(() =>
        RuleManifestParser.Parse(1, bytes, "test-only", Checksum(bytes), default));
    private static string Checksum(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
