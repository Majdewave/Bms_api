using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clienta.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddStaffWorkSummaryFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InterpreterRates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    InterpreterUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Modality = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InterpreterRates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InterpreterRates_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InterpreterRates_Users_InterpreterUserId",
                        column: x => x.InterpreterUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InterpreterWorkItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    InterpretationRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    ImagingOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    ImagingStudyId = table.Column<Guid>(type: "uuid", nullable: false),
                    InterpreterUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OriginalModality = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PayrollModality = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    AccessionNumber = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RateAmount = table.Column<decimal>(type: "numeric", nullable: true),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InterpreterWorkItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InterpreterWorkItems_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InterpreterWorkItems_Users_InterpreterUserId",
                        column: x => x.InterpreterUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StaffWorkSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClockInAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClockOutAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClockOutByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastCorrectedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastCorrectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CorrectionReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffWorkSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StaffWorkSessions_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StaffWorkSessions_Users_ClockOutByUserId",
                        column: x => x.ClockOutByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StaffWorkSessions_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StaffWorkSessions_Users_LastCorrectedByUserId",
                        column: x => x.LastCorrectedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StaffWorkSessions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InterpreterRates_InterpreterUserId",
                table: "InterpreterRates",
                column: "InterpreterUserId");

            migrationBuilder.CreateIndex(
                name: "IX_InterpreterRates_TenantId_InterpreterUserId_Modality",
                table: "InterpreterRates",
                columns: new[] { "TenantId", "InterpreterUserId", "Modality" },
                unique: true,
                filter: "\"IsActive\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_InterpreterWorkItems_InterpreterUserId",
                table: "InterpreterWorkItems",
                column: "InterpreterUserId");

            migrationBuilder.CreateIndex(
                name: "IX_InterpreterWorkItems_TenantId_InterpretationRequestId",
                table: "InterpreterWorkItems",
                columns: new[] { "TenantId", "InterpretationRequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InterpreterWorkItems_TenantId_InterpreterUserId_CompletedAt",
                table: "InterpreterWorkItems",
                columns: new[] { "TenantId", "InterpreterUserId", "CompletedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_StaffWorkSessions_ClockOutByUserId",
                table: "StaffWorkSessions",
                column: "ClockOutByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffWorkSessions_CreatedByUserId",
                table: "StaffWorkSessions",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffWorkSessions_LastCorrectedByUserId",
                table: "StaffWorkSessions",
                column: "LastCorrectedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffWorkSessions_TenantId_UserId",
                table: "StaffWorkSessions",
                columns: new[] { "TenantId", "UserId" },
                unique: true,
                filter: "\"ClockOutAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StaffWorkSessions_TenantId_UserId_ClockInAt",
                table: "StaffWorkSessions",
                columns: new[] { "TenantId", "UserId", "ClockInAt" });

            migrationBuilder.CreateIndex(
                name: "IX_StaffWorkSessions_UserId",
                table: "StaffWorkSessions",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InterpreterRates");

            migrationBuilder.DropTable(
                name: "InterpreterWorkItems");

            migrationBuilder.DropTable(
                name: "StaffWorkSessions");
        }
    }
}
