using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MphRead;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods.Movement;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using MphRead.Mods.Replay;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Real retained archive, completed replica steps and private replay transports.
// Cosmetic history is intentionally absent from the stable checkpoint schema.
internal static class TraversalReplayPresentationTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int _checks;
    private static void Check(bool condition, string message)
    { _checks++; if (!condition) throw new InvalidDataException(message); }
    private static object Field(PlayerEntity player, string name)
        => typeof(PlayerEntity).GetField(name, Hidden)!.GetValue(player)!;
    private static T Field<T>(PlayerEntity player, string name) => (T)Field(player, name);
    private static T TimelineField<T>(object timeline, string name)
        => (T)timeline.GetType().GetField(name, Hidden)!.GetValue(timeline)!;
    private static bool Finite(Vector3 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    private readonly record struct Visual(TraversalGaitPresentation Gait, TraversalWalkPresentation Walk,
        TraversalSlidePresentation Slide, ulong Frame, Vector3 Position, float Reach);
    private static Visual Picture(PlayerEntity player)
    {
        ulong frame = Field<ulong>(player, "_modTraversalPictureFrame");
        // Position is scratch until a completed frame exists. Reset invalidates
        // that frame rather than serializing or clearing unused scratch values.
        return new(Field<TraversalGaitPresentation>(player, "_modCrouchGait"),
            Field<TraversalWalkPresentation>(player, "_modCrouchWalk"), Field<TraversalSlidePresentation>(player, "_modSlidePresentation"),
            frame, frame == ulong.MaxValue ? Vector3.Zero : Field<Vector3>(player, "_modTraversalPicturePosition"),
            Field<float>(player, "_modCrouchWalkReach"));
    }

    internal static int Run(string archive, bool requireActiveTravel = false)
    {
        _checks = 0;
        var size = new Vector2i(256, 192);
        using var replay = new PassiveReplayScene(archive, size);
        Check(replay.Scene.Services.IsReplica && replay.Session.Metadata is
            { RecordedMovementModel: MovementModel.Traversal, ProtocolVersion: >= 69 },
            "cosmetic replay regression uses the supplied real Traversal stance archive");
        int crouched = 0, walking = 0, sliding = 0, sampled = 0;
        int supported = 0, moving = 0, walkingClip = 0, eligible = 0, inferredGlide = 0, qualifying = 0;
        var observations = new List<object>();
        ReplayWorldCheckpoint? retained = null;
        try
        {
            while (replay.Step())
            {
                foreach (PlayerEntity player in replay.Scene.Players.Items)
                {
                    if (!player.ModCrouched || player.Health == 0 || player.IsAltForm || player.IsMorphing || player.IsUnmorphing
                        || !player.LoadFlags.TestFlag(LoadFlags.Spawned)) continue;
                    Check(!replay.Scene.MomentumOwnsPlayer(player) && !replay.Scene.MomentumMovementActive,
                        "recorded crouched actor has no local solver or live slide entitlement");
                    Visual visual = Picture(player);
                    ulong completed = replay.Scene.FrameCount - 1;
                    Check(visual.Frame == completed && visual.Gait.Ready && visual.Slide.Ready
                        && TimelineField<bool>(visual.Walk, "_ready")
                        && TimelineField<ulong>(visual.Gait, "_frame") == completed
                        && TimelineField<ulong>(visual.Walk, "_frame") == completed
                        && TimelineField<ulong>(visual.Slide, "_frame") == completed,
                        "completed replica step captures crouched gait, footwork and slide timelines");
                    Check(float.IsFinite(visual.Reach) && visual.Reach > 0 && Finite(visual.Position),
                        "remote crouched feet use the loaded native skeleton and finite completed travel");
                    if (replay.Scene.ReplayPoses!.Sample(player.SlotIndex, 1, out Vector3 completedPosition, out _))
                        Check(visual.Position == completedPosition, "capture uses completed replay pose rather than the watched viewer clock");
                    crouched++;
                    bool standing = player.Flags1.TestFlag(PlayerFlags1.Standing);
                    bool bodyEligible = (bool)typeof(PlayerEntity).GetProperty("ModMomentumBodyEligible", Hidden)!.GetValue(player)!;
                    PlayerAnimation clip = (PlayerAnimation)Field<ModelInstance>(player, "_bipedModel1").AnimInfo.NodeIndex;
                    bool nativeWalk = clip is PlayerAnimation.WalkForward or PlayerAnimation.WalkBackward
                        or PlayerAnimation.WalkLeft or PlayerAnimation.WalkRight;
                    if (standing) supported++;
                    if (player.Speed.Xz.Length > .00001f) moving++;
                    if (nativeWalk) walkingClip++;
                    if (bodyEligible) eligible++;
                    if (visual.Gait.Gliding) inferredGlide++;
                    if (standing && bodyEligible && nativeWalk && player.Speed.Xz.Length > .00001f) qualifying++;
                    if (observations.Count < 8 && player.Speed.Xz.Length > .00001f)
                        observations.Add(new { frame = replay.Session.CurrentFrame, player.SlotIndex, standing,
                            bodyEligible, clip = clip.ToString(), speed = player.Speed.Xz.Length * 30,
                            gliding = visual.Gait.Gliding, walk = visual.Walk.Sample(1).Amount, slide = visual.Slide.Sample(1) });
                    bool walk = visual.Walk.Sample(1).Amount > 0;
                    bool slide = visual.Slide.Sample(1) > 0;
                    if (walk) walking++;
                    if (slide) sliding++;
                    if (sampled == 0 || walk && walking == 1 || slide && sliding == 1)
                    { CheckSampling(replay, player); sampled++; }
                    if (retained == null && replay.Session.CurrentFrame < replay.Session.LastFrame)
                        retained = ReplayWorldCheckpoint.Capture(replay);
                }
            }
            Console.WriteLine("TRAVERSAL REPLAY OBSERVATIONS " + System.Text.Json.JsonSerializer.Serialize(new
                { crouched, walking, sliding, sampled, supported, moving, walkingClip, eligible, inferredGlide, qualifying, requireActiveTravel, observations }));
            Check(crouched > 0 && sampled > 0 && retained != null,
                "retained archive exercises real crouched cosmetic capture and a restorable recording boundary");
            // Historical scripts can crouch only after releasing movement, or
            // while airborne. Those recordings must retain their actual facts.
            // A fresh strict cohort separately has to demonstrate both modes.
            if (qualifying > 1)
                Check(walking + sliding > 0, "accepted supported native walking candidates activate a cosmetic low envelope");
            if (requireActiveTravel)
                Check(walking > 0 && sliding > 0,
                    $"strict fresh recording activates both low modes: walking={walking} sliding={sliding} qualifying={qualifying}");
            CheckColdRestore(archive, size, replay, retained!);
            CheckSeekReset(archive, size, retained!.Frame);
            Console.WriteLine($"TRAVERSAL REPLAY PRESENTATION PASS {_checks} assertions crouched={crouched} walking={walking} sliding={sliding} sampled={sampled}");
            return _checks;
        }
        finally { retained?.Dispose(); }
    }

    private static void CheckSampling(PassiveReplayScene replay, PlayerEntity player)
    {
        Scene scene = replay.Scene;
        int watched = scene.Players.MainPlayerIndex;
        string gameplay = ReplayStateHash.Compute(scene, replay.Session.CurrentFrame);
        var canonical = (player.Position, player.PrevPosition, player.Speed, player.Flags1, player.Flags2,
            player.ModCrouched, player.ModSliding, player.ModBipedTop, player.ModCaptureCollisionState());
        var clocks = (scene.FrameCount, replay.Session.CurrentFrame, replay.Session.RecordingFrame,
            FrameTiming.TotalSteps, FrameTiming.TotalFrames, FrameTiming.PresentationGeneration, FrameTiming.PresentationAlpha,
            Field(player, "_timeStanding"), Field(player, "_walkViewBob"), Field(player, "_gunViewBob"),
            Field(player, "_modSlideTicks"), Field(player, "_modSlideEntryTicks"), Field(player, "_modStanceFrame"));
        Visual before = Picture(player);
        var models = new[] { Field<ModelInstance>(player, "_bipedModel1"), Field<ModelInstance>(player, "_bipedModel2") }
            .Concat(Field<ModelInstance[]>(player, "_bipedModelLods")).ToArray();
        int[][] animation = models.Select(model => AnimationClock(model.AnimInfo)).ToArray();
        try
        {
            foreach (int viewer in new[] { player.SlotIndex, (player.SlotIndex + 1) % PlayerEntity.SlotCapacity })
            foreach (bool shooter in new[] { false, true })
            foreach (float alpha in new[] { 0f, .25f, .5f, .875f, 1f })
            {
                scene.Players.MainPlayerIndex = viewer;
                for (int repeat = 0; repeat < 3; repeat++)
                {
                    replay.Scene.ReplayPoses!.SamplePresentedForViewer(player.SlotIndex, viewer, shooter, alpha,
                        out Vector3 position, out Vector3 facing);
                    Check(Finite(position) && Finite(facing), "viewer pose sampling remains finite");
                    Visual visual = Picture(player);
                    TraversalWalkSample foot = visual.Walk.Sample(alpha);
                    float slide = visual.Slide.Sample(alpha);
                    Check(float.IsFinite(foot.Amount) && foot.Amount is >= 0 and <= 1
                        && float.IsFinite(foot.Phase) && Finite(foot.Direction) && Finite(foot.Normal)
                        && float.IsFinite(slide) && slide is >= 0 and <= 1,
                        "repeated remote footwork/slide sampling remains bounded");
                    Check(visual.Walk.Sample(alpha) == foot && visual.Slide.Sample(alpha) == slide
                        && visual.Gait.SampleBob(alpha) == visual.Gait.SampleBob(alpha)
                        && visual.Slide.SampleDirection(alpha) == visual.Slide.SampleDirection(alpha),
                        "same picture time cannot advance remote visual history");
                }
            }
        }
        finally { scene.Players.MainPlayerIndex = watched; }
        Check(canonical.Equals((player.Position, player.PrevPosition, player.Speed, player.Flags1, player.Flags2,
                player.ModCrouched, player.ModSliding, player.ModBipedTop, player.ModCaptureCollisionState()))
            && gameplay == ReplayStateHash.Compute(scene, replay.Session.CurrentFrame),
            "viewer changes and repeated pictures preserve canonical position, speed, collision and gameplay");
        Check(clocks.Equals((scene.FrameCount, replay.Session.CurrentFrame, replay.Session.RecordingFrame,
                FrameTiming.TotalSteps, FrameTiming.TotalFrames, FrameTiming.PresentationGeneration, FrameTiming.PresentationAlpha,
                Field(player, "_timeStanding"), Field(player, "_walkViewBob"), Field(player, "_gunViewBob"),
                Field(player, "_modSlideTicks"), Field(player, "_modSlideEntryTicks"), Field(player, "_modStanceFrame")))
            && before == Picture(player) && models.Select((model, index) =>
                AnimationClock(model.AnimInfo).SequenceEqual(animation[index])).All(equal => equal),
            "sampling preserves native animation/bob clocks and completed cosmetic history");
    }

    private static int[] AnimationClock(AnimationInfo info) => info.Index.Concat(info.PrevIndex).Concat(info.Frame)
        .Concat(info.FrameCount).Concat(info.Step).Concat(info.Flags.Select(flag => (int)flag)).ToArray();

    private static void CheckColdRestore(string archive, Vector2i size, PassiveReplayScene warmed, ReplayWorldCheckpoint checkpoint)
    {
        Visual[] publishedVisuals = warmed.Scene.Players.Items.Select(Picture).ToArray();
        string publishedGameplay = ReplayStateHash.Compute(warmed.Scene, warmed.Session.CurrentFrame);
        bool refused = false;
        try { checkpoint.Restore(warmed); }
        catch (InvalidOperationException error) when (error.Message == "Restore requires a new unpublished replica.")
        { refused = true; }
        Check(refused && warmed.Scene.Players.Items.Select(Picture).SequenceEqual(publishedVisuals)
            && ReplayStateHash.Compute(warmed.Scene, warmed.Session.CurrentFrame) == publishedGameplay,
            "published replay restore is refused before canonical or visual history can change");
        using var restored = new PassiveReplayScene(archive, size);
        using var fresh = new PassiveReplayScene(archive, size);
        checkpoint.Restore(restored);
        checkpoint.Restore(fresh);
        Check(restored.Session.CurrentFrame == checkpoint.Frame && fresh.Session.CurrentFrame == checkpoint.Frame,
            "real world restore returns both histories to the retained recording frame");
        foreach (PlayerEntity player in restored.Scene.Players.Items)
        {
            Visual visual = Picture(player);
            Check(!visual.Gait.Ready && !visual.Slide.Ready && !TimelineField<bool>(visual.Walk, "_ready")
                && visual.Frame == ulong.MaxValue && visual.Reach == 0,
                "unpublished world restore starts with cold gait, feet, slide heading and captured-frame history");
            Check(visual.Walk.Sample(1).Amount == 0 && visual.Slide.Sample(1) == 0
                && visual.Slide.SampleDirection(1) == Vector2.UnitY,
                "cold restore has neutral visual envelopes rather than stale pre-seek travel");
        }
        Check(ReplayStateHash.Compute(restored.Scene, checkpoint.Frame) == ReplayStateHash.Compute(fresh.Scene, checkpoint.Frame),
            "equal cold restores retain identical canonical gameplay");
        // Only equally cold worlds are compared. Nonserialized warm visual history
        // is not expected to reappear immediately at the checkpoint boundary.
        for (int tick = 0; tick < 12; tick++)
        {
            bool advanced = restored.Step();
            Check(fresh.Step() == advanced, "equally cold replica continuations agree on archive availability");
            if (!advanced) break;
            foreach (PlayerEntity player in restored.Scene.Players.Items)
                Check(Picture(player) == Picture(fresh.Scene.Players.Items[player.SlotIndex]),
                    "completed cosmetic history rebuilds equally after cold restore");
        }
    }

    private static void CheckSeekReset(string archive, Vector2i size, uint target)
    {
        using var transport = new PassiveReplayPlayer(archive, size,
            new ReplayPlayerOptions(EnableMemoryCheckpoints: false, EnableDurableCheckpoints: false));
        void Seek(uint frame)
        {
            transport.Seek(frame);
            int updates = 0;
            while (transport.Transport.IsSeeking)
            {
                Check(transport.Update() <= PassiveReplayPlayer.MaximumStepsPerUpdate, "cosmetic seek uses bounded real transport steps");
                Check(++updates <= transport.Current.Session.LastFrame + 2, "cosmetic seek cannot loop past the archive bound");
            }
            Check(transport.Current.Session.CurrentFrame == frame, "cosmetic seek reaches the exact requested archive frame");
        }
        Seek(target);
        Visual[] first = transport.Current.Scene.Players.Items.Select(Picture).ToArray();
        Scene old = transport.Current.Scene;
        Seek(0);
        Check(!ReferenceEquals(old, transport.Current.Scene), "backward seek replaces the old presentation lifetime");
        using var zero = new PassiveReplayScene(archive, size);
        // A transport seek to zero may complete recorded frame zero, while a
        // freshly opened reader still precedes that first simulation step.
        while (!zero.Session.HasSimulatedFrame || zero.Session.CurrentFrame < transport.Current.Session.CurrentFrame)
            Check(zero.Step(), "fresh zero reference completes the same recorded-frame boundary");
        foreach (PlayerEntity player in transport.Current.Scene.Players.Items)
            Check(Picture(player) == Picture(zero.Scene.Players.Items[player.SlotIndex]),
                "seek to initial frame has the fresh archive's cold visual semantics");
        Seek(target);
        Check(transport.Current.Scene.Players.Items.Select(Picture).SequenceEqual(first),
            "repeated cold forward seeks rebuild the same visual history without retaining the old seek state");
    }
}
