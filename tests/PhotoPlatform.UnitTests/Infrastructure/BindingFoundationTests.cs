using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using PhotoPlatform.Domain.Entities;
using PhotoPlatform.Infrastructure.Persistence;

namespace PhotoPlatform.UnitTests.Infrastructure;

public sealed class BindingFoundationTests
{
    [Fact]
    public void Migration_AddsOnlyThreeApprovedTablesWithNoActionForeignKeys()
    {
        using var db = new PhotoPlatformDbContext(new DbContextOptionsBuilder<PhotoPlatformDbContext>()
            .UseSqlServer("Server=127.0.0.1,1;Database=Offline;Integrated Security=true").Options);
        var assembly = db.GetService<IMigrationsAssembly>();
        var migration = assembly.CreateMigration(assembly.Migrations.Single(x =>
            x.Key.EndsWith("_AddProcessingInputBindings")).Value, db.Database.ProviderName!);
        var tables = migration.UpOperations.Select(Assert.IsType<CreateTableOperation>).ToArray();
        Assert.Equal(new[] { "processing_original_inputs", "processing_original_step_inputs", "processing_rule_manifest_bindings" },
            tables.Select(x => x.Name).Order().ToArray());
        Assert.All(tables.SelectMany(x => x.ForeignKeys), fk => Assert.Equal(ReferentialAction.NoAction, fk.OnDelete));
        Assert.All(tables, table => Assert.DoesNotContain(table.UniqueConstraints, x => x.Columns.Contains("ManifestId")));
        Assert.Equal(3, migration.DownOperations.Count);
        Assert.All(migration.DownOperations, operation => Assert.IsType<DropTableOperation>(operation));
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Theory]
    [InlineData("")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa ")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaag")]
    public void Entities_RejectInvalidHashWithoutTreatingItAsVersionApproval(string hash)
    {
        Assert.Throws<ArgumentException>(() => new ProcessingOriginalInput(1, hash, 0, "workflow-mvp-v1"));
        Assert.Throws<ArgumentException>(() => new ProcessingRuleManifestBinding(1, "workflow-mvp-v1", "manifest", hash));
        Assert.Throws<ArgumentException>(() => new ProcessingOriginalStepInput(1, "SHA256_CALCULATION", "rule", null, hash));
    }

    [Fact]
    public void Entities_RejectNegativeLengthAndWrongStepTimePairing()
    {
        var hash = new string('a', 64);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProcessingOriginalInput(1, hash, -1, "workflow-mvp-v1"));
        Assert.Throws<ArgumentException>(() => new ProcessingOriginalStepInput(1, "EXIF_PARSING", "rule", null, hash));
        Assert.Throws<ArgumentException>(() => new ProcessingOriginalStepInput(1, "SHA256_CALCULATION", "rule", DateTimeOffset.UtcNow, hash));
        Assert.Throws<ArgumentException>(() => new ProcessingOriginalStepInput(1, "exif_parsing", "rule", null, hash));
    }
}
