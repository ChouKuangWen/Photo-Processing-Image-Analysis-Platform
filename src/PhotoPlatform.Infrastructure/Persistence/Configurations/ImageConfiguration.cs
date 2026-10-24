using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoPlatform.Domain.Entities;

namespace PhotoPlatform.Infrastructure.Persistence.Configurations;

internal sealed class ImageConfiguration : IEntityTypeConfiguration<Image>
{
    public void Configure(EntityTypeBuilder<Image> builder)
    {
        // 圖片 ID 由 SQL Server 產生；BatchId 保留批次關聯，資料庫只存 Storage 位置與描述資料。
        builder.ToTable("images");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnType("bigint").UseIdentityColumn();
        builder.Property(x => x.BatchId).HasColumnType("uniqueidentifier");
        builder.Property(x => x.OriginalFileName).HasMaxLength(255).IsUnicode().IsRequired();
        builder.Property(x => x.StoredPath).HasMaxLength(500).IsUnicode(false).IsRequired();
        // 命名、雜湊與拍攝資訊先依既有 Schema 保留 nullable 欄位，供後續模組填入。
        builder.Property(x => x.NewFileName).HasMaxLength(255).IsUnicode().IsRequired(false);
        builder.Property(x => x.FileSize).HasColumnType("bigint");
        builder.Property(x => x.MimeType).HasMaxLength(100).IsUnicode(false).IsRequired();
        builder.Property(x => x.SHA256).HasMaxLength(64).IsUnicode(false).IsRequired(false);
        builder.Property(x => x.TakenAt).HasColumnType("datetimeoffset(7)").IsRequired(false);
        builder.Property(x => x.CameraModel).HasMaxLength(100).IsUnicode().IsRequired(false);
        builder.Property(x => x.ISO).HasColumnType("int").IsRequired(false);
        builder.Property(x => x.ShutterSpeed).HasMaxLength(50).IsUnicode(false).IsRequired(false);
        builder.Property(x => x.Aperture).HasMaxLength(50).IsUnicode(false).IsRequired(false);
        builder.Property(x => x.Latitude).HasPrecision(10, 7).IsRequired(false);
        builder.Property(x => x.Longitude).HasPrecision(10, 7).IsRequired(false);
        builder.Property(x => x.LocationName).HasMaxLength(150).IsUnicode().IsRequired(false);
        builder.Property(x => x.Status).HasMaxLength(30).IsUnicode(false).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnType("datetimeoffset(7)");
        builder.Property(x => x.UpdatedAt).HasColumnType("datetimeoffset(7)");
        // 以 BatchId 建立外鍵，並禁止刪除 Batch 時由資料庫自動連帶刪除圖片。
        builder.HasOne<Batch>().WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.NoAction);
        // 僅建立 MOD-01 指定的批次、雜湊、狀態與拍攝時間查詢索引。
        builder.HasIndex(x => x.BatchId);
        builder.HasIndex(x => x.SHA256);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.TakenAt);
    }
}
