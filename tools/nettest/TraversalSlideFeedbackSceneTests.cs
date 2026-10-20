using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MphRead;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Formats.Culling;
using MphRead.Mods.Movement;
using MphRead.Sound;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.NetTest;

// Asset-backed CPU input/collision and native audio-request routing. A counting
// silent backend records requests, never opens a device or claims audibility.
// Initial placement is the sole physical fixture seam. The isolated native
// footstep-clock probe restores its audio-local timer/index and scene RNG before
// any subsequent physics; it never disables an earned slide or edits rules.
internal static class TraversalSlideFeedbackSceneTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Action<KeyboardState, Keys, bool> SetKey = typeof(KeyboardState)
        .GetMethod("SetKeyState", Hidden | BindingFlags.Public)!.CreateDelegate<Action<KeyboardState, Keys, bool>>();
    private static readonly MethodInfo Walking = typeof(PlayerEntity).GetMethod("UpdateWalkingSfx", Hidden)!;
    private static readonly PropertyInfo Backend = typeof(Sfx).GetProperty(nameof(Sfx.Instance))!;
    private static int _checks;
    private static void Check(bool good, string label)
    { _checks++; if (!good) throw new InvalidOperationException(label); }
    private static void Near(float actual, float expected, string label)
        => Check(float.IsFinite(actual) && MathF.Abs(actual - expected) <= .001f, $"{label}: {actual} != {expected}");
    private static FieldInfo PlayerField(string name) => typeof(PlayerEntity).GetField(name, Hidden)!;
    private static T Field<T>(PlayerEntity p, string name) => (T)PlayerField(name).GetValue(p)!;
    private static float Amount(PlayerEntity p) => Field<float>(p, "_moveSfxAmount");
    private static Terrain TerrainOf(PlayerEntity p) => Field<Terrain>(p, "_standTerrain");
    private static int Id(PlayerEntity p, TerrainSfx kind) => Metadata.TerrainSfx[(int)TerrainOf(p), (int)kind];
    private static int SlideId(PlayerEntity p)
    {
        int id = Id(p, TerrainSfx.Slide);
        return id >= 0 ? id : Id(p, TerrainSfx.Roll);
    }
    private static bool Footstep(PlayerEntity p, Play request, SoundSource source)
        => ReferenceEquals(request.Source, source) && (request.Id == Id(p, TerrainSfx.Walk1) || request.Id == Id(p, TerrainSfx.Walk2));

    private sealed record Play(int Id, SoundSource? Source, bool Loop, float Amount, string Kind);
    private sealed class Counter : SfxInstanceBase
    {
        internal readonly List<Play> Requests = new();
        internal readonly List<(SoundSource Source, int Id)> Stops = new();
        public override void StopSoundFromSource(SoundSource source, int id) => Stops.Add((source, id));
        public override void PlayDgn(int id, SoundSource? source, bool loop, bool noUpdate,
            float recency, bool cancellable, float amountA, float amountB)
            => Requests.Add(new(id, source, loop, amountA, "dgn"));
        public override void PlayScript(int id, SoundSource? source, bool noUpdate,
            float recency, bool sourceOnly, bool cancellable)
            => Requests.Add(new(id, source, false, 0, "script"));
        public override int PlaySample(int id, SoundSource? source, bool? loop, bool noUpdate,
            float recency, bool sourceOnly, bool cancellable)
        { Requests.Add(new(id, source, loop == true, 0, "sample")); return -1; }
    }

    internal static object Capture(Hunter hunter)
    {
        _checks = 0;
        Check(hunter is >= Hunter.Samus and <= Hunter.Weavel, "one of the seven native Hunters owns this fixture");
        var rows = new List<object>();
        foreach (string scenario in new[] { "release", "jump-noncontact", "native-form", "stance-reset" })
            rows.Add(CaptureScenario(hunter, scenario));
        return new { hunter = hunter.ToString(), checks = _checks,
            scope = "Authored assets, real W then fresh Ctrl, native collision and counting silent SFX backend; bounded control amounts and request suppression only, no acoustic/device/channel-lifetime claim", rows };
    }

    private static object CaptureScenario(Hunter hunter, string scenario)
    {
        object?[] arguments = { hunter, MovementModel.Traversal, null };
        Scene scene = (Scene)typeof(TraversalSceneTests).GetMethod("Load", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, arguments)!;
        var keyboard = (KeyboardState)arguments[2]!;
        SfxInstanceBase? savedBackend = Sfx.Instance;
        var counter = new Counter();
        var trace = new List<object>();
        try
        {
            PlayerEntity p = scene.Players.Main!;
            p.Controls.Crouch.Key = Keys.LeftControl;
            Vector3 placement = new(0, .5f, 12);
            p.Reposition(placement, Vector3.UnitZ, NodeRef.None); p.PrevPosition = placement;
            for (int tick = 0; tick < 12; tick++) scene.OnSimulationFrame();
            Check(p.Flags1.TestFlag(PlayerFlags1.Standing) && p.Speed.Xz.Length == 0,
                "explicit initial empty-floor lane settles with real native support");
            var source = (SoundSource)typeof(EntityBase).GetField("_soundSource", Hidden)!.GetValue(p)!;
            Backend.SetValue(null, counter);
            Check(ReferenceEquals(scene.Audio, counter) && ReferenceEquals(source.Owner, scene),
                "silent request counting uses the actual player-owned native sound source");
            SetKey(keyboard, Keys.W, true);
            for (int tick = 0; tick < 32; tick++) scene.OnSimulationFrame();
            Check(!p.ModSliding && !p.ModCrouched && p.ModMomentumEligible
                && p.Speed.Xz.Length * 30 >= TraversalMovementProfileV5.SlideMinimumSpeed,
                "normal keyboard W genuinely earns running speed before the fresh Ctrl edge");
            int walkingRequests = counter.Requests.Count(request => Footstep(p, request, source));
            Check(walkingRequests > 0, "the normal W control actually emits mapped native walking requests");
            Near(Amount(p), 0, "ordinary non-slippery running has not manufactured slide feedback");
            counter.Requests.Clear();
            SetKey(keyboard, Keys.LeftControl, true);
            float maximum = 0; int positive = 0; bool probed = false;
            for (int tick = 0; tick < 24; tick++)
            {
                scene.OnSimulationFrame();
                Check(p.ModSliding && p.ModCrouched && p.ModMomentumEligible && p.Controls.MoveUp.IsDown
                    && p.Flags1.TestFlag(PlayerFlags1.Standing), "held W and real Ctrl retain an actual supported physical slide");
                float amount = Amount(p); maximum = Math.Max(maximum, amount);
                Check(float.IsFinite(amount) && amount >= 0 && amount <= TraversalSlideFeedback.MaximumAmount + .001f,
                    "the native smoothed slide amount stays inside the quiet control cap");
                if (amount > 0) positive++;
                Check(counter.Requests.All(request => !Footstep(p, request, source)),
                    "physical held-W sliding suppresses walking emission without replacing walking input flags");
                int slideId = SlideId(p);
                Check(counter.Requests.Where(request => request.Id == slideId)
                    .All(request => ReferenceEquals(request.Source, source) && (request.Kind == "script" || request.Loop)),
                    "terrain slide feedback reuses the player-owned source and exposed native loop flag");
                if (!probed && scenario == "release" && NextFootstepOpportunity(scene, p))
                { ProbeWalkingClock(scene, p, counter, source); probed = true; }
                trace.Add(new { tick, amount, horizontalSpeed = p.Speed.Xz.Length * 30,
                    standing = p.Flags1.TestFlag(PlayerFlags1.Standing), p.ModSliding,
                    timer = Field<float>(p, "_walkSfxTimer"), index = Field<int>(p, "_walkSfxIndex") });
            }
            Check(positive > 12 && maximum > 1000, "earned supported sliding requests a persistent positive native movement amount");
            int selectedId = SlideId(p);
            Check(counter.Requests.Any(request => request.Id == selectedId
                && ReferenceEquals(request.Source, source)),
                "actual slide reaches the native terrain slide-or-roll loop backend");
            bool fallback = Id(p, TerrainSfx.Slide) < 0;
            if (scenario == "stance-reset")
            {
                Check(fallback && Field<int>(p, "_modTraversalSlideSfx") == selectedId,
                    "ordinary floor owns a tracked native fallback before reset");
                var physical = (p.Position, p.Speed, scene.FrameCount);
                p.ModResetTraversalStance();
                Check(Field<int>(p, "_modTraversalSlideSfx") == -1
                    && counter.Stops.Any(stop => stop.Id == selectedId && ReferenceEquals(stop.Source, source)),
                    "stance reset explicitly retires the tracked fallback on its original source");
                Check(physical == (p.Position, p.Speed, scene.FrameCount),
                    "audio teardown does not change position, velocity or completed tick");
                return new { scenario, selectedId, fallback, maximumAmount = maximum, trace };
            }
            if (scenario == "release") Check(probed, "actual slide cadence reaches a real native footstep emission opportunity for the clock/RNG probe");
            counter.Requests.Clear();
            float beforeExit = Amount(p); int airborne = 0, ineligible = 0;
            if (scenario == "release") SetKey(keyboard, Keys.LeftControl, false);
            else if (scenario == "jump-noncontact") SetKey(keyboard, Keys.Space, true);
            else { SetKey(keyboard, Keys.W, false); SetKey(keyboard, Keys.Tab, true); }
            int duration = scenario == "native-form" ? 120 : 60;
            for (int tick = 0; tick < duration; tick++)
            {
                scene.OnSimulationFrame();
                SetKey(keyboard, Keys.Space, false); SetKey(keyboard, Keys.Tab, false);
                Check(!p.ModSliding, "release, accepted jump or native form handoff retires the physical slide");
                bool supported = p.Flags1.TestFlag(PlayerFlags1.Standing);
                if (!supported) airborne++;
                if (!p.ModMomentumEligible) ineligible++;
                float requested = TraversalSlideFeedback.Amount(p.Speed.Xz.Length,
                    p.Values.WalkSpeedCap / 4096f, supported && p.ModMomentumEligible, p.ModSliding);
                Near(requested, 0, "non-slide, unsupported or native-owned states request no Traversal feedback");
                if (scenario != "native-form")
                {
                    Check(Amount(p) <= beforeExit + .001f, "native biped movement amount decays after feedback eligibility ends");
                    beforeExit = Amount(p);
                }
                Check(ReferenceEquals(source, typeof(EntityBase).GetField("_soundSource", Hidden)!.GetValue(p)),
                    "feedback and its state transitions never replace the native player sound source");
            }
            if (scenario == "release")
            {
                Near(Amount(p), 0, "native decay and silence threshold eventually clear released slide feedback");
                Check(counter.Requests.Any(request => Footstep(p, request, source)),
                    "walking emission resumes through the same native cadence after Ctrl release");
            }
            else if (scenario == "jump-noncontact")
            {
                Check(airborne > 3, "real native jump produces unsupported physical ticks rather than a fabricated contact flag");
                Near(Amount(p), 0, "native decay clears the unsupported slide tail");
            }
            else Check(ineligible > 0 && p.IsAltForm,
                "real Tab completes a native form transition and excludes Traversal feedback ownership");
            Check(Field<int>(p, "_modTraversalSlideSfx") == -1,
                "release, unsupported decay and native form handoff clear fallback ownership");
            if (fallback) Check(counter.Stops.Any(stop => stop.Id == selectedId && ReferenceEquals(stop.Source, source)),
                "each fallback exit explicitly stops its ID on the native player source");
            return new { scenario, selectedId, fallback, placement = new[] { placement.X, placement.Y, placement.Z },
                walkingRequests, positiveSlideTicks = positive, maximumAmount = maximum,
                clockRngProbe = probed, airborneTicks = airborne, nativeOwnershipTicks = ineligible,
                finalAmount = Amount(p), finalAltForm = p.IsAltForm, trace };
        }
        finally
        {
            Backend.SetValue(null, savedBackend);
            MomentumSceneTests.Release(scene);
        }
    }

    private static bool NextFootstepOpportunity(Scene scene, PlayerEntity p)
    {
        if (!p.Flags1.TestFlag(PlayerFlags1.MovingBiped) || Field<float>(p, "_hSpeedMag") <= 0) return false;
        float next = Field<float>(p, "_walkSfxTimer") + scene.FrameTime;
        return next >= 25 / 30f || next >= 15 / 30f && Field<int>(p, "_walkSfxIndex") == 0;
    }

    private static void ProbeWalkingClock(Scene scene, PlayerEntity p, Counter counter, SoundSource source)
    {
        float timer = Field<float>(p, "_walkSfxTimer"); int index = Field<int>(p, "_walkSfxIndex");
        uint rng1 = scene.Random.Rng1, rng2 = scene.Random.Rng2;
        var physical = (p.Position, p.PrevPosition, p.Speed, p.Flags1, p.Flags2, p.ModCrouched, p.ModSliding,
            scene.FrameCount, p.ModCaptureCollisionState());
        float moveAmount = Amount(p), expectedTimer = timer + scene.FrameTime;
        int expectedIndex = index, expectedId = -1;
        if (expectedTimer >= 15 / 30f && expectedIndex == 0)
        { expectedId = Id(p, TerrainSfx.Walk1); expectedIndex = 1; }
        if (expectedTimer >= 25 / 30f)
        { expectedId = Id(p, TerrainSfx.Walk2); expectedTimer = 5 / 30f; expectedIndex = 0; }
        Check(expectedId >= 0 && p.ModSliding && p.ModMomentumEligible
            && p.Flags1.TestFlag(PlayerFlags1.Standing), "clock probe uses an earned slide at a mapped native footstep opportunity");
        uint expectedRng = rng1;
        MatchRandom.CallRng(ref expectedRng, 0x7FFF);
        int first = counter.Requests.Count;
        try
        {
            Walking.Invoke(p, null);
            Near(Field<float>(p, "_walkSfxTimer"), expectedTimer, "suppression preserves the native walking timer transition");
            Check(Field<int>(p, "_walkSfxIndex") == expectedIndex, "suppression preserves the native left/right footstep index transition");
            Check(scene.Random.Rng1 == expectedRng && scene.Random.Rng2 == rng2,
                "suppression consumes exactly the original shared RNG1 draw and no RNG2 draw");
            Check(!counter.Requests.Skip(first).Any(request => Footstep(p, request, source)),
                "a real native emission opportunity is suppressed after timer and random work");
            Check(physical == (p.Position, p.PrevPosition, p.Speed, p.Flags1, p.Flags2, p.ModCrouched, p.ModSliding,
                scene.FrameCount, p.ModCaptureCollisionState()), "the direct audio probe never changes physical rules or state");
            Near(Amount(p), moveAmount, "walking emission gating cannot alter the slide amount");
        }
        finally
        {
            PlayerField("_walkSfxTimer").SetValue(p, timer);
            PlayerField("_walkSfxIndex").SetValue(p, index);
            scene.Random.SetRng1(rng1); scene.Random.SetRng2(rng2);
        }
        Check(scene.Random.Rng1 == rng1 && scene.Random.Rng2 == rng2
            && Field<float>(p, "_walkSfxTimer") == timer && Field<int>(p, "_walkSfxIndex") == index,
            "isolated probe restores audio-local state and RNG exactly before the next real simulation tick");
    }
}
