using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clienta.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddImagingStudyToInterpretationRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ImagingStudyId",
                table: "InterpretationRequests",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM ""InterpretationRequests"") THEN
        RAISE EXCEPTION 'Cannot add required ImagingStudyId while InterpretationRequests contain existing rows. Backfill each request with its explicitly selected study before applying this migration.';
    END IF;
END $$;");

            migrationBuilder.AlterColumn<Guid>(
                name: "ImagingStudyId",
                table: "InterpretationRequests",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_InterpretationRequests_ImagingStudyId",
                table: "InterpretationRequests",
                column: "ImagingStudyId");

            migrationBuilder.AddForeignKey(
                name: "FK_InterpretationRequests_ImagingStudies_ImagingStudyId",
                table: "InterpretationRequests",
                column: "ImagingStudyId",
                principalTable: "ImagingStudies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InterpretationRequests_ImagingStudies_ImagingStudyId",
                table: "InterpretationRequests");

            migrationBuilder.DropIndex(
                name: "IX_InterpretationRequests_ImagingStudyId",
                table: "InterpretationRequests");

            migrationBuilder.DropColumn(
                name: "ImagingStudyId",
                table: "InterpretationRequests");
        }
    }
}
