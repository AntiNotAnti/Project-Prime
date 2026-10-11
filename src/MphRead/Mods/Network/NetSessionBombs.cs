using System;
using System.Collections.Generic;

namespace MphRead.Mods.Network;

public static partial class NetSession
{
    internal static SnapshotSink? BombEventSink;
    private static readonly Queue<(PacketType Type, byte[] Payload)> BombPackets = new();
    internal static int PendingBombPackets => BombPackets.Count;
    internal static void ResetBombPackets() => BombPackets.Clear();
    internal static bool TrySendBombIntent(BombIntent request)
    {
        if (_hostEndPoint == null || _transport == null) return false;
        Span<byte> payload = stackalloc byte[BombIntent.Size]; request.Write(payload);
        return _transport.TrySendSemantic(_hostEndPoint, PacketType.BombIntent, payload);
    }
    internal static void PublishBombEvent(BombEvent fact)
    {
        Span<byte> payload = stackalloc byte[BombEvent.Size]; fact.Write(payload);
        BombEventSink?.Invoke(payload);
    }
    private static void HandleBombPacket(ReceivedPacket packet)
    {
        if (!WorldIsReady) return;
        // Loading pumps may finish a bootstrap without advancing the effect
        // clock. Apply presentation on a real simulation step so zero-age
        // explosion emitters cannot silently miss their birth frame.
        if (BombPackets.Count >= 2048)
        { LastError = "Bomb presentation queue exceeded its recovery budget."; Stop(); return; }
        BombPackets.Enqueue((packet.Type, packet.Payload.ToArray()));
    }
    internal static void ApplyBombPackets()
    {
        if (!WorldIsReady || FreezeGameplay || NetBombs.LiveScene is not { } scene) return;
        scene.Bombs.Begin(CurrentMatchId, AuthorityEpoch);
        while (BombPackets.TryDequeue(out var packet))
        {
            if (packet.Type == PacketType.BombState)
            {
                if (scene.Bombs.ReceiveState(packet.Payload)) ReplayCapture.AcceptBombState(packet.Payload);
            }
            else
            {
                if (!NetFactBatch.Validate(PacketType.BombEvent, packet.Payload)) continue;
                for (int at = 0; at < packet.Payload.Length; at += BombEvent.Size)
                    if (BombEvent.TryRead(packet.Payload.AsSpan(at, BombEvent.Size), out var fact)
                        && scene.Bombs.ReceiveEvent(fact)) ReplayCapture.AcceptBombEvent(fact);
            }
            if (scene.Bombs.Faulted)
            {
                LastError = "Bomb state history could not be recovered; reconnect to receive a fresh world.";
                Stop(); return;
            }
        }
    }
}
