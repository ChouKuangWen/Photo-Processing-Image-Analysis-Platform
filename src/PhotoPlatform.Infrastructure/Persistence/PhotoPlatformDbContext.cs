using Microsoft.EntityFrameworkCore;
using PhotoPlatform.Domain.Entities;
using PhotoPlatform.Infrastructure.Persistence.Configurations;

namespace PhotoPlatform.Infrastructure.Persistence;
// 集中管理 Upload 與 Processing 要存進資料庫的資料；原有三張表的設定保持不變。
public sealed class PhotoPlatformDbContext(DbContextOptions<PhotoPlatformDbContext> options) : DbContext(options)
{
    public DbSet<Batch> Batches => Set<Batch>();
    public DbSet<Image> Images => Set<Image>();
    public DbSet<ProcessingJob> ProcessingJobs => Set<ProcessingJob>();
    // 新增這些集合只是讓 EF Core 能存取處理資料，還沒有啟用 Worker 或多張表一起更新的流程。
    public DbSet<ProcessingRun> ProcessingRuns => Set<ProcessingRun>();
    public DbSet<ProcessingStepCheckpoint> ProcessingStepCheckpoints => Set<ProcessingStepCheckpoint>();
    public DbSet<ProcessingLog> ProcessingLogs => Set<ProcessingLog>();
    public DbSet<ImageProcessingBinding> ImageProcessingBindings => Set<ImageProcessingBinding>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // 明確套用 Task 6 的三份對應，讓執行階段與 Migration 使用同一個 EF 模型。
        modelBuilder.ApplyConfiguration(new BatchConfiguration());
        modelBuilder.ApplyConfiguration(new ImageConfiguration());
        modelBuilder.ApplyConfiguration(new ProcessingJobConfiguration());
        modelBuilder.ApplyConfiguration(new ProcessingRunConfiguration());
        modelBuilder.ApplyConfiguration(new ProcessingStepCheckpointConfiguration());
        modelBuilder.ApplyConfiguration(new ProcessingLogConfiguration());
        modelBuilder.ApplyConfiguration(new ImageProcessingBindingConfiguration());
    }
}
