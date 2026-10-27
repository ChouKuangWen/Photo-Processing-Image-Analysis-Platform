using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoPlatform.Domain.Entities;

namespace PhotoPlatform.Infrastructure.Persistence.Configurations;
// 保存每一輪處理；同一 Job 只能有一個尚未結束的 Run，包括人工重試後還在 Pending 的那一個。
internal sealed class ProcessingRunConfiguration : IEntityTypeConfiguration<ProcessingRun>
{
    public void Configure(EntityTypeBuilder<ProcessingRun> builder)
    {
        // 把狀態轉成字串並不能擋住錯誤 SQL，所以用 CHECK 限制允許的值，並確認初次執行與人工重試的輪次正確。
        builder.ToTable("processing_runs", table =>
        {
            table.HasCheckConstraint("CK_processing_runs_RunNo", "[RunNo] BETWEEN 1 AND 4");
            table.HasCheckConstraint("CK_processing_runs_Reason", "([Reason] = 'Initial' AND [RunNo] = 1) OR ([Reason] = 'ManualRetry' AND [RunNo] BETWEEN 2 AND 4)");
            table.HasCheckConstraint("CK_processing_runs_Status", "[Status] IN ('Pending', 'Active', 'Completed', 'Failed')");
        });
        builder.HasKey(run => run.Id);
        builder.Property(run => run.Id).HasColumnType("bigint").UseIdentityColumn();
        builder.Property(run => run.JobId).HasColumnType("bigint");
        builder.Property(run => run.RunNo).HasColumnType("int");
        builder.Property(run => run.Reason).HasConversion<string>().HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(run => run.Status).HasConversion<string>().HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(run => run.WorkflowVersion).HasMaxLength(50).IsUnicode(false).IsRequired();
        builder.Property(run => run.CreatedAt).HasColumnType("datetimeoffset(7)");
        builder.Property(run => run.StartedAt).HasColumnType("datetimeoffset(7)").IsRequired(false);
        builder.Property(run => run.CompletedAt).HasColumnType("datetimeoffset(7)").IsRequired(false);
        builder.Property(run => run.TraceId).HasMaxLength(100).IsUnicode(false).IsRequired(false);
        // NoAction 防止刪除 Job 時連帶刪掉 Run 歷史；Run 和 Job 的狀態如何一起更新，留給後續交易處理。
        builder.HasOne<ProcessingJob>().WithMany().HasForeignKey(run => run.JobId).OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(run => new { run.JobId, run.RunNo }).IsUnique();
        builder.HasIndex(run => run.JobId).IsUnique().HasFilter("[Status] IN ('Pending', 'Active')");
    }
}
