using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoPlatform.Domain.Entities;

namespace PhotoPlatform.Infrastructure.Persistence.Configurations;

internal sealed class ProcessingJobConfiguration : IEntityTypeConfiguration<ProcessingJob>
{
    public void Configure(EntityTypeBuilder<ProcessingJob> builder)
    {
        // 工作 ID 由 SQL Server 產生；ImageId 與 BatchId 同時記錄工作所屬圖片及批次。
        builder.ToTable("processing_jobs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnType("bigint").UseIdentityColumn();
        builder.Property(x => x.ImageId).HasColumnType("bigint");
        builder.Property(x => x.BatchId).HasColumnType("uniqueidentifier");
        // Enum 以名稱儲存為 varchar，保留規格要求的 Workflow 與 Status 字串值。
        builder.Property(x => x.Workflow).HasConversion<string>().HasMaxLength(30).IsUnicode(false).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).IsUnicode(false).IsRequired();
        builder.Property(x => x.RetryCount).HasColumnType("int").HasDefaultValue(0);
        builder.Property(x => x.CreatedAt).HasColumnType("datetimeoffset(7)");
        builder.Property(x => x.StartedAt).HasColumnType("datetimeoffset(7)").IsRequired(false);
        builder.Property(x => x.CompletedAt).HasColumnType("datetimeoffset(7)").IsRequired(false);
        builder.Property(x => x.ErrorCode).HasMaxLength(50).IsUnicode(false).IsRequired(false);
        builder.Property(x => x.ErrorMessage).HasColumnType("nvarchar(max)").IsRequired(false);
        // 兩條外鍵都使用 NoAction，避免刪除圖片或批次時默默刪除處理紀錄。
        builder.HasOne<Image>().WithMany().HasForeignKey(x => x.ImageId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Batch>().WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(x => x.ImageId);
        builder.HasIndex(x => x.BatchId);
        builder.HasIndex(x => x.Status);
        // 由資料庫阻止同一圖片建立重複 Workflow；不同 Workflow 仍可各自建 Job。
        builder.HasIndex(x => new { x.ImageId, x.Workflow }).IsUnique();
    }
}
