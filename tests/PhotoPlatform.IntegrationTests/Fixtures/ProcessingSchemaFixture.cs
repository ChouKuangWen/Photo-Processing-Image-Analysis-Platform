using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PhotoPlatform.Infrastructure.Persistence;

namespace PhotoPlatform.IntegrationTests.Fixtures;

// 每個案例都使用自己的臨時資料庫；先不套用 Migration，讓升級測試能從舊版開始。
// 執行前仍要確認連到專用測試 SQL Server；名稱有隨機碼，不代表連線目標就一定安全。

internal sealed class ProcessingSchemaFixture : IAsyncLifetime
{
    // 用 GUID 避免不同測試撞名，不借用既有資料庫，也不依名稱開頭批次刪除。
    private readonly string databaseName = "PhotoPlatform_Mod02Schema_" + Guid.NewGuid().ToString("N");
    private string? testConnection;
    private bool created;

    // 這裡只建空資料庫，由各測試決定套用哪一版 Migration，才能測試從舊版升級。
    public async Task InitializeAsync()
    {
        var supplied = Environment.GetEnvironmentVariable("PHOTO_PLATFORM_TEST_DB_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(supplied))
            throw new InvalidOperationException("SQL schema tests require an approved PHOTO_PLATFORM_TEST_DB_CONNECTION_STRING.");
        var builder = new SqlConnectionStringBuilder(supplied) { InitialCatalog = databaseName };
        testConnection = builder.ConnectionString;
        builder.InitialCatalog = "master";
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE [{databaseName}]";
        await command.ExecuteNonQueryAsync();
        created = true;
    }

    public PhotoPlatformDbContext CreateContext() => new(new DbContextOptionsBuilder<PhotoPlatformDbContext>()
        .UseSqlServer(testConnection ?? throw new InvalidOperationException("Schema fixture not initialized.")).Options);

    public async Task DisposeAsync()
    {
        if (!created)
            return;
        var builder = new SqlConnectionStringBuilder(testConnection);
        if (builder.InitialCatalog != databaseName || !databaseName.StartsWith("PhotoPlatform_Mod02Schema_", StringComparison.Ordinal))
            throw new InvalidOperationException("Unexpected schema test cleanup target.");
        // 只刪除這個 Fixture 成功建立的那一個資料庫，不搜尋或清理其他資料庫。
        await using var db = CreateContext();
        await db.Database.EnsureDeletedAsync();
        created = false;
    }
}
