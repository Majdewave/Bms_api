namespace Clienta.Api.Infrastructure.TeamChat.Dtos;

public sealed record TeamChatMessageDto(
    Guid Id,
    Guid SenderUserId,
    string SenderName,
    Guid RecipientUserId,
    string Text,
    DateTime SentAt
);