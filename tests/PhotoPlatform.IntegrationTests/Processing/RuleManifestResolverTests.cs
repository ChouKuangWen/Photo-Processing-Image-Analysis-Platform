using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PhotoPlatform.Application.Processing;
using PhotoPlatform.Domain.Entities;
using PhotoPlatform.Domain.Enums;
using PhotoPlatform.Infrastructure.Processing;
using PhotoPlatform.Infrastructure.Persistence;
using PhotoPlatform.IntegrationTests.Fixtures;

namespace PhotoPlatform.IntegrationTests.Processing;

public sealed class RuleManifestResolverTests : IAsyncLifetime
{
    private readonly ProcessingSchemaFixture fixture = new();
    public Task InitializeAsync() => fixture.InitializeAsync();
    public Task DisposeAsync() => fixture.DisposeAsync();

    [Theory]
    [InlineData("legacy", FrozenInputFailureReason.VersionUnavailable)]
    [InlineData("missing-root", FrozenInputFailureReason.Missing)]
    [InlineData("bad-root", FrozenInputFailureReason.Mismatch)]
    [InlineData("missing-child", FrozenInputFailureReason.Missing)]
    [InlineData("bad-child", FrozenInputFailureReason.Mismatch)]
    [InlineData("unsupported-runtime", FrozenInputFailureReason.VersionUnavailable)]
    [InlineData("workflow-mismatch", FrozenInputFailureReason.Mismatch)]
    [InlineData("binding-version-mismatch", FrozenInputFailureReason.Mismatch)]
    public async Task Resolve_UnavailableOrUnapprovedSourcesNeverWriteOrGrantExecution(string scenario, FrozenInputFailureReason expected)
    {
        await using var db = fixture.CreateContext();
        await db.Database.MigrateAsync();
        var batch = new Batch(Guid.NewGuid(), 1, DateTimeOffset.UtcNow);
        db.Batches.Add(batch);
        var image = new Image(batch.Id, "test-only.jpg", "original/test-only", 3, "image/jpeg", DateTimeOffset.UtcNow);
        db.Images.Add(image);
        await db.SaveChangesAsync();
        var job = new ProcessingJob(image.Id, batch.Id, scenario == "workflow-mismatch" ? WorkflowType.Naming :
            WorkflowType.DuplicateDetection, DateTimeOffset.UtcNow);
        db.ProcessingJobs.Add(job);
        await db.SaveChangesAsync();

        byte[] child = [1, 2, 3]; // Not a formal publication or child schema.
        var reference = new ResourceReference("Rule", "test-only-rule", "test-only-version", Hash(child));
        var root = JsonSerializer.SerializeToUtf8Bytes(new
        {
            formatVersion = 1, manifestId = "test-only-old", workflow = "DuplicateDetection", workflowVersion = "workflow-mvp-v1",
            steps = new[] { "SHA256_CALCULATION", "EXACT_DUPLICATE_CHECK" }.Select((step, index) => new
            {
                step, stepOrder = index + 1, ruleVersion = "test-only-version",
                resources = new[] { new { kind = reference.Kind, id = reference.Id, version = reference.Version, checksum = reference.Checksum } }
            })
        });
        if (scenario != "legacy")
        {
            db.ProcessingRuleManifestBindings.Add(new(job.Id, scenario == "binding-version-mismatch" ? "test-other" :
                "workflow-mvp-v1", "test-only-old", Hash(root)));
            await db.SaveChangesAsync();
        }
        var roots = new Dictionary<string, byte[]>();
        if (scenario != "missing-root") roots.Add("test-only-old", scenario == "bad-root" ? [0] : root);
        var resources = new Dictionary<ResourceReference, byte[]>();
        if (scenario != "missing-child") resources.Add(reference, scenario == "bad-child" ? [0] : child);
        var deployment = new RuleManifestDeployment(new Dictionary<WorkflowType, RootManifestReference>
        {
            // The new selection must not override an existing old binding.
            [job.Workflow] = new("test-only-new", new string('0', 64))
        }, roots, resources);
        // Caller mutation cannot replace historical bytes after the deployment snapshot was built.
        Array.Fill(root, (byte)0);
        Array.Fill(child, (byte)0);
        db.ChangeTracker.Clear();
        var error = await Assert.ThrowsAsync<FrozenInputUnavailableException>(() =>
            new RuleManifestResolver(db, deployment).ResolveAsync(job.Id, default));
        Assert.Equal(expected, error.Reason);
        Assert.Null(error.InnerException);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Equal(ProcessingJobStatus.Pending, (await db.ProcessingJobs.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(scenario == "legacy" ? 0 : 1, await db.ProcessingRuleManifestBindings.CountAsync());
        Assert.Empty(await db.ProcessingOriginalInputs.ToListAsync());
        Assert.Empty(await db.ProcessingRuns.ToListAsync());
        Assert.Empty(await db.ProcessingLogs.ToListAsync());
    }

    [Fact]
    public async Task Resolve_CancellationPreservesTokenBeforeAnyDatabaseRead()
    {
        await using var db = fixture.CreateContext();
        var deployment = new RuleManifestDeployment(new Dictionary<WorkflowType, RootManifestReference>(),
            new Dictionary<string, byte[]>(), new Dictionary<ResourceReference, byte[]>());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new RuleManifestResolver(db, deployment).ResolveAsync(1, cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
    }
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    [Theory]
    [InlineData("valid")]
    [InlineData("unregistered-rule")]
    [InlineData("unregistered-step")]
    [InlineData("duplicate-implementation")]
    [InlineData("wrong-kind")]
    [InlineData("wrong-version")]
    [InlineData("wrong-id")]
    [InlineData("missing-Provider")]
    [InlineData("missing-Rule")]
    [InlineData("missing-Profile")]
    [InlineData("missing-Engine")]
    [InlineData("missing-TargetPolicy")]
    [InlineData("bad-checksum")]
    [InlineData("missing-child")]
    [InlineData("incompatible-child")]
    [InlineData("wrong-workflow")]
    [InlineData("wrong-order")]
    [InlineData("cancel-in-validation")]
    public async Task Resolve_ChecksDeliveredImplementationAndEveryDependencyWithoutGrantingClaim(string scenario)
    {
        await using var db = fixture.CreateContext();
        await db.Database.MigrateAsync();
        var batch = new Batch(Guid.NewGuid(), 1, DateTimeOffset.UtcNow);
        db.Batches.Add(batch);
        var image = new Image(batch.Id, "fixture.jpg", "original/fixture", 3, "image/jpeg", DateTimeOffset.UtcNow);
        db.Images.Add(image);
        await db.SaveChangesAsync();
        var job = new ProcessingJob(image.Id, batch.Id, WorkflowType.DuplicateDetection, DateTimeOffset.UtcNow);
        db.ProcessingJobs.Add(job);
        await db.SaveChangesAsync();

        // These schemas and implementations exist ONLY in this test assembly. They do not publish
        // production versions or imply that DuplicateDetection needs all these resource kinds.
        byte[] child = JsonSerializer.SerializeToUtf8Bytes(new { implementation = "fixture-only", revision = 1 });
        var required = new[] { "Rule", "Provider", "Profile", "Engine", "TargetPolicy" }
            .Select(kind => new ResourceReference(kind, "fixture-" + kind, "fixture-v1", Hash(child))).ToArray();
        var references = required.Where(reference => scenario != "missing-" + reference.Kind).ToArray();
        if (scenario == "wrong-kind") references[0] = references[0] with { Kind = "Provider" };
        if (scenario == "wrong-version") references[0] = references[0] with { Version = "fixture-v2" };
        if (scenario == "wrong-id") references[0] = references[0] with { Id = "other-rule" };
        if (scenario == "incompatible-child")
        {
            child = JsonSerializer.SerializeToUtf8Bytes(new { implementation = "fixture-only", revision = 2 });
            references = references.Select(reference => reference with { Checksum = Hash(child) }).ToArray();
            required = references.ToArray(); // Approved byte identity alone must NOT bypass semantic checking.
        }
        var root = JsonSerializer.SerializeToUtf8Bytes(new
        {
            formatVersion = 1, manifestId = "fixture-root", workflow = scenario == "wrong-workflow" ? "Naming" : "DuplicateDetection",
            workflowVersion = "workflow-mvp-v1",
            steps = new[] { "SHA256_CALCULATION", "EXACT_DUPLICATE_CHECK" }.Select((step, index) => new
            {
                step, stepOrder = scenario == "wrong-order" ? 2 - index : index + 1,
                ruleVersion = scenario == "unregistered-rule" ? "fixture-v2" : "fixture-v1",
                resources = references.Select(reference => new { kind = reference.Kind, id = reference.Id, version = reference.Version, checksum = reference.Checksum })
            })
        });
        db.ProcessingRuleManifestBindings.Add(new(job.Id, "workflow-mvp-v1", "fixture-root", Hash(root)));
        await db.SaveChangesAsync();
        var resources = references.ToDictionary(reference => reference, _ => child);
        if (scenario == "bad-checksum") resources[references[0]] = [0];
        if (scenario == "missing-child") resources.Remove(references[0]);
        var deployment = new RuleManifestDeployment(new Dictionary<WorkflowType, RootManifestReference>(),
            new Dictionary<string, byte[]> { ["fixture-root"] = root }, resources);
        using var cancellation = new CancellationTokenSource();
        var capabilities = new[] { "SHA256_CALCULATION", "EXACT_DUPLICATE_CHECK" }
            .Select(step => new FixtureImplementation(step, required,
                scenario == "cancel-in-validation" ? cancellation : null)).ToList();
        if (scenario == "unregistered-step") capabilities.RemoveAt(1);
        if (scenario == "duplicate-implementation") capabilities.Add(capabilities[0]);
        db.ChangeTracker.Clear();
        var resolver = new RuleManifestResolver(db, deployment, capabilities);
        if (scenario == "valid")
        {
            var result = await resolver.ResolveAsync(job.Id, cancellation.Token);
            Assert.Equal("fixture-root", result.ManifestId);
            Assert.Equal(Hash(root), result.Checksum);
            Assert.Equal(2, result.Steps.Count);
            Assert.All(capabilities, capability => Assert.Equal(1, capability.Validations));
            // The public production constructor has NO fixture capabilities, even for the same bytes.
            var denied = await Assert.ThrowsAsync<FrozenInputUnavailableException>(() =>
                new RuleManifestResolver(db, deployment).ResolveAsync(job.Id, default));
            Assert.Equal(FrozenInputFailureReason.VersionUnavailable, denied.Reason);
        }
        else if (scenario == "cancel-in-validation")
        {
            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => resolver.ResolveAsync(job.Id, cancellation.Token));
            Assert.Equal(cancellation.Token, error.CancellationToken);
        }
        else
        {
            var error = await Assert.ThrowsAsync<FrozenInputUnavailableException>(() => resolver.ResolveAsync(job.Id, default));
            Assert.Equal(scenario switch
            {
                "bad-checksum" or "wrong-workflow" or "wrong-order" => FrozenInputFailureReason.Mismatch,
                "missing-child" => FrozenInputFailureReason.Missing,
                _ => FrozenInputFailureReason.VersionUnavailable
            }, error.Reason);
        }
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Equal(ProcessingJobStatus.Pending, (await db.ProcessingJobs.AsNoTracking().SingleAsync()).Status);
        Assert.Empty(await db.ProcessingOriginalInputs.ToListAsync());
        Assert.Empty(await db.ProcessingRuns.ToListAsync());
        Assert.Empty(await db.ProcessingStepCheckpoints.ToListAsync());
        Assert.Empty(await db.ProcessingLogs.ToListAsync());
        var factory = new RejectingFactory();
        var calls = 0;
        var coordinator = new ProcessingInvocationCoordinator(new ProcessingExecutionStore(factory));
        Assert.False(await coordinator.ExecuteAsync(null, true, (_, _) => { calls++; return Task.CompletedTask; }, default));
        Assert.Equal(0, factory.Calls);
        Assert.Equal(0, calls);
    }

    private sealed class RejectingFactory : IDbContextFactory<PhotoPlatformDbContext>
    {
        internal int Calls { get; private set; }
        public PhotoPlatformDbContext CreateDbContext()
        {
            Calls++;
            throw new InvalidOperationException("Resolution alone cannot authorize T1.");
        }
    }

    private sealed class FixtureImplementation(string step, IReadOnlyList<ResourceReference> required,
        CancellationTokenSource? cancellation) : IManifestStepImplementation
    {
        public string Workflow => "DuplicateDetection";
        public string WorkflowVersion => "workflow-mvp-v1";
        public string Step => step;
        public string RuleVersion => "fixture-v1";
        public IReadOnlyList<ResourceReference> RequiredResources => required;
        internal int Validations { get; private set; }

        public void ValidateCompatibility(long jobId, ResolvedRuleManifest manifest, ManifestStep manifestStep,
            IReadOnlyDictionary<ResourceReference, ReadOnlyMemory<byte>> resources, CancellationToken token)
        {
            cancellation?.Cancel();
            token.ThrowIfCancellationRequested();
            foreach (var reference in required)
            {
                using var document = JsonDocument.Parse(resources[reference]);
                // The delivered fixture implementation only understands revision 1. Revision 2
                // with a perfectly matching checksum must still be rejected by module code.
                if (document.RootElement.GetProperty("implementation").GetString() != "fixture-only"
                    || document.RootElement.GetProperty("revision").GetInt32() != 1)
                    throw new FrozenInputUnavailableException(jobId, FrozenInputFailureReason.VersionUnavailable);
            }
            Validations++;
        }
    }
}
