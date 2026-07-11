using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clienta.Api.Migrations
{
    /// <inheritdoc />
    public partial class RepairAppointmentDepartmentSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
                        migrationBuilder.Sql(@"
DO $$
DECLARE
        pre_null_count bigint;
        pre_invalid_count bigint;
        pre_will_update_count bigint;
        post_null_count bigint;
        post_invalid_count bigint;
        post_will_update_count bigint;
        updated_rows bigint;
BEGIN
        SELECT COUNT(*)
        INTO pre_null_count
        FROM ""Appointments"" a
        WHERE a.""DepartmentId"" IS NULL;

        SELECT COUNT(*)
        INTO pre_invalid_count
        FROM ""Appointments"" a
        WHERE a.""DepartmentId"" IS NOT NULL
            AND NOT EXISTS (
                    SELECT 1
                    FROM ""Departments"" d
                    WHERE d.""Id"" = a.""DepartmentId""
                        AND d.""TenantId"" = a.""TenantId""
            );

        SELECT COUNT(*)
        INTO pre_will_update_count
        FROM ""Appointments"" a
        JOIN ""Services"" s
            ON s.""Id"" = a.""ServiceId""
         AND s.""TenantId"" = a.""TenantId""
        WHERE s.""DepartmentId"" IS NOT NULL
            AND (
                    a.""DepartmentId"" IS NULL
                    OR NOT EXISTS (
                            SELECT 1
                            FROM ""Departments"" d
                            WHERE d.""Id"" = a.""DepartmentId""
                                AND d.""TenantId"" = a.""TenantId""
                    )
            );

        RAISE NOTICE '[RepairAppointmentDepartmentSnapshot][Before] NULL DepartmentId: %', pre_null_count;
        RAISE NOTICE '[RepairAppointmentDepartmentSnapshot][Before] Invalid DepartmentId: %', pre_invalid_count;
        RAISE NOTICE '[RepairAppointmentDepartmentSnapshot][Before] Will update: %', pre_will_update_count;

        UPDATE ""Appointments"" a
        SET ""DepartmentId"" = s.""DepartmentId""
        FROM ""Services"" s
        WHERE a.""ServiceId"" = s.""Id""
            AND a.""TenantId"" = s.""TenantId""
            AND s.""DepartmentId"" IS NOT NULL
            AND (
                    a.""DepartmentId"" IS NULL
                    OR NOT EXISTS (
                            SELECT 1
                            FROM ""Departments"" d
                            WHERE d.""Id"" = a.""DepartmentId""
                                AND d.""TenantId"" = a.""TenantId""
                    )
            );

        GET DIAGNOSTICS updated_rows = ROW_COUNT;
        RAISE NOTICE '[RepairAppointmentDepartmentSnapshot] Updated rows: %', updated_rows;

        SELECT COUNT(*)
        INTO post_null_count
        FROM ""Appointments"" a
        WHERE a.""DepartmentId"" IS NULL;

        SELECT COUNT(*)
        INTO post_invalid_count
        FROM ""Appointments"" a
        WHERE a.""DepartmentId"" IS NOT NULL
            AND NOT EXISTS (
                    SELECT 1
                    FROM ""Departments"" d
                    WHERE d.""Id"" = a.""DepartmentId""
                        AND d.""TenantId"" = a.""TenantId""
            );

        SELECT COUNT(*)
        INTO post_will_update_count
        FROM ""Appointments"" a
        JOIN ""Services"" s
            ON s.""Id"" = a.""ServiceId""
         AND s.""TenantId"" = a.""TenantId""
        WHERE s.""DepartmentId"" IS NOT NULL
            AND (
                    a.""DepartmentId"" IS NULL
                    OR NOT EXISTS (
                            SELECT 1
                            FROM ""Departments"" d
                            WHERE d.""Id"" = a.""DepartmentId""
                                AND d.""TenantId"" = a.""TenantId""
                    )
            );

        RAISE NOTICE '[RepairAppointmentDepartmentSnapshot][After] NULL DepartmentId: %', post_null_count;
        RAISE NOTICE '[RepairAppointmentDepartmentSnapshot][After] Invalid DepartmentId: %', post_invalid_count;
        RAISE NOTICE '[RepairAppointmentDepartmentSnapshot][After] Will update: %', post_will_update_count;
END
$$;
");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
                        // Intentionally no-op: this is a one-time data repair migration.

        }
    }
}
