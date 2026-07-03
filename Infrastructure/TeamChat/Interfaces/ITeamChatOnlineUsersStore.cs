using Clienta.Api.Infrastructure.TeamChat.Models;

namespace Clienta.Api.Infrastructure.TeamChat.Interfaces;

public interface ITeamChatOnlineUsersStore
{
    void AddConnection(TeamChatConnection connection);
    TeamChatConnectionRemovalResult? RemoveConnection(string connectionId);
    IReadOnlyCollection<TeamChatOnlineUser> GetOnlineUsers(Guid tenantId);
    bool IsUserOnline(Guid tenantId, Guid userId);
    IReadOnlyCollection<string> GetConnectionIds(Guid tenantId, Guid userId);
}
