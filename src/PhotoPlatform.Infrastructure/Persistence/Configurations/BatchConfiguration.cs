using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoPlatform.Domain.Entities;

namespace PhotoPlatform.Infrastructure.Persistence.Configurations;

internal sealed class BatchConfiguration : IEntityTypeConfiguration<Batch>
{
    public void Configure(EntityTypeBuilder<Batch> builder)
    {
        // 將 Domain Batch 對應至既有 batches 表；由呼叫端指定 Guid，計數欄位由資料庫保留初始值。
        builder.ToTable("batches");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnType("uniqueidentifier").ValueGeneratedNever();
        builder.Property(x => x.TotalCount).HasColumnType("int").HasDefaultValue(0);
        builder.Property(x => x.ProcessedCount).HasColumnType("int").HasDefaultValue(0);
        builder.Property(x => x.SuccessCount).HasColumnType("int").HasDefaultValue(0);
        builder.Property(x => x.FailedCount).HasColumnType("int").HasDefaultValue(0);
        // 狀態使用非 Unicode 固定長度欄位；時間保留時區與七位小數精度。
        builder.Property(x => x.Status).HasMaxLength(30).IsUnicode(false).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnType("datetimeoffset(7)").IsRequired();
        builder.Property(x => x.CompletedAt).HasColumnType("datetimeoffset(7)").IsRequired(false);
        // 批次狀態查詢依 MOD-01 指定建立索引。
        builder.HasIndex(x => x.Status);
    }
}
