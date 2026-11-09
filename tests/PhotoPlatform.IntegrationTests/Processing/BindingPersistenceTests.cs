using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PhotoPlatform.Domain.Entities;
using PhotoPlatform.Domain.Enums;
using PhotoPlatform.Infrastructure.Persistence;
using PhotoPlatform.IntegrationTests.Fixtures;

namespace PhotoPlatform.IntegrationTests.Processing;

// Persistence foundation only; these tests do not claim T1 execution or Manifest approval.
public sealed class BindingPersistenceTests : IAsyncLifetime
{
    private readonly ProcessingSchemaFixture fixture = new();
    private static readonly DateTimeOffset Time = new(2026, 11, 9, 0, 0, 0, TimeSpan.Zero);
    private static readonly string Hash = new('a', 64);
    public Task InitializeAsync() => fixture.InitializeAsync();
    public Task DisposeAsync() => fixture.DisposeAsync();

    [Fact]
    public async Task UpgradeFromExecution_PreservesUploadCatalogAndDoesNotBackfill()
    {
        await using var db = fixture.CreateContext();
        await db.GetService<IMigrator>().MigrateAsync("20261026064936_AddProcessingExecutionPersistence");
        var job = await SeedJobAsync(db);
        var before = await UploadColumnsAsync(db);
        await db.Database.MigrateAsync();
        Assert.Equal(before, await UploadColumnsAsync(db));
        Assert.Equal(job, (await db.ProcessingJobs.SingleAsync()).Id);
        Assert.Empty(await db.ProcessingRuleManifestBindings.ToListAsync());
        Assert.Empty(await db.ProcessingOriginalInputs.ToListAsync());
        Assert.Empty(await db.ProcessingOriginalStepInputs.ToListAsync());
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Equal(3, (await db.Database.GetAppliedMigrationsAsync()).Count());

        var columns = await db.Database.SqlQueryRaw<string>("""
            SELECT CONCAT(t.name,'|',c.name,'|',ty.name,'|',c.max_length,'|',c.is_nullable,'|',c.is_identity) AS [Value]
            FROM sys.tables t JOIN sys.columns c ON c.object_id=t.object_id
            JOIN sys.types ty ON ty.user_type_id=c.user_type_id
            WHERE t.name IN ('processing_rule_manifest_bindings','processing_original_inputs','processing_original_step_inputs')
            """).ToListAsync();
        Assert.Equal(13, columns.Count);
        Assert.Contains("processing_original_inputs|ContentVersion|char|64|0|0", columns);
        Assert.Contains("processing_original_step_inputs|UploadCreatedAt|datetimeoffset|10|1|0", columns);
        var foreignKeys = await db.Database.SqlQueryRaw<int>("""
            SELECT CONVERT(int, fk.delete_referential_action) AS [Value] FROM sys.foreign_keys fk
            JOIN sys.tables t ON t.object_id=fk.parent_object_id
            WHERE t.name IN ('processing_rule_manifest_bindings','processing_original_inputs','processing_original_step_inputs')
            """).ToListAsync();
        Assert.Equal(3, foreignKeys.Count);
        Assert.All(foreignKeys, action => Assert.Equal(0, action));
    }

