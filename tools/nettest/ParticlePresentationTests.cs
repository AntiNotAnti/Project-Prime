using System;
using System.Collections.Generic;
using System.Reflection;
using MphRead.Effects;
using MphRead.Entities;
using MphRead.Mods.Input;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

/// <summary>Actual particle geometry sampling without assets, GL, or a second effect simulation.</summary>
internal static class ParticlePresentationTests
{
    private static int _checks, _failures;

    private static void Check(bool value, string label)
    {
        _checks++;
        if (!value) _failures++;
        Console.WriteLine($"[particle-presentation] {(value ? "PASS" : "FAIL")} {label}");
    }

    private static bool Near(Vector3 actual, Vector3 expected) => (actual - expected).Length < .0001f;

    private static EffectParticle NewParticle(int drawId = 4)
    {
        return new EffectParticle
        {
            Owner = new EffectElementEntry { Transform = Matrix4.Identity, Flags = EffElemFlags.DrawEnabled },
            Random = new MatchRandom(),
            CreationTime = 1, ExpirationTime = 10, Lifespan = 9,
            Position = Vector3.Zero, Speed = Vector3.UnitX,
            Scale = 1, Rotation = 0, Alpha = 1, Red = 1, Green = .25f, Blue = .5f,
            SetVecsId = 1, DrawId = drawId
        };
    }

    private static EffectParticle Pair(int drawId = 4)
    {
        var particle = NewParticle(drawId);
        particle.ModCaptureDrawState(1, 1, 1);
        particle.Position = new Vector3(2, 0, 0);
        particle.Scale = 3; particle.Rotation = 90;
        particle.Red = .25f; particle.Green = 1; particle.Blue = .75f; particle.Alpha = .5f;
        particle.Speed = new Vector3(3, 1, 0);
        particle.ModCaptureDrawState(1, 1, 2);
        return particle;
    }

    // Capturing all simulation particle properties makes repeated draw checks
    // independent of how the implementation stores its private display state.
    private static object Canonical(EffectParticle p) => (p.Position, p.Speed, p.Scale, p.Rotation,
        p.Red, p.Green, p.Blue, p.Alpha, p.CreationTime, p.ExpirationTime, p.Lifespan,
        p.ParticleId, p.DrawId, p.SetVecsId, p.Owner, p.MaterialId,
        p.PortionTotal, p.RoField1, p.RoField2, p.RoField3, p.RoField4,
        p.RwField1, p.RwField2, p.RwField3, p.RwField4);

    private static object ArchivedDrawCaches(EffectParticle p) => (p.EffectVec1, p.EffectVec2, p.EffectVec3,
        p.ShouldDraw, p.Color, p.Texcoord0, p.Vertex0, p.Texcoord1, p.Vertex1,
        p.Texcoord2, p.Vertex2, p.Texcoord3, p.Vertex3, p.DrawNode, p.BillboardMode, p.NodeTransform);

    private static (Vector3 V0, Vector3 V1, Vector3 Color, Matrix4 Node, bool Draw) Draw(
        EffectParticle p, double alpha, bool interpolate = true,
        long scene = 1, long timeline = 1, ulong frame = 2)
    {
        object before = Canonical(p);
        // Independently run the preexisting canonical geometry entry points.
        // Finishing a fractional picture must leave exactly those historical
        // caches, including the same view-dependent vector basis.
        p.ModBeginCanonicalDraw();
        p.InvokeSetVecsFunc(Matrix4.Identity);
        p.InvokeDrawFunc(1);
        object caches = ArchivedDrawCaches(p);
        uint rng1 = p.Random.Rng1, rng2 = p.Random.Rng2;
        p.ModPrepareDraw(scene, timeline, frame, alpha, interpolate);
        try
        {
            Check(before.Equals(Canonical(p)), "preparing display sample preserves canonical properties");
            p.InvokeSetVecsFunc(Matrix4.Identity);
            p.InvokeDrawFunc(1);
            Check(before.Equals(Canonical(p)), "geometry generation preserves canonical properties");
            return (p.Vertex0, p.Vertex1, p.Color, p.NodeTransform, p.ShouldDraw);
        }
        finally
        {
            p.ModFinishDraw();
            Check(before.Equals(Canonical(p)), "finishing display sample preserves canonical properties");
            Check(caches.Equals(ArchivedDrawCaches(p)), "finishing display sample restores exact canonical checkpoint draw caches");
            Check(rng1 == p.Random.Rng1 && rng2 == p.Random.Rng2, "display sample preserves both RNG streams");
        }
    }

