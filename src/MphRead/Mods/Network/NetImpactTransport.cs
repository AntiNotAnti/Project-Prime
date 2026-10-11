using System;
using System.Net;
using System.Threading;

namespace MphRead.Mods.Network;

public sealed partial class NetTransport
{
    private bool CanHoldImpact(IPEndPoint sender, ReadOnlySpan<byte> bytes)
    {
        if (!NetConfirmedImpacts.Enabled || bytes.Length != NetHeader.Size + ConfirmedImpact.Size
            || !NetHeader.TryRead(bytes, out var header) || header.Flags != NetHeaderFlags.None
            || header.Ack != 0 || header.AckBits != 0 || !ConfirmedImpact.TryRead(bytes[NetHeader.Size..], out _)) return false;
        long stamp = EnterConnectionLock();
        try { return _connections.TryGetValue(sender, out var peer) && peer.RetiredAt == null
            && peer.ReceivesAuthorityFacts && !peer.QueueOnly && peer.Accepts(sender, header)
            && _cosmeticIngressBudget.Take(NowMilliseconds, 512, 64); }
        finally { ExitConnectionLock(stamp); }
    }
    // Fixed 77-byte datagrams, global 512/s and per-peer 120/s. All admission
    // precedes copies. Cosmetic sends never reserve reliable state or ACKs.
    internal bool TrySendImpact(IPEndPoint target, ReadOnlySpan<byte> payload)
    {
        if (!NetConfirmedImpacts.Enabled || !ConfirmedImpact.TryRead(payload, out _)) return false;
        Span<byte> bytes = stackalloc byte[NetHeader.Size + ConfirmedImpact.Size];
        double now = NowMilliseconds;
        long stamp = EnterConnectionLock();
        try
        {
            if (!_connections.TryGetValue(target, out var peer) || peer.RetiredAt.HasValue
                || peer.QueueOnly || peer.ReceivesAuthorityFacts) return false;
            if (!peer.AllowCosmeticSend(now, bytes.Length)
                || !_cosmeticSendBytes.Take(now, bytes.Length, 512 * bytes.Length, 64 * bytes.Length)
                || !_cosmeticSendBudget.Take(now, 512, 64))
            { Interlocked.Increment(ref _cosmeticDrops); return false; }
            peer.Send(PacketType.ConfirmedImpact, now).Write(bytes);
        }
        finally { ExitConnectionLock(stamp); }
        payload.CopyTo(bytes[NetHeader.Size..]);
        if (_lagWorker != null)
        {
            // These are immutable managed copies, not pooled leases; bounded
            // fault-queue drops leave no buffer ownership to release.
            byte[] copy = bytes.ToArray();
            lock (_heldLock) _cosmeticHeldOut.Enqueue(now, (target, copy, copy.Length));
        }
        else if (CurrentCosmeticConnection(target, bytes)) SendNow(target, bytes);
        return true;
    }
    private bool CurrentCosmeticConnection(IPEndPoint target, ReadOnlySpan<byte> bytes)
    {
        if (!NetHeader.TryRead(bytes, out var header)) return false;
        long stamp = EnterConnectionLock();
        try { return _connections.TryGetValue(target, out var peer) && peer.RetiredAt == null
            && !peer.QueueOnly && !peer.ReceivesAuthorityFacts && peer.Accepts(target, header); }
        finally { ExitConnectionLock(stamp); }
    }
}
