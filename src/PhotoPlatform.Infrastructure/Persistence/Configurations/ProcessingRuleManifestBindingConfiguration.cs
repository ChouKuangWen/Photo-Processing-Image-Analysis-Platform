using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoPlatform.Domain.Entities;

namespace PhotoPlatform.Infrastructure.Persistence.Configurations;

internal sealed class ProcessingRuleManifestBindingConfiguration : IEntityTypeConfiguration<ProcessingRuleManifestBinding>
{
    public void Configure(EntityTypeBuilder<ProcessingRuleManifestBinding> builder)
    {
        builder.ToTable("processing_rule_manifest_bindings", table =>
        {
            table.HasCheckConstraint("CK_processing_rule_manifest_bindings_Checksum", BindingSqlChecks.Hash("ManifestChecksum"));
            table.HasCheckConstraint("CK_processing_rule_manifest_bindings_WorkflowVersion", BindingSqlChecks.Text("WorkflowVersion"));
            table.HasCheckConstraint("CK_processing_rule_manifest_bindings_ManifestId", BindingSqlChecks.Text("ManifestId"));
        });
        builder.HasKey(x => x.JobId);
        builder.Property(x => x.JobId).HasColumnType("bigint").ValueGeneratedNever();
        builder.Property(x => x.WorkflowVersion).HasColumnType("varchar(50)").IsRequired();
        builder.Property(x => x.ManifestId).HasColumnType("varchar(100)").IsRequired();
        builder.Property(x => x.ManifestChecksum).HasColumnType("char(64)").UseCollation(BindingSqlChecks.HashCollation).IsRequired();
        builder.HasOne<ProcessingJob>().WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.NoAction);
    }
}
