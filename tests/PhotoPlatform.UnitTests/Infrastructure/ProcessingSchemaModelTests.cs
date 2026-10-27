using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using PhotoPlatform.Domain.Entities;
using PhotoPlatform.Infrastructure.Persistence;

namespace PhotoPlatform.UnitTests.Infrastructure;
// 不連線資料庫，只檢查 EF Core 模型和 Migration；資料庫是否真的擋住錯誤資料，仍要靠 SQL 整合測試。
public sealed class ProcessingSchemaModelTests
{
    private static readonly string[] NewTables =
        ["image_processing_bindings", "processing_logs", "processing_runs", "processing_step_checkpoints"];

    private static PhotoPlatformDbContext CreateContext() => new(new DbContextOptionsBuilder<PhotoPlatformDbContext>()
        .UseSqlServer("Server=127.0.0.1,1;Database=OfflineModelOnly;Integrated Security=true;Connect Timeout=1")
        .Options);

    // 測試目的：確認 EF Core 能建立模型及四種新實體的查詢。
    // 測試情境：不連線資料庫，只產生查詢 SQL 並檢查模型是否與 Snapshot 一致。
    // 預期結果：七種實體可建立且沒有模型差異；這不等於已驗證資料庫實際回讀。
    [Fact]
    public void Model_BuildsQueriesIncludingLogMaterializationWithoutConnecting()
    {
        using var db = CreateContext();
        Assert.Equal(7, db.Model.GetEntityTypes().Count());
        Assert.Contains("processing_logs", db.ProcessingLogs.AsNoTracking().ToQueryString());
        Assert.Contains("processing_runs", db.ProcessingRuns.AsNoTracking().ToQueryString());
        Assert.Contains("processing_step_checkpoints", db.ProcessingStepCheckpoints.AsNoTracking().ToQueryString());
        Assert.Contains("image_processing_bindings", db.ImageProcessingBindings.AsNoTracking().ToQueryString());
        Assert.False(db.Database.HasPendingModelChanges());
    }

    // 測試目的：確認 Migration 只增加核准的新表，不動 MOD-01 的舊表。
    // 測試情境：檢查升級與回滾操作，並比較舊 Migration 模型和目前模型。
    // 預期結果：只新增四表與索引，外鍵不連帶刪除資料；回滾只移除這四表。
    [Fact]
    public void Migration_OnlyCreatesApprovedTablesAndPreservesUploadModel()
    {
        using var db = CreateContext();
        var assembly = db.GetService<IMigrationsAssembly>();
        Assert.Equal(2, assembly.Migrations.Count);
        var initial = assembly.CreateMigration(assembly.Migrations.Single(pair => pair.Key.EndsWith("_InitialUploadPersistence")).Value,
            db.Database.ProviderName!);
        var execution = assembly.CreateMigration(assembly.Migrations.Single(pair => pair.Key.EndsWith("_AddProcessingExecutionPersistence")).Value,
            db.Database.ProviderName!);
        var creates = execution.UpOperations.OfType<CreateTableOperation>().ToArray();
        Assert.Equal(NewTables, creates.Select(table => table.Name).OrderBy(name => name).ToArray());
        Assert.Equal(8, creates.Sum(table => table.ForeignKeys.Count));
        Assert.All(creates.SelectMany(table => table.ForeignKeys), foreignKey => Assert.Equal(ReferentialAction.NoAction, foreignKey.OnDelete));
        Assert.All(execution.UpOperations, operation =>
        {
            Assert.True(operation is CreateTableOperation or CreateIndexOperation);
            if (operation is CreateIndexOperation index)
                Assert.Contains(index.Table, NewTables);
        });
        Assert.Equal(NewTables, execution.DownOperations.Select(operation => Assert.IsType<DropTableOperation>(operation).Name)
            .OrderBy(name => name).ToArray());

        // 拿舊版模型來比較，避免新設定不小心產生修改舊表的操作。
        var initializer = db.GetService<IModelRuntimeInitializer>();
        var oldModel = initializer.Initialize(initial.TargetModel, designTime: true);
        var current = db.GetService<IDesignTimeModel>().Model;
        var differences = db.GetService<IMigrationsModelDiffer>()
            .GetDifferences(oldModel.GetRelationalModel(), current.GetRelationalModel());
        Assert.All(differences, operation => Assert.True(operation is CreateTableOperation or CreateIndexOperation));
        Assert.Equal(4, differences.OfType<CreateTableOperation>().Count());
    }

