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
    internal int Pending(Cursor cursor) => cursor.Next <= _next ? (int)Math.Min(_next - cursor.Next, int.MaxValue) : int.MaxValue;
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
    internal delegate bool BatchSink(PacketType type, ReadOnlySpan<byte> payload);
    internal bool PumpBatched(Cursor cursor, BatchSink accept, int budget = 64)
    {
        if (cursor.Next < First || cursor.Next > _next) return false;
        Span<byte> batch = stackalloc byte[NetFactBatch.MaximumPayload];
        for (int sent = 0; sent < budget && cursor.Next < _next;)
        {
            var first = _history[(int)(cursor.Next % (ulong)_history.Length)];
            int stride = NetFactBatch.Stride(first.Type), count = 0;
            if (stride == 0 || first.Payload.Length != stride)
                throw new InvalidOperationException("Invalid retained authority fact.");
            while (cursor.Next + (ulong)count < _next && sent + count < budget
                && (count + 1) * stride <= batch.Length)
            {
                var fact = _history[(int)((cursor.Next + (ulong)count) % (ulong)_history.Length)];
                if (fact.Type != first.Type || fact.Payload.Length != stride) break;
                fact.Payload.CopyTo(batch.Slice(count * stride, stride)); count++;
            }
            if (!accept(first.Type, batch[..(count * stride)])) break;
            cursor.Next += (ulong)count; sent += count;
        }
        return true;
    }
}