    private static Vector3 RotatingVertex(float scale, float degrees)
    {
        // Independent billboard corner: the first edge is the authored angle
        // plus 90 degrees; the other edge is the authored angle.
        double angle = degrees * Math.PI / 180;
        return new Vector3((float)((-Math.Cos(angle) + Math.Sin(angle)) * scale / 2),
            (float)((-Math.Sin(angle) - Math.Cos(angle)) * scale / 2), 0);
    }

    private static void Sampling()
    {
        var p = Pair();
        foreach (double alpha in new[] { 0.0, .25, .5, .75, 1.0, .5 })
        {
            var sample = Draw(p, alpha);
            Check(sample.Draw && Near(sample.V0, RotatingVertex(1 + 2 * (float)alpha, 90 * (float)alpha)),
                $"rotating billboard samples independent scale/angle geometry at alpha {alpha}");
            Check(Near(sample.Color, Vector3.Lerp(new Vector3(1, .25f, .5f), new Vector3(.25f, 1, .75f), (float)alpha)),
                $"particle color samples completed endpoints at alpha {alpha}");
        }
        var current = Draw(p, 1);
        Check(Near(Draw(p, .5, interpolate: false).V0, current.V0), "60 Hz policy selects the current completed particle");
        foreach (double invalid in new[] { double.NaN, double.NegativeInfinity, double.PositiveInfinity })
            Check(Near(Draw(p, invalid).V0, current.V0), "nonfinite display fraction selects canonical geometry");
        Check(Near(Draw(p, -1).V0, RotatingVertex(1, 0))
            && Near(Draw(p, 2).V0, current.V0), "finite fractions clamp within completed endpoints");

        p = NewParticle(); p.Rotation = 359; p.ModCaptureDrawState(1, 1, 1);
        p.Rotation = 1; p.ModCaptureDrawState(1, 1, 2);
        Check(Near(Draw(p, .5).V0, RotatingVertex(1, 0)), "359-to-1 billboard rotation crosses zero by the short arc");

        p = NewParticle(6); p.Rotation = 359; p.ModCaptureDrawState(1, 1, 1);
        p.Rotation = 1; p.ModCaptureDrawState(1, 1, 2);
        // DrawShared uses Rotation as a width, rather than an angle. UnitX
        // crossed with -UnitY gives -UnitZ, so midpoint width 180 puts Z=-90.
        Check(Near(Draw(p, .5).V0, new Vector3(-.5f, 0, -90)),
            "nonangular DrawId 6 interpolates Rotation as linear width");

        p = Pair(7);
        Check(Near(Draw(p, .5).Node.Row3.Xyz, new Vector3(1, 0, 0)),
            "model-particle transform samples translation without mutating Position");
        Check(Near(Draw(p, .5).Node.Row0.Xyz, new Vector3(2, 0, 0)),
            "model-particle transform samples scale on the same interval");
    }

