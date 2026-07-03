namespace Clienta.Api.Infrastructure.TeamChat.Models;

public sealed record TeamChatConnection(
    Guid TenantId,
    Guid UserId,
    string FullName,
    string ConnectionId,
    DateTime ConnectedAt
);
