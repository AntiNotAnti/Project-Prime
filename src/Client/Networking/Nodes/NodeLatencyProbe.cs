using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Accounts;

namespace MphRead.Mods.Network;

/// <summary>Bounded discovery-time latency sampling. Results influence Node choice only;</summary>
/// <remarks>admission and every authoritative decision still occur on Backend/Node.</remarks>
internal static class NodeLatencyProbe
{
    internal const int MaximumCandidates = 8;
    internal static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(750);
    private static readonly HttpMessageInvoker Http = new(new SocketsHttpHandler
    {
        ConnectTimeout = TimeSpan.FromMilliseconds(500),
        PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    });

    internal static async Task<IReadOnlyDictionary<Guid, TimeSpan>> ProbeAsync(
        IEnumerable<NodeListing> nodes, string? preferredRegion,
        CancellationToken cancellationToken, HttpMessageInvoker? http = null)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        bool preferRegion = !string.IsNullOrWhiteSpace(preferredRegion)
            && !StringComparer.OrdinalIgnoreCase.Equals(preferredRegion, "Automatic");
        NodeListing[] candidates = nodes
            .Where(node => node.Capacity > node.OnlineUsers)
            .OrderByDescending(node => preferRegion
                && StringComparer.Ordinal.Equals(node.Region, preferredRegion))
            .ThenByDescending(node => node.LobbyCount > 0)
            .ThenBy(node => node.OnlineUsers)
            .ThenBy(node => node.NodeId)
            .Take(MaximumCandidates)
            .ToArray();
        if (candidates.Length == 0) return new Dictionary<Guid, TimeSpan>();

        var tasks = new Task<(Guid NodeId, TimeSpan? Rtt)>[candidates.Length];
        for (int i = 0; i < candidates.Length; i++)
            tasks[i] = ProbeOneAsync(candidates[i], http ?? Http, cancellationToken);
        (Guid NodeId, TimeSpan? Rtt)[] samples = await Task.WhenAll(tasks).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var result = new Dictionary<Guid, TimeSpan>(samples.Length);
        foreach ((Guid nodeId, TimeSpan? rtt) in samples)
            if (rtt is { } measured) result[nodeId] = measured;
        return result;
    }

    internal static Uri HealthUri(NodeListing node)
    {
        if (!Uri.TryCreate(node.PublicControlUri, UriKind.Absolute, out Uri? control)
            || control.Scheme != "wss" || control.UserInfo.Length != 0
            || control.Query.Length != 0 || control.Fragment.Length != 0)
            throw new ArgumentException("Node control URI is invalid.", nameof(node));
        return new UriBuilder(control) { Scheme = "https", Path = "/health", Query = "", Fragment = "" }.Uri;
    }

    private static async Task<(Guid NodeId, TimeSpan? Rtt)> ProbeOneAsync(
        NodeListing node, HttpMessageInvoker http, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, HealthUri(node));
            long started = Stopwatch.GetTimestamp();
            using HttpResponseMessage response = await http.SendAsync(request, timeout.Token).ConfigureAwait(false);
            return (node.NodeId, response.StatusCode == HttpStatusCode.OK
                ? Stopwatch.GetElapsedTime(started) : null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (node.NodeId, null);
        }
        catch (HttpRequestException)
        {
            return (node.NodeId, null);
        }
    }
}
