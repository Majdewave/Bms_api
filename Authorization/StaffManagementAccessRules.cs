namespace Clienta.Api.Authorization;

public static class StaffManagementAccessRules
{
    public static bool CanManage(bool isBusinessTenant, bool isAdmin, bool hasManageStaffPermission) =>
        hasManageStaffPermission && (!isBusinessTenant || isAdmin);

    public static bool CanReadDirectory(bool isBusinessTenant, bool isStaff, bool hasManageStaffPermission) =>
        (isBusinessTenant && isStaff) || hasManageStaffPermission;
}