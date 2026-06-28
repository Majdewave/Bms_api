using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clienta.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointmentsConsentIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
        CREATE INDEX IF NOT EXISTS "IX_Appointments_Tenant_Status_StartTime"
        ON "Appointments" ("TenantId", "Status", "StartTime");
    """);

            migrationBuilder.Sql("""
        CREATE INDEX IF NOT EXISTS "IX_ClientConsents_AppointmentId"
        ON "ClientConsents" ("AppointmentId");
    """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
        DROP INDEX IF EXISTS "IX_Appointments_Tenant_Status_StartTime";
    """);

            migrationBuilder.Sql("""
        DROP INDEX IF EXISTS "IX_ClientConsents_AppointmentId";
    """);
        }
    }
}
