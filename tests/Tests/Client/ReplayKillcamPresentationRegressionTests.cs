using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using MphRead.Combat;
using MphRead.Entities;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;
using Xunit;

namespace MphRead.Tests.Client;

[Collection("Match baseline globals")]
public sealed class ReplayKillcamPresentationRegressionTests : IDisposable
{
    private readonly HitMarkerMode _previousHitMarkers = CombatFeedbackSettings.HitMarkers;
    private readonly HitMarkerTiming _previousTiming = CombatFeedbackSettings.Timing;
    private readonly bool _previousHeadshotCue = CombatFeedbackSettings.HeadshotCue;
    private readonly bool _previousKillConfirmation = CombatFeedbackSettings.KillConfirmation;

    public ReplayKillcamPresentationRegressionTests()
    {
        CombatFeedbackSettings.HitMarkers = HitMarkerMode.Visual;
        CombatFeedbackSettings.Timing = HitMarkerTiming.Confirmed;
        CombatFeedbackSettings.HeadshotCue = true;
        CombatFeedbackSettings.KillConfirmation = true;
    }

    public void Dispose()
    {
        CombatFeedbackSettings.HitMarkers = _previousHitMarkers;
        CombatFeedbackSettings.Timing = _previousTiming;
        CombatFeedbackSettings.HeadshotCue = _previousHeadshotCue;
        CombatFeedbackSettings.KillConfirmation = _previousKillConfirmation;
    }

    [Fact]
    public void ReplayFormBoundariesKeepCameraHistoryAndLocomotionPolicyDistinct()
    {
        SnapshotPlayer biped = Snapshot(1, SnapshotPlayerFlags.Grounded,
            speed: Vector3.Zero, weapon: BeamType.PowerBeam);
        SnapshotPlayer morphing = Snapshot(2, SnapshotPlayerFlags.Grounded
            | SnapshotPlayerFlags.Morphing, speed: Vector3.UnitZ,
            weapon: BeamType.Missile);
        SnapshotPlayer alt = Snapshot(3, SnapshotPlayerFlags.Grounded
            | SnapshotPlayerFlags.AltForm, speed: Vector3.UnitZ,
            weapon: BeamType.Missile);
        SnapshotPlayer unmorphing = Snapshot(4, SnapshotPlayerFlags.Grounded
            | SnapshotPlayerFlags.Unmorphing, speed: Vector3.Zero,
            weapon: BeamType.PowerBeam);

        Assert.Equal(PlayerAnimation.Idle,
            PlayerEntity.DeriveRemoteBipedAnimation(biped, biped.Speed));
        Assert.Equal(PlayerAnimation.None,
            PlayerEntity.DeriveRemoteBipedAnimation(morphing, morphing.Speed));
        Assert.Equal(PlayerAnimation.None,
            PlayerEntity.DeriveRemoteBipedAnimation(alt, alt.Speed));
        Assert.Equal(PlayerAnimation.None,
            PlayerEntity.DeriveRemoteBipedAnimation(unmorphing, unmorphing.Speed));

        // This is a headless presentation-key check. It does not claim that
        // the renderer produced a particular camera pose or mesh image.
        Assert.Equal(CameraType.First,
            CameraTypeForSnapshot(biped.Flags));
        Assert.Equal(CameraType.Third1,
            CameraTypeForSnapshot(morphing.Flags));
        Assert.Equal(CameraType.Third1,
            CameraTypeForSnapshot(alt.Flags));
        Assert.Equal(CameraType.First,
            CameraTypeForSnapshot(unmorphing.Flags));

        Assert.True(PlayerEntity.ShouldRecenterNetworkAimCamera(
            CameraType.First, isAltForm: false));
        Assert.False(PlayerEntity.ShouldRecenterNetworkAimCamera(
            CameraType.Third1, isAltForm: false));
        Assert.False(PlayerEntity.ShouldRecenterNetworkAimCamera(
            CameraType.Third1, isAltForm: true));

        int bipedHistory = ScenePresentation.CameraHistoryState(
            dead: false, altForm: false, morphing: false, unmorphing: false,
            zoomed: false, CameraType.First, currentSequence: null);
        int morphHistory = ScenePresentation.CameraHistoryState(
            dead: false, altForm: false, morphing: true, unmorphing: false,
            zoomed: false, CameraType.Third1, currentSequence: null);
        int altHistory = ScenePresentation.CameraHistoryState(
            dead: false, altForm: true, morphing: false, unmorphing: false,
            zoomed: false, CameraType.Third1, currentSequence: null);
        int unmorphHistory = ScenePresentation.CameraHistoryState(
            dead: false, altForm: false, morphing: false, unmorphing: true,
            zoomed: false, CameraType.First, currentSequence: null);

        Assert.NotEqual(bipedHistory, morphHistory);
        Assert.NotEqual(morphHistory, altHistory);
        Assert.NotEqual(altHistory, unmorphHistory);
    }

