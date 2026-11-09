using Microsoft.EntityFrameworkCore;
using PhotoPlatform.Application.Processing;
using PhotoPlatform.Domain.Entities;
using PhotoPlatform.Domain.Enums;
using PhotoPlatform.Infrastructure.Persistence;
using PhotoPlatform.IntegrationTests.Fixtures;

namespace PhotoPlatform.IntegrationTests.Processing;

// Real SQL lock protocol tests only; test bindings are not T1/production rule publications.
public sealed class ManifestChecksumLockTests : IAsyncLifetime
{
    private readonly ProcessingSchemaFixture fixture = new();
    private static readonly string FirstHash = new('a', 64);
    private static readonly string OtherHash = new('b', 64);
    private long firstJob;
    private long secondJob;

    public async Task InitializeAsync()
    {
        await fixture.InitializeAsync();
        await using var db = fixture.CreateContext();
        await db.Database.MigrateAsync();
        var batch = new Batch(Guid.NewGuid(), 2, DateTimeOffset.UtcNow);
        db.Batches.Add(batch);
        var images = Enumerable.Range(0, 2).Select(index => new Image(batch.Id, $"{index}.jpg",
            $"original/test-only-{index}", 3, "image/jpeg", DateTimeOffset.UtcNow)).ToArray();
        db.Images.AddRange(images);
        await db.SaveChangesAsync();
        var jobs = images.Select(image => new ProcessingJob(image.Id, batch.Id,
            WorkflowType.DuplicateDetection, DateTimeOffset.UtcNow)).ToArray();
        db.ProcessingJobs.AddRange(jobs);
        await db.SaveChangesAsync();
        firstJob = jobs[0].Id;
        secondJob = jobs[1].Id;
    }
    public Task DisposeAsync() => fixture.DisposeAsync();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CompetingWriters_SerializeAndOnlyAcceptSameChecksum(bool sameChecksum)
    {
        await using var first = fixture.CreateContext();
        await using var firstTransaction = await first.Database.BeginTransactionAsync();
        await ManifestChecksumLock.VerifyAsync(first, firstJob, "test-only-shared", FirstHash, 0, default);
        first.ProcessingRuleManifestBindings.Add(new(firstJob, "workflow-mvp-v1", "test-only-shared", FirstHash));
        await first.SaveChangesAsync();

        await using var second = fixture.CreateContext();
        await using var secondTransaction = await second.Database.BeginTransactionAsync();
        var requestedHash = sameChecksum ? FirstHash : OtherHash;
        // Deterministic boundary: first has the lock and uncommitted binding; contender cannot pass it.
        var timeout = await Assert.ThrowsAsync<FrozenInputUnavailableException>(() =>
            ManifestChecksumLock.VerifyAsync(second, secondJob, "test-only-shared", requestedHash, 0, default));
        Assert.Equal(FrozenInputFailureReason.SourceUnavailable, timeout.Reason);
        await firstTransaction.CommitAsync();
        if (sameChecksum)
        {
            await ManifestChecksumLock.VerifyAsync(second, secondJob, "test-only-shared", requestedHash, 1000, default);
            second.ProcessingRuleManifestBindings.Add(new(secondJob, "workflow-mvp-v1", "test-only-shared", requestedHash));
            await second.SaveChangesAsync();
            await secondTransaction.CommitAsync();
        }
        else
        {
            var mismatch = await Assert.ThrowsAsync<FrozenInputUnavailableException>(() =>
                ManifestChecksumLock.VerifyAsync(second, secondJob, "test-only-shared", requestedHash, 1000, default));
            Assert.Equal(FrozenInputFailureReason.Mismatch, mismatch.Reason);
            await secondTransaction.RollbackAsync();
        }
        await using var read = fixture.CreateContext();
        Assert.Equal(sameChecksum ? 2 : 1, await read.ProcessingRuleManifestBindings.CountAsync());
        Assert.All(await read.ProcessingRuleManifestBindings.ToListAsync(), binding => Assert.Equal(FirstHash, binding.ManifestChecksum));
    }

    [Fact]
    public async Task Rollback_ReleasesLockAndDoesNotEstablishChecksumIdentity()
    {
        await using var first = fixture.CreateContext();
        await using var transaction = await first.Database.BeginTransactionAsync();
        await ManifestChecksumLock.VerifyAsync(first, firstJob, "test-only-shared", FirstHash, 0, default);
        first.ProcessingRuleManifestBindings.Add(new(firstJob, "workflow-mvp-v1", "test-only-shared", FirstHash));
        await first.SaveChangesAsync();
        await transaction.RollbackAsync();
        await using var second = fixture.CreateContext();
        await using var next = await second.Database.BeginTransactionAsync();
        await ManifestChecksumLock.VerifyAsync(second, secondJob, "test-only-shared", OtherHash, 0, default);
        second.ProcessingRuleManifestBindings.Add(new(secondJob, "workflow-mvp-v1", "test-only-shared", OtherHash));
        await second.SaveChangesAsync();
        await next.CommitAsync();
        await using var read = fixture.CreateContext();
        Assert.Equal(OtherHash, (await read.ProcessingRuleManifestBindings.SingleAsync()).ManifestChecksum);
    }

    [Fact]
    public async Task Cancellation_WhileWaitingPreservesTokenAndLeavesNoBinding()
    {
        await using var first = fixture.CreateContext();
        await using var firstTransaction = await first.Database.BeginTransactionAsync();
        await ManifestChecksumLock.VerifyAsync(first, firstJob, "test-only-shared", FirstHash, 0, default);
        await using var second = fixture.CreateContext();
        await using var secondTransaction = await second.Database.BeginTransactionAsync();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ManifestChecksumLock.VerifyAsync(second, secondJob, "test-only-shared", OtherHash, 10000, cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        await secondTransaction.RollbackAsync();
        await firstTransaction.RollbackAsync();
        await using var read = fixture.CreateContext();
        Assert.Empty(await read.ProcessingRuleManifestBindings.ToListAsync());
    }

    [Fact]
    public async Task MissingTransactionIsProgrammingError()
    {
        await using var db = fixture.CreateContext();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ManifestChecksumLock.VerifyAsync(db, firstJob, "test-only-shared", FirstHash, 0, default));
    }
}
