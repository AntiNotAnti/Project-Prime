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

    internal static void Reset()
    {
        _scene = null;
        _frame = 0;
        _seekGeneration = -1;
    }

    internal static void Update(Scene scene)
    {
        if (!DemoPlayback.IsActive || DemoPlayback.Session.Transport.IsSeeking)
        {
            Sync(scene);
            return;
        }

        uint frame = DemoPlayback.CurrentFrame;
        long generation = DemoPlayback.Session.Transport.SeekGeneration;
        if (!ReferenceEquals(_scene, scene) || generation != _seekGeneration || frame < _frame)
        {
            _scene = scene;
            _frame = frame;
            _seekGeneration = generation;
            return;
        }
        if (frame == _frame) return;

        int pov = scene.Players.MainPlayerIndex;
        if ((uint)pov < (uint)scene.Players.Items.Count)
        {
            foreach (ReplayEvent kill in DemoPlayback.Events)
            {
                if (kill.Type != ReplayEventType.Kill || kill.Frame <= _frame || kill.Frame > frame
                    || kill.ActorSlot != pov || kill.TargetSlot >= scene.Players.Items.Count)
                    continue;
                Queue(scene, kill);
            }
        }

        _frame = frame;
        _seekGeneration = generation;
    }

    private static void Sync(Scene scene)
    {
        _scene = scene;
        _frame = DemoPlayback.CurrentFrame;
        _seekGeneration = DemoPlayback.Session.Transport.SeekGeneration;
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
