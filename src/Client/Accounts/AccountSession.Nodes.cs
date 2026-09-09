using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Accounts;

public sealed record NodeListing(Guid NodeId, string Name, string Region, string PublicControlUri, int ProtocolVersion,
    string BuildVersion, string ContentHash, int Capacity, int OnlineUsers, int LobbyCount, int ActiveMatches,
    string TrustClass, DateTimeOffset LastHeartbeat, string[]? MapKeys = null);
public sealed record NodeAdmissionTicket(string Ticket, DateTimeOffset ExpiresAt, Guid NodeId, string PublicControlUri)
{
    public override string ToString() => $"Node admission for {NodeId}";
}
public sealed partial class AccountSession
{
    public async Task<NodeListing[]> GetNodesAsync(int protocol, string build, string content, CancellationToken cancel = default)
    {
        var entries = await SendAsync<NodeListing[]>(HttpMethod.Get,
            $"v1/nodes?protocol={protocol}&build={Uri.EscapeDataString(build)}&content={Uri.EscapeDataString(content)}", null, null, cancel).ConfigureAwait(false);
        if (entries.Length > 10000 || entries.Any(x => x is null || x.NodeId == Guid.Empty || !ValidNodeUri(x.PublicControlUri)
            || x.ProtocolVersion != protocol || x.BuildVersion != build || x.ContentHash != content
            || !ValidMapKeys(x.MapKeys)))
            throw new InvalidOperationException("Backend returned an invalid Node directory.");
        return entries;
    }
    public async Task<NodeAdmissionTicket> GetNodeTicketAsync(Guid nodeId, CancellationToken cancel = default)
    {
        if (nodeId == Guid.Empty) throw new ArgumentException("A Node identity is required.", nameof(nodeId));
        var ticket = await SendAsync<NodeAdmissionTicket>(HttpMethod.Post, "v1/node-admissions", new { nodeId },
            await AccessTokenAsync(cancel).ConfigureAwait(false), cancel).ConfigureAwait(false);
        return ValidateNodeAdmission(ticket, nodeId);
    }
    public async Task<NodeAdmissionTicket> GetGuestNodeTicketAsync(Guid nodeId, string displayName,
        CancellationToken cancel = default)
    {
        if (nodeId == Guid.Empty) throw new ArgumentException("A Node identity is required.", nameof(nodeId));
        displayName = ValidateGuestDisplayName(displayName);
        var ticket = await SendAsync<NodeAdmissionTicket>(HttpMethod.Post,
            "v1/guest-node-admissions", new { nodeId, displayName }, null, cancel).ConfigureAwait(false);
        return ValidateNodeAdmission(ticket, nodeId);
    }
    private NodeAdmissionTicket ValidateNodeAdmission(NodeAdmissionTicket ticket, Guid nodeId)
    {
        DateTimeOffset now = _time.GetUtcNow();
        if (ticket.NodeId != nodeId || !ValidNodeUri(ticket.PublicControlUri) || ticket.ExpiresAt <= now
            || ticket.ExpiresAt > now.AddSeconds(120) || ticket.Ticket is not { Length: > 0 and <= 4096 })
            throw new InvalidOperationException("Backend returned an invalid Node admission.");
        return ticket;
    }
    internal static string ValidateGuestDisplayName(string? displayName)
    {
        string value = displayName?.Trim() ?? throw new ArgumentNullException(nameof(displayName));
        if (value is not { Length: >= 1 and <= 16 } || value.Any(c => c is < ' ' or > '~'))
            throw new ArgumentException("Guest display names must be 1 to 16 printable ASCII characters.", nameof(displayName));
        return value;
    }
    internal static bool ValidNodeUri(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == "wss" && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0;

    internal static bool ValidMapKeys(string[]? mapKeys)
        => mapKeys is null || (mapKeys.Length <= 256
            && mapKeys.All(key => key is { Length: > 0 and <= 128 } && !string.IsNullOrWhiteSpace(key)
                && key.All(c => c is >= ' ' and <= '~'))
            && mapKeys.Distinct(StringComparer.Ordinal).Count() == mapKeys.Length);
}
