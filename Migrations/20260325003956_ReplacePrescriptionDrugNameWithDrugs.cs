using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clienta.Api.Migrations
{
    /// <inheritdoc />
    public partial class ReplacePrescriptionDrugNameWithDrugs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "DrugName",
                table: "Prescriptions",
                newName: "Drugs");

            migrationBuilder.Sql(@"
                UPDATE Prescriptions
                SET Drugs = CASE
                    WHEN Drugs IS NULL OR TRIM(Drugs) = '' THEN '[]'
                    ELSE '[' || json_quote(Drugs) || ']'
                END;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Drugs",
                table: "Prescriptions",
                newName: "DrugName");

            migrationBuilder.Sql(@"
                UPDATE Prescriptions
                SET DrugName = COALESCE(json_extract(DrugName, '$[0]'), '');
            ");
        }
    }
}
