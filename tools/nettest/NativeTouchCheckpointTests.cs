using System;
using System.IO;
using System.Reflection;
using MphRead;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

internal static class NativeTouchCheckpointTests
{
    private static Scene Replica(ReplayPlaybackSession session, PassiveReplaySessionHost host)
        => new(new Vector2i(32, 32), SyntheticInput.CreateKeyboard(), SyntheticInput.CreateMouse(),
            _ => { }, () => { }, new ReplaySceneServices(session, host.State), initializeRuntime: false);
    private static byte[] Write(PlayerEntity player)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        player.ModWriteNativeTouchCheckpoint(writer); return stream.ToArray();
    }
    internal static byte[] WriteOwnedAppendix()
    {
        Headless.Enter();
        var host = new PassiveReplaySessionHost(); using var session = new ReplayPlaybackSession(host);
        var scene = Replica(session, host);
        try
        {
            var actor = scene.Players.Items[0];
            var touch = new NativeTouchReport(true, true, true, 120, -80, 123, true);
            actor.ModReceiveNativeTouch(touch, 4, 5, 23);
            var boost = (MorphBallBoostStateMachine)typeof(PlayerEntity).GetField("_nativeBoost", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(actor)!;
            var state = new MorphBallBoostStateMachine.State { CanTouchBoost = true, Charge = 120 };
            boost.Advance(ref state, touch, false, 1, 120, false);
            using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
            foreach (var player in scene.Players.Items) player.ModWriteNativeTouchCheckpoint(writer);
            return stream.ToArray();
        }
        finally { scene.DoCleanup(); scene.UnloadGl(); }
    }
    internal static void Run(Action<bool, string> check)
    {
        var foreground = GameState.Current; var registry = PlayerEntity.LegacyRegistry; var random = Rng.Current;
        byte[] appendix = WriteOwnedAppendix();
        var host = new PassiveReplaySessionHost(); using var session = new ReplayPlaybackSession(host);
        var target = Replica(session, host);
        try
        {
            using var stream = new MemoryStream(appendix, writable: false); using var reader = new BinaryReader(stream);
            foreach (var player in target.Players.Items) player.ModReadNativeTouchCheckpoint(reader);
            check(stream.Position == stream.Length && appendix.Length == 8 * 57,
                "actual private-player touch appendix consumes eight complete source rows");
            using var roundtrip = new MemoryStream(); using var writer = new BinaryWriter(roundtrip);
            foreach (var player in target.Players.Items) player.ModWriteNativeTouchCheckpoint(writer);
            check(roundtrip.ToArray().AsSpan().SequenceEqual(appendix),
                "actual private-player restore preserves touch phase, remote life, roll share and boost latch bytes");
            byte[] first = Write(target.Players.Items[0]);
            check(first[1] == 1 && first[42] != 0 && BitConverter.ToUInt32(first, 47) == 123 && first[51] == 1,
                "native source fixture has positive classic-mode, nonzero sample identity and latched boost coverage");
            check(ReferenceEquals(foreground, GameState.Current) && ReferenceEquals(registry, PlayerEntity.LegacyRegistry)
                && ReferenceEquals(random, Rng.Current), "private native-touch checkpoint creation and restore leave live facades unchanged");
        }
        finally { target.DoCleanup(); target.UnloadGl(); }
    }
}