    [Fact]
    public void AlternateFormDeathRetainsVictimIdentityAndUsesSafeReplayFocus()
    {
        CombatActor killer = new(1, 200, 4);
        CombatActor victim = new(2, 300, 7);
        var gate = new DeathPresentationGate();

        Assert.False(gate.ObserveSnapshot(victim, dead: false,
            altForm: true, tick: 100).IsValid);
        DeathPresentationCue cue = gate.ObserveSnapshot(victim, dead: true,
            altForm: true, tick: 110);
        Assert.True(cue.IsValid);
        Assert.Equal(victim, cue.Actor);
        Assert.True(cue.AltForm);
        Assert.False(gate.ObserveSnapshot(victim, dead: true,
            altForm: true, tick: 111).IsValid);

        KillEvent kill = new(12, 110, 1, 3, killer, victim, 4,
            KillEventFlags.Headshot, ImmutableArray<CombatActor>.Empty);
        Assert.Equal(killer, KillcamController.ResolveFocusActor(kill));
        Assert.Equal(SpectatorCameraMode.Chase,
            KillcamController.ResolveFocusMode(kill));
        Assert.True(SpectatorCameraController.IsExactDirectorActor(
            killer, killer));
        Assert.False(SpectatorCameraController.IsExactDirectorActor(
            killer, killer with { Life = killer.Life + 1 }));

        // A detached killcam camera cannot rely on the victim's portal set;
        // the room path must fail open for the visible geometry instead.
        Assert.True(RoomEntityPresentation.ShouldBypassPortalCulling(
            detachedView: true, trackedPartCouldContainCamera: false));
    }

    [Theory]
    [InlineData(0f, 0f, true, false)]
    [InlineData(0.03f, 0f, true, true)]
    [InlineData(0.03f, 0f, false, false)]
    public void ReplayWeaponBobUsesRecordedBipedMotionOnly(
        float speedX, float speedZ, bool grounded, bool expected)
    {
        SnapshotPlayer state = Snapshot(1,
            grounded ? SnapshotPlayerFlags.Grounded : SnapshotPlayerFlags.None,
            new Vector3(speedX, 0, speedZ));
        Assert.Equal(expected, PlayerEntity.ShouldAdvanceReplayWeaponBob(
            state, state.Speed));
    }

    [Fact]
    public void ReplayWeaponBobRejectsMorphAirborneDeadAndSpectatingStates()
    {
        SnapshotPlayer moving = Snapshot(1, SnapshotPlayerFlags.Grounded,
            Vector3.UnitZ);
        foreach (SnapshotPlayerFlags flag in new[]
        {
            SnapshotPlayerFlags.Morphing,
            SnapshotPlayerFlags.Unmorphing,
            SnapshotPlayerFlags.AltForm,
            SnapshotPlayerFlags.Spectating,
            SnapshotPlayerFlags.WaitingForMatch
        })
        {
            SnapshotPlayer state = moving;
            state.Flags |= flag;
            Assert.False(PlayerEntity.ShouldAdvanceReplayWeaponBob(
                state, state.Speed));
        }

        moving.Health = 0;
        Assert.False(PlayerEntity.ShouldAdvanceReplayWeaponBob(
            moving, moving.Speed));
    }

