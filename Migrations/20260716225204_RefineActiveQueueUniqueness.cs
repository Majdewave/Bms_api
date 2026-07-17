using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clienta.Api.Migrations
{
    /// <inheritdoc />
    public partial class RefineActiveQueueUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Appointments_Tenant_AppointmentDate_Status_QueueNumber",
                table: "Appointments");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_Tenant_AppointmentDate_Status_QueueNumber",
                table: "Appointments",
                columns: new[] { "TenantId", "AppointmentDate", "QueueNumber" },
                unique: true,
                filter: "\"QueueNumber\" IS NOT NULL AND \"Status\" IN ('Scheduled','Waiting','InProgress')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Appointments_Tenant_AppointmentDate_Status_QueueNumber",
                table: "Appointments");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_Tenant_AppointmentDate_Status_QueueNumber",
                table: "Appointments",
                columns: new[] { "TenantId", "AppointmentDate", "Status", "QueueNumber" },
                unique: true);
        }
    }
}
