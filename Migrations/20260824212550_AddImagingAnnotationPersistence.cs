using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clienta.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddImagingAnnotationPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ImagingAnnotations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ImagingInstanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    FrameNumber = table.Column<int>(type: "integer", nullable: false),
                    AnnotationUid = table.Column<string>(type: "text", nullable: false),
                    ToolName = table.Column<string>(type: "text", nullable: false),
                    Geometry = table.Column<string>(type: "text", nullable: false),
                    Label = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImagingAnnotations", x => x.Id);
                    table.CheckConstraint("CK_ImagingAnnotations_FrameNumber_GreaterThanZero", "\"FrameNumber\" >= 1");
                    table.ForeignKey(
                        name: "FK_ImagingAnnotations_ImagingInstances_ImagingInstanceId",
                        column: x => x.ImagingInstanceId,
                        principalTable: "ImagingInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ImagingAnnotations_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ImagingAnnotations_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ImagingAnnotations_Users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ImagingAnnotations_CreatedByUserId",
                table: "ImagingAnnotations",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ImagingAnnotations_ImagingInstanceId",
                table: "ImagingAnnotations",
                column: "ImagingInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_ImagingAnnotations_TenantId_AnnotationUid",
                table: "ImagingAnnotations",
                columns: new[] { "TenantId", "AnnotationUid" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImagingAnnotations_TenantId_ImagingInstanceId_FrameNumber",
                table: "ImagingAnnotations",
                columns: new[] { "TenantId", "ImagingInstanceId", "FrameNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_ImagingAnnotations_UpdatedByUserId",
                table: "ImagingAnnotations",
                column: "UpdatedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ImagingAnnotations");
        }
    }
}
