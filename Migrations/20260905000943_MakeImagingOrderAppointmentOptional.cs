using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clienta.Api.Migrations
{
    /// <inheritdoc />
    public partial class MakeImagingOrderAppointmentOptional : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ImagingOrders_Appointments_AppointmentId",
                table: "ImagingOrders");

            migrationBuilder.AlterColumn<Guid>(
                name: "AppointmentId",
                table: "ImagingOrders",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddForeignKey(
                name: "FK_ImagingOrders_Appointments_AppointmentId",
                table: "ImagingOrders",
                column: "AppointmentId",
                principalTable: "Appointments",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ImagingOrders_Appointments_AppointmentId",
                table: "ImagingOrders");

            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM ""ImagingOrders"" WHERE ""AppointmentId"" IS NULL) THEN
                        RAISE EXCEPTION 'Cannot restore required ImagingOrders.AppointmentId while detached imaging orders exist.';
                    END IF;
                END $$;");

            migrationBuilder.AlterColumn<Guid>(
                name: "AppointmentId",
                table: "ImagingOrders",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ImagingOrders_Appointments_AppointmentId",
                table: "ImagingOrders",
                column: "AppointmentId",
                principalTable: "Appointments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
