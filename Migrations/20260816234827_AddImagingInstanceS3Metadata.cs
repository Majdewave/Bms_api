using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clienta.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddImagingInstanceS3Metadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "S3Bucket",
                table: "ImagingInstances",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "S3ETag",
                table: "ImagingInstances",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "S3Key",
                table: "ImagingInstances",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "S3UploadedAt",
                table: "ImagingInstances",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "S3Bucket",
                table: "ImagingInstances");

            migrationBuilder.DropColumn(
                name: "S3ETag",
                table: "ImagingInstances");

            migrationBuilder.DropColumn(
                name: "S3Key",
                table: "ImagingInstances");

            migrationBuilder.DropColumn(
                name: "S3UploadedAt",
                table: "ImagingInstances");
        }
    }
}
