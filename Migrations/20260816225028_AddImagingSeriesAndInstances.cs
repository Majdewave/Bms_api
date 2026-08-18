using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clienta.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddImagingSeriesAndInstances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ImagingSeries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ImagingStudyId = table.Column<Guid>(type: "uuid", nullable: false),
                    SeriesInstanceUID = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Modality = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SeriesNumber = table.Column<int>(type: "integer", nullable: true),
                    SeriesDescription = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImagingSeries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImagingSeries_ImagingStudies_ImagingStudyId",
                        column: x => x.ImagingStudyId,
                        principalTable: "ImagingStudies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ImagingSeries_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ImagingInstances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ImagingStudyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ImagingSeriesId = table.Column<Guid>(type: "uuid", nullable: false),
                    SOPInstanceUID = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SOPClassUID = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    InstanceNumber = table.Column<int>(type: "integer", nullable: true),
                    LocalFilePath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    StorageStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImagingInstances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImagingInstances_ImagingSeries_ImagingSeriesId",
                        column: x => x.ImagingSeriesId,
                        principalTable: "ImagingSeries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ImagingInstances_ImagingStudies_ImagingStudyId",
                        column: x => x.ImagingStudyId,
                        principalTable: "ImagingStudies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ImagingInstances_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ImagingInstances_ImagingSeriesId",
                table: "ImagingInstances",
                column: "ImagingSeriesId");

            migrationBuilder.CreateIndex(
                name: "IX_ImagingInstances_ImagingStudyId",
                table: "ImagingInstances",
                column: "ImagingStudyId");

            migrationBuilder.CreateIndex(
                name: "IX_ImagingInstances_TenantId_SOPInstanceUID",
                table: "ImagingInstances",
                columns: new[] { "TenantId", "SOPInstanceUID" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImagingSeries_ImagingStudyId",
                table: "ImagingSeries",
                column: "ImagingStudyId");

            migrationBuilder.CreateIndex(
                name: "IX_ImagingSeries_TenantId_SeriesInstanceUID",
                table: "ImagingSeries",
                columns: new[] { "TenantId", "SeriesInstanceUID" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ImagingInstances");

            migrationBuilder.DropTable(
                name: "ImagingSeries");
        }
    }
}
