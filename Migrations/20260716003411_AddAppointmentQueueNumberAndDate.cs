using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clienta.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointmentQueueNumberAndDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AppointmentDate",
                table: "Appointments",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "QueueNumber",
                table: "Appointments",
                type: "integer",
                nullable: true);

            migrationBuilder.Sql(@"
                UPDATE ""Appointments""
                SET ""AppointmentDate"" = DATE_TRUNC('day', ""StartTime"");
            ");

            migrationBuilder.Sql(@"
                WITH waiting_ranked AS (
                    SELECT
                        ""Id"",
                        ROW_NUMBER() OVER (
                            PARTITION BY ""TenantId"", ""AppointmentDate""
                            ORDER BY ""StartTime"", ""Id""
                        ) AS rn
                    FROM ""Appointments""
                    WHERE ""Status"" = 'Waiting'
                )
                UPDATE ""Appointments"" a
                SET ""QueueNumber"" = wr.rn
                FROM waiting_ranked wr
                WHERE a.""Id"" = wr.""Id"";
            ");

            migrationBuilder.AlterColumn<DateTime>(
                name: "AppointmentDate",
                table: "Appointments",
                type: "date",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "date",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_Tenant_AppointmentDate_Status_QueueNumber",
                table: "Appointments",
                columns: new[] { "TenantId", "AppointmentDate", "Status", "QueueNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Appointments_Tenant_AppointmentDate_Status_QueueNumber",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "AppointmentDate",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "QueueNumber",
                table: "Appointments");
        }
    }
}
