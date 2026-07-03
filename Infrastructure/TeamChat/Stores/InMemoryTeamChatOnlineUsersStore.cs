using System.Collections.Concurrent;
using Clienta.Api.Infrastructure.TeamChat.Interfaces;
using Clienta.Api.Infrastructure.TeamChat.Models;

namespace Clienta.Api.Infrastructure.TeamChat.Stores;

public sealed class InMemoryTeamChatOnlineUsersStore : ITeamChatOnlineUsersStore
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, OnlineUserRecord>> _tenantUsers = new();
    private readonly ConcurrentDictionary<string, ConnectionPointer> _connections = new(StringComparer.Ordinal);

    public void AddConnection(TeamChatConnection connection)
    {
        var usersByTenant = _tenantUsers.GetOrAdd(connection.TenantId, _ => new ConcurrentDictionary<Guid, OnlineUserRecord>());

        var userRecord = usersByTenant.GetOrAdd(connection.UserId, _ => new OnlineUserRecord(
            connection.FullName,
            connection.ConnectedAt
        ));

        lock (userRecord.SyncRoot)
        {
            if (!string.IsNullOrWhiteSpace(connection.FullName))
            {
                userRecord.FullName = connection.FullName;
            }

            if (connection.ConnectedAt < userRecord.ConnectedAt)
            {
                userRecord.ConnectedAt = connection.ConnectedAt;
            }

            userRecord.ConnectionIds.Add(connection.ConnectionId);
        }

        _connections[connection.ConnectionId] = new ConnectionPointer(connection.TenantId, connection.UserId);
    }

    public TeamChatConnectionRemovalResult? RemoveConnection(string connectionId)
    {
        if (!_connections.TryRemove(connectionId, out var pointer))
        {
            return null;
        }

        if (!_tenantUsers.TryGetValue(pointer.TenantId, out var usersByTenant))
        {
            return null;
        }

        if (!usersByTenant.TryGetValue(pointer.UserId, out var userRecord))
        {
            return null;
        }

        var removeUser = false;
        var userStillOnline = false;

        lock (userRecord.SyncRoot)
        {
            userRecord.ConnectionIds.Remove(connectionId);
            removeUser = userRecord.ConnectionIds.Count == 0;
            userStillOnline = !removeUser;
        }

        var tenantOnlineUsersCount = usersByTenant.Count;

        if (removeUser)
        {
            usersByTenant.TryRemove(pointer.UserId, out _);
            tenantOnlineUsersCount = usersByTenant.Count;

            if (usersByTenant.IsEmpty)
            {
                _tenantUsers.TryRemove(pointer.TenantId, out _);
                tenantOnlineUsersCount = 0;
            }
        }

        return new TeamChatConnectionRemovalResult(
            pointer.TenantId,
            pointer.UserId,
            userStillOnline,
            tenantOnlineUsersCount
        );
    }

    public IReadOnlyCollection<TeamChatOnlineUser> GetOnlineUsers(Guid tenantId)
    {
        if (!_tenantUsers.TryGetValue(tenantId, out var usersByTenant))
        {
            return Array.Empty<TeamChatOnlineUser>();
        }

        var snapshot = new List<TeamChatOnlineUser>(usersByTenant.Count);

        foreach (var (userId, record) in usersByTenant)
        {
            string fullName;
            DateTime connectedAt;
            List<string> connectionIds;

            lock (record.SyncRoot)
            {
                fullName = record.FullName;
                connectedAt = record.ConnectedAt;
                connectionIds = record.ConnectionIds.ToList();
            }

            if (connectionIds.Count == 0)
            {
                continue;
            }

            snapshot.Add(new TeamChatOnlineUser(
                tenantId,
                userId,
                fullName,
                connectionIds,
                connectedAt
            ));
        }

        return snapshot
            .OrderBy(user => user.FullName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(user => user.ConnectedAt)
            .ToArray();
    }

    public bool IsUserOnline(Guid tenantId, Guid userId)
    {
        if (!_tenantUsers.TryGetValue(tenantId, out var usersByTenant))
        {
            return false;
        }

        if (!usersByTenant.TryGetValue(userId, out var record))
        {
            return false;
        }

        lock (record.SyncRoot)
        {
            return record.ConnectionIds.Count > 0;
        }
    }

    public IReadOnlyCollection<string> GetConnectionIds(Guid tenantId, Guid userId)
    {
        if (!_tenantUsers.TryGetValue(tenantId, out var usersByTenant))
        {
            return Array.Empty<string>();
        }

        if (!usersByTenant.TryGetValue(userId, out var record))
        {
            return Array.Empty<string>();
        }

        lock (record.SyncRoot)
        {
            return record.ConnectionIds.ToArray();
        }
    }

    private sealed class OnlineUserRecord
    {
        public OnlineUserRecord(string fullName, DateTime connectedAt)
        {
            FullName = fullName;
            ConnectedAt = connectedAt;
        }

        public object SyncRoot { get; } = new();
        public string FullName { get; set; }
        public DateTime ConnectedAt { get; set; }
        public HashSet<string> ConnectionIds { get; } = new(StringComparer.Ordinal);
    }

    private sealed record ConnectionPointer(Guid TenantId, Guid UserId);
}
