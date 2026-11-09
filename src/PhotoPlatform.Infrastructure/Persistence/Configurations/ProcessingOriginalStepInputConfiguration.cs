using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoPlatform.Domain.Entities;

namespace PhotoPlatform.Infrastructure.Persistence.Configurations;

internal sealed class ProcessingOriginalStepInputConfiguration : IEntityTypeConfiguration<ProcessingOriginalStepInput>
{
    public void Configure(EntityTypeBuilder<ProcessingOriginalStepInput> builder)
    {
        builder.ToTable("processing_original_step_inputs", table =>
        {
            table.HasCheckConstraint("CK_processing_original_step_inputs_Fingerprint", BindingSqlChecks.Hash("InputFingerprint"));
            table.HasCheckConstraint("CK_processing_original_step_inputs_RuleVersion", BindingSqlChecks.Text("RuleVersion"));
            table.HasCheckConstraint("CK_processing_original_step_inputs_StepTime", """
                (Step COLLATE Latin1_General_100_BIN2 = 'EXIF_PARSING' AND DATALENGTH(Step) = 12 AND UploadCreatedAt IS NOT NULL)
                OR (Step COLLATE Latin1_General_100_BIN2 = 'SHA256_CALCULATION' AND DATALENGTH(Step) = 18 AND UploadCreatedAt IS NULL)
                """);
        });
        builder.HasKey(x => new { x.JobId, x.Step });
        builder.Property(x => x.JobId).HasColumnType("bigint").ValueGeneratedNever();
        builder.Property(x => x.Step).HasColumnType("varchar(50)").IsRequired();
        builder.Property(x => x.RuleVersion).HasColumnType("varchar(50)").IsRequired();
        builder.Property(x => x.UploadCreatedAt).HasColumnType("datetimeoffset(7)").IsRequired(false);
        builder.Property(x => x.InputFingerprint).HasColumnType("char(64)").UseCollation(BindingSqlChecks.HashCollation).IsRequired();
        builder.HasOne<ProcessingOriginalInput>().WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.NoAction);
    }
}
