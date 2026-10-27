using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhotoPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProcessingExecutionPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "image_processing_bindings",
                columns: table => new
                {
                    ImageId = table.Column<long>(type: "bigint", nullable: false),
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PrimaryJobId = table.Column<long>(type: "bigint", nullable: false),
                    PrimaryWorkflow = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    PrimaryStatus = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_image_processing_bindings", x => x.ImageId);
                    table.CheckConstraint("CK_image_processing_bindings_PrimaryStatus", "[PrimaryStatus] IN ('Pending', 'Processing', 'Completed', 'Failed')");
                    table.CheckConstraint("CK_image_processing_bindings_PrimaryWorkflow", "[PrimaryWorkflow] IN ('Naming', 'Analysis', 'DuplicateDetection', 'Full')");
                    table.ForeignKey(
                        name: "FK_image_processing_bindings_batches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "batches",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_image_processing_bindings_images_ImageId",
                        column: x => x.ImageId,
                        principalTable: "images",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_image_processing_bindings_processing_jobs_PrimaryJobId",
                        column: x => x.PrimaryJobId,
                        principalTable: "processing_jobs",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "processing_runs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    JobId = table.Column<long>(type: "bigint", nullable: false),
                    RunNo = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    WorkflowVersion = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    TraceId = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processing_runs", x => x.Id);
                    table.CheckConstraint("CK_processing_runs_Reason", "([Reason] = 'Initial' AND [RunNo] = 1) OR ([Reason] = 'ManualRetry' AND [RunNo] BETWEEN 2 AND 4)");
                    table.CheckConstraint("CK_processing_runs_RunNo", "[RunNo] BETWEEN 1 AND 4");
                    table.CheckConstraint("CK_processing_runs_Status", "[Status] IN ('Pending', 'Active', 'Completed', 'Failed')");
                    table.ForeignKey(
                        name: "FK_processing_runs_processing_jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "processing_jobs",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "processing_step_checkpoints",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RunId = table.Column<long>(type: "bigint", nullable: false),
                    Step = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    StepOrder = table.Column<int>(type: "int", nullable: false),
                    RuleVersion = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    InputFingerprint = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    RetryOrdinal = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    NextRetryAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    ResultReference = table.Column<string>(type: "varchar(500)", unicode: false, maxLength: 500, nullable: true),
                    ResultStatus = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    ErrorCode = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    ErrorClassification = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    DependencyWaitDeadlineAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    DependencyWaitStartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    DependencyWaitStatus = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processing_step_checkpoints", x => x.Id);
                    table.CheckConstraint("CK_processing_step_checkpoints_DependencyWait", "([DependencyWaitStartedAt] IS NULL AND [DependencyWaitDeadlineAt] IS NULL AND [DependencyWaitStatus] IS NULL)\nOR ([DependencyWaitStartedAt] IS NOT NULL AND [DependencyWaitDeadlineAt] IS NOT NULL\n    AND [DependencyWaitStatus] IS NOT NULL AND [DependencyWaitDeadlineAt] > [DependencyWaitStartedAt]\n    AND [DependencyWaitStatus] IN ('Waiting', 'Resolved', 'Expired')\n    AND [Step] = 'VERSION_ANALYSIS'\n    AND ([DependencyWaitStatus] <> 'Waiting' OR [Status] = 'Pending'))");
                    table.CheckConstraint("CK_processing_step_checkpoints_ErrorClassification", "[ErrorClassification] IS NULL OR [ErrorClassification] IN ('Transient', 'Permanent')");
                    table.CheckConstraint("CK_processing_step_checkpoints_ResultStatus", "[ResultStatus] IS NULL OR [ResultStatus] IN ('Success', 'Warning', 'Skipped')");
                    table.CheckConstraint("CK_processing_step_checkpoints_RetryOrdinal", "[RetryOrdinal] BETWEEN 0 AND 3");
                    table.CheckConstraint("CK_processing_step_checkpoints_Status", "[Status] IN ('Pending', 'Processing', 'RetryWaiting', 'Completed', 'Failed')");
                    table.CheckConstraint("CK_processing_step_checkpoints_StepOrder", "[StepOrder] > 0");
                    table.ForeignKey(
                        name: "FK_processing_step_checkpoints_processing_runs_RunId",
                        column: x => x.RunId,
                        principalTable: "processing_runs",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "processing_logs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StepCheckpointId = table.Column<long>(type: "bigint", nullable: false),
                    JobId = table.Column<long>(type: "bigint", nullable: false),
                    ImageId = table.Column<long>(type: "bigint", nullable: false),
                    AttemptNo = table.Column<int>(type: "int", nullable: false),
                    RetryOrdinal = table.Column<int>(type: "int", nullable: false),
                    ResumeNo = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    DurationMs = table.Column<long>(type: "bigint", nullable: true),
                    ErrorCode = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Timestamp = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    TraceId = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processing_logs", x => x.Id);
                    table.CheckConstraint("CK_processing_logs_AttemptNo", "[AttemptNo] > 0");
                    table.CheckConstraint("CK_processing_logs_DurationMs", "[DurationMs] IS NULL OR [DurationMs] >= 0");
                    table.CheckConstraint("CK_processing_logs_ResumeNo", "[ResumeNo] >= 0");
                    table.CheckConstraint("CK_processing_logs_RetryOrdinal", "[RetryOrdinal] BETWEEN 0 AND 3");
                    table.CheckConstraint("CK_processing_logs_Status", "[Status] IN ('Running', 'Success', 'Warning', 'Failed', 'Interrupted', 'Skipped')");
                    table.ForeignKey(
                        name: "FK_processing_logs_images_ImageId",
                        column: x => x.ImageId,
                        principalTable: "images",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_processing_logs_processing_jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "processing_jobs",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_processing_logs_processing_step_checkpoints_StepCheckpointId",
                        column: x => x.StepCheckpointId,
                        principalTable: "processing_step_checkpoints",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_image_processing_bindings_BatchId",
                table: "image_processing_bindings",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_image_processing_bindings_PrimaryJobId",
                table: "image_processing_bindings",
                column: "PrimaryJobId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_processing_logs_ImageId_Timestamp_Id",
                table: "processing_logs",
                columns: new[] { "ImageId", "Timestamp", "Id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_processing_logs_JobId_Timestamp_Id",
                table: "processing_logs",
                columns: new[] { "JobId", "Timestamp", "Id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_processing_logs_StepCheckpointId",
                table: "processing_logs",
                column: "StepCheckpointId",
                unique: true,
                filter: "[Status] = 'Running'");

            migrationBuilder.CreateIndex(
                name: "IX_processing_logs_StepCheckpointId_AttemptNo",
                table: "processing_logs",
                columns: new[] { "StepCheckpointId", "AttemptNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_processing_logs_StepCheckpointId_RetryOrdinal_ResumeNo",
                table: "processing_logs",
                columns: new[] { "StepCheckpointId", "RetryOrdinal", "ResumeNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_processing_runs_JobId",
                table: "processing_runs",
                column: "JobId",
                unique: true,
                filter: "[Status] IN ('Pending', 'Active')");

            migrationBuilder.CreateIndex(
                name: "IX_processing_runs_JobId_RunNo",
                table: "processing_runs",
                columns: new[] { "JobId", "RunNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_processing_step_checkpoints_RunId_Step",
                table: "processing_step_checkpoints",
                columns: new[] { "RunId", "Step" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_processing_step_checkpoints_RunId_StepOrder",
                table: "processing_step_checkpoints",
                columns: new[] { "RunId", "StepOrder" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "image_processing_bindings");

            migrationBuilder.DropTable(
                name: "processing_logs");

            migrationBuilder.DropTable(
                name: "processing_step_checkpoints");

            migrationBuilder.DropTable(
                name: "processing_runs");
        }
    }
}