    private static void Boundaries()
    {
        void Current(EffectParticle p, string label, long scene = 1, long timeline = 1, ulong frame = 2)
        {
            var expected = Draw(p, 1, interpolate: false, scene, timeline, frame);
            var actual = Draw(p, .5, scene: scene, timeline: timeline, frame: frame);
            Check(Near(actual.V0, expected.V0) && Near(actual.Color, expected.Color), label);
        }
        Current(Pair(), "scene generation change fences old particle endpoints", scene: 2);
        Current(Pair(), "timeline generation change fences old particle endpoints", timeline: 2);
        Current(Pair(), "missing completed particle capture selects canonical geometry", frame: 3);

        foreach (var boundary in new (string Label, Action<EffectParticle> Change)[]
        {
            ("owner replacement", p => p.Owner = new EffectElementEntry { Transform = Matrix4.Identity }),
            ("new creation time", p => p.CreationTime += 1),
            ("particle definition change", p => p.ParticleId += 1),
            ("draw function change", p => p.DrawId = 5),
            ("set-vector function change", p => p.SetVecsId = 2),
            ("owner flag change", p => p.Owner.Flags |= EffElemFlags.UseTransform)
        })
        {
            var p = Pair(); boundary.Change(p);
            Current(p, $"immediate {boundary.Label} fences particle carry");
            p.ModCaptureDrawState(1, 1, 3);
            Current(p, $"first capture after {boundary.Label} rebases particle carry", frame: 3);
        }
        foreach (ulong frame in new ulong[] { 2, 1, 4 })
        {
            var p = Pair(); p.Scale = 4; p.ModCaptureDrawState(1, 1, frame);
            Current(p, $"duplicate, backward or skipped capture {frame} rebases particle carry", frame: frame);
        }
        var overflow = Pair(); overflow.ModCaptureDrawState(1, 1, ulong.MaxValue);
        overflow.Scale = 4; overflow.ModCaptureDrawState(1, 1, 0);
        Current(overflow, "overflowed effect frame cannot carry across the wrapped timeline", frame: 0);
        var reset = Pair(); reset.ModResetDrawState();
        Current(reset, "pooled particle reset discards predecessor geometry");
        reset.ModCaptureDrawState(1, 1, 3);
        Current(reset, "first pooled birth capture starts with canonical geometry", frame: 3);

        var edited = Pair(); edited.Scale = 7;
        Current(edited, "canonical particle edit after capture rejects stale history");
        var alpha = Pair(); alpha.Alpha = 0;
        Check(!Draw(alpha, .5).Draw, "uncaptured canonical alpha zero cannot revive a stale visible particle");

        foreach (var generations in new[] { (Scene: 2L, Timeline: 1L), (Scene: 1L, Timeline: 2L) })
        {
            var p = Pair(); p.Scale = 4; p.ModCaptureDrawState(generations.Scene, generations.Timeline, 3);
            Current(p, "first capture in new scene/timeline rebases", generations.Scene, generations.Timeline, 3);
            p.Scale = 6; p.ModCaptureDrawState(generations.Scene, generations.Timeline, 4);
            Check(Near(Draw(p, .5, scene: generations.Scene, timeline: generations.Timeline, frame: 4).V0,
                RotatingVertex(5, 90)), "second consecutive capture resumes interpolation after generation fence");
        }
    }

    private static void NondrawingCanonicalCaches()
    {
        var fade = NewParticle();
        fade.InvokeSetVecsFunc(Matrix4.Identity); fade.InvokeDrawFunc(1);
        fade.ModCaptureDrawState(1, 1, 1);
        fade.Alpha = 0; fade.Scale = 3; fade.Red = .25f;
        fade.ModCaptureDrawState(1, 1, 2);
        Check(Draw(fade, .5).Draw,
            "fading particle remains visible between captured positive-alpha and invisible current endpoints");
        Check(!fade.ShouldDraw, "visible fractional fade restores the invisible canonical draw flag and untouched caches");

        var streak = NewParticle(3);
        streak.Speed = new Vector3(0, 0, 3); streak.Rotation = 1;
        streak.InvokeSetVecsFunc(Matrix4.Identity); streak.InvokeDrawFunc(1);
        streak.ModCaptureDrawState(1, 1, 1);
        streak.Speed = Vector3.Zero; streak.Scale = 3; streak.Red = .25f;
        streak.ModCaptureDrawState(1, 1, 2);
        Check(Draw(streak, .5).Draw,
            "fractional streak draws while canonical speed falls below the native draw threshold");
        Check(!streak.ShouldDraw && streak.EffectVec1 == Vector3.UnitX,
            "visible fractional streak restores canonical low-speed flag and set-vector basis exactly");
    }

