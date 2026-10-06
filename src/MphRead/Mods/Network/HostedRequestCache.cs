using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Cryptography;

namespace MphRead.Mods.Network;

/// <summary>Endpoint/nonce retries share the original result, including its owner
/// token. A bounded cache does not consume another child or substitute NAT identity.</summary>
internal sealed class HostedRequestCache
{
    internal const int Capacity = 2048;
    internal const double LifetimeSeconds = 180;
    private readonly Dictionary<(IPEndPoint Endpoint, ulong Nonce), Entry> _entries = new();
    private readonly record struct Entry(string Fingerprint, double Expires, HostReplyPacket Reply);
    internal int Count => _entries.Count;

    internal static string Fingerprint(HostRequestPacket request)
    {
        byte[] bytes = new byte[request.Length];
        request.Write(bytes);
        // Cookies may rotate on a retry. They authorize admission, not request identity.
        bytes.AsSpan(bytes.Length - 8).Clear();
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    internal HostReplyPacket GetOrStart(HostRequestPacket request, IPEndPoint sender,
        double now, Func<HostReplyPacket> start)
    {
        var key = (sender, request.HostNonce);
        string fingerprint = Fingerprint(request);
        if (_entries.TryGetValue(key, out Entry entry) && entry.Expires >= now)
            return entry.Fingerprint == fingerprint ? entry.Reply
                : new HostReplyPacket { Reason = "hosting challenge was already used for a different request" };
        // Expire in a separate pass: Dictionary enumeration must not be mutated.
        var expired = new List<(IPEndPoint, ulong)>();
        foreach (var item in _entries)
            if (item.Value.Expires < now) expired.Add(item.Key);
        foreach (var item in expired) _entries.Remove(item);
        if (_entries.Count >= Capacity)
            return new HostReplyPacket { Reason = "host retry table is busy; try again shortly" };
        HostReplyPacket reply = start();
        _entries[key] = new(fingerprint, now + LifetimeSeconds, reply);
        return reply;
    }
}
