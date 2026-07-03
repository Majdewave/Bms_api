using Clienta.Api.Infrastructure.TeamChat.Dtos;
using Clienta.Api.Infrastructure.TeamChat.Models;

namespace Clienta.Api.Infrastructure.TeamChat.Mappers;

public static class TeamChatOnlineUserMapper
{
    public static TeamChatOnlineUserDto ToDto(this TeamChatOnlineUser user)
    {
        return new TeamChatOnlineUserDto(
            user.UserId,
            user.FullName,
            user.ConnectedAt
        );
    }
}
