using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clienta.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddInterpretationReportDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InterpretationReportDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    InterpretationRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    InterpretationReportId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    StorageKey = table.Column<string>(type: "text", nullable: false),
                    ContentType = table.Column<string>(type: "text", nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InterpretationReportDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InterpretationReportDocuments_InterpretationReports_Interpr~",
                        column: x => x.InterpretationReportId,
                        principalTable: "InterpretationReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InterpretationReportDocuments_InterpretationRequests_Interp~",
                        column: x => x.InterpretationRequestId,
                        principalTable: "InterpretationRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InterpretationReportDocuments_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InterpretationReportDocuments_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InterpretationReportDocuments_Users_DeletedByUserId",
                        column: x => x.DeletedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InterpretationReportDocuments_CreatedByUserId",
                table: "InterpretationReportDocuments",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_InterpretationReportDocuments_DeletedByUserId",
                table: "InterpretationReportDocuments",
                column: "DeletedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_InterpretationReportDocuments_InterpretationReportId",
                table: "InterpretationReportDocuments",
                column: "InterpretationReportId");

            migrationBuilder.CreateIndex(
                name: "IX_InterpretationReportDocuments_InterpretationRequestId_Versi~",
                table: "InterpretationReportDocuments",
                columns: new[] { "InterpretationRequestId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InterpretationReportDocuments_TenantId",
                table: "InterpretationReportDocuments",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InterpretationReportDocuments");
        }
    }
}