    [Fact]
    public void ReplayFeedbackCuesDeduplicateAcrossRestoreButAcceptNewProgress()
    {
        CombatActor local = new(0, 100, 1);
        CombatActor enemy = new(1, 200, 1);
        var roster = new[]
        {
            new NetRosterEntry(0, 100, Hunter.Samus, 0, "Local"),
            new NetRosterEntry(1, 200, Hunter.Trace, 1, "Enemy")
        };
        var source = new CombatFeedback();
        source.Bind(1, local, roster, presentationTick: 100,
            phaseRevision: 3);
        CombatEvent hit = new(10, 100, 3, CombatEventKind.Damage, 0,
            CombatEventFlags.None, local, enemy, 80, 20, Vector3.Zero,
            Vector3.UnitZ, 0, 0, 0);
        CombatEvent headshot = hit with
        {
            Id = 11,
            Flags = CombatEventFlags.Headshot,
            Amount = 40,
            Health = 40
        };
        KillEvent kill = new(12, 100, 1, 3, local, enemy, 4,
            KillEventFlags.Headshot, ImmutableArray<CombatActor>.Empty);

        Assert.True(source.Process(hit));
        uint afterHit = source.State.MarkerSequence;
        Assert.True(source.Process(headshot));
        uint afterHeadshot = source.State.MarkerSequence;
        Assert.Equal(afterHit + 1, afterHeadshot);
        Assert.True(source.Process(kill));
        uint afterKill = source.State.MarkerSequence;
        Assert.Equal(afterHeadshot + 1, afterKill);
        Assert.Equal(HitMarkerKind.Kill, source.VisibleMarker(100));
        Assert.True(source.IsHeadshotNoticeVisible(100));
        Assert.True(source.IsKillNoticeVisible(100));
        uint markerSequence = source.State.MarkerSequence;
        uint markerAudioSequence = source.State.MarkerAudioSequence;
        byte[] checkpoint = ReplayFeedbackState.Capture(source,
            new WorldFeedback());

        var restored = new CombatFeedback();
        var restoredWorld = new WorldFeedback();
        Assert.True(ReplayFeedbackState.Restore(checkpoint, restored,
            restoredWorld));
        Assert.False(restored.Process(hit));
        Assert.False(restored.Process(headshot));
        Assert.False(restored.Process(kill));
        Assert.Equal(markerSequence, restored.State.MarkerSequence);
        Assert.Equal(markerAudioSequence, restored.State.MarkerAudioSequence);
        Assert.True(restored.IsHeadshotNoticeVisible(100));
        Assert.True(restored.IsKillNoticeVisible(100));

        // A weaker hit inside the kill-marker window is intentionally not a
        // new presentation cue. Advance beyond that window to prove that a
        // genuinely new replay event is accepted after restore.
        CombatEvent nextHit = hit with { Id = 13, Tick = 130 };
        Assert.True(restored.Process(nextHit));
        Assert.True(restored.State.MarkerSequence > markerSequence);
    }

    [Fact]
    public void SeekingAcrossMorphBoundariesRestoresSnapshotAndSuppressesAudioUntilCrossing()
    {
        SnapshotPlayer biped = Snapshot(1, SnapshotPlayerFlags.Grounded,
            Vector3.Zero, BeamType.PowerBeam);
        SnapshotPlayer morphing = Snapshot(2, SnapshotPlayerFlags.Grounded
            | SnapshotPlayerFlags.Morphing, Vector3.UnitZ, BeamType.Missile);
        SnapshotPlayer alt = Snapshot(3, SnapshotPlayerFlags.Grounded
            | SnapshotPlayerFlags.AltForm, Vector3.UnitZ, BeamType.Missile);
        SnapshotPlayer unmorphing = Snapshot(4, SnapshotPlayerFlags.Grounded
            | SnapshotPlayerFlags.Unmorphing, Vector3.Zero,
            BeamType.PowerBeam);
        SnapshotPlayer finalBiped = Snapshot(5, SnapshotPlayerFlags.Grounded,
            Vector3.Zero, BeamType.PowerBeam);

        ReplayTimelineClip clip = Clip(
            new ReplayTimelineRecord(30, 150, SnapshotRecord(1, 2, morphing)),
            new ReplayTimelineRecord(60, 180, SnapshotRecord(1, 3, alt)),
            new ReplayTimelineRecord(90, 210, SnapshotRecord(1, 4, unmorphing)),
            new ReplayTimelineRecord(120, 240, SnapshotRecord(1, 5, finalBiped)));
        using var session = new ReplayPlaybackSession();
        Assert.True(session.Join(clip), session.LastError);

        AssertSeekState(session, 10, biped, PlayerAnimation.Idle,
            expectAudioSuppressed: true);
        AssertSeekState(session, 45, morphing, PlayerAnimation.None,
            expectAudioSuppressed: true);
        AssertSeekState(session, 75, alt, PlayerAnimation.None,
            expectAudioSuppressed: true);
        AssertSeekState(session, 105, unmorphing, PlayerAnimation.None,
            expectAudioSuppressed: true);
        AssertSeekState(session, 135, finalBiped, PlayerAnimation.Idle,
            expectAudioSuppressed: true);
    }

