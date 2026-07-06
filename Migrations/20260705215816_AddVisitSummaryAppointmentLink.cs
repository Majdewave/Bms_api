using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clienta.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddVisitSummaryAppointmentLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AppointmentId",
                table: "VisitSummaries",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_VisitSummaries_AppointmentId",
                table: "VisitSummaries",
                column: "AppointmentId");

            migrationBuilder.AddForeignKey(
                name: "FK_VisitSummaries_Appointments_AppointmentId",
                table: "VisitSummaries",
                column: "AppointmentId",
                principalTable: "Appointments",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_VisitSummaries_Appointments_AppointmentId",
                table: "VisitSummaries");

            migrationBuilder.DropIndex(
                name: "IX_VisitSummaries_AppointmentId",
                table: "VisitSummaries");

            migrationBuilder.DropColumn(
                name: "AppointmentId",
                table: "VisitSummaries");
        }
    }
}
