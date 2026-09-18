using Microsoft.EntityFrameworkCore;
using PhotoPlatform.Domain.Entities;
using PhotoPlatform.Infrastructure.Persistence.Configurations;

namespace PhotoPlatform.Infrastructure.Persistence;

// 集中管理 Upload 的三種持久化實體；各資料表的欄位與約束由獨立 Configuration 定義。
public sealed class PhotoPlatformDbContext(DbContextOptions<PhotoPlatformDbContext> options) : DbContext(options)
{
    public DbSet<Batch> Batches => Set<Batch>();
    public DbSet<Image> Images => Set<Image>();
    public DbSet<ProcessingJob> ProcessingJobs => Set<ProcessingJob>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // 明確套用 Task 6 的三份對應，讓執行階段與 Migration 使用同一個 EF 模型。
        modelBuilder.ApplyConfiguration(new BatchConfiguration());
        modelBuilder.ApplyConfiguration(new ImageConfiguration());
        modelBuilder.ApplyConfiguration(new ProcessingJobConfiguration());
    }
}