    [Fact]
    public void PerspectiveSelectionRemainsExplicitWhenReplayChangesOwner()
    {
        using var session = new ReplayPlaybackSession();
        session.SetPerspective(-1);
        Assert.Equal(-1, session.PerspectiveSlot);
        Assert.Equal(-1, session.SceneServices.LocalSlot);
        Assert.True(session.SceneServices.DesiredSpectating);

        session.SetPerspective(4);
        Assert.Equal(4, session.PerspectiveSlot);
        Assert.Equal(4, session.SceneServices.LocalSlot);
        Assert.False(session.SceneServices.DesiredSpectating);
    }

    [Fact]
    public void DetachedReplayCameraFailsOpenForUnknownRoomPart()
    {
        Assert.True(RoomEntityPresentation.ShouldBypassPortalCulling(
            detachedView: true, trackedPartCouldContainCamera: false));
        Assert.True(RoomEntityPresentation.ShouldBypassPortalCulling(
            detachedView: false, trackedPartCouldContainCamera: false));
        Assert.False(RoomEntityPresentation.ShouldBypassPortalCulling(
            detachedView: false, trackedPartCouldContainCamera: true));
    }

    [Fact]
    [Trait("RequiresGameContent", "true")]
    public void ReplayPerspectiveSwapDuringMorphRebuildsCameraHudWeaponAndModelState()
    {
        string data = FindAmhe1();
        using var content = ServerContent.PreserveContext("AMHE1");
        Read.ServerMode = false;
        ServerContent.Open(data, "AMHE1");
        using var session = new ReplayPlaybackSession();
        using var scene = new Scene { Services = session.SceneServices };
        ScenePresentation? presentation = null;
        try
        {
            SnapshotPlayer first = Snapshot(1, SnapshotPlayerFlags.Grounded,
                Vector3.Zero, BeamType.PowerBeam, slot: 0, connection: 100);
            SnapshotPlayer second = Snapshot(1, SnapshotPlayerFlags.Grounded,
                Vector3.Zero, BeamType.PowerBeam, slot: 1, connection: 200,
                hunter: Hunter.Trace);
            Feed(session, first, second, perspective: 0);
            presentation = CreatePresentation(session, scene);

            Apply(session, scene);
            PlayerEntity local = scene.Players[0];
            Assert.Same(local, scene.LocalPlayer);
            Assert.Equal(CameraType.First, local.CameraType);
            Assert.True(local.GetPresentation().HudReady);
            Assert.False(local.Flags2.TestFlag(PlayerFlags2.HideModel));
            Assert.Equal(BeamType.PowerBeam, local.CurrentWeapon);

            // Keep the wire sequence monotonic while changing only the
            // authoritative presentation state.
            SnapshotPlayer morphing = Snapshot(2, SnapshotPlayerFlags.Grounded
                | SnapshotPlayerFlags.Morphing, Vector3.UnitZ,
                BeamType.Missile, slot: 0, connection: 100);
            SnapshotPlayer secondMorph = second with { Life = 1 };
            FeedSnapshot(session, 2, morphing, secondMorph);
            Apply(session, scene);
            CameraType altCamera = local.UsesStrafeAltMovement
                ? CameraType.Third2 : CameraType.Third1;
            Assert.Equal(altCamera, local.CameraType);
            Assert.True(local.GetPresentation().HudReady);
            Assert.False(local.Flags2.TestFlag(PlayerFlags2.HideModel));
            Assert.Equal(BeamType.Missile, local.CurrentWeapon);

            // A perspective swap is a room rebuild boundary. Re-feed the
            // latest snapshot so the new local owner receives the morph state.
            session.SetPerspective(1);
            SnapshotPlayer morphingAgain = Snapshot(3,
                SnapshotPlayerFlags.Grounded | SnapshotPlayerFlags.Morphing,
                Vector3.UnitZ, BeamType.Missile, slot: 0, connection: 100);
            FeedSnapshot(session, 3, morphingAgain, secondMorph);
            session.Modern.RequestSceneReload();
            Apply(session, scene);
            RefreshHud(presentation);
            local = scene.Players[1];
            Assert.Same(local, scene.LocalPlayer);
            Assert.Equal(CameraType.First, local.CameraType);
            Assert.True(local.GetPresentation().HudReady);
            Assert.False(scene.Players[0].GetPresentation().HudReady);
            Assert.False(local.Flags2.TestFlag(PlayerFlags2.HideModel));
            Assert.Equal(BeamType.PowerBeam, local.CurrentWeapon);

            SnapshotPlayer alt = Snapshot(4, SnapshotPlayerFlags.Grounded
                | SnapshotPlayerFlags.AltForm, Vector3.UnitZ,
                BeamType.Missile, slot: 0, connection: 100);
            FeedSnapshot(session, 4, alt, secondMorph);
            Apply(session, scene);
            Assert.Equal(altCamera, scene.Players[0].CameraType);
            Assert.False(scene.Players[0].Flags2.TestFlag(PlayerFlags2.HideModel));
            Assert.Equal(BeamType.Missile, scene.Players[0].CurrentWeapon);

            SnapshotPlayer unmorphing = Snapshot(5,
                SnapshotPlayerFlags.Grounded | SnapshotPlayerFlags.Unmorphing,
                Vector3.Zero, BeamType.PowerBeam, slot: 0, connection: 100);
            FeedSnapshot(session, 5, unmorphing, secondMorph);
            Apply(session, scene);
            Assert.Equal(CameraType.First, scene.Players[0].CameraType);
            Assert.False(scene.Players[0].IsAltForm);
            Assert.True(scene.Players[0].IsUnmorphing);
            Assert.Equal(BeamType.PowerBeam, scene.Players[0].CurrentWeapon);

            session.SetPerspective(0);
            SnapshotPlayer altAgain = Snapshot(6,
                SnapshotPlayerFlags.Grounded | SnapshotPlayerFlags.AltForm,
                Vector3.UnitZ, BeamType.Missile, slot: 0, connection: 100);
            FeedSnapshot(session, 6, altAgain, secondMorph);
            session.Modern.RequestSceneReload();
            Apply(session, scene);
            RefreshHud(presentation);
            local = scene.Players[0];
            Assert.Same(local, scene.LocalPlayer);
            Assert.Equal(altCamera, local.CameraType);
            Assert.True(local.GetPresentation().HudReady);
            Assert.False(local.Flags2.TestFlag(PlayerFlags2.HideModel));
            Assert.Equal(BeamType.Missile, local.CurrentWeapon);
        }
        finally
        {
            presentation?.DoCleanup(preserveSharedAudio: true);
        }
    }

