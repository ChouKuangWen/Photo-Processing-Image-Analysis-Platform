using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PhotoPlatform.Domain.Entities;
using PhotoPlatform.Domain.Enums;
using PhotoPlatform.Infrastructure.Persistence;

namespace PhotoPlatform.IntegrationTests.Persistence;

// 驗證 Task 6 的 EF 對應在真實 SQL Server 上能否正確持久化，以及資料庫是否執行外鍵與唯一索引。
// xUnit 為每個案例建立測試類別實例；初始化時建立獨立資料庫，結束時清理，避免案例互相影響。
public sealed class UploadPersistenceTests : IAsyncLifetime
{
    // 隨機名稱用於隔離測試；固定時間讓時間欄位的寫入與回讀結果可以精確比對。
    private readonly string _databaseName = "PhotoPlatform_Task06_" + Guid.NewGuid().ToString("N");
    private DbContextOptions<PhotoPlatformDbContext>? _options;
    private bool _created;
    private static readonly DateTimeOffset CreatedAt = new(2026, 9, 17, 1, 2, 3, TimeSpan.Zero);

    /*
    測試環境初始化
    它會讀 SQL Server 連線字串，先連到 master，建立一個隨機名稱的測試資料庫，
    再把 EF Core 指向這個新資料庫，最後執行 Migration。
    目的就是讓每個 Integration Test 都在一個乾淨、真的 SQL Server Database 上跑。
    */
    public async Task InitializeAsync()
    {
        // 測試必須連到外部 SQL Server，缺少連線字串就直接失敗，避免誤把未連線視為驗證通過。
        var connectionString = Environment.GetEnvironmentVariable("PHOTO_PLATFORM_TEST_DB_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Set PHOTO_PLATFORM_TEST_DB_CONNECTION_STRING to a Docker SQL Server test instance with database creation permission.");

        // 先連到 master 建立本案例專用資料庫；不在連線字串原本指定的資料庫上直接執行 Migration。
        var builder = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" };
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE [{_databaseName}]";
        await command.ExecuteNonQueryAsync();
        _created = true;
        // 切換至新資料庫並套用 Migration，後續案例才有實際表與約束可驗證。
        builder.InitialCatalog = _databaseName;
        _options = new DbContextOptionsBuilder<PhotoPlatformDbContext>().UseSqlServer(builder.ConnectionString).Options;
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }
    /*
    測試結束後清理資料庫
    DisposeAsync() 會確認測試資料庫真的有成功建立，
    然後用 EnsureDeletedAsync() 把它刪掉，避免測試跑完留下很多 Database。
    */
    public async Task DisposeAsync()
    {
        // 只有完成建立且備妥連線設定時才清理；目標是本案例的隨機資料庫。
        if (_created && _options is not null)
        {
            await using var db = CreateContext();
            await db.Database.EnsureDeletedAsync();
        }
    }
    /*
    測試共用的小工具方法
    CreateContext() 每次建立新的 PhotoPlatformDbContext；
    NewImage() 快速建立測試用 Image；
    SaveImageAsync() 則幫很多測試先建立一組有效的 Batch + Image，
    避免每個測試都重複寫一樣的準備程式。
    */
    private PhotoPlatformDbContext CreateContext() => new(_options ?? throw new InvalidOperationException("Database has not been initialized."));
    // 使用含中文的檔名作為樣本，同時涵蓋 Unicode 欄位的往返驗證。
    private static Image NewImage(Guid batchId) => new(batchId, "旅行照片.jpg", "original/550e8400e29b41d4a716446655440000", 1024, "image/jpeg", CreatedAt);

    private async Task<(Guid BatchId, long ImageId)> SaveImageAsync()
    {
        // 先保存 Batch，再保存引用它的 Image；資料庫產生的 ImageId 供 Job 測試使用。
        await using var db = CreateContext();
        var batch = new Batch(Guid.NewGuid(), 1, CreatedAt);
        db.Batches.Add(batch);
        await db.SaveChangesAsync();
        var image = NewImage(batch.Id);
        db.Images.Add(image);
        await db.SaveChangesAsync();
        return (batch.Id, image.Id);
    }

