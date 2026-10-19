using System;
using System.Collections.Generic;

namespace MphRead.Mods.Network;

// Read-only, bounded observations at the existing simulation/control owners.
// No evidence value participates in load, admission, timing or rotation decisions.
internal static partial class NetCampaignEvidence
{
    private sealed record RotationEvent(long Sequence, string Stage, ulong Epoch, ushort Match,
        uint StartGeneration, string Room, long SceneGeneration, ulong SceneFrame, uint AuthorityFrame,
        int Slot, ushort Generation, uint BootstrapRevision, bool AppliedWorld, bool GameplayReady,
        bool WorldAdmissionConfirmed, bool Completed, string? EndReason, long SimFrames,
        long StepFailures, long DroppedSteps, long Stalls, int OwnerThread,
        string SessionPolicy, string StartStage);
    private readonly record struct RotationKey(string Stage, MatchStartIdentity Start, long Scene,
        int Slot, ushort Generation, uint Revision);
    private static List<RotationEvent>? _rotationEvents;
    private static HashSet<RotationKey>? _rotationKeys;
    private static long _rotationOverflow;

    private static object CaptureRotationReport() => new
    {
        Schema = 1, Scope = "OwnerThreadAppliedRotationV1", Overflow = _rotationOverflow,
        Events = _rotationEvents
    };

    private static bool RotationWorld(Scene scene, MatchStartIdentity start, string room) =>
        !scene.Services.IsReplica && scene.Room != null && scene.Room.Meta.Name == room
        && start.MatchId != 0 && start.AuthorityEpoch != 0
        && start.MatchId == NetSession.CurrentMatchId && start.AuthorityEpoch == NetSession.AuthorityEpoch
        && NetSession.ServerSession is { } session && start.StartGeneration == session.StartGeneration
        // The initial continuous world predates the lobby load barrier; its
        // real wire generation is zero. Never invent a diagnostic generation.
        && (start.StartGeneration != 0 || session.Policy == ServerSessionPolicy.Continuous
            && session.StartStage == StartStage.None);

    private static void Rotation(string stage, Scene scene, MatchStartIdentity start, uint frame,
        int slot = -1, ushort generation = 0, uint revision = 0, bool admitted = false,
        string? reason = null, ServerSim? sim = null)
    {
        var key = new RotationKey(stage, start, scene.LifetimeGeneration, slot, generation, revision);
        if (_rotationKeys?.Contains(key) == true) return;
        if ((_rotationEvents?.Count ?? 0) >= RowLimit) { _rotationOverflow++; return; }
        (_rotationKeys ??= new()).Add(key);
        var rows = _rotationEvents ??= new();
        rows.Add(new(rows.Count + 1L, stage, start.AuthorityEpoch, start.MatchId, start.StartGeneration,
            scene.Room!.Meta.Name, scene.LifetimeGeneration, scene.FrameCount, frame, slot, generation,
            revision, true, NetRoomChange.GameplayReady, admitted, reason != null, reason,
            sim?.Frames ?? 0, sim?.StepFailures ?? 0, sim?.DroppedSteps ?? 0, sim?.Stalls ?? 0,
            Environment.CurrentManagedThreadId, NetSession.ServerSession!.Value.Policy.ToString(),
            NetSession.ServerSession.Value.StartStage.ToString()));
    }

    internal static void CaptureRotationLifecycle(Scene scene)
    {
        if (!Enabled || !NetSession.Active || NetSession.SemanticLegacyPlayback
            || NetSession.ServerSession is not { } session) return;
        var start = new MatchStartIdentity(session.MatchId, session.AuthorityEpoch, session.StartGeneration);
        if (!RotationWorld(scene, start, session.Match.RoomKey)) return;
        if (NetSession.IsAuthority)
        {
            if (session.Phase == SessionPhase.InMatch && NetSession.NetFrame != 0)
                Rotation("authority-started", scene, start, NetSession.NetFrame);
            return;
        }
        if (!NetSession.IsClient || !NetRoomChange.GameplayReady || !NetSession.WorldAdmissionConfirmed
            || NetSession.ConfirmedWorldBootstrap is not { } baseline || baseline.Start != start
            || baseline.SlotGeneration != NetPlayerLifecycle.Generation(NetSession.LocalSlot)) return;
        uint frame = NetSession.LastSnapshotFrame;
        if (frame == 0) return;
        Rotation("client-applied-confirmed", scene, start, frame, NetSession.LocalSlot,
            baseline.SlotGeneration, baseline.Revision, admitted: true);
        if (NetSession.ServerMatch is { Ending: true })
            Rotation("client-completed", scene, start, frame, NetSession.LocalSlot,
                baseline.SlotGeneration, baseline.Revision, admitted: true, reason: "authoritative-ending");
    }

    internal static void CompleteAuthorityRotation(ServerSim? sim, MatchStartIdentity start, string reason)
    {
        if (!Enabled || !NetSession.Active || NetSession.SemanticLegacyPlayback
            || !NetSession.IsAuthority || sim?.EvidenceScene is not { } scene
            || NetSession.ServerSession is not { } session || !RotationWorld(scene, start, session.Match.RoomKey)) return;
        Rotation("authority-completed", scene, start, NetSession.NetFrame, reason: reason, sim: sim);
    }
}
