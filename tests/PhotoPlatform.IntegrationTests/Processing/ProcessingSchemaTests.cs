using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using PhotoPlatform.Domain.Entities;
using PhotoPlatform.Domain.Enums;
using PhotoPlatform.Domain.Policies;
using PhotoPlatform.Infrastructure.Persistence;
using PhotoPlatform.IntegrationTests.Fixtures;

namespace PhotoPlatform.IntegrationTests.Processing;
// 使用真實 SQL Server 檢查資料表、升級與資料限制；不代表已驗證 Claim 或多表寫入流程。
public sealed class ProcessingSchemaTests : IAsyncLifetime
{
    private readonly ProcessingSchemaFixture fixture = new();
    private static readonly DateTimeOffset Timestamp = new(2026, 10, 26, 0, 0, 0, TimeSpan.Zero);
    private static readonly string[] NewTables =
        ["image_processing_bindings", "processing_logs", "processing_runs", "processing_step_checkpoints"];

    public Task InitializeAsync() => fixture.InitializeAsync();
    public Task DisposeAsync() => fixture.DisposeAsync();

    // 測試目的：確認全新資料庫能建立 MOD-02 的四張資料表。
    // 測試情境：執行 Migration，再比對 SQL Server 的欄位、外鍵、索引與資料限制。
    // 預期結果：實際結構與 EF Core 設定一致，沒有尚未套用的 Migration。
    [Fact]
    public async Task FreshMigration_CreatesExactColumnsKeysIndexesAndChecks()
    {
        await using var db = fixture.CreateContext();
        await db.Database.MigrateAsync();
        Assert.Equal(2, (await db.Database.GetAppliedMigrationsAsync()).Count());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges());
        var model = db.GetService<IDesignTimeModel>().Model;
        var entities = model.GetEntityTypes().Where(entity => NewTables.Contains(entity.GetTableName())).ToArray();
        var columns = await db.Database.SqlQueryRaw<string>("""
            SELECT t.name + '|' + c.name + '|' + ty.name +
                CASE WHEN ty.name IN ('varchar','nvarchar','char','nchar') THEN
                    '(' + CASE WHEN c.max_length = -1 THEN 'max'
                        ELSE CONVERT(varchar(10), c.max_length / CASE WHEN ty.name IN ('nvarchar','nchar') THEN 2 ELSE 1 END) END + ')'
                    WHEN ty.name IN ('datetimeoffset','datetime2','time') THEN '(' + CONVERT(varchar(10), c.scale) + ')'
                    ELSE '' END + '|' + CONVERT(varchar(1), c.is_nullable) + '|' + CONVERT(varchar(1), c.is_identity) AS [Value]
            FROM sys.tables t JOIN sys.columns c ON t.object_id = c.object_id JOIN sys.types ty ON c.user_type_id = ty.user_type_id
            WHERE t.name IN ('processing_runs','processing_step_checkpoints','processing_logs','image_processing_bindings')
            """).ToListAsync();
        var expectedColumns = entities.SelectMany(entity => entity.GetProperties().Select(property =>
            $"{entity.GetTableName()}|{property.GetColumnName()}|{property.GetColumnType()}|{(property.IsNullable ? 1 : 0)}|{(property.GetValueGenerationStrategy() == SqlServerValueGenerationStrategy.IdentityColumn ? 1 : 0)}"));
        Assert.Equal(expectedColumns.OrderBy(value => value), columns.OrderBy(value => value));
        Assert.Equal(49, columns.Count);

        var foreignKeys = await db.Database.SqlQueryRaw<string>("""
            SELECT t.name + '|' + c.name + '|' + rt.name + '|' + rc.name + '|' + CONVERT(varchar(1), fk.delete_referential_action) AS [Value]
            FROM sys.foreign_keys fk JOIN sys.foreign_key_columns fc ON fk.object_id=fc.constraint_object_id
            JOIN sys.tables t ON t.object_id=fc.parent_object_id JOIN sys.columns c ON c.object_id=t.object_id AND c.column_id=fc.parent_column_id
            JOIN sys.tables rt ON rt.object_id=fc.referenced_object_id JOIN sys.columns rc ON rc.object_id=rt.object_id AND rc.column_id=fc.referenced_column_id
            WHERE t.name IN ('processing_runs','processing_step_checkpoints','processing_logs','image_processing_bindings')
            """).ToListAsync();
        var expectedForeignKeys = entities.SelectMany(entity => entity.GetForeignKeys().Select(foreignKey =>
            $"{entity.GetTableName()}|{foreignKey.Properties.Single().GetColumnName()}|{foreignKey.PrincipalEntityType.GetTableName()}|{foreignKey.PrincipalKey.Properties.Single().GetColumnName()}|0"));
        Assert.Equal(expectedForeignKeys.OrderBy(value => value), foreignKeys.OrderBy(value => value));
        Assert.Equal(8, foreignKeys.Count);

