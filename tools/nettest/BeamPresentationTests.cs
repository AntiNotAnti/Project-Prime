using System;
using System.Linq;
using System.Reflection;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods.Input;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using MphRead.Mods.Replay;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

/// <summary>Actual beam draw selectors without assets, GL, collision, or weapon simulation.</summary>
internal static class BeamPresentationTests
{
    private static readonly FieldInfo Frame = typeof(Scene).GetField("_frameCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly MethodInfo ModelTransform = typeof(BeamProjectileEntity).GetMethod("GetModelTransform", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static int _checks, _failures;
    private static void Check(bool value, string label)
    {
        _checks++; if (!value) _failures++;
        Console.WriteLine($"[beam-presentation] {(value ? "PASS" : "FAIL")} {label}");
    }
    private static bool Near(Vector3 a, Vector3 b) => (a - b).Length < .0001f;
    private static void Tick(Scene scene, ulong tick) => Frame.SetValue(scene, tick);
    private static Scene NewScene(ISceneServices? services = null) => new(new Vector2i(32, 32),
        SyntheticInput.CreateKeyboard(), SyntheticInput.CreateMouse(), _ => { }, () => { },
        services, initializeRuntime: false);
    private static void Clock(int rate = 120)
    {
        FrameTiming.FrameRateCap = rate; FrameTiming.Reset(); FrameTiming.ResetDiagnostics();
        FrameTiming.Advance(1d / 120);
    }
    private static void Values(BeamProjectileEntity beam, float x, bool second)
    {
        beam.Position = new Vector3(x, 0, 0);
        beam.BackPosition = new Vector3(x - 1, 0, 0);
        beam.Direction = second ? Vector3.UnitX : Vector3.UnitZ;
        beam.Up = Vector3.UnitY; beam.Right = Vector3.UnitX;
        beam.Velocity = second ? new Vector3(3, 2, 0) : Vector3.UnitX;
        for (int i = 0; i < beam.PastPositions.Length; i++) beam.PastPositions[i] = new Vector3(x - i * .25f, 0, 0);
    }
    private static BeamProjectileEntity Pair(Scene scene, int draw = 3)
    {
        var beam = new BeamProjectileEntity(scene) { Age = 1, Lifespan = 10, DrawFuncId = draw, Flags = BeamFlags.None };
        Tick(scene, 0); Values(beam, 0, false); beam.ModResetDrawState(); beam.ModCaptureDrawState();
        Tick(scene, 1); Values(beam, 2, true); beam.ModCaptureDrawState(); Tick(scene, 2);
        return beam;
    }
    private static object Canonical(BeamProjectileEntity beam) => (beam.Position, beam.BackPosition,
        beam.Direction, beam.Velocity, beam.Up, beam.Right, beam.Transform, beam.Age, beam.Lifespan,
        beam.Flags, beam.DrawFuncId, beam.Owner, beam.Target, beam.Color,
        beam.SpawnPosition, beam.ModLaunchKey, beam.ModContinuousPhase);
    private static void Current(BeamProjectileEntity beam, string label)
    {
        object before = Canonical(beam); var past = beam.PastPositions.ToArray();
        beam.ModPrepareBeamDrawPose();
        try
        {
            Check(beam.ModBeamDrawPosition == beam.Position && beam.ModBeamDrawBack == beam.BackPosition
                && beam.ModBeamDrawDirection == beam.Direction && beam.ModBeamDrawVelocity == beam.Velocity
                && Enumerable.Range(0, 10).All(i => beam.ModBeamDrawPast(i) == beam.PastPositions[i]), label);
        }
        finally { beam.ModFinishBeamDrawPose(); }
        Check(before.Equals(Canonical(beam)) && past.SequenceEqual(beam.PastPositions), "boundary draw reads preserve canonical beam and trail values");
    }
    private static void Sampling(Scene scene)
    {
        var beam = Pair(scene);
        object before = Canonical(beam); var past = beam.PastPositions.ToArray();
        uint rng1 = scene.Random.Rng1, rng2 = scene.Random.Rng2;
        for (int repeat = 0; repeat < 5; repeat++)
        {
            Check(beam.ModPrepareBeamDrawPose(), "consecutive completed beam snapshots enable fractional drawing");
            try
            {
                Check(Near(beam.ModBeamDrawPosition, Vector3.UnitX)
                    && Near(beam.ModBeamDrawBack, Vector3.Zero), "beam head and previous endpoint share alpha .5");
                Check(Near(beam.ModBeamDrawDirection, new Vector3(1, 0, 1).Normalized())
                    && MathF.Abs(beam.ModBeamDrawDirection.Length - 1) < .0001f,
                    "beam direction uses a normalized angular midpoint");
                Check(Near(beam.ModBeamDrawVelocity, new Vector3(2, 1, 0)), "beam curve velocity uses the same completed interval");
                for (int i = 0; i < 10; i++)
                    Check(Near(beam.ModBeamDrawPast(i), new Vector3(1 - i * .25f, 0, 0)),
                        $"trail history point {i} shares the same fractional picture");
                Matrix4 actual = (Matrix4)ModelTransform.Invoke(beam, new object?[] { null, 0 })!;
                Matrix4 expected = EntityBase.GetTransformMatrix(new Vector3(1, 0, 1).Normalized(), Vector3.UnitY);
                expected.Row3.Xyz = Vector3.UnitX;
                Check(Near(actual.Row3.Xyz, expected.Row3.Xyz) && Near(actual.Row2.Xyz, expected.Row2.Xyz),
                    "actual Judicator model transform consumes the shared beam head and direction");
                Check(before.Equals(Canonical(beam)) && past.SequenceEqual(beam.PastPositions),
                    "sampling beam and actual model transform never writes canonical state");
            }
            finally { beam.ModFinishBeamDrawPose(); }
            Check(before.Equals(Canonical(beam)) && past.SequenceEqual(beam.PastPositions)
                && scene.Random.Rng1 == rng1 && scene.Random.Rng2 == rng2,
                "repeated sampled beam drawings preserve canonical fields, trail array, matrix and both RNG streams");
        }
        Check(beam.ModBeamDrawPosition == beam.Position && beam.ModBeamDrawBack == beam.BackPosition,
            "finished beam scope exposes current canonical values");

        beam = Pair(scene, 17);
        beam.ModPrepareBeamDrawPose();
        try
        {
            Matrix4 matrix = (Matrix4)ModelTransform.Invoke(beam, new object?[] { null, 0 })!;
            Check(Near(matrix.Row3.Xyz, Vector3.Zero) && MathF.Abs(matrix.Row2.Xyz.Length - 1) < .0001f,
                "actual green-energy mesh shares sampled back endpoint and head-to-back length");
        }
        finally { beam.ModFinishBeamDrawPose(); }
        FrameTiming.FrameRateCap = 60;
        Current(beam, "60 Hz presentation selects the current beam without interpolation delay");
        FrameTiming.FrameRateCap = 120;
    }
    private static void Boundaries(Scene scene)
    {
        foreach (var edit in new (string Label, Action<BeamProjectileEntity> Change)[]
        {
            ("uncaptured teleport", b => b.Position = new Vector3(20, 0, 0)),
            ("uncaptured direction edit", b => b.Direction = -Vector3.UnitZ),
            ("uncaptured trail edit", b => b.PastPositions[4] += Vector3.UnitY),
            ("draw-function identity change", b => b.DrawFuncId = 17),
            ("collision flag change", b => b.Flags |= BeamFlags.Collided),
            ("owner replacement", b => b.Owner = scene.Players.Items[0])
        })
        {
            var beam = Pair(scene); edit.Change(beam); Current(beam, edit.Label + " fences every beam component immediately");
        }
        var missing = Pair(scene); Tick(scene, 3); Current(missing, "a missing completed capture expires beam carry");
        foreach (ulong frame in new ulong[] { 1, 0, 4 })
        {
            var beam = Pair(scene); Tick(scene, frame); Values(beam, 3, true);
            beam.ModCaptureDrawState(); Tick(scene, frame + 1);
            Current(beam, $"duplicate/backward/gapped capture {frame} rebases beam carry");
        }
        var pooled = Pair(scene); Values(pooled, 3, false); pooled.ModResetDrawState();
        Current(pooled, "pooled beam reset cannot expose its predecessor's trajectory");
        var focus = Pair(scene); FrameTiming.Reset(); FrameTiming.Advance(1d / 120);
        Current(focus, "live clock reset fences beam and trail history together");
        Tick(scene, 2); Values(focus, 3, true); focus.ModCaptureDrawState(); Tick(scene, 3);
        Current(focus, "first post-focus beam capture remains canonical");
        Tick(scene, 3); Values(focus, 4, true); focus.ModCaptureDrawState(); Tick(scene, 4);
        focus.ModPrepareBeamDrawPose();
        try { Check(Near(focus.ModBeamDrawPosition, new Vector3(3.5f, 0, 0)), "second consecutive post-focus capture resumes beam sampling"); }
        finally { focus.ModFinishBeamDrawPose(); }
    }
    private static void ReplicaSeek()
    {
        var host = new PassiveReplaySessionHost();
        using var session = new ReplayPlaybackSession(host);
        var services = new ReplaySceneServices(session, host.State);
        var scene = NewScene(services); scene.ReplayRenderAlpha = .5f;
        var beam = Pair(scene);
        FrameTiming.Reset(); FrameTiming.Advance(1d / 120);
        beam.ModPrepareBeamDrawPose();
        try { Check(Near(beam.ModBeamDrawPosition, Vector3.UnitX), "replica beam carry belongs to replay seek generation rather than host focus clock"); }
        finally { beam.ModFinishBeamDrawPose(); }
        typeof(ReplayTransport).GetProperty("SeekGeneration", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(session.Transport, session.Transport.SeekGeneration + 1);
        Current(beam, "replay seek generation fences beam and all trail points before another capture");
    }
    private static void FastTravel(Scene scene)
    {
        foreach (float distance in new[] { 9f, 14f })
        {
            var beam = new BeamProjectileEntity(scene) { Age = 1, Lifespan = 10, DrawFuncId = 3 };
            Tick(scene, 0); Values(beam, 0, false); beam.Velocity = new Vector3(9, 0, 0);
            beam.ModResetDrawState(); beam.ModCaptureDrawState();
            Tick(scene, 1); Values(beam, distance, false); beam.Velocity = new Vector3(9, 0, 0);
            beam.ModCaptureDrawState(); Tick(scene, 2);
            bool sampled = beam.ModPrepareBeamDrawPose();
            try
            {
                Check(distance == 9 ? sampled && Near(beam.ModBeamDrawPosition, new Vector3(4.5f, 0, 0))
                    : !sampled && beam.ModBeamDrawPosition == beam.Position,
                    distance == 9 ? "legitimate nine-unit-per-step projectile travel interpolates beyond the ordinary entity teleport bound"
                        : "travel beyond declared projectile velocity still rebases the completed beam history");
            }
            finally { beam.ModFinishBeamDrawPose(); }
        }
    }
    private static void ProjectionRemap()
    {
        Matrix4 view = Matrix4.LookAt(new Vector3(2, 3, 4), new Vector3(2.3f, 2.8f, 3), Vector3.UnitY);
        Vector3 point = Vector3.TransformPosition(new Vector3(.2f, -.15f, -.5f), view.Inverted());
        foreach (float authoredDegrees in new[] { 39f, 78f, 110f })
        foreach (float worldDegrees in new[] { 55f, 78f, 120f, 150f })
        foreach (float aspect in new[] { 4f / 3, 16f / 9, 9f / 16 })
        {
            float authored = MathHelper.DegreesToRadians(authoredDegrees), world = MathHelper.DegreesToRadians(worldDegrees);
            Vector3 remapped = Scene.ModWorldSpaceViewModelPoint(point, view, authored, world);
            Vector4 authoredClip = Vector4.TransformRow(new Vector4(point, 1),
                view * Matrix4.CreatePerspectiveFieldOfView(authored, aspect, .1f, 1000));
            Vector4 worldClip = Vector4.TransformRow(new Vector4(remapped, 1),
                view * Matrix4.CreatePerspectiveFieldOfView(world, aspect, .1f, 1000));
            Check(MathF.Abs(authoredClip.X / authoredClip.W - worldClip.X / worldClip.W) < .0001f
                && MathF.Abs(authoredClip.Y / authoredClip.W - worldClip.Y / worldClip.W) < .0001f,
                "world-projected continuous muzzle matches authored-viewmodel projected coordinates at differing FOV/aspect");
            Check(MathF.Abs(Vector3.TransformPosition(point, view).Z - Vector3.TransformPosition(remapped, view).Z) < .0001f,
                "viewmodel-to-world remap preserves camera depth");
        }
        Check(Scene.ModWorldSpaceViewModelPoint(point, view, 1, 1) == point,
            "equal authored/world FOV preserves the exact original muzzle point");
        Check(Scene.ModWorldSpaceViewModelPoint(point, view, 0, 1) == point
            && Scene.ModWorldSpaceViewModelPoint(point, view, 1, float.NaN) == point
            && Scene.ModWorldSpaceViewModelPoint(point, default, 1, 2) == point,
            "invalid tangent or singular view remap preserves the finite original muzzle fallback");
    }
    internal static int Run()
    {
        _checks = _failures = 0;
        var priorGame = GameState.Current; var priorPlayers = PlayerEntity.LegacyRegistry; var priorRandom = Rng.Current;
        int priorCap = FrameTiming.FrameRateCap;
        try
        {
            Clock(); var scene = NewScene(); Sampling(scene); Boundaries(scene); FastTravel(scene); ProjectionRemap(); ReplicaSeek();
            const string contract = "A4512223EC0CC49516DB00DD3733168DA6969DEA059E9DDE84B31062ADEA675A";
            Check(ReplayWorldCheckpoint.ComputeContract(stable: true) == contract && ReplayCheckpointAccessors.Contract == contract,
                "beam presentation preserves the exact frozen historical world schema and generated accessor contract");
        }
        catch (Exception exception) { Check(false, exception.ToString()); }
        finally
        {
            GameState.Current = priorGame; PlayerEntity.LegacyRegistry = priorPlayers; Rng.Current = priorRandom;
            FrameTiming.FrameRateCap = priorCap; FrameTiming.Reset(); FrameTiming.ResetDiagnostics();
        }
        Console.WriteLine($"[beam-presentation] {_checks - _failures}/{_checks} checks passed.");
        return _failures == 0 ? 0 : 1;
    }
}
