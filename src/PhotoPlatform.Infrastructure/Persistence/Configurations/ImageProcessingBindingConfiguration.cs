using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoPlatform.Domain.Entities;

namespace PhotoPlatform.Infrastructure.Persistence.Configurations;
// 用新表記錄圖片的主要工作，不修改 Upload 舊表，也不替舊資料猜一個主要工作。
internal sealed class ImageProcessingBindingConfiguration : IEntityTypeConfiguration<ImageProcessingBinding>
{
    public void Configure(EntityTypeBuilder<ImageProcessingBinding> builder)
    {
        builder.ToTable("image_processing_bindings", table =>
        {
            table.HasCheckConstraint("CK_image_processing_bindings_PrimaryWorkflow", "[PrimaryWorkflow] IN ('Naming', 'Analysis', 'DuplicateDetection', 'Full')");
            table.HasCheckConstraint("CK_image_processing_bindings_PrimaryStatus", "[PrimaryStatus] IN ('Pending', 'Processing', 'Completed', 'Failed')");
        });
        builder.HasKey(binding => binding.ImageId);
        // 直接使用既有圖片的 ImageId，不能讓資料庫另外產生一個編號。
        builder.Property(binding => binding.ImageId).HasColumnType("bigint").ValueGeneratedNever();
        builder.Property(binding => binding.BatchId).HasColumnType("uniqueidentifier");
        builder.Property(binding => binding.PrimaryJobId).HasColumnType("bigint");
        builder.Property(binding => binding.PrimaryWorkflow).HasConversion<string>().HasMaxLength(30).IsUnicode(false).IsRequired();
        builder.Property(binding => binding.PrimaryStatus).HasConversion<string>().HasMaxLength(30).IsUnicode(false).IsRequired();
        builder.Property(binding => binding.UpdatedAt).HasColumnType("datetimeoffset(7)");
        // NoAction 會拒絕刪除仍被綁定使用的資料，避免綁定被連帶刪除；外鍵只確認對象存在，是否屬於同一圖片與批次仍由交易檢查。
        builder.HasOne<Image>().WithMany().HasForeignKey(binding => binding.ImageId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Batch>().WithMany().HasForeignKey(binding => binding.BatchId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ProcessingJob>().WithMany().HasForeignKey(binding => binding.PrimaryJobId).OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(binding => binding.PrimaryJobId).IsUnique();
    }
}
