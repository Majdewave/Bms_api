namespace Clienta.Api.Infrastructure.TeamChat.Models;

public sealed record TeamChatConnectionRemovalResult(
    Guid TenantId,
    Guid UserId,
    bool UserStillOnline,
    int TenantOnlineUsersCount
);