    [Fact]
    public async Task Bindings_RoundTripAllowSharedManifestAndEnforceAllPrimaryAndForeignKeys()
    {
        await using var db = fixture.CreateContext();
        await db.Database.MigrateAsync();
        var first = await SeedJobAsync(db);
        var second = await SeedJobAsync(db);
        AddBindings(db, first);
        AddBindings(db, second);
        await db.SaveChangesAsync();
        await using var read = fixture.CreateContext();
        Assert.Equal(2, await read.ProcessingRuleManifestBindings.CountAsync(x => x.ManifestId == "shared-manifest"));
        Assert.Equal(4, await read.ProcessingOriginalStepInputs.CountAsync());
        Assert.Equal(Time, (await read.ProcessingOriginalStepInputs.FirstAsync(x => x.Step == "EXIF_PARSING")).UploadCreatedAt);
        Assert.Null((await read.ProcessingOriginalStepInputs.FirstAsync(x => x.Step == "SHA256_CALCULATION")).UploadCreatedAt);

        await RejectAsync(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO processing_rule_manifest_bindings VALUES ({first}, 'workflow-mvp-v1', 'shared-manifest', {Hash})"), 2627);
        await RejectAsync(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO processing_original_inputs VALUES ({first}, {Hash}, 3, 'workflow-mvp-v1')"), 2627);
        await RejectAsync(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO processing_original_step_inputs VALUES ({first}, 'SHA256_CALCULATION', 'test-rule', NULL, {Hash})"), 2627);
        await RejectAsync(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO processing_rule_manifest_bindings VALUES ({long.MaxValue}, 'workflow-mvp-v1', 'm', {Hash})"));
        await RejectAsync(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO processing_original_inputs VALUES ({long.MaxValue}, {Hash}, 3, 'workflow-mvp-v1')"));
        var withoutParent = await SeedJobAsync(db);
        await RejectAsync(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO processing_original_step_inputs VALUES ({withoutParent}, 'SHA256_CALCULATION', 'test-rule', NULL, {Hash})"));
    }

    [Fact]
    public async Task SqlChecks_RejectNegativeLengthWhitespaceVersionsAndEveryInvalidHashColumn()
    {
        await using var db = fixture.CreateContext();
        await db.Database.MigrateAsync();
        var job = await SeedJobAsync(db);
        AddBindings(db, job);
        await db.SaveChangesAsync();
        await RejectAsync(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE processing_original_inputs SET ContentLength = {-1} WHERE JobId = {job}"));
        foreach (var invalid in new[] { "", new string('a', 63), new string('A', 64),
                     new string('a', 63) + " ", new string('a', 63) + "g", new string('a', 63) + "ａ" })
        {
            await RejectAsync(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE processing_original_inputs SET ContentVersion = {invalid} WHERE JobId = {job}"));
            await RejectAsync(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE processing_rule_manifest_bindings SET ManifestChecksum = {invalid} WHERE JobId = {job}"));
            await RejectAsync(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE processing_original_step_inputs SET InputFingerprint = {invalid} WHERE JobId = {job}"));
        }
        foreach (var blank in new[] { "", " ", "\t\r\n", " \t ", "\u00a0" })
        {
            await RejectAsync(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE processing_original_inputs SET WorkflowVersion = {blank} WHERE JobId = {job}"));
            await RejectAsync(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE processing_rule_manifest_bindings SET WorkflowVersion = {blank} WHERE JobId = {job}"));
            await RejectAsync(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE processing_rule_manifest_bindings SET ManifestId = {blank} WHERE JobId = {job}"));
            await RejectAsync(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE processing_original_step_inputs SET RuleVersion = {blank} WHERE JobId = {job}"));
        }
        Assert.Equal(Hash, (await db.ProcessingOriginalInputs.AsNoTracking().SingleAsync()).ContentVersion);
    }

    [Fact]
    public async Task SqlChecks_RejectNullCombinationsUnknownCaseAndPaddedStep()
    {
        await using var db = fixture.CreateContext();
        await db.Database.MigrateAsync();
        var job = await SeedJobAsync(db);
        db.ProcessingOriginalInputs.Add(new(job, Hash, 3, "workflow-mvp-v1"));
        await db.SaveChangesAsync();
        foreach (var step in new[] { "EXIF_PARSING", "exif_parsing", "EXIF_PARSING ", "OTHER" })
            await RejectAsync(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO processing_original_step_inputs VALUES ({job}, {step}, 'test-rule', NULL, {Hash})"));
        await RejectAsync(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO processing_original_step_inputs VALUES ({job}, 'SHA256_CALCULATION', 'test-rule', {Time}, {Hash})"));
        await RejectAsync(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO processing_original_step_inputs VALUES ({job}, NULL, 'test-rule', NULL, {Hash})"), 515);
        await RejectAsync(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO processing_rule_manifest_bindings VALUES ({job}, 'workflow-mvp-v1', 'm', NULL)"), 515);
    }

    [Fact]
    public async Task TransactionRollbackAndRestrictedDelete_LeaveNoOrphanBindings()
    {
        await using var db = fixture.CreateContext();
        await db.Database.MigrateAsync();
        var job = await SeedJobAsync(db);
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            AddBindings(db, job);
            await db.SaveChangesAsync();
            await transaction.RollbackAsync();
        }
        db.ChangeTracker.Clear();
        Assert.Empty(await db.ProcessingRuleManifestBindings.ToListAsync());
        Assert.Empty(await db.ProcessingOriginalInputs.ToListAsync());
        Assert.Empty(await db.ProcessingOriginalStepInputs.ToListAsync());
        AddBindings(db, job);
        await db.SaveChangesAsync();
        await RejectAsync(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM processing_jobs WHERE Id = {job}"));
        await RejectAsync(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM processing_original_inputs WHERE JobId = {job}"));
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM processing_original_step_inputs WHERE JobId = {job}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM processing_original_inputs WHERE JobId = {job}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM processing_rule_manifest_bindings WHERE JobId = {job}");
            await transaction.CommitAsync();
        }
        Assert.Empty(await db.ProcessingOriginalStepInputs.AsNoTracking().ToListAsync());
        Assert.Empty(await db.ProcessingOriginalInputs.AsNoTracking().ToListAsync());
        Assert.Empty(await db.ProcessingRuleManifestBindings.AsNoTracking().ToListAsync());
        Assert.Single(await db.ProcessingJobs.ToListAsync());
    }

    private static void AddBindings(PhotoPlatformDbContext db, long job)
    {
        db.ProcessingRuleManifestBindings.Add(new(job, "workflow-mvp-v1", "shared-manifest", Hash));
        db.ProcessingOriginalInputs.Add(new(job, Hash, 3, "workflow-mvp-v1"));
        db.ProcessingOriginalStepInputs.Add(new(job, "EXIF_PARSING", "test-rule", Time, Hash));
        db.ProcessingOriginalStepInputs.Add(new(job, "SHA256_CALCULATION", "test-rule", null, Hash));
    }

    private static async Task<long> SeedJobAsync(PhotoPlatformDbContext db)
    {
        var batch = new Batch(Guid.NewGuid(), 1, Time);
        var image = new Image(batch.Id, "image.jpg", "original/550e8400e29b41d4a716446655440000", 3, "image/jpeg", Time);
        db.Batches.Add(batch);
        db.Images.Add(image);
        await db.SaveChangesAsync();
        var job = new ProcessingJob(image.Id, batch.Id, WorkflowType.Full, Time);
        db.ProcessingJobs.Add(job);
        await db.SaveChangesAsync();
        return job.Id;
    }

    private static async Task RejectAsync(Func<Task> command, int number = 547)
    {
        var exception = await Assert.ThrowsAsync<SqlException>(command);
        Assert.Contains(exception.Number, new[] { number, number == 547 ? 2628 : number });
    }

    private static async Task<string[]> UploadColumnsAsync(PhotoPlatformDbContext db) =>
        (await db.Database.SqlQueryRaw<string>("""
            SELECT CONCAT(t.name,'|',c.name,'|',ty.name,'|',c.max_length,'|',c.is_nullable,'|',c.is_identity) AS [Value]
            FROM sys.tables t JOIN sys.columns c ON t.object_id=c.object_id
            JOIN sys.types ty ON c.user_type_id=ty.user_type_id
            WHERE t.name IN ('batches','images','processing_jobs')
            """).ToListAsync()).Order().ToArray();
}