    // Migration 是否正確建立 Schema。
    [Fact]
    public async Task Migration_CreatesApprovedTablesAndConstraints()
    {
        await using var db = CreateContext();
        Assert.Single(await db.Database.GetAppliedMigrationsAsync());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges());
        // 排除 EF 自用的歷程表；Upload 範圍只應建立這三張業務資料表。
        var tables = await db.Database.SqlQueryRaw<string>("SELECT name AS [Value] FROM sys.tables WHERE name <> '__EFMigrationsHistory' ORDER BY name").ToListAsync();
        Assert.Equal(new[] { "batches", "images", "processing_jobs" }, tables);
        var actions = await db.Database.SqlQueryRaw<int>("SELECT CONVERT(int, delete_referential_action) AS [Value] FROM sys.foreign_keys").ToListAsync();
        Assert.Equal(3, actions.Count);
        // SQL Server 的 delete_referential_action = 0 表示 NoAction，避免外鍵自動級聯刪除。
        Assert.All(actions, action => Assert.Equal(0, action));
        // 直接查系統目錄，確認欄位、非主鍵索引、Identity 與預設值數量。
        Assert.Equal(38, await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id IN (OBJECT_ID('batches'), OBJECT_ID('images'), OBJECT_ID('processing_jobs'))").SingleAsync());
        Assert.Equal(9, await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.indexes WHERE is_primary_key = 0 AND index_id > 0 AND object_id IN (OBJECT_ID('batches'), OBJECT_ID('images'), OBJECT_ID('processing_jobs'))").SingleAsync());
        Assert.Equal(2, await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.identity_columns WHERE object_id IN (OBJECT_ID('batches'), OBJECT_ID('images'), OBJECT_ID('processing_jobs'))").SingleAsync());
        Assert.Equal(5, await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.default_constraints WHERE parent_object_id IN (OBJECT_ID('batches'), OBJECT_ID('images'), OBJECT_ID('processing_jobs'))").SingleAsync());
    }

    // 先寫入指定 Id 與總數，再用新 Context 回讀；確認初始計數、狀態和時間均正確保存。
    [Fact]
    public async Task Batch_RoundTripsSuppliedDataAndInitialState()
    {
        var id = Guid.NewGuid();
        await using (var db = CreateContext())
        {
            db.Batches.Add(new Batch(id, 3, CreatedAt));
            await db.SaveChangesAsync();
        }
        await using var read = CreateContext();
        var batch = await read.Batches.SingleAsync(x => x.Id == id);
        Assert.Equal(3, batch.TotalCount);
        Assert.Equal(0, batch.ProcessedCount);
        Assert.Equal(0, batch.SuccessCount);
        Assert.Equal(0, batch.FailedCount);
        Assert.Equal("Pending", batch.Status);
        Assert.Equal(CreatedAt, batch.CreatedAt);
        Assert.Null(batch.CompletedAt);
    }

    // 由資料庫建立圖片 Identity，再回讀驗證 Batch 關聯、Unicode 檔名及尚未填入的選填欄位。
    [Fact]
    public async Task Image_RoundTripsIdentityUnicodeAndNullableMetadata()
    {
        var (batchId, imageId) = await SaveImageAsync();
        Assert.True(imageId > 0);
        await using var db = CreateContext();
        var image = await db.Images.SingleAsync(x => x.Id == imageId);
        Assert.Equal(batchId, image.BatchId);
        Assert.True(await db.Batches.AnyAsync(x => x.Id == image.BatchId));
        Assert.Equal("旅行照片.jpg", image.OriginalFileName);
        Assert.Equal(1024, image.FileSize);
        Assert.Equal("Pending", image.Status);
        Assert.Equal(CreatedAt, image.CreatedAt);
        Assert.Equal(CreatedAt, image.UpdatedAt);
        Assert.Null(image.NewFileName);
        Assert.Null(image.SHA256);
        Assert.Null(image.TakenAt);
        Assert.Null(image.CameraModel);
        Assert.Null(image.ISO);
        Assert.Null(image.ShutterSpeed);
        Assert.Null(image.Aperture);
        Assert.Null(image.Latitude);
        Assert.Null(image.Longitude);
        Assert.Null(image.LocationName);
    }

    // 建立 Job 後用新 Context 回讀，確認兩個 FK、預設狀態、重試次數及選填時間。
    // 再直接查 SQL 欄位，確認 Workflow 和 Status 儲存的是 Enum 名稱，而非數字。
    [Fact]
    public async Task Job_RoundTripsRelationshipsAndStringEnums()
    {
        var (batchId, imageId) = await SaveImageAsync();
        await using (var db = CreateContext())
        {
            db.ProcessingJobs.Add(new ProcessingJob(imageId, batchId, WorkflowType.Full, CreatedAt));
            await db.SaveChangesAsync();
        }
        await using var read = CreateContext();
        var job = await read.ProcessingJobs.SingleAsync();
        Assert.True(job.Id > 0);
        Assert.Equal(imageId, job.ImageId);
        Assert.Equal(batchId, job.BatchId);
        Assert.Equal(WorkflowType.Full, job.Workflow);
        Assert.Equal(ProcessingJobStatus.Pending, job.Status);
        Assert.Equal(0, job.RetryCount);
        Assert.Equal(CreatedAt, job.CreatedAt);
        Assert.Null(job.StartedAt);
        Assert.Null(job.CompletedAt);
        Assert.Null(job.ErrorCode);
        Assert.Null(job.ErrorMessage);
        Assert.Equal("Full", await read.Database.SqlQueryRaw<string>("SELECT Workflow AS [Value] FROM processing_jobs").SingleAsync());
        Assert.Equal("Pending", await read.Database.SqlQueryRaw<string>("SELECT Status AS [Value] FROM processing_jobs").SingleAsync());
    }

    // 同一圖片與 Workflow 先成功寫入一次，再嘗試寫入第二次。
    // 檢查 SQL Server 唯一索引錯誤及資料筆數，證明限制由資料庫執行。
    [Fact]
    public async Task Job_DuplicateImageAndWorkflowIsRejectedByDatabase()
    {
        var (batchId, imageId) = await SaveImageAsync();
        await using var db = CreateContext();
        db.ProcessingJobs.Add(new ProcessingJob(imageId, batchId, WorkflowType.Full, CreatedAt));
        await db.SaveChangesAsync();
        db.ProcessingJobs.Add(new ProcessingJob(imageId, batchId, WorkflowType.Full, CreatedAt));
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains(Assert.IsType<SqlException>(error.InnerException).Number, new[] { 2601, 2627 });
        await using var read = CreateContext();
        Assert.Equal(1, await read.ProcessingJobs.CountAsync());
    }

    // 同一 ImageId 搭配兩種不同 Workflow 應同時存在，避免唯一索引錯誤地只限制 ImageId。
    [Fact]
    public async Task Job_DifferentWorkflowsForSameImageAreAllowed()
    {
        var (batchId, imageId) = await SaveImageAsync();
        await using var db = CreateContext();
        db.ProcessingJobs.AddRange(new ProcessingJob(imageId, batchId, WorkflowType.Full, CreatedAt),
            new ProcessingJob(imageId, batchId, WorkflowType.Naming, CreatedAt));
        await db.SaveChangesAsync();
        await using var read = CreateContext();
        Assert.Equal(2, await read.ProcessingJobs.CountAsync(x => x.ImageId == imageId));
    }

    // 三組資料分別破壞 Image→Batch、Job→Image、Job→Batch 關聯。
    // 每組都應由 SQL Server 回報 FK 錯誤 547，而不是由應用程式自行預先攔截。
    [Theory]
    [InlineData("ImageBatch")]
    [InlineData("JobImage")]
    [InlineData("JobBatch")]
    public async Task InvalidForeignKey_IsRejectedByDatabase(string relationship)
    {
        var (batchId, imageId) = await SaveImageAsync();
        await using var db = CreateContext();
        if (relationship == "ImageBatch")
            db.Images.Add(NewImage(Guid.NewGuid()));
        else
            db.ProcessingJobs.Add(new ProcessingJob(relationship == "JobImage" ? long.MaxValue : imageId,
                relationship == "JobBatch" ? Guid.NewGuid() : batchId, WorkflowType.Full, CreatedAt));
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(547, Assert.IsType<SqlException>(error.InnerException).Number);
    }

    // 在同一個資料庫交易中依 FK 順序寫入 Batch、Image、Job，然後明確 Rollback。
    // 換新 Context 查詢三張表，確認 Rollback 撤銷所有已呼叫 SaveChanges 的寫入。
    [Fact]
    public async Task Transaction_RollbackRemovesBatchImageAndJob()
    {
        await using (var db = CreateContext())
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            var batch = new Batch(Guid.NewGuid(), 1, CreatedAt);
            db.Batches.Add(batch);
            await db.SaveChangesAsync();
            var image = NewImage(batch.Id);
            db.Images.Add(image);
            await db.SaveChangesAsync();
            db.ProcessingJobs.Add(new ProcessingJob(image.Id, batch.Id, WorkflowType.Full, CreatedAt));
            await db.SaveChangesAsync();
            await transaction.RollbackAsync();
        }
        await using var read = CreateContext();
        Assert.Empty(await read.Batches.ToListAsync());
        Assert.Empty(await read.Images.ToListAsync());
        Assert.Empty(await read.ProcessingJobs.ToListAsync());
    }

    // 寫入目前尚無 Domain 更新方法的選填欄位，測試 EF Mapping 能保存各種資料型別。
    // 另以 5000 字 Unicode 訊息驗證 ErrorMessage 的 nvarchar(max) 不會截斷內容。
    [Fact]
    public async Task NullableMetadataAndLongUnicodeError_RoundTripWithoutNewDomainBehavior()
    {
        var (batchId, imageId) = await SaveImageAsync();
        var errorMessage = new string('錯', 5000);
        await using (var db = CreateContext())
        {
            var image = await db.Images.SingleAsync(x => x.Id == imageId);
            // 這裡只測持久化能力，因此透過 EF Entry 設值，不新增尚未核准的 Domain 行為。
            var entry = db.Entry(image);
            entry.Property(x => x.NewFileName).CurrentValue = "新照片.jpg";
            entry.Property(x => x.SHA256).CurrentValue = new string('a', 64);
            entry.Property(x => x.TakenAt).CurrentValue = CreatedAt.AddTicks(1234567);
            entry.Property(x => x.CameraModel).CurrentValue = "相機型號";
            entry.Property(x => x.ISO).CurrentValue = 100;
            entry.Property(x => x.ShutterSpeed).CurrentValue = "1/125";
            entry.Property(x => x.Aperture).CurrentValue = "f/2.8";
            entry.Property(x => x.Latitude).CurrentValue = 25.1234567m;
            entry.Property(x => x.Longitude).CurrentValue = 121.1234567m;
            entry.Property(x => x.LocationName).CurrentValue = "臺北";
            var job = new ProcessingJob(imageId, batchId, WorkflowType.Full, CreatedAt);
            db.ProcessingJobs.Add(job);
            db.Entry(job).Property(x => x.ErrorMessage).CurrentValue = errorMessage;
            await db.SaveChangesAsync();
        }
        await using var read = CreateContext();
        var saved = await read.Images.SingleAsync();
        Assert.Equal("新照片.jpg", saved.NewFileName);
        Assert.Equal(new string('a', 64), saved.SHA256);
        Assert.Equal(CreatedAt.AddTicks(1234567), saved.TakenAt);
        Assert.Equal("相機型號", saved.CameraModel);
        Assert.Equal(100, saved.ISO);
        Assert.Equal("1/125", saved.ShutterSpeed);
        Assert.Equal("f/2.8", saved.Aperture);
        Assert.Equal(25.1234567m, saved.Latitude);
        Assert.Equal(121.1234567m, saved.Longitude);
        Assert.Equal("臺北", saved.LocationName);
        Assert.Equal(errorMessage, (await read.ProcessingJobs.SingleAsync()).ErrorMessage);
    }

    // 建立完整的 Batch→Image→Job 關聯後，分別刪除仍被參照的 Image 與 Batch。
    // 應取得 FK 錯誤 547，並確認三筆資料都未被級聯刪除。
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NoAction_RejectsPrincipalDeleteWithoutDeletingDependents(bool deleteImage)
    {
        var (batchId, imageId) = await SaveImageAsync();
        await using (var db = CreateContext())
        {
            db.ProcessingJobs.Add(new ProcessingJob(imageId, batchId, WorkflowType.Full, CreatedAt));
            await db.SaveChangesAsync();
        }
        await using (var db = CreateContext())
        {
            if (deleteImage)
                db.Images.Remove(await db.Images.SingleAsync());
            else
                db.Batches.Remove(await db.Batches.SingleAsync());
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Equal(547, Assert.IsType<SqlException>(error.InnerException).Number);
        }
        await using var read = CreateContext();
        Assert.Equal(1, await read.Batches.CountAsync());
        Assert.Equal(1, await read.Images.CountAsync());
        Assert.Equal(1, await read.ProcessingJobs.CountAsync());
    }
}
