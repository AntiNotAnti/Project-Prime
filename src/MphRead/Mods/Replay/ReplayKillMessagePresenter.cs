using System;
using System.Collections.Generic;
using System.Linq;
using MphRead.Entities;
using MphRead.Mods.Network;
using MphRead.Text;

namespace MphRead.Mods.Replay;

/// <summary>
/// Foreground replay combat side effects. Modern recordings consume mapped
/// ReplayShotFact evidence for hit/headshot/lethal feedback; old recordings
/// retain the durable kill-marker adapter. Nothing here changes health, score,
/// projectiles, RNG, or replay checkpoints.
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
        uint frame = DemoPlayback.CurrentFrame;
        long generation = DemoPlayback.Session.Transport.SeekGeneration;
        bool timelineReset = !ReferenceEquals(_scene, scene)
            || generation != _seekGeneration || frame < _frame;

        // A seek rebuilds visual state by stepping the replica. Transient HUD
        // messages and audio must not replay that history when the new target
        // becomes visible.
        if (timelineReset || DemoPlayback.Session.Transport.IsSeeking)
        {
            _scene = scene;
            _frame = frame;
            _seekGeneration = generation;
            return;
        }

        // Use the camera the replica is actually presenting. ReplayCamera.Mode is
        // editor intent and can briefly disagree with the scene during seeks/export
        // segment setup, which caused valid POV feedback to be skipped.
        bool firstPersonPov = scene.CameraMode == CameraMode.Player
            && !scene.IsFreeCam && !SpectatorMode.FreeCamera;
        if (!DemoPlayback.IsActive || !firstPersonPov)
        {
            _frame = frame;
            _seekGeneration = generation;
            return;
        }

        int pov = scene.Players.MainPlayerIndex;
        ReplayPoseStream? poses = scene.ReplayPoses;
        if ((uint)pov < PlayerEntity.SlotCapacity && poses != null)
        {
            uint start = _frame == uint.MaxValue ? frame : _frame + 1;
            var headshotAudio = new HashSet<(uint ShotId, ushort Generation, ushort Life)>();
            for (uint at = start; at <= frame; at++)
            {
                foreach (ReplayShotFact fact in poses.ResolvedShotFactsAt(at))
                {
                    PresentFact(scene, poses, pov, fact, headshotAudio);
                }
                if (at == uint.MaxValue) break;
            }

            // Legacy/non-weapon deaths still use the durable replay marker.
            // A modern lethal shot fact owns the same kill and suppresses the
            // old adapter so the native message cannot be queued twice.
            foreach (ReplayEvent kill in DemoPlayback.Events)
            {
                if (kill.Type != ReplayEventType.Kill || kill.Frame < start || kill.Frame > frame
                    || kill.ActorSlot != pov || kill.TargetSlot >= PlayerEntity.SlotCapacity
                    || poses.HasResolvedLethalShotNear(kill.Frame, kill.ActorSlot, kill.TargetSlot))
                {
                    continue;
                }
                QueueLegacy(scene, kill);
            }
        }

        _frame = frame;
        _seekGeneration = generation;
    }

    private static void PresentFact(Scene scene, ReplayPoseStream poses, int pov,
        in ReplayShotFact fact,
        HashSet<(uint ShotId, ushort Generation, ushort Life)> headshotAudio)
    {
        if (scene.Services is not ReplaySceneServices replay) return;

        bool shooterPov = fact.ShooterSlot == pov
            && SameLife(replay, fact.ShooterSlot,
                fact.ShooterGeneration, fact.ShooterLifeId);
        bool victimPov = fact.VictimSlot == pov
            && SameLife(replay, fact.VictimSlot,
                fact.VictimGeneration, fact.VictimLifeId);

        if (victimPov && fact.Damage > 0
            && poses.TryResolvedShotDirection(fact, out var direction))
        {
            scene.Players.Main.ModPresentReplayDamageIndicator(direction);
        }

        if (!shooterPov) return;

        if (fact.Headshot && fact.Damage > 0
            && headshotAudio.Add((fact.ShotId,
                fact.ShooterGeneration, fact.ShooterLifeId)))
        {
            Mods.Sound.CombatFeedbackAudio.OnReplayConfirmedHeadshot(
                scene, (BeamType)fact.Weapon);
        }

        if (fact.Lethal && !ReplayVideoExporter.SuppressGameHud)
        {
            QueueAuthoritative(scene, fact);
        }
    }

    private static bool SameLife(ReplaySceneServices replay, int slot,
        ushort generation, ushort life)
    {
        return (uint)slot < PlayerEntity.SlotCapacity
            && replay.State.TryGetPlayer(slot, out var state)
            && state.SlotGeneration == generation
            && state.LifeId == life;
    }

    private static void QueueAuthoritative(Scene scene, in ReplayShotFact fact)
    {
        PlayerEntity attacker = scene.Players.Items[fact.ShooterSlot];
        PlayerEntity victim = scene.Players.Items[fact.VictimSlot];
        bool friendly = scene.GameState.Teams
            && TeamRules.AreAllies(attacker.TeamIndex, victim.TeamIndex);
        Queue(scene, fact.VictimSlot, MessageId(friendly, fact.Headshot));
    }

    private static void QueueLegacy(Scene scene, ReplayEvent kill)
    {
        PlayerEntity attacker = scene.Players.Items[kill.ActorSlot];
        PlayerEntity victim = scene.Players.Items[kill.TargetSlot];
        bool friendly = scene.GameState.Teams && attacker.TeamIndex >= 0
            && TeamRules.AreAllies(attacker.TeamIndex, victim.TeamIndex);
        bool headshot = !friendly && DemoPlayback.Events.Any(e =>
            e.Type == ReplayEventType.Headshot
            && e.ActorSlot == kill.ActorSlot
            && e.TargetSlot == kill.TargetSlot
            && Math.Abs((long)e.Frame - kill.Frame) <= 1);
        if (!ReplayVideoExporter.SuppressGameHud)
        {
            Queue(scene, kill.TargetSlot, MessageId(friendly, headshot));
        }
    }

    internal static int MessageId(bool friendly, bool headshot)
        => friendly ? 240 : headshot ? 239 : 238;

    private static void Queue(Scene scene, int targetSlot, int messageId)
    {
        string nickname = scene.GameState.Nicknames[targetSlot];
        string message = Strings.GetHudMessage(messageId);
        scene.Players.Main.QueueHudMessage(128, 70, 140, 60 / 30f, 2,
            message.Replace("%s", PlayerNameCodec.ToNative(nickname)));
    }
}