        var indexes = await db.Database.SqlQueryRaw<IndexRow>("""
            SELECT t.name AS TableName, i.name AS IndexName, i.is_unique AS IsUnique, i.is_primary_key AS IsPrimaryKey,
                i.filter_definition AS FilterDefinition,
                STRING_AGG(CONVERT(nvarchar(max), c.name + ':' + CONVERT(varchar(1), ic.is_descending_key)), ',') WITHIN GROUP (ORDER BY ic.key_ordinal) AS KeyColumns
            FROM sys.tables t JOIN sys.indexes i ON t.object_id=i.object_id
            JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
            JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
            WHERE ic.key_ordinal > 0 AND t.name IN ('processing_runs','processing_step_checkpoints','processing_logs','image_processing_bindings')
            GROUP BY t.name,i.name,i.is_unique,i.is_primary_key,i.filter_definition
            """).ToListAsync();
        Assert.Equal(entities.Sum(entity => entity.GetIndexes().Count() + 1), indexes.Count);
        foreach (var entity in entities)
        {
            var primary = Assert.Single(indexes, index => index.TableName == entity.GetTableName() && index.IsPrimaryKey);
            Assert.True(primary.IsUnique);
            Assert.Equal(entity.FindPrimaryKey()!.Properties.Single().Name + ":0", primary.KeyColumns);
            foreach (var index in entity.GetIndexes())
            {
                var actual = Assert.Single(indexes, row => row.IndexName == index.GetDatabaseName() && row.TableName == entity.GetTableName());
                Assert.Equal(index.IsUnique, actual.IsUnique);
                Assert.Equal(string.Join(',', index.Properties.Select((property, position) =>
                    property.Name + ":" + (index.IsDescending?[position] == true ? "1" : "0"))), actual.KeyColumns);
                Assert.Equal(index.GetFilter() is null, actual.FilterDefinition is null);
            }
        }
        Assert.Contains("Running", indexes.Single(index => index.TableName == "processing_logs" && index.FilterDefinition != null).FilterDefinition!);
        var runFilter = indexes.Single(index => index.TableName == "processing_runs" && index.FilterDefinition != null).FilterDefinition!;
        Assert.Contains("Pending", runFilter);
        Assert.Contains("Active", runFilter);

