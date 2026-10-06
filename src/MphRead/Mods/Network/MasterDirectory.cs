using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Cryptography;

namespace MphRead.Mods.Network;

/// <summary>Bounded directory admission. An existing host challenge proves the
/// advertised game port answers before it enters the public list. No new wire ID
/// or packet layout is required. This proves reachability, not account identity.</summary>
internal sealed class MasterDirectory
{
    internal const int Capacity = 255, PendingCapacity = 128, AddressCapacity = 16;
    internal const double ProofSeconds = 5;
    internal sealed class Entry
    {
        public IPEndPoint Key = null!, Reporter = null!;
        public uint Address;
        public ushort Port;
        public byte Players, MaxPlayers, Mode, Protocol;
        public string ServerName = "", RoomKey = "";
        public double LastSeen;
    }
    private readonly List<Entry> _entries = new();
    private readonly Dictionary<IPEndPoint, Pending> _pending = new();
    private readonly record struct Pending(Entry Entry, ulong Nonce, double Started);
    private readonly Dictionary<IPAddress, double> _queries = new();
    private double _nextQuery;
    internal List<Entry> Entries => _entries;
    internal int PendingCount => _pending.Count;
    internal int QueryCount => _queries.Count;

    internal bool Begin(Entry candidate, double now, out ulong nonce)
    {
        nonce = 0;
        Expire(now);
        Entry? active = _entries.Find(e => e.Key.Equals(candidate.Key));
        // A live registration is owned by its reporter endpoint, not merely its NAT.
        if (active != null && !active.Reporter.Equals(candidate.Reporter)) return false;
        if (_pending.ContainsKey(candidate.Key)) return false;
        if (_pending.Count >= PendingCapacity || active == null && _entries.Count >= Capacity) return false;
        int count = 0;
        foreach (Entry entry in _entries) if (entry.Key.Address.Equals(candidate.Key.Address)) count++;
        foreach (var item in _pending)
            if (item.Key.Address.Equals(candidate.Key.Address)
                && !_entries.Exists(e => e.Key.Equals(item.Key))) count++;
        // Operator-owned loopback children may use the entire configured host
        // pool; remote addresses retain their admission share.
        if (active == null && count >= AddressCapacity && !IPAddress.IsLoopback(candidate.Key.Address)) return false;
        do { nonce = BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8)); } while (nonce == 0);
        _pending.Add(candidate.Key, new(candidate, nonce, now));
        return true;
    }

    internal bool Complete(IPEndPoint sender, ulong nonce, double now)
    {
        if (!_pending.TryGetValue(sender, out Pending pending)
            || pending.Nonce != nonce || now - pending.Started > ProofSeconds) return false;
        _pending.Remove(sender);
        Entry? active = _entries.Find(e => e.Key.Equals(sender));
        if (active == null && _entries.Count >= Capacity) return false;
        if (active != null) _entries.Remove(active);
        pending.Entry.LastSeen = now;
        _entries.Add(pending.Entry);
        return true;
    }

    internal bool Farewell(IPEndPoint reporter, ushort port)
    {
        int at = _entries.FindIndex(e => e.Port == port && e.Reporter.Equals(reporter));
        if (at < 0) return false;
        _pending.Remove(_entries[at].Key);
        _entries.RemoveAt(at);
        return true;
    }

    internal void Expire(double now)
    {
        _entries.RemoveAll(e => now - e.LastSeen > NetMasterConfig.ExpirySeconds);
        var expired = new List<IPEndPoint>();
        foreach (var item in _pending)
            if (now - item.Value.Started > ProofSeconds) expired.Add(item.Key);
        foreach (IPEndPoint key in expired) _pending.Remove(key);
    }

    internal bool AllowQuery(IPAddress address, double now)
    {
        // Bound response bytes as well as inbound packets: at most 32 complete
        // lists/s globally and four lists/s per address, regardless of UDP source ports.
        if (now < _nextQuery || _queries.TryGetValue(address, out double last) && now - last < .25) return false;
        if (_queries.Count >= 1024)
        {
            var expired = new List<IPAddress>();
            foreach (var item in _queries) if (now - item.Value > 60) expired.Add(item.Key);
            foreach (IPAddress key in expired) _queries.Remove(key);
            if (_queries.Count >= 1024) return false;
        }
        _queries[address] = now;
        _nextQuery = now + 1.0 / 32;
        return true;
    }
}
