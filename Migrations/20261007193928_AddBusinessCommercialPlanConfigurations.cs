using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clienta.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessCommercialPlanConfigurations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CommercialPlanConfigurations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductType = table.Column<int>(type: "integer", nullable: false),
                    PlanType = table.Column<int>(type: "integer", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    MonthlyPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    AnnualPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    AvailableForPurchase = table.Column<bool>(type: "boolean", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommercialPlanConfigurations", x => x.Id);
                    table.CheckConstraint("CK_CommercialPlanConfigurations_AnnualPrice_NonNegative", "\"AnnualPrice\" IS NULL OR \"AnnualPrice\" >= 0");
                    table.CheckConstraint("CK_CommercialPlanConfigurations_DisplayOrder_NonNegative", "\"DisplayOrder\" >= 0");
                    table.CheckConstraint("CK_CommercialPlanConfigurations_MonthlyPrice_NonNegative", "\"MonthlyPrice\" IS NULL OR \"MonthlyPrice\" >= 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CommercialPlanConfigurations_ProductType_PlanType",
                table: "CommercialPlanConfigurations",
                columns: new[] { "ProductType", "PlanType" },
                unique: true);

            migrationBuilder.InsertData(
                table: "CommercialPlanConfigurations",
                columns: new[]
                {
                    "Id", "ProductType", "PlanType", "DisplayName", "Description",
                    "MonthlyPrice", "AnnualPrice", "Enabled", "AvailableForPurchase",
                    "DisplayOrder", "CreatedAt", "UpdatedAt"
                },
                values: new object[,]
                {
                    {
                        new Guid("9a210001-0000-4000-8000-000000000001"), 2, 0, "Business Trial", null,
                        null, null, true, false, 0,
                        new DateTime(2026, 10, 7, 19, 39, 28, DateTimeKind.Utc),
                        new DateTime(2026, 10, 7, 19, 39, 28, DateTimeKind.Utc)
                    },
                    {
                        new Guid("9a210001-0000-4000-8000-000000000002"), 2, 1, "Business Basic", null,
                        null, null, true, true, 1,
                        new DateTime(2026, 10, 7, 19, 39, 28, DateTimeKind.Utc),
                        new DateTime(2026, 10, 7, 19, 39, 28, DateTimeKind.Utc)
                    },
                    {
                        new Guid("9a210001-0000-4000-8000-000000000003"), 2, 2, "Business Pro", null,
                        null, null, true, false, 2,
                        new DateTime(2026, 10, 7, 19, 39, 28, DateTimeKind.Utc),
                        new DateTime(2026, 10, 7, 19, 39, 28, DateTimeKind.Utc)
                    }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CommercialPlanConfigurations");
        }
    }
}
