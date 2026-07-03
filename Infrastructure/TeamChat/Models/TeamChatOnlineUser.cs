namespace Clienta.Api.Infrastructure.TeamChat.Models;

public sealed record TeamChatOnlineUser(
    Guid TenantId,
    Guid UserId,
    string FullName,
    IReadOnlyCollection<string> ConnectionIds,
    DateTime ConnectedAt
);