    private static void NodeSubmission()
    {
        object rawNode = default(RawNode);
        typeof(RawNode).GetField(nameof(RawNode.Name))!.SetValue(rawNode, new byte[64]);
        object rawMaterial = default(RawMaterial);
        typeof(RawMaterial).GetField(nameof(RawMaterial.Name))!.SetValue(rawMaterial, new byte[64]);
        typeof(RawMaterial).GetField(nameof(RawMaterial.Alpha))!.SetValue(rawMaterial, (byte)31);
        typeof(RawMaterial).GetField(nameof(RawMaterial.TextureId))!.SetValue(rawMaterial, (short)-1);
        typeof(RawMaterial).GetField(nameof(RawMaterial.PaletteId))!.SetValue(rawMaterial, (short)-1);
        var model = new Model("particle-presentation-synthetic", false, default,
            new[] { (RawNode)rawNode }, new[] { default(RawMesh) }, new[] { (RawMaterial)rawMaterial },
            Array.Empty<DisplayList>(), Array.Empty<IReadOnlyList<RenderInstruction>>(), new AnimationResults(),
            Array.Empty<Matrix4>(), Array.Empty<Recolor>(), Array.Empty<int>(),
            Array.Empty<Vector3Fx>(), Array.Empty<Vector3Fx>(), Array.Empty<int>(), Array.Empty<Fixed>());
        var priorGame = GameState.Current;
        var priorPlayers = PlayerEntity.LegacyRegistry;
        var priorRandom = Rng.Current;
        try
        {
            // No textures means UpdateMaterials performs no driver or binding
            // work; submission still executes the real copied packet owner.
            var scene = new Scene(new Vector2i(32, 32), SyntheticInput.CreateKeyboard(),
                SyntheticInput.CreateMouse(), _ => { }, () => { }, initializeRuntime: false);
            var packets = (Queue<RenderItem>)typeof(Scene).GetField("_usedRenderItems",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scene)!;
            foreach (bool canonicalVisible in new[] { true, false })
            {
                var p = NewParticle(7);
                p.Owner.Model = model; p.Owner.Nodes.Add(model.Nodes[0]);
                p.ModCaptureDrawState(1, 1, 1);
                p.Position = new Vector3(2, 0, 0); p.Scale = 3;
                p.Red = .25f; p.Green = 1; p.Blue = .75f; p.Alpha = canonicalVisible ? .5f : 0;
                p.ModCaptureDrawState(1, 1, 2);
                var material = model.Materials[0];
                Vector3 priorDiffuse = new(.2f, .3f, .4f);
                material.CurrentDiffuse = priorDiffuse; material.CurrentAlpha = .125f;
                material.RenderMode = RenderMode.Translucent;
                p.ModBeginCanonicalDraw(); p.InvokeSetVecsFunc(Matrix4.Identity); p.InvokeDrawFunc(1);
                object caches = ArchivedDrawCaches(p), canonical = Canonical(p);
                p.ModPrepareDraw(1, 1, 2, .5, true);
                RenderItem packet;
                try
                {
                    p.InvokeSetVecsFunc(Matrix4.Identity); p.InvokeDrawFunc(1);
                    Check(p.ShouldDraw && p.DrawNode, "fractional model particle reaches real mesh submission");
                    int count = packets.Count;
                    p.AddRenderItem(scene);
                    Check(packets.Count == count + 1, "model particle submits exactly one owned render packet");
                    packet = packets.ToArray()[^1];
                    Check(Near(packet.Diffuse, new Vector3(.625f, .625f, .625f))
                        && MathF.Abs(packet.Alpha - (canonicalVisible ? .75f : .5f)) < .0001f,
                        "submitted model packet owns fractional particle color and alpha");
                    Check(Near(packet.Transform.Row3.Xyz, new Vector3(1, 0, 0))
                        && Near(packet.Transform.Row0.Xyz, new Vector3(2, 0, 0)),
                        "submitted model packet owns matching fractional translation and scale");
                    Check(material.CurrentDiffuse == (canonicalVisible ? new Vector3(.25f, 1, .75f) : priorDiffuse)
                        && material.CurrentAlpha == (canonicalVisible ? .5f : .125f)
                        && material.RenderMode == RenderMode.Translucent,
                        "sampled submission restores canonical material values, preserving untouched invisible material");
                }
                finally { p.ModFinishDraw(); }
                Check(canonical.Equals(Canonical(p)) && caches.Equals(ArchivedDrawCaches(p)),
                    "real model submission leaves canonical fields and historical geometry caches exact");
                Check(Near(packet.Diffuse, new Vector3(.625f, .625f, .625f))
                    && MathF.Abs(packet.Alpha - (canonicalVisible ? .75f : .5f)) < .0001f,
                    "canonical cache restoration cannot rewrite the submitted packet");
            }
        }
        finally
        {
            GameState.Current = priorGame;
            PlayerEntity.LegacyRegistry = priorPlayers;
            Rng.Current = priorRandom;
        }
    }

