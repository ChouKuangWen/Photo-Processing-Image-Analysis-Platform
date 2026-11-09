using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhotoPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProcessingInputBindings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "processing_original_inputs",
                columns: table => new
                {
                    JobId = table.Column<long>(type: "bigint", nullable: false),
                    ContentVersion = table.Column<string>(type: "char(64)", nullable: false, collation: "Latin1_General_100_BIN2_UTF8"),
                    ContentLength = table.Column<long>(type: "bigint", nullable: false),
                    WorkflowVersion = table.Column<string>(type: "varchar(50)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processing_original_inputs", x => x.JobId);
                    table.CheckConstraint("CK_processing_original_inputs_ContentLength", "[ContentLength] >= 0");
                    table.CheckConstraint("CK_processing_original_inputs_ContentVersion", "DATALENGTH([ContentVersion]) = 64 AND LEN([ContentVersion]) = 64 AND [ContentVersion] COLLATE Latin1_General_100_BIN2_UTF8 NOT LIKE '%[^0-9a-f]%'");
                    table.CheckConstraint("CK_processing_original_inputs_WorkflowVersion", "LEN(LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE([WorkflowVersion], CHAR(9), ''), CHAR(10), ''), CHAR(11), ''), CHAR(12), ''), CHAR(13), ''), NCHAR(160), '')))) > 0");
                    table.ForeignKey(
                        name: "FK_processing_original_inputs_processing_jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "processing_jobs",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "processing_rule_manifest_bindings",
                columns: table => new
                {
                    JobId = table.Column<long>(type: "bigint", nullable: false),
                    WorkflowVersion = table.Column<string>(type: "varchar(50)", nullable: false),
                    ManifestId = table.Column<string>(type: "varchar(100)", nullable: false),
                    ManifestChecksum = table.Column<string>(type: "char(64)", nullable: false, collation: "Latin1_General_100_BIN2_UTF8")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processing_rule_manifest_bindings", x => x.JobId);
                    table.CheckConstraint("CK_processing_rule_manifest_bindings_Checksum", "DATALENGTH([ManifestChecksum]) = 64 AND LEN([ManifestChecksum]) = 64 AND [ManifestChecksum] COLLATE Latin1_General_100_BIN2_UTF8 NOT LIKE '%[^0-9a-f]%'");
                    table.CheckConstraint("CK_processing_rule_manifest_bindings_ManifestId", "LEN(LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE([ManifestId], CHAR(9), ''), CHAR(10), ''), CHAR(11), ''), CHAR(12), ''), CHAR(13), ''), NCHAR(160), '')))) > 0");
                    table.CheckConstraint("CK_processing_rule_manifest_bindings_WorkflowVersion", "LEN(LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE([WorkflowVersion], CHAR(9), ''), CHAR(10), ''), CHAR(11), ''), CHAR(12), ''), CHAR(13), ''), NCHAR(160), '')))) > 0");
                    table.ForeignKey(
                        name: "FK_processing_rule_manifest_bindings_processing_jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "processing_jobs",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "processing_original_step_inputs",
                columns: table => new
                {
                    JobId = table.Column<long>(type: "bigint", nullable: false),
                    Step = table.Column<string>(type: "varchar(50)", nullable: false),
                    RuleVersion = table.Column<string>(type: "varchar(50)", nullable: false),
                    UploadCreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    InputFingerprint = table.Column<string>(type: "char(64)", nullable: false, collation: "Latin1_General_100_BIN2_UTF8")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processing_original_step_inputs", x => new { x.JobId, x.Step });
                    table.CheckConstraint("CK_processing_original_step_inputs_Fingerprint", "DATALENGTH([InputFingerprint]) = 64 AND LEN([InputFingerprint]) = 64 AND [InputFingerprint] COLLATE Latin1_General_100_BIN2_UTF8 NOT LIKE '%[^0-9a-f]%'");
                    table.CheckConstraint("CK_processing_original_step_inputs_RuleVersion", "LEN(LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE([RuleVersion], CHAR(9), ''), CHAR(10), ''), CHAR(11), ''), CHAR(12), ''), CHAR(13), ''), NCHAR(160), '')))) > 0");
                    table.CheckConstraint("CK_processing_original_step_inputs_StepTime", "(Step COLLATE Latin1_General_100_BIN2 = 'EXIF_PARSING' AND DATALENGTH(Step) = 12 AND UploadCreatedAt IS NOT NULL)\nOR (Step COLLATE Latin1_General_100_BIN2 = 'SHA256_CALCULATION' AND DATALENGTH(Step) = 18 AND UploadCreatedAt IS NULL)");
                    table.ForeignKey(
                        name: "FK_processing_original_step_inputs_processing_original_inputs_JobId",
                        column: x => x.JobId,
                        principalTable: "processing_original_inputs",
                        principalColumn: "JobId");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "processing_original_step_inputs");

            migrationBuilder.DropTable(
                name: "processing_rule_manifest_bindings");

            migrationBuilder.DropTable(
                name: "processing_original_inputs");
        }
    }
}
