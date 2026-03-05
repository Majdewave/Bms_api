using Microsoft.EntityFrameworkCore.Migrations;

namespace Clienta.Api.Migrations
{
    public partial class FixClientTenantId : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"UPDATE Clients SET TenantId = '40aff269-58db-4d97-b391-ccbb701cd458' WHERE TenantId = '00000000-0000-0000-0000-000000000000';");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No down migration needed for data fix
        }
    }
}