        var checks = await db.Database.SqlQueryRaw<string>("""
            SELECT cc.name AS [Value] FROM sys.check_constraints cc JOIN sys.tables t ON t.object_id=cc.parent_object_id
            WHERE cc.is_disabled=0 AND cc.is_not_trusted=0 AND t.name IN ('processing_runs','processing_step_checkpoints','processing_logs','image_processing_bindings')
            """).ToListAsync();
        Assert.Equal(entities.SelectMany(entity => entity.GetCheckConstraints()).Select(check => check.Name).OrderBy(name => name), checks.OrderBy(name => name));
    }

    // 測試目的：確認升級不會破壞 MOD-01 的資料和資料表。
    // 測試情境：先建立舊版資料庫並寫入圖片與工作，再執行本次 Migration。
    // 預期結果：原有結構和資料保留，使用新的 DbContext 仍能讀取資料及新增批次。
    [Fact]
    public async Task UpgradeFromUpload_PreservesRowsAndOriginalCatalog()
    {
        await using var db = fixture.CreateContext();
        await db.GetService<IMigrator>().MigrateAsync("20260917025957_InitialUploadPersistence");
        var upload = await SeedUploadAsync(db);
        var before = await UploadCatalogAsync(db);
        await db.Database.MigrateAsync();
        Assert.Equal(before, await UploadCatalogAsync(db));

        // 換一個 DbContext，確保讀到的是資料庫內容，而不是剛才留在記憶體裡的物件。
        await using var read = fixture.CreateContext();
        Assert.Equal(1, (await read.Batches.SingleAsync()).TotalCount);
        var image = await read.Images.SingleAsync();
        Assert.Equal(upload.ImageId, image.Id);
        Assert.Equal("原始照片.jpg", image.OriginalFileName);
        Assert.Equal("original/photo.jpg", image.StoredPath);
        var job = await read.ProcessingJobs.SingleAsync();
        Assert.Equal(upload.JobId, job.Id);
        Assert.Equal(ProcessingJobStatus.Pending, job.Status);
        Assert.Equal(Timestamp, job.CreatedAt);
        Assert.Empty(await read.ImageProcessingBindings.ToListAsync());
        read.Batches.Add(new Batch(Guid.NewGuid(), 0, Timestamp));
        await read.SaveChangesAsync();
        Assert.Equal(2, await read.Batches.CountAsync());
    }

    // 測試目的：確認處理紀錄能存入 SQL Server，再正確讀回。
    // 測試情境：寫入 Run、Checkpoint、Log 和 Binding，包含長篇中文錯誤訊息，再用新的 DbContext 讀取。
    // 預期結果：已檢查的內容保持一致，沒有填寫的耗時及等待狀態仍是 NULL。
    [Fact]
    public async Task Entities_RoundTripTerminalHistoryAndNullableWaitFields()
    {
        var seed = await SeedExecutionAsync();
        await using (var db = fixture.CreateContext())
        {
            var checkpoint = await db.ProcessingStepCheckpoints.SingleAsync();
            checkpoint.Start(ProcessingAttemptIdentity.Initial(), Timestamp);
            checkpoint.Complete(ProcessingResultStatus.Warning, "metadata:1:v1", Timestamp.AddSeconds(1));
            var log = new ProcessingLog(seed.CheckpointId, seed.JobId, seed.ImageId, ProcessingAttemptIdentity.Initial(), Timestamp, "trace");
            log.Finish(ProcessingLogStatus.Failed, Timestamp.AddSeconds(1), null, "GEO_UNAVAILABLE", new string('錯', 5000));
            db.ProcessingLogs.Add(log);
            (await db.ProcessingRuns.SingleAsync()).Complete(Timestamp.AddSeconds(1));
            db.ImageProcessingBindings.Add(new ImageProcessingBinding(seed.ImageId, seed.BatchId, seed.JobId, WorkflowType.Full, ProcessingJobStatus.Pending, Timestamp));
            await db.SaveChangesAsync();
        }
        await using var read = fixture.CreateContext();
        var savedLog = await read.ProcessingLogs.AsNoTracking().SingleAsync();
        Assert.True(savedLog.Id > 0);
        Assert.Equal((1, 0, 0), (savedLog.AttemptNo, savedLog.RetryOrdinal, savedLog.ResumeNo));
        Assert.Equal(ProcessingLogStatus.Failed, savedLog.Status);
        Assert.Equal(Timestamp, savedLog.Timestamp);
        Assert.Equal(Timestamp.AddSeconds(1), savedLog.CompletedAt);
        Assert.Null(savedLog.DurationMs);
        Assert.Equal(new string('錯', 5000), savedLog.ErrorMessage);
        Assert.Equal("trace", savedLog.TraceId);
        var savedCheckpoint = await read.ProcessingStepCheckpoints.SingleAsync();
        Assert.Equal(ProcessingResultStatus.Warning, savedCheckpoint.ResultStatus);
        Assert.Equal("metadata:1:v1", savedCheckpoint.ResultReference);
        Assert.Null(read.Entry(savedCheckpoint).Property("DependencyWaitStatus").CurrentValue);
        Assert.Equal(ProcessingRunStatus.Completed, (await read.ProcessingRuns.SingleAsync()).Status);
        Assert.Equal(seed.JobId, (await read.ImageProcessingBindings.SingleAsync()).PrimaryJobId);
    }

    // 測試目的：確認允許的最大執行次數及各種結束狀態都能保存。
    // 測試情境：使用 RunNo 4、RetryOrdinal 3，分別寫入各種已結束的 Log，結束時間只比開始多一個 tick。
    // 預期結果：讀回後次數與時間不變；尚未開始的 Run 沒有起訖時間，未知耗時仍是 NULL。
    [Fact]
    public async Task BoundaryValues_AndAllTerminalOutcomesRoundTrip()
    {
        var seed = await SeedExecutionAsync();
        await using (var db = fixture.CreateContext())
        {
            (await db.ProcessingRuns.SingleAsync()).Fail(Timestamp);
            var fourth = new ProcessingRun(seed.JobId, 4, ProcessingRunReason.ManualRetry, "workflow-mvp-v1", Timestamp);
            db.ProcessingRuns.Add(fourth);
            await db.SaveChangesAsync();
            foreach (var status in Enum.GetValues<ProcessingLogStatus>().Where(status => status != ProcessingLogStatus.Running))
            {
                var order = (int)status;
                var checkpoint = new ProcessingStepCheckpoint(fourth.Id, "BOUNDARY_" + status, order, "v1", new string('b', 64));
                db.ProcessingStepCheckpoints.Add(checkpoint);
                await db.SaveChangesAsync();
                // 每種結束狀態各用一個步驟，避免唯一鍵互相干擾；重試次數用上限 3，恢復次數用 0。
                var log = new ProcessingLog(checkpoint.Id, seed.JobId, seed.ImageId, new ProcessingAttemptIdentity(4, 3, 0), Timestamp);
                log.Finish(status, Timestamp.AddTicks(1), status == ProcessingLogStatus.Interrupted ? null : 0,
                    status == ProcessingLogStatus.Failed ? "TIMEOUT" : null);
                db.ProcessingLogs.Add(log);
            }
            await db.SaveChangesAsync();
        }
        await using var read = fixture.CreateContext();
        var pending = await read.ProcessingRuns.SingleAsync(run => run.RunNo == 4);
        Assert.Equal(ProcessingRunStatus.Pending, pending.Status);
        Assert.Null(pending.StartedAt);
        Assert.Null(pending.CompletedAt);
        var logs = await read.ProcessingLogs.AsNoTracking().ToListAsync();
        Assert.Equal(5, logs.Count);
        Assert.All(logs, log =>
        {
            Assert.Equal((4, 3, 0), (log.AttemptNo, log.RetryOrdinal, log.ResumeNo));
            Assert.Equal(Timestamp.AddTicks(1), log.CompletedAt);
            Assert.Equal(Timestamp, log.StartedAt);
            Assert.Equal(Timestamp, log.Timestamp);
        });
        Assert.Null(logs.Single(log => log.Status == ProcessingLogStatus.Interrupted).DurationMs);
    }

    // 測試目的：確認資料庫本身會拒絕不合法的資料，不只依靠 C# 驗證。
    // 測試情境：直接執行 SQL，故意寫入錯誤狀態、負數及超出範圍的次數。
    // 預期結果：SQL Server 回報 547 約束違反，原本的紀錄仍然存在。
    [Fact]
    public async Task CheckConstraints_RejectInvalidValuesWithoutChangingRows()
    {
        var seed = await SeedExecutionAsync();
        await using var db = fixture.CreateContext();
        db.ProcessingLogs.Add(new ProcessingLog(seed.CheckpointId, seed.JobId, seed.ImageId, ProcessingAttemptIdentity.Initial(), Timestamp));
        db.ImageProcessingBindings.Add(new ImageProcessingBinding(seed.ImageId, seed.BatchId, seed.JobId, WorkflowType.Full, ProcessingJobStatus.Pending, Timestamp));
        await db.SaveChangesAsync();
        // 直接送錯誤 SQL，才能確認是資料庫擋下資料，而不是 C# 建構子先拋出例外。
        var invalidUpdates = new[]
        {
            "UPDATE processing_runs SET RunNo=0", "UPDATE processing_runs SET RunNo=5",
            "UPDATE processing_runs SET Reason='ManualRetry'", "UPDATE processing_runs SET Status='Unknown'",
            "UPDATE processing_step_checkpoints SET StepOrder=0", "UPDATE processing_step_checkpoints SET RetryOrdinal=-1",
            "UPDATE processing_step_checkpoints SET RetryOrdinal=4", "UPDATE processing_step_checkpoints SET Status='Unknown'",
            "UPDATE processing_step_checkpoints SET ResultStatus='Unknown'", "UPDATE processing_step_checkpoints SET ErrorClassification='Unknown'",
            "UPDATE processing_logs SET AttemptNo=0", "UPDATE processing_logs SET ResumeNo=-1",
            "UPDATE processing_logs SET RetryOrdinal=-1", "UPDATE processing_logs SET RetryOrdinal=4",
            "UPDATE processing_logs SET DurationMs=-1", "UPDATE processing_logs SET Status='Unknown'",
            "UPDATE image_processing_bindings SET PrimaryWorkflow='Unknown'", "UPDATE image_processing_bindings SET PrimaryStatus='Unknown'"
        };
        foreach (var sql in invalidUpdates)
            Assert.Equal(547, (await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(sql))).Number);
        Assert.Equal(1, await db.ProcessingLogs.CountAsync());
        Assert.Equal(1, (await db.ProcessingRuns.AsNoTracking().SingleAsync()).RunNo);
    }

    // 測試目的：確認等待欄位不能只填一部分，且等待期限和步驟狀態受到限制。
    // 測試情境：嘗試部分 NULL、錯誤期限和錯誤步驟，再保存合法的 VERSION_ANALYSIS 等待資料。
    // 預期結果：非法資料被拒絕，合法資料可讀回並改存已解決或已到期；不代表已驗證等待排程。
    [Fact]
    public async Task DependencyWait_EnforcesCompleteTripleScopeAndDeadline()
    {
        var seed = await SeedExecutionAsync();
        await using var db = fixture.CreateContext();
        await db.Database.ExecuteSqlRawAsync("UPDATE processing_step_checkpoints SET Step='VERSION_ANALYSIS'");
        // 三欄只有部分有值的六種組合都要測，避免 NULL 讓 SQL CHECK 漏擋資料。
        for (var mask = 1; mask < 7; mask++)
        {
            var start = (mask & 1) != 0 ? "'2026-10-26T00:00:00+00:00'" : "NULL";
            var deadline = (mask & 2) != 0 ? "'2026-10-26T00:02:00+00:00'" : "NULL";
            var status = (mask & 4) != 0 ? "'Waiting'" : "NULL";
            var sql = $"UPDATE processing_step_checkpoints SET DependencyWaitStartedAt={start}, DependencyWaitDeadlineAt={deadline}, DependencyWaitStatus={status}";
            Assert.Equal(547, (await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(sql))).Number);
        }
        await db.Database.ExecuteSqlRawAsync("""
            UPDATE processing_step_checkpoints SET DependencyWaitStartedAt='2026-10-26T00:00:00+00:00',
            DependencyWaitDeadlineAt='2026-10-26T00:02:00+00:00', DependencyWaitStatus='Waiting'
            """);
        foreach (var sql in new[]
        {
            "UPDATE processing_step_checkpoints SET DependencyWaitDeadlineAt=DependencyWaitStartedAt",
            "UPDATE processing_step_checkpoints SET DependencyWaitDeadlineAt=DATEADD(second,-1,DependencyWaitStartedAt)",
            "UPDATE processing_step_checkpoints SET DependencyWaitStatus='Unknown'",
            "UPDATE processing_step_checkpoints SET Step='VISUAL_SIMILARITY'",
            "UPDATE processing_step_checkpoints SET Status='Processing'"
        })
            Assert.Equal(547, (await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(sql))).Number);
        await using (var read = fixture.CreateContext())
        {
            var checkpoint = await read.ProcessingStepCheckpoints.SingleAsync();
            Assert.Equal(Timestamp.AddSeconds(120), read.Entry(checkpoint).Property<DateTimeOffset?>("DependencyWaitDeadlineAt").CurrentValue);
            Assert.Equal("Waiting", read.Entry(checkpoint).Property<string>("DependencyWaitStatus").CurrentValue);
        }
        await db.Database.ExecuteSqlRawAsync("UPDATE processing_step_checkpoints SET DependencyWaitStatus='Resolved', Status='Processing'");
        await db.Database.ExecuteSqlRawAsync("UPDATE processing_step_checkpoints SET DependencyWaitStatus='Expired', Status='Failed', ErrorCode='DEPENDENCY_NOT_READY'");
        Assert.Empty(await db.ProcessingLogs.ToListAsync());
        Assert.True(seed.CheckpointId > 0);
    }

    // 測試目的：確認兩個工作同時新增時，不會留下重複執行的紀錄。
    // 測試情境：runningLog=false 同時新增同 Job 的兩個未結束 Run；true 同時新增同 Checkpoint 的兩筆 Running Log。
    // 預期結果：恰好一筆成功，另一筆因唯一性限制被拒絕；不代表已驗證 Worker 或 Claim。
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentUnclosedRows_AllowOnlyOneInsert(bool runningLog)
    {
        var seed = await SeedExecutionAsync();
        await using (var setup = fixture.CreateContext())
        {
            (await setup.ProcessingRuns.SingleAsync()).Complete(Timestamp);
            await setup.SaveChangesAsync();
        }
        // 用 TaskCompletionSource 讓兩邊先等在同一處，再一起放行；最後仍要確認只有一筆成功。
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> InsertAsync(int ordinal)
        {
            await using var db = fixture.CreateContext();
            if (runningLog)
                db.ProcessingLogs.Add(new ProcessingLog(seed.CheckpointId, seed.JobId, seed.ImageId,
                    new ProcessingAttemptIdentity(ordinal + 1, 0, ordinal), Timestamp));
            else
                db.ProcessingRuns.Add(new ProcessingRun(seed.JobId, ordinal + 2, ProcessingRunReason.ManualRetry, "workflow-mvp-v1", Timestamp));
            await gate.Task;
            try { await db.SaveChangesAsync(); return true; }
            catch (DbUpdateException error) when (error.InnerException is SqlException { Number: 2601 or 2627 }) { return false; }
        }
        // 兩筆使用不同輪次或執行編號，避免先撞到別的唯一鍵；此處要測的是「只能一筆尚未結束」。
        var first = InsertAsync(0);
        var second = InsertAsync(1);
        gate.SetResult();
        Assert.Single(await Task.WhenAll(first, second), success => success);
        await using var verification = fixture.CreateContext();
        if (runningLog)
            Assert.Equal(1, await verification.ProcessingLogs.CountAsync());
        else
            Assert.Equal(1, await verification.ProcessingRuns.CountAsync(run => run.Status == ProcessingRunStatus.Pending));
    }

    // 測試目的：確認已結束的歷史可以保留多筆，但識別相同的資料不能重複新增。
    // 測試情境：先保存已結束的 Run 與 Log，再故意重複新增步驟、執行識別及主要工作綁定。
    // 預期結果：歷史資料仍在，重複寫入回報 2601（唯一索引）或 2627（主鍵／唯一約束）錯誤。
    [Fact]
    public async Task UniqueKeys_RejectDuplicatesButAllowTerminalHistory()
    {
        var seed = await SeedExecutionAsync();
        await using var db = fixture.CreateContext();
        var run = await db.ProcessingRuns.SingleAsync();
        run.Complete(Timestamp);
        var retry = new ProcessingRun(seed.JobId, 2, ProcessingRunReason.ManualRetry, "workflow-mvp-v1", Timestamp);
        retry.Activate(Timestamp);
        retry.Fail(Timestamp);
        db.ProcessingRuns.Add(retry);
        var first = new ProcessingLog(seed.CheckpointId, seed.JobId, seed.ImageId, ProcessingAttemptIdentity.Initial(), Timestamp);
        first.Finish(ProcessingLogStatus.Interrupted, Timestamp, null);
        var resumed = new ProcessingLog(seed.CheckpointId, seed.JobId, seed.ImageId, ProcessingAttemptIdentity.Initial().Resume(), Timestamp);
        resumed.Finish(ProcessingLogStatus.Success, Timestamp, 0);
        db.ProcessingLogs.AddRange(first, resumed);
        db.ImageProcessingBindings.Add(new ImageProcessingBinding(seed.ImageId, seed.BatchId, seed.JobId, WorkflowType.Full, ProcessingJobStatus.Pending, Timestamp));
        await db.SaveChangesAsync();
        var duplicateStatements = new[]
        {
            "INSERT INTO processing_runs (JobId,RunNo,Reason,Status,WorkflowVersion,CreatedAt) SELECT JobId,RunNo,Reason,Status,WorkflowVersion,CreatedAt FROM processing_runs WHERE RunNo=1",
            "INSERT INTO processing_step_checkpoints (RunId,Step,StepOrder,RuleVersion,InputFingerprint,Status,RetryOrdinal) SELECT RunId,Step,2,RuleVersion,InputFingerprint,Status,RetryOrdinal FROM processing_step_checkpoints",
            "INSERT INTO processing_step_checkpoints (RunId,Step,StepOrder,RuleVersion,InputFingerprint,Status,RetryOrdinal) SELECT RunId,'OTHER',StepOrder,RuleVersion,InputFingerprint,Status,RetryOrdinal FROM processing_step_checkpoints",
            "INSERT INTO processing_logs (StepCheckpointId,JobId,ImageId,AttemptNo,RetryOrdinal,ResumeNo,Status,StartedAt,Timestamp) SELECT StepCheckpointId,JobId,ImageId,AttemptNo,1,0,'Success',StartedAt,Timestamp FROM processing_logs WHERE AttemptNo=1",
            "INSERT INTO processing_logs (StepCheckpointId,JobId,ImageId,AttemptNo,RetryOrdinal,ResumeNo,Status,StartedAt,Timestamp) SELECT StepCheckpointId,JobId,ImageId,3,RetryOrdinal,ResumeNo,'Success',StartedAt,Timestamp FROM processing_logs WHERE AttemptNo=1",
            "INSERT INTO image_processing_bindings SELECT ImageId,BatchId,PrimaryJobId,PrimaryWorkflow,PrimaryStatus,UpdatedAt FROM image_processing_bindings"
        };
        foreach (var sql in duplicateStatements)
            Assert.Contains((await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(sql))).Number, new[] { 2601, 2627 });
        // 先建立真的存在的第二張圖片，避免外鍵先報錯；再確認同一 PrimaryJobId 不能綁定兩張圖片。
        var image = NewImage(seed.BatchId);
        db.Images.Add(image);
        await db.SaveChangesAsync();
        var duplicatePrimary = db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO image_processing_bindings (ImageId,BatchId,PrimaryJobId,PrimaryWorkflow,PrimaryStatus,UpdatedAt) VALUES ({image.Id},{seed.BatchId},{seed.JobId},'Full','Pending',{Timestamp})");
        Assert.Contains((await Assert.ThrowsAsync<SqlException>(() => duplicatePrimary)).Number, new[] { 2601, 2627 });
        Assert.Equal(2, await db.ProcessingRuns.CountAsync());
        Assert.Equal(2, await db.ProcessingLogs.CountAsync());
    }

    // 測試目的：確認資料不能指向不存在的對象，也不能刪除仍被其他資料使用的對象。
    // 測試情境：寫入不存在的外鍵值，並嘗試刪除仍被參照的步驟、工作、圖片和批次。
    // 預期結果：SQL Server 回報 547 外鍵違反，處理紀錄和綁定不會被連帶刪除。
    [Fact]
    public async Task ForeignKeys_RejectInvalidReferencesAndPrincipalDeletion()
    {
        var seed = await SeedExecutionAsync();
        await using var db = fixture.CreateContext();
        db.ProcessingLogs.Add(new ProcessingLog(seed.CheckpointId, seed.JobId, seed.ImageId, ProcessingAttemptIdentity.Initial(), Timestamp));
        db.ImageProcessingBindings.Add(new ImageProcessingBinding(seed.ImageId, seed.BatchId, seed.JobId, WorkflowType.Full, ProcessingJobStatus.Pending, Timestamp));
        await db.SaveChangesAsync();
        foreach (var sql in new[]
        {
            "UPDATE processing_runs SET JobId=9223372036854775807",
            "UPDATE processing_step_checkpoints SET RunId=9223372036854775807",
            "UPDATE processing_logs SET StepCheckpointId=9223372036854775807",
            "UPDATE processing_logs SET JobId=9223372036854775807",
            "UPDATE processing_logs SET ImageId=9223372036854775807",
            "UPDATE image_processing_bindings SET ImageId=9223372036854775807",
            "UPDATE image_processing_bindings SET PrimaryJobId=9223372036854775807",
            "UPDATE image_processing_bindings SET BatchId='00000000-0000-0000-0000-000000000000'",
            "DELETE FROM processing_step_checkpoints", "DELETE FROM processing_runs",
            "DELETE FROM processing_jobs", "DELETE FROM images", "DELETE FROM batches"
        })
            Assert.Equal(547, (await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(sql))).Number);
        Assert.Equal(1, await db.ProcessingLogs.CountAsync());
        Assert.Equal(1, await db.ImageProcessingBindings.CountAsync());
    }

    // 建立目前版本的資料表與一組 Upload 資料，再加上初始 Run 和一個步驟，供資料限制測試使用。
    private async Task<Seed> SeedExecutionAsync()
    {
        await using var db = fixture.CreateContext();
        await db.Database.MigrateAsync();
        var seed = await SeedUploadAsync(db);
        var run = new ProcessingRun(seed.JobId, 1, ProcessingRunReason.Initial, "workflow-mvp-v1", Timestamp);
        db.ProcessingRuns.Add(run);
        await db.SaveChangesAsync();
        var checkpoint = new ProcessingStepCheckpoint(run.Id, "EXIF_PARSING", 1, "metadata-v1", new string('a', 64));
        db.ProcessingStepCheckpoints.Add(checkpoint);
        await db.SaveChangesAsync();
        return seed with { RunId = run.Id, CheckpointId = checkpoint.Id };
    }

    // 按外鍵順序建立批次、圖片和工作；先存入資料庫取得編號，後續測試才有有效的關聯資料。
    private static async Task<Seed> SeedUploadAsync(PhotoPlatformDbContext db)
    {
        var batch = new Batch(Guid.NewGuid(), 1, Timestamp);
        db.Batches.Add(batch);
        await db.SaveChangesAsync();
        var image = NewImage(batch.Id);
        db.Images.Add(image);
        await db.SaveChangesAsync();
        var job = new ProcessingJob(image.Id, batch.Id, WorkflowType.Full, Timestamp);
        db.ProcessingJobs.Add(job);
        await db.SaveChangesAsync();
        return new Seed(batch.Id, image.Id, job.Id, 0, 0);
    }

    private static Image NewImage(Guid batchId) => new(batchId, "原始照片.jpg", "original/photo.jpg", 1024, "image/jpeg", Timestamp);

    private static async Task<string[]> UploadCatalogAsync(PhotoPlatformDbContext db)
    {
        // 從 SQL Server 系統目錄讀出原表的欄位、索引、外鍵和預設值；只比資料筆數，會漏掉結構被改壞的問題。
        var rows = await db.Database.SqlQueryRaw<string>("""
            SELECT CONCAT('column|',t.name,'|',c.name,'|',ty.name,'|',c.max_length,'|',c.precision,'|',c.scale,'|',c.is_nullable,'|',c.is_identity,'|',COALESCE(dc.definition,'')) AS [Value]
            FROM sys.tables t JOIN sys.columns c ON t.object_id=c.object_id JOIN sys.types ty ON c.user_type_id=ty.user_type_id
            LEFT JOIN sys.default_constraints dc ON dc.object_id=c.default_object_id
            WHERE t.name IN ('batches','images','processing_jobs')
            UNION ALL
            SELECT CONCAT('index|',t.name,'|',i.name,'|',i.is_unique,'|',i.is_primary_key,'|',ic.key_ordinal,'|',c.name,'|',ic.is_descending_key,'|',COALESCE(i.filter_definition,''))
            FROM sys.tables t JOIN sys.indexes i ON t.object_id=i.object_id JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
            JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE t.name IN ('batches','images','processing_jobs')
            UNION ALL
            SELECT CONCAT('fk|',t.name,'|',fk.name,'|',c.name,'|',rt.name,'|',rc.name,'|',fk.delete_referential_action)
            FROM sys.foreign_keys fk JOIN sys.foreign_key_columns fc ON fk.object_id=fc.constraint_object_id
            JOIN sys.tables t ON t.object_id=fc.parent_object_id JOIN sys.columns c ON c.object_id=t.object_id AND c.column_id=fc.parent_column_id
            JOIN sys.tables rt ON rt.object_id=fc.referenced_object_id JOIN sys.columns rc ON rc.object_id=rt.object_id AND rc.column_id=fc.referenced_column_id
            WHERE t.name IN ('batches','images','processing_jobs')
            """).ToListAsync();
        return rows.OrderBy(value => value, StringComparer.Ordinal).ToArray();
    }

    private sealed record Seed(Guid BatchId, long ImageId, long JobId, long RunId, long CheckpointId);

    public sealed class IndexRow
    {
        public string TableName { get; set; } = "";
        public string IndexName { get; set; } = "";
        public bool IsUnique { get; set; }
        public bool IsPrimaryKey { get; set; }
        public string? FilterDefinition { get; set; }
        public string KeyColumns { get; set; } = "";
    }
}
