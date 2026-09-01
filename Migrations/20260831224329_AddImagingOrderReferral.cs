using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clienta.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddImagingOrderReferral : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReferringDoctorName",
                table: "ImagingOrders",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ImagingOrderDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId = table.Column<Guid>(type: "uuid", nullable: false),
                    ImagingOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentType = table.Column<string>(type: "text", nullable: false),
                    OriginalFileName = table.Column<string>(type: "text", nullable: false),
                    ContentType = table.Column<string>(type: "text", nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    FileData = table.Column<byte[]>(type: "bytea", nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImagingOrderDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImagingOrderDocuments_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ImagingOrderDocuments_ImagingOrders_ImagingOrderId",
                        column: x => x.ImagingOrderId,
                        principalTable: "ImagingOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ImagingOrderDocuments_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ImagingOrderDocuments_Users_DeletedByUserId",
                        column: x => x.DeletedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ImagingOrderDocuments_Users_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ImagingOrderDocuments_ClientId",
                table: "ImagingOrderDocuments",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_ImagingOrderDocuments_DeletedByUserId",
                table: "ImagingOrderDocuments",
                column: "DeletedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ImagingOrderDocuments_ImagingOrderId_DocumentType",
                table: "ImagingOrderDocuments",
                columns: new[] { "ImagingOrderId", "DocumentType" },
                unique: true,
                filter: "\"DocumentType\" = 'Referral' AND \"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_ImagingOrderDocuments_TenantId",
                table: "ImagingOrderDocuments",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ImagingOrderDocuments_UploadedByUserId",
                table: "ImagingOrderDocuments",
                column: "UploadedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ImagingOrderDocuments");

            migrationBuilder.DropColumn(
                name: "ReferringDoctorName",
                table: "ImagingOrders");
        }
    }
}
