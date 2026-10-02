using System;
using System.Linq;
using MphRead.Entities;
using MphRead.Mods.Network;
using MphRead.Text;

namespace MphRead.Mods.Replay;

/// <summary>
/// Replays the local-only native kill HUD side effect from durable replay events.
/// This never changes replay simulation or score state; it only queues the same
/// presentation message the live attacker saw.
/// </summary>
internal static class ReplayKillMessagePresenter
{
    private static Scene? _scene;
    private static uint _frame;
    private static long _seekGeneration = -1;
    private static uint? _presentedKillFrame;
    private static byte _presentedKiller = byte.MaxValue;
    private static byte _presentedVictim = byte.MaxValue;

    internal static void Reset()
    {
        _scene = null;
        _frame = 0;
        _seekGeneration = -1;
        _presentedKillFrame = null;
        _presentedKiller = byte.MaxValue;
        _presentedVictim = byte.MaxValue;
    }

    internal static void Update(Scene scene)
    {
        uint frame = DemoPlayback.CurrentFrame;
        long generation = DemoPlayback.Session.Transport.SeekGeneration;
        bool timelineReset = !ReferenceEquals(_scene, scene)
            || generation != _seekGeneration || frame < _frame;
        if (timelineReset)
        {
            _scene = scene;
            _frame = frame;
            _seekGeneration = generation;
            _presentedKillFrame = null;
            _presentedKiller = byte.MaxValue;
            _presentedVictim = byte.MaxValue;
        }

        // Use the camera the replica is actually presenting. ReplayCamera.Mode is
        // editor intent and can briefly disagree with the scene during seeks/export
        // segment setup, which caused valid POV kill notices to be skipped.
        bool firstPersonPov = scene.CameraMode == CameraMode.Player
            && !scene.IsFreeCam && !SpectatorMode.FreeCamera;
        if (!DemoPlayback.IsActive || DemoPlayback.Session.Transport.IsSeeking || !firstPersonPov)
        {
            _frame = frame;
            _seekGeneration = generation;
            return;
        }

        int pov = scene.Players.MainPlayerIndex;
        if ((uint)pov < PlayerEntity.SlotCapacity)
        {
            uint start = timelineReset ? (frame > 0 ? frame - 1 : 0) : _frame;
            foreach (ReplayEvent kill in DemoPlayback.Events)
            {
                if (kill.Type != ReplayEventType.Kill || kill.Frame < start || kill.Frame > frame
                    || kill.ActorSlot != pov || kill.TargetSlot >= PlayerEntity.SlotCapacity)
                    continue;
                if (_presentedKillFrame == kill.Frame
                    && _presentedKiller == kill.ActorSlot
                    && _presentedVictim == kill.TargetSlot)
                    continue;
                Queue(scene, kill);
                _presentedKillFrame = kill.Frame;
                _presentedKiller = kill.ActorSlot;
                _presentedVictim = kill.TargetSlot;
            }
        }

        _frame = frame;
        _seekGeneration = generation;
    }

    private static void Queue(Scene scene, ReplayEvent kill)
    {
        PlayerEntity attacker = scene.Players.Items[kill.ActorSlot];
        PlayerEntity victim = scene.Players.Items[kill.TargetSlot];
        bool friendly = scene.GameState.Teams && attacker.TeamIndex >= 0
            && attacker.TeamIndex == victim.TeamIndex;
        bool headshot = !friendly && DemoPlayback.Events.Any(e =>
            e.Type == ReplayEventType.Headshot
            && e.ActorSlot == kill.ActorSlot
            && e.TargetSlot == kill.TargetSlot
            && Math.Abs((long)e.Frame - kill.Frame) <= 1);

        string nickname = scene.GameState.Nicknames[kill.TargetSlot];
        string message = Strings.GetHudMessage(friendly ? 240 : headshot ? 239 : 238);
        scene.Players.Main.QueueHudMessage(128, 70, 140, 60 / 30f, 2,
            message.Replace("%s", PlayerNameCodec.ToNative(nickname)));
    }
}
