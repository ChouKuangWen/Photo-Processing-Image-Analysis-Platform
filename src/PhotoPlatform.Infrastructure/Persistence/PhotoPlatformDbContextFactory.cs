using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PhotoPlatform.Infrastructure.Persistence;

// 讓 EF Migration 工具不啟動 API 也能建立 DbContext；連線資訊由環境變數提供。
public sealed class PhotoPlatformDbContextFactory : IDesignTimeDbContextFactory<PhotoPlatformDbContext>
{
    public PhotoPlatformDbContext CreateDbContext(string[] args)
    {
        // 缺少連線設定時立即中止，避免工具意外連到未指定的資料庫。
        var connectionString = Environment.GetEnvironmentVariable("PHOTO_PLATFORM_TEST_DB_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Set PHOTO_PLATFORM_TEST_DB_CONNECTION_STRING before running EF tooling.");

        // 設定已核准的 SQL Server Provider，並交由相同 DbContext 建立 Migration 模型。
        return new PhotoPlatformDbContext(new DbContextOptionsBuilder<PhotoPlatformDbContext>()
            .UseSqlServer(connectionString).Options);
    }
}
