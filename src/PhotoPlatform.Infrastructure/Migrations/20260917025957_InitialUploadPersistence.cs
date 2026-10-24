using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhotoPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialUploadPersistence : Migration
    {
        /// <summary>建立 Upload 所需三張資料表，依序加入欄位、外鍵與索引。</summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "batches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TotalCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    ProcessedCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    SuccessCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    FailedCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    Status = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_batches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "images",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    StoredPath = table.Column<string>(type: "varchar(500)", unicode: false, maxLength: 500, nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    MimeType = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    NewFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    SHA256 = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    TakenAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    CameraModel = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ISO = table.Column<int>(type: "int", nullable: true),
                    ShutterSpeed = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    Aperture = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    Latitude = table.Column<decimal>(type: "decimal(10,7)", precision: 10, scale: 7, nullable: true),
                    Longitude = table.Column<decimal>(type: "decimal(10,7)", precision: 10, scale: 7, nullable: true),
                    LocationName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_images", x => x.Id);
                    table.ForeignKey(
                        name: "FK_images_batches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "batches",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "processing_jobs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ImageId = table.Column<long>(type: "bigint", nullable: false),
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Workflow = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    RetryCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    ErrorCode = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processing_jobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_processing_jobs_batches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "batches",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_processing_jobs_images_ImageId",
                        column: x => x.ImageId,
                        principalTable: "images",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_batches_Status",
                table: "batches",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_images_BatchId",
                table: "images",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_images_SHA256",
                table: "images",
                column: "SHA256");

            migrationBuilder.CreateIndex(
                name: "IX_images_Status",
                table: "images",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_images_TakenAt",
                table: "images",
                column: "TakenAt");

            migrationBuilder.CreateIndex(
                name: "IX_processing_jobs_BatchId",
                table: "processing_jobs",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_processing_jobs_ImageId",
                table: "processing_jobs",
                column: "ImageId");

            migrationBuilder.CreateIndex(
                name: "IX_processing_jobs_ImageId_Workflow",
                table: "processing_jobs",
                columns: new[] { "ImageId", "Workflow" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_processing_jobs_Status",
                table: "processing_jobs",
                column: "Status");
        }

        /// <summary>先移除相依的工作與圖片表，最後移除批次表，以反向還原 Migration。</summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "processing_jobs");

            migrationBuilder.DropTable(
                name: "images");

            migrationBuilder.DropTable(
                name: "batches");
        }
    }
}