    private static CameraType CameraTypeForSnapshot(SnapshotPlayerFlags flags)
        => (flags & SnapshotPlayerFlags.Morphing) != 0
            || (flags & SnapshotPlayerFlags.AltForm) != 0
            ? CameraType.Third1 : CameraType.First;

    private static void AssertSeekState(ReplayPlaybackSession session,
        uint target, SnapshotPlayer expected, PlayerAnimation expectedAnimation,
        bool expectAudioSuppressed)
    {
        Assert.True(session.Seek(target));
        bool observedSuppression = false;
        int updates = 0;
        do
        {
            Assert.True(session.ProcessSeek(() =>
            {
                observedSuppression |= session.Modern.PresentationAudioSuppressed;
                session.PumpFrame();
            }));
            Assert.True(++updates <= 10);
        }
        while (session.IsSeeking);

        Assert.Equal(target, session.CurrentFrame);
        Assert.Equal(expected.Flags, session.Modern.Players[0].Flags);
        Assert.Equal(expected.Weapon, session.Modern.Players[0].Weapon);
        Assert.Equal(expected.Speed, session.Modern.Players[0].Speed);
        Assert.Equal(expectAudioSuppressed, observedSuppression);
        Assert.False(session.Modern.PresentationAudioSuppressed);
        Assert.Equal(expected.Flags.HasFlag(SnapshotPlayerFlags.AltForm),
            (session.Modern.Players[0].Flags & SnapshotPlayerFlags.AltForm) != 0);
        Assert.Equal(expectedAnimation,
            PlayerEntity.DeriveRemoteBipedAnimation(expected, expected.Speed));
    }