    // 測試目的：確認產生的升級 SQL 不會重建或刪除舊表資料。
    // 測試情境：離線產生從 Initial Migration 升級的 SQL，檢查建表與唯一索引條件。
    // 預期結果：四張新表與兩種條件式唯一索引存在，沒有修改舊表或刪除資料的 SQL；腳本不會被執行。
    [Fact]
    public void Migration_GeneratesAdditiveSqlWithoutConnecting()
    {
        using var db = CreateContext();
        var script = db.GetService<IMigrator>().GenerateScript("20260917025957_InitialUploadPersistence");
        foreach (var table in NewTables)
            Assert.Contains($"CREATE TABLE [{table}]", script);
        foreach (var table in new[] { "batches", "images", "processing_jobs" })
        {
            Assert.DoesNotContain($"ALTER TABLE [{table}]", script);
            Assert.DoesNotContain($"DROP TABLE [{table}]", script);
            Assert.DoesNotContain($"CREATE TABLE [{table}]", script);
        }
        Assert.Contains("WHERE [Status] IN ('Pending', 'Active')", script);
        Assert.Contains("WHERE [Status] = 'Running'", script);
        Assert.DoesNotContain("DELETE FROM", script);
    }

    // 測試目的：確認 EF Core 設定能表達執行識別、查詢排序及等待欄位。
    // 測試情境：直接檢查模型中的唯一鍵、欄位型別、可空設定和外鍵刪除規則，不連線資料庫。
    // 預期結果：設定符合預期，沒有多存 Step／RunId／RetryCount；SQL 實際限制另由整合測試確認。
    [Fact]
    public void Model_MapsInvocationKeysWaitFieldsAndStringEnums()
    {
        using var db = CreateContext();
        var model = db.GetService<IDesignTimeModel>().Model;
        var run = model.FindEntityType(typeof(ProcessingRun))!;
        var checkpoint = model.FindEntityType(typeof(ProcessingStepCheckpoint))!;
        var log = model.FindEntityType(typeof(ProcessingLog))!;
        var binding = model.FindEntityType(typeof(ImageProcessingBinding))!;
        Assert.Equal("[Status] IN ('Pending', 'Active')", run.GetIndexes().Single(index => index.GetFilter() != null).GetFilter());
        Assert.Equal("[Status] = 'Running'", log.GetIndexes().Single(index => index.GetFilter() != null).GetFilter());
        Assert.Equal(3, log.GetIndexes().Count(index => index.IsUnique));
        Assert.Equal(2, checkpoint.GetIndexes().Count(index => index.IsUnique));
        Assert.Equal(ValueGenerated.Never, binding.FindProperty("ImageId")!.ValueGenerated);
        Assert.Equal("varchar(20)", log.FindProperty("Status")!.GetColumnType());
        Assert.Equal("nvarchar(max)", log.FindProperty("ErrorMessage")!.GetColumnType());
        Assert.Equal("bigint", log.FindProperty("DurationMs")!.GetColumnType());
        Assert.True(log.FindProperty("DurationMs")!.IsNullable);
        foreach (var name in new[] { "DependencyWaitStartedAt", "DependencyWaitDeadlineAt", "DependencyWaitStatus" })
        {
            var property = checkpoint.FindProperty(name)!;
            Assert.True(property.IsShadowProperty());
            Assert.True(property.IsNullable);
        }
        Assert.Null(log.FindProperty("Step"));
        Assert.Null(log.FindProperty("RunId"));
        Assert.Null(log.FindProperty("RetryCount"));
        Assert.All(model.GetEntityTypes().SelectMany(entity => entity.GetForeignKeys()),
            foreignKey => Assert.Equal(DeleteBehavior.NoAction, foreignKey.DeleteBehavior));
        foreach (var key in new[] { "ImageId", "JobId" })
        {
            var index = log.GetIndexes().Single(candidate => candidate.Properties.Select(property => property.Name)
                .SequenceEqual(new[] { key, "Timestamp", "Id" }));
            Assert.Equal(new[] { false, true, true }, index.IsDescending);
        }
    }
}
