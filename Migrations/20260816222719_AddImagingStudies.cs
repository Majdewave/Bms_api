using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clienta.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddImagingStudies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ImagingStudies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId = table.Column<Guid>(type: "uuid", nullable: false),
                    ImagingOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    AccessionNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    StudyInstanceUID = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Modality = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LocalStoragePath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    StorageStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImagingStudies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImagingStudies_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ImagingStudies_ImagingOrders_ImagingOrderId",
                        column: x => x.ImagingOrderId,
                        principalTable: "ImagingOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ImagingStudies_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ImagingStudies_ClientId",
                table: "ImagingStudies",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_ImagingStudies_ImagingOrderId",
                table: "ImagingStudies",
                column: "ImagingOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ImagingStudies_TenantId_AccessionNumber",
                table: "ImagingStudies",
                columns: new[] { "TenantId", "AccessionNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_ImagingStudies_TenantId_ClientId_ReceivedAt",
                table: "ImagingStudies",
                columns: new[] { "TenantId", "ClientId", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ImagingStudies_TenantId_StudyInstanceUID",
                table: "ImagingStudies",
                columns: new[] { "TenantId", "StudyInstanceUID" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ImagingStudies");
        }
    }
}
