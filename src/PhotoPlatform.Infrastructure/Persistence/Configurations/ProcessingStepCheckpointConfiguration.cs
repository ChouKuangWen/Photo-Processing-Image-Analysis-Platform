using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoPlatform.Domain.Entities;

namespace PhotoPlatform.Infrastructure.Persistence.Configurations;
// 保存每個步驟的進度和等待資料；這裡只設定資料表，不決定何時喚醒工作或如何一起更新其他表。
internal sealed class ProcessingStepCheckpointConfiguration : IEntityTypeConfiguration<ProcessingStepCheckpoint>
{
    public void Configure(EntityTypeBuilder<ProcessingStepCheckpoint> builder)
    {
        builder.ToTable("processing_step_checkpoints", table =>
        {
            table.HasCheckConstraint("CK_processing_step_checkpoints_StepOrder", "[StepOrder] > 0");
            table.HasCheckConstraint("CK_processing_step_checkpoints_RetryOrdinal", "[RetryOrdinal] BETWEEN 0 AND 3");
            table.HasCheckConstraint("CK_processing_step_checkpoints_Status", "[Status] IN ('Pending', 'Processing', 'RetryWaiting', 'Completed', 'Failed')");
            table.HasCheckConstraint("CK_processing_step_checkpoints_ResultStatus", "[ResultStatus] IS NULL OR [ResultStatus] IN ('Success', 'Warning', 'Skipped')");
            table.HasCheckConstraint("CK_processing_step_checkpoints_ErrorClassification", "[ErrorClassification] IS NULL OR [ErrorClassification] IN ('Transient', 'Permanent')");
            // 等待三欄必須一起填或一起留空；明確檢查 NULL，避免 SQL 把不完整的資料當成「無法判定」而放行。
            table.HasCheckConstraint("CK_processing_step_checkpoints_DependencyWait", """
                ([DependencyWaitStartedAt] IS NULL AND [DependencyWaitDeadlineAt] IS NULL AND [DependencyWaitStatus] IS NULL)
                OR ([DependencyWaitStartedAt] IS NOT NULL AND [DependencyWaitDeadlineAt] IS NOT NULL
                    AND [DependencyWaitStatus] IS NOT NULL AND [DependencyWaitDeadlineAt] > [DependencyWaitStartedAt]
                    AND [DependencyWaitStatus] IN ('Waiting', 'Resolved', 'Expired')
                    AND [Step] = 'VERSION_ANALYSIS'
                    AND ([DependencyWaitStatus] <> 'Waiting' OR [Status] = 'Pending'))
                """);
        });
        builder.HasKey(checkpoint => checkpoint.Id);
        builder.Property(checkpoint => checkpoint.Id).HasColumnType("bigint").UseIdentityColumn();
        builder.Property(checkpoint => checkpoint.RunId).HasColumnType("bigint");
        builder.Property(checkpoint => checkpoint.Step).HasMaxLength(50).IsUnicode(false).IsRequired();
        builder.Property(checkpoint => checkpoint.StepOrder).HasColumnType("int");
        builder.Property(checkpoint => checkpoint.RuleVersion).HasMaxLength(50).IsUnicode(false).IsRequired();
        builder.Property(checkpoint => checkpoint.InputFingerprint).HasMaxLength(64).IsUnicode(false).IsRequired();
        builder.Property(checkpoint => checkpoint.Status).HasConversion<string>().HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(checkpoint => checkpoint.RetryOrdinal).HasColumnType("int").HasDefaultValue(0);
        builder.Property(checkpoint => checkpoint.NextRetryAt).HasColumnType("datetimeoffset(7)").IsRequired(false);
        builder.Property(checkpoint => checkpoint.ResultReference).HasMaxLength(500).IsUnicode(false).IsRequired(false);
        builder.Property(checkpoint => checkpoint.ResultStatus).HasConversion<string>().HasMaxLength(20).IsUnicode(false).IsRequired(false);
        builder.Property(checkpoint => checkpoint.ErrorCode).HasMaxLength(50).IsUnicode(false).IsRequired(false);
        builder.Property(checkpoint => checkpoint.ErrorClassification).HasConversion<string>().HasMaxLength(20).IsUnicode(false).IsRequired(false);
        builder.Property(checkpoint => checkpoint.StartedAt).HasColumnType("datetimeoffset(7)").IsRequired(false);
        builder.Property(checkpoint => checkpoint.CompletedAt).HasColumnType("datetimeoffset(7)").IsRequired(false);
        // 用 Shadow Property 讓 EF Core 保存等待欄位，暫時不在 Domain 增加屬性；實際等待行為留給後續 TASK。
        builder.Property<DateTimeOffset?>("DependencyWaitStartedAt").HasColumnType("datetimeoffset(7)");
        builder.Property<DateTimeOffset?>("DependencyWaitDeadlineAt").HasColumnType("datetimeoffset(7)");
        builder.Property<string>("DependencyWaitStatus").HasMaxLength(20).IsUnicode(false).IsRequired(false);
        builder.HasOne<ProcessingRun>().WithMany().HasForeignKey(checkpoint => checkpoint.RunId).OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(checkpoint => new { checkpoint.RunId, checkpoint.Step }).IsUnique();
        builder.HasIndex(checkpoint => new { checkpoint.RunId, checkpoint.StepOrder }).IsUnique();
    }
}
