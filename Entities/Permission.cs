namespace Clienta.Api.Entities;

public class Permission
{
    public Guid Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}