    private static void AllocationCycle(EffectParticle particle)
    {
        particle.ModBeginCanonicalDraw();
        particle.InvokeSetVecsFunc(Matrix4.Identity);
        particle.InvokeDrawFunc(1);
        particle.ModPrepareDraw(1, 1, 2, .5, true);
        particle.InvokeSetVecsFunc(Matrix4.Identity);
        particle.InvokeDrawFunc(1);
        particle.ModFinishDraw();
    }

    private static void AllocationRegression()
    {
        var particle = Pair();
        for (int i = 0; i < 100; i++) AllocationCycle(particle);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 20_000; i++) AllocationCycle(particle);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocated == 0,
            $"20,000 warmed actual canonical/sample/restore geometry cycles allocate {allocated} managed bytes");
    }

    private static void PresentationOwnerCaches()
    {
        var particle = NewParticle(7);
        particle.Position = Vector3.UnitX;
        particle.Owner.Flags |= EffElemFlags.UseTransform;
        particle.Owner.Transform = Matrix4.CreateTranslation(10, 0, 0);
        particle.Owner.EffectEntry = new EffectEntry { DrawTransformOverride = Matrix4.CreateTranslation(20, 0, 0) };
        var sample = Draw(particle, .5);
        Check(Near(sample.Node.Row3.Xyz, new Vector3(21, 0, 0)),
            "presentation-only emitter transform reaches sampled model-particle geometry without particle motion history");
        Check(Near(particle.NodeTransform.Row3.Xyz, new Vector3(11, 0, 0)),
            "owner transform override restores canonical particle node cache instead of serializing the displayed emitter");
        Check(particle.Owner.Transform == Matrix4.CreateTranslation(10, 0, 0)
            && particle.Position == Vector3.UnitX,
            "emitter presentation override never writes canonical emitter transform or particle position");
    }

    internal static int Run()
    {
        _checks = _failures = 0;
        try { Sampling(); Boundaries(); NondrawingCanonicalCaches(); NodeSubmission(); PresentationOwnerCaches(); AllocationRegression(); }
        catch (Exception exception) { Check(false, exception.ToString()); }
        Console.WriteLine($"[particle-presentation] {_checks - _failures}/{_checks} checks passed.");
        return _failures == 0 ? 0 : 1;
    }
}
