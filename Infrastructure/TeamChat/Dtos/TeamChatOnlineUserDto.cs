namespace Clienta.Api.Infrastructure.TeamChat.Dtos;

public sealed record TeamChatOnlineUserDto(
    Guid UserId,
    string FullName,
    DateTime ConnectedAt
);
