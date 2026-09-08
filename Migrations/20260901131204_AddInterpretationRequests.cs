using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clienta.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddInterpretationRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InterpretationRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ImagingOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedInterpreterId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InterpretationRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InterpretationRequests_ImagingOrders_ImagingOrderId",
                        column: x => x.ImagingOrderId,
                        principalTable: "ImagingOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InterpretationRequests_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InterpretationRequests_Users_AssignedInterpreterId",
                        column: x => x.AssignedInterpreterId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InterpretationRequests_Users_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InterpretationRequests_AssignedInterpreterId",
                table: "InterpretationRequests",
                column: "AssignedInterpreterId");

            migrationBuilder.CreateIndex(
                name: "IX_InterpretationRequests_ImagingOrderId",
                table: "InterpretationRequests",
                column: "ImagingOrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InterpretationRequests_RequestedByUserId",
                table: "InterpretationRequests",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_InterpretationRequests_TenantId_Status_RequestedAt",
                table: "InterpretationRequests",
                columns: new[] { "TenantId", "Status", "RequestedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InterpretationRequests");
        }
    }
}
