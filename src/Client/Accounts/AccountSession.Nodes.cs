using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Accounts;

public sealed record NodeListing(Guid NodeId, string Name, string Region, string PublicControlUri, int ProtocolVersion,
    string BuildVersion, string ContentHash, int Capacity, int OnlineUsers, int LobbyCount, int ActiveMatches,
    string TrustClass, DateTimeOffset LastHeartbeat);
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
        if (entries.Length > 10000 || entries.Any(x => x.NodeId == Guid.Empty || !ValidNodeUri(x.PublicControlUri)
            || x.ProtocolVersion != protocol || x.BuildVersion != build || x.ContentHash != content))
            throw new InvalidOperationException("Backend returned an invalid Node directory.");
        return entries;
    }
    public async Task<NodeAdmissionTicket> GetNodeTicketAsync(Guid nodeId, CancellationToken cancel = default)
    {
        if (nodeId == Guid.Empty) throw new ArgumentException("A Node identity is required.", nameof(nodeId));
        var ticket = await SendAsync<NodeAdmissionTicket>(HttpMethod.Post, "v1/node-admissions", new { nodeId },
            await AccessTokenAsync(cancel).ConfigureAwait(false), cancel).ConfigureAwait(false);
        if (ticket.NodeId != nodeId || !ValidNodeUri(ticket.PublicControlUri) || ticket.ExpiresAt <= _time.GetUtcNow()
            || ticket.ExpiresAt > _time.GetUtcNow().AddSeconds(120) || ticket.Ticket is not { Length: > 0 and <= 4096 })
            throw new InvalidOperationException("Backend returned an invalid Node admission.");
        return ticket;
    }
    internal static bool ValidNodeUri(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == "wss" && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0;
}
