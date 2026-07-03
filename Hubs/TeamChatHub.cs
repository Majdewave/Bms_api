using Clienta.Api.Infrastructure.TeamChat.Constants;
using Clienta.Api.Infrastructure.TeamChat.Dtos;
using Clienta.Api.Infrastructure.TeamChat.Interfaces;
using Clienta.Api.Infrastructure.TeamChat.Mappers;
using Clienta.Api.Infrastructure.TeamChat.Models;
using Clienta.Api.Infrastructure.TeamChat.Utilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Clienta.Api.Hubs;

[Authorize]
public class TeamChatHub : Hub
{
    private readonly ITeamChatOnlineUsersStore _onlineUsersStore;
    private readonly ILogger<TeamChatHub> _logger;

    public TeamChatHub(ITeamChatOnlineUsersStore onlineUsersStore, ILogger<TeamChatHub> logger)
    {
        _onlineUsersStore = onlineUsersStore;
        _logger = logger;
    }

    private static string TenantGroup(Guid tenantId) => $"tenant:{tenantId:D}";

    public override async Task OnConnectedAsync()
    {
        if (!TryBuildConnection(out var connection))
        {
            _logger.LogWarning("TeamChat connect skipped: missing claims. ConnectionId={ConnectionId}", Context.ConnectionId);
            await base.OnConnectedAsync();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, TenantGroup(connection.TenantId));

        _onlineUsersStore.AddConnection(connection);

        _logger.LogInformation(
            "TeamChat user connected. TenantId={TenantId} UserId={UserId} ConnectionId={ConnectionId}",
            connection.TenantId,
            connection.UserId,
            connection.ConnectionId
        );

        await NotifyOnlineUsersChanged(connection.TenantId);

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var removal = _onlineUsersStore.RemoveConnection(Context.ConnectionId);

        if (removal is not null)
        {
            _logger.LogInformation(
                "TeamChat user disconnected. TenantId={TenantId} UserId={UserId} ConnectionId={ConnectionId}",
                removal.TenantId,
                removal.UserId,
                Context.ConnectionId
            );

            await NotifyOnlineUsersChanged(removal.TenantId);
        }

        await base.OnDisconnectedAsync(exception);
    }

    public Task<IReadOnlyCollection<TeamChatOnlineUserDto>> GetOnlineUsers()
    {
        if (!TeamChatClaimReader.TryReadTenantId(Context.User, out var tenantId))
        {
            return Task.FromResult<IReadOnlyCollection<TeamChatOnlineUserDto>>(Array.Empty<TeamChatOnlineUserDto>());
        }

        IReadOnlyCollection<TeamChatOnlineUserDto> users = _onlineUsersStore
            .GetOnlineUsers(tenantId)
            .Select(user => user.ToDto())
            .ToArray();

        return Task.FromResult(users);
    }

    public async Task SendMessage(Guid recipientUserId, string text)
    {
        if (!TryBuildConnection(out var senderConnection))
        {
            _logger.LogWarning("TeamChat message skipped: missing claims. ConnectionId={ConnectionId}", Context.ConnectionId);
            return;
        }

        if (recipientUserId == senderConnection.UserId)
        {
            return;
        }

        var normalizedText = (text ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(normalizedText))
        {
            return;
        }

        if (normalizedText.Length > 1000)
        {
            normalizedText = normalizedText[..1000];
        }

        var senderConnectionIds = _onlineUsersStore.GetConnectionIds(senderConnection.TenantId, senderConnection.UserId);
        var recipientConnectionIds = _onlineUsersStore.GetConnectionIds(senderConnection.TenantId, recipientUserId);

        var targetConnectionIds = senderConnectionIds
            .Concat(recipientConnectionIds)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (targetConnectionIds.Length == 0)
        {
            return;
        }

        var message = new TeamChatMessageDto(
            Guid.NewGuid(),
            senderConnection.UserId,
            senderConnection.FullName,
            recipientUserId,
            normalizedText,
            DateTime.UtcNow
        );

        await Clients.Clients(targetConnectionIds).SendAsync(TeamChatEventNames.ReceiveMessage, message);
    }

    private bool TryBuildConnection(out TeamChatConnection connection)
    {
        connection = default!;

        if (!TeamChatClaimReader.TryReadTenantId(Context.User, out var tenantId))
        {
            return false;
        }

        if (!TeamChatClaimReader.TryReadUserId(Context.User, out var userId))
        {
            return false;
        }

        var fullName = TeamChatClaimReader.ReadDisplayName(Context.User);

        connection = new TeamChatConnection(
            tenantId,
            userId,
            fullName,
            Context.ConnectionId,
            DateTime.UtcNow
        );

        return true;
    }

    private async Task NotifyOnlineUsersChanged(Guid tenantId)
    {
        var users = _onlineUsersStore
            .GetOnlineUsers(tenantId)
            .Select(user => user.ToDto())
            .ToArray();

        await Clients.Group(TenantGroup(tenantId)).SendAsync(TeamChatEventNames.OnlineUsersChanged, users);
    }
}
