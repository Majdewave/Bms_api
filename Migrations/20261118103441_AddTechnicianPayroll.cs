using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clienta.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTechnicianPayroll : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PerformingTechnicianId",
                table: "ImagingOrders",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TechnicianRates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TechnicianUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Modality = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TechnicianRates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TechnicianRates_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TechnicianRates_Users_TechnicianUserId",
                        column: x => x.TechnicianUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TechnicianWorkItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ImagingOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    ImagingStudyId = table.Column<Guid>(type: "uuid", nullable: false),
                    TechnicianUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PerformedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OriginalModality = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PayrollModality = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    AccessionNumber = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RateAmount = table.Column<decimal>(type: "numeric", nullable: true),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TechnicianWorkItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TechnicianWorkItems_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TechnicianWorkItems_Users_TechnicianUserId",
                        column: x => x.TechnicianUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ImagingOrders_PerformingTechnicianId",
                table: "ImagingOrders",
                column: "PerformingTechnicianId");

            migrationBuilder.CreateIndex(
                name: "IX_TechnicianRates_TechnicianUserId",
                table: "TechnicianRates",
                column: "TechnicianUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TechnicianRates_TenantId_TechnicianUserId_Modality",
                table: "TechnicianRates",
                columns: new[] { "TenantId", "TechnicianUserId", "Modality" },
                unique: true,
                filter: "\"IsActive\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_TechnicianWorkItems_TechnicianUserId",
                table: "TechnicianWorkItems",
                column: "TechnicianUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TechnicianWorkItems_TenantId_ImagingStudyId",
                table: "TechnicianWorkItems",
                columns: new[] { "TenantId", "ImagingStudyId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TechnicianWorkItems_TenantId_TechnicianUserId_PerformedAt",
                table: "TechnicianWorkItems",
                columns: new[] { "TenantId", "TechnicianUserId", "PerformedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_ImagingOrders_Users_PerformingTechnicianId",
                table: "ImagingOrders",
                column: "PerformingTechnicianId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ImagingOrders_Users_PerformingTechnicianId",
                table: "ImagingOrders");

            migrationBuilder.DropTable(
                name: "TechnicianRates");

            migrationBuilder.DropTable(
                name: "TechnicianWorkItems");

            migrationBuilder.DropIndex(
                name: "IX_ImagingOrders_PerformingTechnicianId",
                table: "ImagingOrders");

            migrationBuilder.DropColumn(
                name: "PerformingTechnicianId",
                table: "ImagingOrders");
        }
    }
}