    private static ReplayTimelineClip Clip(params ReplayTimelineRecord[] records)
    {
        var restore = new List<ReplayTimelineRecord>
        {
            new(0, 120, ReplayPlaybackTests.Match(1)),
            new(0, 120, RosterRecord(1)),
            new(0, 120, SnapshotRecord(1, 1, Snapshot(0,
                SnapshotPlayerFlags.Grounded, Vector3.Zero,
                BeamType.PowerBeam))),
            new(0, 120, new byte[] { (byte)ReplayRecordKind.Perspective, 0 }),
            new(0, 120, ClockRecord()),
        };
        foreach (byte[] world in ReplayPlaybackTests.LiveWorld(1))
            restore.Add(new ReplayTimelineRecord(0, 120, world));
        byte[] feedback = ReplayFeedbackState.Capture(new CombatFeedback(),
            new WorldFeedback());
        int parts = (feedback.Length + 999) / 1000;
        for (int part = 0; part < parts; part++)
        {
            int length = Math.Min(1000, feedback.Length - part * 1000);
            byte[] fragment = new byte[9 + length];
            fragment[0] = (byte)ReplayRecordKind.Presentation;
            BinaryPrimitives.WriteUInt16LittleEndian(fragment.AsSpan(1),
                (ushort)part);
            BinaryPrimitives.WriteUInt16LittleEndian(fragment.AsSpan(3),
                (ushort)parts);
            BinaryPrimitives.WriteInt32LittleEndian(fragment.AsSpan(5),
                feedback.Length);
            feedback.AsSpan(part * 1000, length).CopyTo(fragment.AsSpan(9));
            restore.Add(new ReplayTimelineRecord(0, 120, fragment));
        }
        Assert.True(ReplayRestorePoint.TryCreate(0, 120, restore,
            out ReplayRestorePoint? point));
        return new ReplayTimelineClip(point!, records, 0,
            records.Length == 0 ? 0 : records[^1].RecordingFrame + 30);
    }

    private static void Feed(ReplayPlaybackSession session,
        SnapshotPlayer first, SnapshotPlayer second, int perspective)
    {
        Assert.True(session.Modern.Receive(ReplayPlaybackTests.Match(1)));
        Assert.True(session.Modern.Receive(RosterRecord(1,
            new NetRosterEntry(first.Slot, first.ConnectionId, first.Hunter,
                first.TeamIndex, "First"),
            new NetRosterEntry(second.Slot, second.ConnectionId, second.Hunter,
                second.TeamIndex, "Second"))));
        Assert.True(session.Modern.Receive(SnapshotRecord(1, 1, first, second)));
        foreach (byte[] world in ReplayPlaybackTests.LiveWorld(1))
            Assert.True(session.Modern.Receive(world));
        session.SetPerspective(perspective);
    }

    private static void FeedSnapshot(ReplayPlaybackSession session, uint sequence,
        SnapshotPlayer first, SnapshotPlayer second)
        => Assert.True(session.Modern.Receive(SnapshotRecord(1, sequence,
            first, second)));

