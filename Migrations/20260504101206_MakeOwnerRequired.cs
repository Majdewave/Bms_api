using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clienta.Api.Migrations
{
    /// <inheritdoc />
    public partial class MakeOwnerRequired : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "OwnerUserId",
                table: "Tenants",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "OwnerUserId",
                table: "Tenants",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");
        }
    }
}
