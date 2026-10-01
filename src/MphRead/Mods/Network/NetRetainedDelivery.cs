using System;
namespace MphRead.Mods.Network;

/// <summary>Owner-thread bounded fact history. Reliable transport owns accepted copies;
/// a cursor advances only after transport admission. Slow consumers fail explicitly.</summary>
internal sealed class NetRetainedDelivery
{
    internal sealed class Cursor { internal ulong Next; }
    internal readonly record struct Fact(PacketType Type, byte[] Payload);
    private readonly Fact[] _history;
    private ulong _next = 1;
    internal NetRetainedDelivery(int capacity = 4096)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _history = new Fact[capacity];
    }
    internal ulong First => _next > (ulong)_history.Length ? _next - (ulong)_history.Length : 1;
    // Late join begins at the current tail, never with historical announcements.
    internal Cursor Join() => new() { Next = _next };
    internal void Append(PacketType type, ReadOnlySpan<byte> payload)
    {
        if (_next == ulong.MaxValue) throw new InvalidOperationException("Semantic delivery sequence exhausted.");
        _history[(int)(_next % (ulong)_history.Length)] = new(type, payload.ToArray());
        _next++;
    }
    internal bool Pump(Cursor cursor, Func<Fact, bool> accept, int budget = 8)
    {
        if (cursor.Next < First || cursor.Next > _next) return false;
        for (int count = 0; count < budget && cursor.Next < _next; count++)
        {
            var fact = _history[(int)(cursor.Next % (ulong)_history.Length)];
            if (!accept(fact)) break;
            cursor.Next++;
        }
        return true;
    }
}