    private static void Apply(ReplayPlaybackSession session, Scene scene)
    {
        session.Modern.BeforeSimulation(scene);
        session.Modern.AfterSimulation(scene);
    }

    private static void RefreshHud(ScenePresentation presentation)
    {
        typeof(ScenePresentation).GetMethod("UpdateLocalHud",
            System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(presentation, null);
    }

    private static ScenePresentation CreatePresentation(
        ReplayPlaybackSession session, Scene scene)
    {
        var presentation = new ScenePresentation(scene, new Vector2i(640, 480),
            (KeyboardState)Activator.CreateInstance(typeof(KeyboardState),
                nonPublic: true)!,
            (MouseState)Activator.CreateInstance(typeof(MouseState),
                nonPublic: true)!, static _ => { }, static () => { },
            new FrameTiming(), session.SceneServices, session);
        session.BuildPlayers(scene);
        presentation.AddRoom("MP1 SANCTORUS", GameMode.Battle,
            playerCount: session.Modern.Roster.Length);
        presentation.OnLoad();
        return presentation;
    }

    private static byte[] RosterRecord(uint matchId,
        params NetRosterEntry[] entries)
    {
        if (entries.Length == 0)
        {
            entries = new[]
            {
                new NetRosterEntry(0, 100, Hunter.Samus, 0, "Local")
            };
        }
        byte[] body = new byte[4 + SessionRosterPacket.MaxSize];
        BinaryPrimitives.WriteUInt32LittleEndian(body, matchId);
        int length = SessionRosterPacket.Write(body.AsSpan(4), 1, entries);
        return ReplayPlaybackTests.Record(ReplayRecordKind.Roster,
            body.AsSpan(0, 4 + length));
    }

    private static byte[] SnapshotRecord(uint matchId, uint sequence,
        params SnapshotPlayer[] players)
    {
        byte[] body = new byte[SnapshotPacket.HeaderSize
            + players.Length * SnapshotPlayer.Size];
        new SnapshotPacket(120 + sequence, sequence, matchId,
            0, false, 1, 2).Write(body, players);
        return ReplayPlaybackTests.Record(ReplayRecordKind.Snapshot, body);
    }

    private static byte[] ClockRecord()
    {
        byte[] record = new byte[33];
        record[0] = (byte)ReplayRecordKind.Clock;
        return record;
    }

    private static string FindAmhe1()
    {
        string? configured = Environment.GetEnvironmentVariable(
            "GAME_DATA_DIRECTORY");
        string[] starts = configured is null
            ? new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory }
            : new[] { configured, Directory.GetCurrentDirectory(),
                AppContext.BaseDirectory };
        foreach (string start in starts)
        {
            DirectoryInfo? directory = new(Path.GetFullPath(start));
            while (directory != null)
            {
                string candidate = Path.Combine(directory.FullName, "AMHE1");
                if (File.Exists(Path.Combine(candidate, "_bin", "arm9.bin"))
                    && Directory.Exists(Path.Combine(candidate, "models"))
                    && Directory.Exists(Path.Combine(candidate, "levels")))
                {
                    return candidate;
                }
                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException(
            "AMHE1 extracted content was not found.");
    }

    private static SnapshotPlayer Snapshot(uint sequence,
        SnapshotPlayerFlags stateFlags, Vector3 speed,
        BeamType weapon = BeamType.PowerBeam, byte slot = 0,
        ulong connection = 100, Hunter hunter = Hunter.Samus)
        => new()
        {
            Slot = slot,
            Hunter = hunter,
            TeamIndex = slot,
            Weapon = (byte)weapon,
            Flags = SnapshotPlayerFlags.Active | SnapshotPlayerFlags.Spawned
                | stateFlags,
            Health = 100,
            AmmoUa = 31,
            AmmoMissiles = 10,
            Life = 1,
            ConnectionId = connection,
            Position = new Vector3(slot, 0, 0),
            Speed = speed,
            Aim = Vector3.UnitZ,
            Facing = Vector3.UnitZ,
            AvailableWeapons = 0x1FF,
            ChargeLevel = 0,
            Points = 0,
            Kills = 0,
            Deaths = 0,
            Assists = 0
        };

}
