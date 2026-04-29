namespace Clienta.Api.Entities;

public class UserPermission
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }
    public virtual User User { get; set; } = null!;

    public Guid PermissionId { get; set; }
    public virtual Permission Permission { get; set; } = null!;
}
