using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoPlatform.Domain.Entities;

namespace PhotoPlatform.Infrastructure.Persistence.Configurations;

internal sealed class ProcessingOriginalInputConfiguration : IEntityTypeConfiguration<ProcessingOriginalInput>
{
    public void Configure(EntityTypeBuilder<ProcessingOriginalInput> builder)
    {
        builder.ToTable("processing_original_inputs", table =>
        {
            table.HasCheckConstraint("CK_processing_original_inputs_ContentVersion", BindingSqlChecks.Hash("ContentVersion"));
            table.HasCheckConstraint("CK_processing_original_inputs_ContentLength", "[ContentLength] >= 0");
            table.HasCheckConstraint("CK_processing_original_inputs_WorkflowVersion", BindingSqlChecks.Text("WorkflowVersion"));
        });
        builder.HasKey(x => x.JobId);
        builder.Property(x => x.JobId).HasColumnType("bigint").ValueGeneratedNever();
        builder.Property(x => x.ContentVersion).HasColumnType("char(64)").UseCollation(BindingSqlChecks.HashCollation).IsRequired();
        builder.Property(x => x.ContentLength).HasColumnType("bigint");
        builder.Property(x => x.WorkflowVersion).HasColumnType("varchar(50)").IsRequired();
        builder.HasOne<ProcessingJob>().WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.NoAction);
    }
}
