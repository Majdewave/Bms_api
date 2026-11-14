using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clienta.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddInterpretationReportTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InterpretationReportTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    InterpreterUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Modality = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    Content = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InterpretationReportTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InterpretationReportTemplates_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InterpretationReportTemplates_Users_InterpreterUserId",
                        column: x => x.InterpreterUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InterpretationReportTemplates_InterpreterUserId",
                table: "InterpretationReportTemplates",
                column: "InterpreterUserId");

            migrationBuilder.CreateIndex(
                name: "IX_InterpretationReportTemplates_TenantId_InterpreterUserId_Is~",
                table: "InterpretationReportTemplates",
                columns: new[] { "TenantId", "InterpreterUserId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_InterpretationReportTemplates_TenantId_InterpreterUserId_Mo~",
                table: "InterpretationReportTemplates",
                columns: new[] { "TenantId", "InterpreterUserId", "Modality", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InterpretationReportTemplates");
        }
    }
}
