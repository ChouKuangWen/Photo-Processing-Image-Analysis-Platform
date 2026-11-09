using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PhotoPlatform.Application.Processing;

namespace PhotoPlatform.Infrastructure.Processing;

// This parser checks published identity/structure, never approval or executable module capability.
internal static class RuleManifestParser
{
    internal static ResolvedRuleManifest Parse(long jobId, ReadOnlyMemory<byte> bytes, string expectedId,
        string expectedChecksum, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            Require(Hash(expectedChecksum));
            Require(Convert.ToHexStringLower(SHA256.HashData(bytes.Span)) == expectedChecksum);
            Require(!bytes.Span.StartsWith(new byte[] { 0xef, 0xbb, 0xbf }));
            _ = new UTF8Encoding(false, true).GetString(bytes.Span);
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
            var root = document.RootElement;
            Fields(root, "formatVersion", "manifestId", "workflow", "workflowVersion", "steps");
            if (Integer(root, "formatVersion") != 1)
                throw new FrozenInputUnavailableException(jobId, FrozenInputFailureReason.VersionUnavailable);
            var id = Text(root, "manifestId", 100);
            Require(id == expectedId);
            var workflow = Text(root, "workflow", 30);
            var workflowVersion = Text(root, "workflowVersion", 50);
            WorkflowDefinition definition;
            try { definition = new WorkflowDefinitionResolver().Resolve(workflow, workflowVersion); }
            catch (ArgumentException)
            { throw new FrozenInputUnavailableException(jobId, FrozenInputFailureReason.VersionUnavailable); }
            var steps = root.GetProperty("steps");
            Require(steps.ValueKind == JsonValueKind.Array && steps.GetArrayLength() == definition.Steps.Count);
            var parsed = new List<ManifestStep>();
            foreach (var step in steps.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                Fields(step, "step", "stepOrder", "ruleVersion", "resources");
                var name = Text(step, "step", 50);
                var order = Integer(step, "stepOrder");
                var expected = definition.Steps[parsed.Count];
                Require(name == expected.Step && order == expected.StepOrder);
                var rule = Text(step, "ruleVersion", 50);
                var resources = step.GetProperty("resources");
                Require(resources.ValueKind == JsonValueKind.Array);
                var references = new List<ResourceReference>();
                foreach (var resource in resources.EnumerateArray())
                {
                    Fields(resource, "kind", "id", "version", "checksum");
                    var kind = Text(resource, "kind", 50);
                    Require(kind is "Rule" or "Profile" or "Engine" or "Provider" or "TargetPolicy");
                    var reference = new ResourceReference(kind, Text(resource, "id", 100),
                        Text(resource, "version", 50), Text(resource, "checksum", 64));
                    Require(Hash(reference.Checksum));
                    Require(!references.Any(value => value.Kind == reference.Kind && value.Id == reference.Id));
                    references.Add(reference);
                }
                // Module-specific required resources/compatibility are checked by the resolver.
                parsed.Add(new ManifestStep(name, order, rule, references));
            }
            return new ResolvedRuleManifest(id, expectedChecksum, workflow, workflowVersion, parsed);
        }
        catch (Exception error) when (error is JsonException or DecoderFallbackException or
            InvalidDataException or InvalidOperationException or FormatException or OverflowException or ArgumentException)
        {
            throw new FrozenInputUnavailableException(jobId, FrozenInputFailureReason.Mismatch);
        }
    }

    private static void Fields(JsonElement element, params string[] expected)
    {
        Require(element.ValueKind == JsonValueKind.Object);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            Require(expected.Contains(property.Name, StringComparer.Ordinal) && names.Add(property.Name));
        Require(names.Count == expected.Length);
    }

    private static string Text(JsonElement element, string name, int maximum)
    {
        var value = element.GetProperty(name);
        Require(value.ValueKind == JsonValueKind.String);
        var text = value.GetString()!;
        Require(!string.IsNullOrWhiteSpace(text) && text.Length <= maximum && text == text.Trim());
        Require(text != "latest" && text != "default");
        return text;
    }

    private static int Integer(JsonElement element, string name)
    {
        var value = element.GetProperty(name);
        Require(value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out _));
        return value.GetInt32();
    }
    private static bool Hash(string value) => value.Length == 64 &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidDataException();
    }
}
