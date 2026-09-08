using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clienta.Api.Migrations
{
    /// <inheritdoc />
    public partial class ConvertInterpretationReportDocumentsToDatabaseStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "StorageKey",
                table: "InterpretationReportDocuments",
                newName: "FileName");

            migrationBuilder.AddColumn<byte[]>(
                name: "Content",
                table: "InterpretationReportDocuments",
                type: "bytea",
                nullable: false,
                defaultValue: new byte[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Content",
                table: "InterpretationReportDocuments");

            migrationBuilder.RenameColumn(
                name: "FileName",
                table: "InterpretationReportDocuments",
                newName: "StorageKey");
        }
    }
}
