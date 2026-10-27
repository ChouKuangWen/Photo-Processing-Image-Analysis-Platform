using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoPlatform.Domain.Entities;

namespace PhotoPlatform.Infrastructure.Persistence.Configurations;
// 保存每次執行的紀錄；唯一索引只允許同一步驟有一筆 Running，避免重複執行。
internal sealed class ProcessingLogConfiguration : IEntityTypeConfiguration<ProcessingLog>
{
    public void Configure(EntityTypeBuilder<ProcessingLog> builder)
    {
        builder.ToTable("processing_logs", table =>
        {
            table.HasCheckConstraint("CK_processing_logs_AttemptNo", "[AttemptNo] > 0");
            table.HasCheckConstraint("CK_processing_logs_RetryOrdinal", "[RetryOrdinal] BETWEEN 0 AND 3");
            table.HasCheckConstraint("CK_processing_logs_ResumeNo", "[ResumeNo] >= 0");
            table.HasCheckConstraint("CK_processing_logs_DurationMs", "[DurationMs] IS NULL OR [DurationMs] >= 0");
            table.HasCheckConstraint("CK_processing_logs_Status", "[Status] IN ('Running', 'Success', 'Warning', 'Failed', 'Interrupted', 'Skipped')");
        });
        builder.HasKey(log => log.Id);
        builder.Property(log => log.Id).HasColumnType("bigint").UseIdentityColumn();
        builder.Property(log => log.StepCheckpointId).HasColumnType("bigint");
        builder.Property(log => log.JobId).HasColumnType("bigint");
        builder.Property(log => log.ImageId).HasColumnType("bigint");
        builder.Property(log => log.AttemptNo).HasColumnType("int");
        builder.Property(log => log.RetryOrdinal).HasColumnType("int");
        builder.Property(log => log.ResumeNo).HasColumnType("int");
        builder.Property(log => log.Status).HasConversion<string>().HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(log => log.StartedAt).HasColumnType("datetimeoffset(7)");
        builder.Property(log => log.CompletedAt).HasColumnType("datetimeoffset(7)").IsRequired(false);
        builder.Property(log => log.DurationMs).HasColumnType("bigint").IsRequired(false);
        builder.Property(log => log.ErrorCode).HasMaxLength(50).IsUnicode(false).IsRequired(false);
        builder.Property(log => log.ErrorMessage).HasColumnType("nvarchar(max)").IsRequired(false);
        builder.Property(log => log.Timestamp).HasColumnType("datetimeoffset(7)");
        builder.Property(log => log.TraceId).HasMaxLength(100).IsUnicode(false).IsRequired(false);
        // 外鍵使用 NoAction，避免刪除相關資料時連帶刪掉歷史；寫入端仍須確認步驟、Run、Job 和圖片確實互相對應。
        builder.HasOne<ProcessingStepCheckpoint>().WithMany().HasForeignKey(log => log.StepCheckpointId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ProcessingJob>().WithMany().HasForeignKey(log => log.JobId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Image>().WithMany().HasForeignKey(log => log.ImageId).OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(log => new { log.StepCheckpointId, log.AttemptNo }).IsUnique();
        builder.HasIndex(log => new { log.StepCheckpointId, log.RetryOrdinal, log.ResumeNo }).IsUnique();
        // 這個唯一索引只檢查 Running；已結束的執行必須能保留多筆，不能連歷史也一起限制。
        builder.HasIndex(log => log.StepCheckpointId).IsUnique().HasFilter("[Status] = 'Running'");
        builder.HasIndex(log => new { log.ImageId, log.Timestamp, log.Id }).IsDescending(false, true, true);
        builder.HasIndex(log => new { log.JobId, log.Timestamp, log.Id }).IsDescending(false, true, true);
    }
}
