using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Formats.Culling;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Movement;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.NetTest;

// Owned AMHE1 offline CPU acceptance. All form/attack/hover changes use actual
// KeyboardState events and OnSimulationFrame after real Scene initialization.
// Aim-order acceptance uses raw MouseState samples gathered by native input.
// Initial split can rest on the planted native body. The hover arm deliberately
// walks clear via real Forward input before asserting actual room-floor support.
// The second inert target, public native Spawn placements, remaining-health
// cap setup and injected native freeze timer are explicitly synthetic fixtures.
// The capacity arm directly invokes the production volley hook without advancing
// frames; it qualifies native allocation/retirement, not gameplay shot cadence.
// A separate body-first retirement arm sets an explicit native target/cooldown
// and calls actual body.Process before the player after a rules-owner conflict.
// No reflection writes drone state, controls, health split, timers or shot counts.
// This prototype uses the native turret model/sphere as its planted body and
// Psycho Bit as its mobile fallback. It does not qualify the donor's standing
// cylinder/headshots, imported presentation, laser, combat balance or online play.
internal static class ScoutDroneSceneTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int _checks;
    // Independent literal source-authored oracle: 19 twin volleys, not values
    // queried from production constants or copied state-machine arithmetic.
    private static readonly int[] VolleySteps = { 4, 12, 20, 28, 36, 44, 52, 60, 68, 76, 84, 92, 100, 108, 116, 124, 132, 140, 148 };
    private readonly record struct Input(bool Morph = false, bool Attack = false, bool Jump = false, bool Shoot = false, bool Forward = false);
    private static readonly Action<KeyboardState, Keys, bool> SetKey = typeof(KeyboardState)
        .GetMethod("SetKeyState", Hidden | BindingFlags.Public)!.CreateDelegate<Action<KeyboardState, Keys, bool>>();
    private static void Check(bool condition, string label, bool verbose = true)
    {
        if (!condition) throw new InvalidOperationException(label);
        _checks++; if (verbose) Console.WriteLine("SCOUT DRONE SCENE PASS " + label);
    }
    private static void Near(float actual, float expected, float tolerance, string label)
        => Check(float.IsFinite(actual) && MathF.Abs(actual - expected) <= tolerance, label + $" (actual={actual}, expected={expected})");

    private sealed class Fixture : IDisposable
    {
        internal Scene Scene { get; }
        internal KeyboardState Keyboard { get; }
        internal PlayerEntity Player => Scene.Players.Main;
        internal PlayerEntity? Target { get; }
        internal Fixture(bool drone, string room, bool target = false)
        {
            Rng.SetRng1(8128); Rng.SetRng2(0x8128a5a5);
            Keyboard = SyntheticInput.CreateKeyboard(); Scene? candidate = null;
            try
            {
                candidate = new Scene(new Vector2i(256, 192), Keyboard, SyntheticInput.CreateMouse(), _ => { }, () => { }, lifetimeOwned: value => candidate = value);
                Scene = candidate;
                var plan = new LaunchPlan { Kind = LaunchKind.Offline, Hunter = Hunter.Weavel, RoomKey = room,
                    Mode = GameMode.Battle, Bots = 0, IsPlaytest = true,
                    OfflineCharacter = drone ? OfflineCharacterMode.TrooperScoutDronePractice : OfflineCharacterMode.Native,
                    MatchRules = new MatchDefinition { RoomKey = room, Mode = GameMode.Battle, TimeLimitSeconds = 0, PointGoal = 10000 } };
                plan.MatchRules.ApplyInitialConfiguration(Scene.GameState);
                Scene.AddPlayer(Hunter.Weavel);
                Check(OfflineCharacterGameplay.TryBind(Scene, plan, out string reason), "actual offline plan admission/bind " + reason);
                Check(Player.ModCharacterGameplay == (drone ? CharacterGameplayRules.TrooperDroneV1 : CharacterGameplayRules.Native)
                    && Scene.PrivateCharacterGameplayUnsupported == drone, "actual plan captures finite rule and private replay exclusion");
                // Fixture-only positive firing target, added after the admitted
                // zero-bot plan binds. This does not prove two-seat product admission.
                if (target) { Scene.AddPlayer(Hunter.Samus); Target = Scene.Players.Items[1]; Target.IsBot = false; }
                Scene.AddRoom(room, GameMode.Battle, playerCount: target ? 2 : 1);
                Scene.OnLoad();
                var inputMode = typeof(Scene).GetField("_inputMode", Hidden)!;
                inputMode.SetValue(Scene, Enum.Parse(inputMode.FieldType, "All"));
                Scene.GameState.MatchTime = -1; Scene.GameState.PointGoal = 10000;
                foreach (var actor in Scene.Players.Items)
                {
                    foreach (var control in actor.Controls.All) { control.Type = ButtonType.Key; control.Key = Keys.Unknown; }
                    actor.IgnoreItemPickups = true;
                }
                Player.Controls.MoveUp.Key = Keys.W; Player.Controls.RollUp.Key = Keys.W; Player.Controls.Jump.Key = Keys.Space;
                Player.Controls.Morph.Key = Keys.B; Player.Controls.AltAttack.Key = Keys.F; Player.Controls.Shoot.Key = Keys.Enter;
                Player.Controls.MouseAim = false; Player.Controls.KeyboardAim = true;
                SetKey(Keyboard, Keys.Enter, true);
                for (int i = 0; i < 600 && !Player.LoadFlags.TestFlag(LoadFlags.Spawned); i++) Scene.OnSimulationFrame();
                SetKey(Keyboard, Keys.Enter, false);
                Check(Player.LoadFlags.TestFlag(LoadFlags.Spawned), "actual native shoot-to-spawn completed");
                Scene.CameraSequences.Current?.End();
                for (int i = 0; i < 240; i++) Scene.OnSimulationFrame();
                Check(Player.ModIsInPlay && Player.Health > 0 && !Player.IsAltForm && !Player.IsBot && !NetSession.Active,
                    "fully initialized living offline Weavel");
                Check(Player.ScoutDroneEnabled == drone, "explicit gameplay rule alone selects drone mechanics");
            }
            catch { if (candidate != null) Release(candidate); throw; }
        }
        internal void Step(Input input = default, bool requireAlive = true)
        {
            SetKey(Keyboard, Keys.B, input.Morph); SetKey(Keyboard, Keys.F, input.Attack);
            SetKey(Keyboard, Keys.Space, input.Jump); SetKey(Keyboard, Keys.Enter, input.Shoot); SetKey(Keyboard, Keys.W, input.Forward);
            Scene.OnSimulationFrame();
            Check(!NetSession.Active && float.IsFinite(Player.Position.Y) && float.IsFinite(Player.Speed.Y), "finite offline actual simulation tick", verbose: false);
            if (requireAlive) Check(Player.ModIsInPlay && Player.Health > 0, "tick retains living native player owner", verbose: false);
        }
        internal void Split()
        {
            Check(!Player.IsAltForm, "split starts in genuine biped form");
            Step(new Input(Morph: true));
            Check(Player.Controls.Morph.IsPressed, "actual Morph input edge observed");
            for (int i = 0; i < 120 && (!Player.IsAltForm || Player.IsMorphing); i++) Step();
            Check(Player.IsAltForm && !Player.IsMorphing && Player.Flags2.TestFlag(PlayerFlags2.Halfturret), "native split/form owner reaches mobile half");
            Check(ReferenceEquals(Player.DroneBody, Player.Halfturret), "drone body is exact native form-owned Halfturret");
            Check(SceneContains(Scene, Player.Halfturret), "native planted body belongs to actual Scene owner list by reference identity");
            Step(); // release Morph even when the prototype transition is immediate
        }
        internal void Merge()
        {
            Check(Player.IsAltForm, "merge starts in genuine split form");
            Step(new Input(Morph: true));
            for (int i = 0; i < 120 && (Player.IsAltForm || Player.IsUnmorphing); i++) Step();
            Check(!Player.IsAltForm && !Player.IsMorphing && !Player.IsUnmorphing, "actual Morph completes native merge");
            Step();
        }
        public void Dispose() => Release(Scene);
    }
    private static void Release(Scene scene)
    {
        var commit = PostMatchCommitContext.Capture(scene.GameState, 0, SaveWhen.Never, 0);
        try { scene.DoCleanup(); }
        finally
        {
            try { scene.UnloadGl(); }
            finally { commit.Commit(); commit.Release(); ForegroundSceneBinding.TryRelease(commit); }
        }
    }

    internal static int Run(string assets, string? outputPath, string room = "MP3 PROVING GROUND")
    {
        try
        {
            _checks = 0;
            Check(!NetSession.Active && !DemoPlayback.IsActive, "fresh offline non-replay process");
            Paths.SetPath(Ver.AMHE1, Path.GetFullPath(assets)); Paths.MphKey = Ver.AMHE1;
            Check(ServerSim.Available(out string reason), "owned AMHE1 filesystem available " + reason);
            Headless.Enter();
            object bodyFirstRetirement = RetiringBodyCannotEmitBeforePlayer(room);
            object health = HealthOwnership(room);
            object hover = Hover(room);
            object burst = FullBurst(room);
            object inputOrder = InputOrder(room);
            object release = ReleaseDuringUnfold(room);
            object cancellation = Cancellation(room);
            object body = PlantedBodyControl(room);
            object pools = OrdinaryPoolIndependence(room);
            object capacity = DirectHookCapacityAndRetirement(room);
            object fences = RuntimeFences(room);
            object native = NativeWeavel(room);
            if (outputPath != null)
            {
                string path = Path.GetFullPath(outputPath); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(new { scope = "owned AMHE1 initialized offline CPU Scene/input/collision; native turret/sphere body and Psycho Bit mobile fallback; synthetic inert target, public Spawn, health-cap setup and native freeze-timer injection explicitly identified",
                    room, checks = _checks, bodyFirstRetirement, health, hover, burst, inputOrder, release, cancellation, body, pools, capacity, fences, native,
                    emittedFlightContract = "merge/freeze stop later emission and preserve existing native flight/impact; death, owner retirement and Scene cleanup retire flights",
                    unqualified = new[] { "physical controls or graphics", "standing-cylinder/headshot donor parity", "ceiling/ledge/jump-pad traversal", "laser", "network/replay/career", "imported Trooper presentation or combat balance" } }, new JsonSerializerOptions { WriteIndented = true }));
            }
            Console.WriteLine($"SCOUT DRONE SCENE PASS {_checks} assertions; scope is owned offline CPU mechanics with explicit native fallbacks.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("SCOUT DRONE SCENE FAIL " + ex); return 1; }
    }

    private static object HealthOwnership(string room)
    {
        using var f = new Fixture(true, room); var p = f.Player;
        Check(p.HealthMax >= 79, "native metadata admits explicit odd79 health fixture");
        p.Health = 79; // explicit synthetic odd-health starting allocation within native cap
        f.Split();
        Check(p.Health == 40 && p.DroneBody.Health == 39, "actual native odd-health split gives mobile40/body39 from79");
        Check(p.Health + p.DroneBody.Health == 79, "actual split conserves total health");
        p.TakeDamage(12, DamageFlags.Halfturret | DamageFlags.IgnoreInvuln | DamageFlags.NoDmgInvuln | DamageFlags.NoSfx, null, null);
        Check(p.Health == 34 && p.DroneBody.Health == 33, "actual native Halfturret damage owner pays12 across both halves");
        f.Merge();
        Check(p.Health == 67, "actual native merge returns surviving34+33 health");
        f.Split();
        // Synthetic remaining-health setup isolates the native GainHealth cap.
        p.Health = p.HealthMax - 1; p.DroneBody.Health = 9;
        f.Merge(); Check(p.Health == p.HealthMax, "native merge caps synthetic surviving combined health at actual HealthMax");
        Check(p.ScoutDroneEnabled && p.ModCharacterGameplay == CharacterGameplayRules.TrooperDroneV1,
            "native split/merge never changes captured character rule");
        return new { initial = 79, mobile = 40, planted = 39, nativeBodyDamage = 12, merged = 67, maximum = p.HealthMax };
    }

    private static void TraceHover(Fixture f, string stage, int neutralSteps)
    {
        // Bounded read-only setup diagnostics retained from attempt02.
        // Records actual control-driven placement; never injects gameplay state.
        var p = f.Player; var volume = p.Volume;
        static float[] Components(Vector3 value) => new[] { value.X, value.Y, value.Z };
        Console.WriteLine("SCOUT DRONE HOVER TRACE " + JsonSerializer.Serialize(new
        {
            stage, neutralSteps, frame = f.Scene.FrameCount,
            position = Components(p.Position), speed = Components(p.Speed),
            flags1 = (uint)p.Flags1, flags1Names = p.Flags1.ToString(),
            flags2 = (uint)p.Flags2, flags2Names = p.Flags2.ToString(),
            p.DroneFloorGap, p.DroneCap, bodyPosition = Components(p.DroneBody.Position), bodyHealth = p.DroneBody.Health,
            room = f.Scene.Room?.Meta.Name, volumeType = volume.Type.ToString(),
            collisionSpherePosition = Components(volume.SpherePosition), collisionSphereRadius = volume.SphereRadius
        }));
    }

    private static object Hover(string room)
    {
        using var f = new Fixture(true, room); var p = f.Player; f.Split();
        float BodyClearance() => new Vector2(p.Position.X - p.DroneBody.Position.X, p.Position.Z - p.DroneBody.Position.Z).Length;
        bool OnRoomFloor() => p.Flags1.TestFlag(PlayerFlags1.Standing) && p.DroneFloorGap >= 0 && p.DroneFloorGap < .06f;
        int walkSteps = 0, neutralSteps = 0;
        TraceHover(f, "after-split", 0);
        // Native split leaves its body directly beneath the mobile sphere.
        // Real Forward control must leave that footprint before floor settling;
        // no synthetic teleport, native Spawn or flag/speed write is used here.
        for (; walkSteps < 180 && BodyClearance() <= 2; walkSteps++)
        {
            f.Step(new Input(Forward: true));
            Check(p.Controls.RollUp.IsDown, "actual native Forward control walks split mobile sphere clear of body", verbose: false);
            if ((walkSteps + 1) % 30 == 0) TraceHover(f, "walk-off-body", walkSteps + 1);
        }
        float bodyClearance = BodyClearance();
        TraceHover(f, "after-walk-off-body", walkSteps);
        Check(bodyClearance > 2, "bounded actual Forward input clears planted body footprint by more than2 units");
        for (; neutralSteps < 180 && !OnRoomFloor(); neutralSteps++)
        {
            f.Step();
            if ((neutralSteps + 1) % 30 == 0) TraceHover(f, "neutral-after-walk-off", neutralSteps + 1);
        }
        TraceHover(f, "before-standing-assertion", neutralSteps);
        Check(p.Flags1.TestFlag(PlayerFlags1.Standing) && p.DroneFloorGap >= 0 && p.DroneFloorGap < .06f,
            "hover fixture begins Standing on actual room floor with known gap below.06 after real walk-off-body input");
        float startY = p.Position.Y, maxGap = 0, minimumPlateau = float.MaxValue, maximumPlateau = float.MinValue;
        bool rose = false; int floorSamples = 0;
        for (int i = 0; i < 150; i++)
        {
            f.Step(new Input(Jump: true));
            Check(p.Controls.Jump.IsDown, "real native Jump hold observed", verbose: false);
            rose |= p.Speed.Y > .02f && p.Position.Y > startY + .2f;
            if (p.DroneFloorGap >= 0)
            {
                floorSamples++; maxGap = MathF.Max(maxGap, p.DroneFloorGap);
                Check(p.DroneFloorGap <= p.DroneCap + .06f, "actual floor-probed hover stays within cap plus one-step tolerance", verbose: false);
            }
            if (i >= 130) { minimumPlateau = MathF.Min(minimumPlateau, p.Position.Y); maximumPlateau = MathF.Max(maximumPlateau, p.Position.Y); }
        }
        Check(rose && floorSamples >= 100, "real Jump hold rises using actual floor collision probe");
        Near(p.DroneCap, Fixed.ToFloat(p.Values.MaxPickupHeight) - Fixed.ToFloat(p.Values.MinPickupHeight), .00001f, "cap belongs to native biped height metadata");
        Check(maxGap >= p.DroneCap - .1f && maximumPlateau - minimumPlateau < .06f, "held Jump reaches stable native-height cap");
        float releaseY = p.Position.Y; bool sank = false;
        for (int i = 0; i < 20; i++) { f.Step(); sank |= p.Speed.Y <= -.02f; }
        Check(sank && p.Position.Y < releaseY - .1f, "real Jump release produces downward sink");
        for (int i = 0; i < 180 && !p.Flags1.TestFlag(PlayerFlags1.Standing); i++) f.Step();
        Check(p.Flags1.TestFlag(PlayerFlags1.Standing), "released drone returns to actual room floor");
        return new { setup = "native split initially supported by planted body; bounded real Forward walk clears footprint before room-floor settle",
            walkSteps, bodyClearance, neutralSettleSteps = neutralSteps, rising = rose, floorSamples, maxGap, cap = p.DroneCap,
            plateauDrift = maximumPlateau - minimumPlateau, releasedSink = sank };
    }

    private static void StartBurst(Fixture f)
    {
        var p = f.Player; int bursts = p.DroneBursts;
        f.Step(new Input(Attack: true));
        Check(p.Controls.AltAttack.IsDown && p.Controls.AltAttack.IsPressed, "actual native AltAttack press observed");
        Check(p.DroneFireState == 1 && p.DroneFireFrame == 0 && p.DroneBursts == bursts + 1,
            "real press enters unfold at frame0 exactly once");
    }
    private static void Unfold(Fixture f)
    {
        var p = f.Player; int shots = p.DroneShots;
        for (int step = 1; step <= 32; step++)
        {
            f.Step(new Input(Attack: true));
            Check(p.DroneShots == shots, "32-step unfold emits no shots", verbose: false);
            Check(p.DroneFireState == (step < 32 ? 1 : 2) && p.DroneFireFrame == (step < 32 ? step : 0),
                "unfold boundary follows independent literal32-step oracle", verbose: false);
        }
        Check(p.DroneFireState == 2 && p.DroneFireFrame == 0, "unfold completes after32 subsequent real input steps");
    }
    private static object FullBurst(string room)
    {
        using var f = new Fixture(true, room); var p = f.Player; f.Split();
        var normalPool = p.EquipInfo.Beams;
        Check(normalPool.Length == 16 && p.DroneBeams.Count == 40, "native16 and independent drone40 projectile allocations");
        Check(p.DroneBeams.All(drone => !normalPool.Any(normal => ReferenceEquals(drone, normal))), "actual native and drone pool objects are disjoint");
        int shots = p.DroneShots, damage = p.DroneDamageTotal, left = p.DroneShotsBySide[0], right = p.DroneShotsBySide[1];
        StartBurst(f); Unfold(f);
        var observedVolleys = new List<int>();
        for (int step = 1; step <= 150; step++)
        {
            int before = p.DroneShots; f.Step(new Input(Attack: true));
            bool volley = Array.IndexOf(VolleySteps, step) >= 0;
            Check(p.DroneShots - before == (volley ? 2 : 0), "actual twin volley cadence agrees with literal fire steps4..148", verbose: false);
            if (p.DroneShots != before) observedVolleys.Add(step);
            Check(p.DroneFireState == (step < 150 ? 2 : 3) && p.DroneFireFrame == (step < 150 ? step : 0),
                "firing boundary follows independent literal150-step oracle", verbose: false);
            Check(ReferenceEquals(p.EquipInfo.Beams, normalPool), "burst never replaces native projectile pool owner", verbose: false);
        }
        Check(observedVolleys.SequenceEqual(VolleySteps) && p.DroneShots - shots == 38, "full150-step burst produces19 twin volleys/38 actual native spawned bolts");
        Check(p.DroneDamageTotal - damage == 94 && p.DroneShotsBySide[0] - left == 19 && p.DroneShotsBySide[1] - right == 19,
            "full ordinary burst has independent literal94 nominal damage and19 shots per side");
        for (int step = 1; step <= 24; step++)
        {
            f.Step(new Input(Attack: true));
            Check(p.DroneShots - shots == 38, "24-step fold emits no shots", verbose: false);
            Check(p.DroneFireState == (step < 24 ? 3 : 0) && p.DroneFireFrame == (step < 24 ? step : 0),
                "fold boundary follows literal24-step oracle", verbose: false);
        }
        Check(p.DroneCooldown == 120, "cooldown begins at120 only after complete fold");
        int bursts = p.DroneBursts;
        for (int step = 1; step < 120; step++)
        {
            f.Step(new Input(Attack: true));
            Check(p.DroneFireState == 0 && p.DroneCooldown == 120 - step && p.DroneBursts == bursts && p.DroneShots - shots == 38,
                "held trigger cannot skip literal120-step cooldown", verbose: false);
        }
        f.Step(new Input(Attack: true));
        Check(p.DroneCooldown == 0 && p.DroneFireState == 1 && p.DroneFireFrame == 0 && p.DroneBursts == bursts + 1,
            "held trigger starts next unfold exactly when full120-step cooldown elapses");
        return new { unfoldSteps = 32, firingSteps = 150, volleySteps = observedVolleys, shots = 38, nominalDamage = 94, foldSteps = 24, cooldownSteps = 120 };
    }

    private static Vector3 ReadCanonicalAim(PlayerEntity player)
    {
        // Read-only observation of the same native gun aim used by ordinary
        // shots. Never inject a direction or call an aim helper from the test.
        var aim = (Vector3)typeof(PlayerEntity).GetField("_gunVec1", Hidden)!.GetValue(player)!;
        Check(float.IsFinite(aim.X) && float.IsFinite(aim.Y) && float.IsFinite(aim.Z) && aim.LengthSquared > .99f,
            "actual native canonical gun aim is finite and nondegenerate", verbose: false);
        return aim.Normalized();
    }
    private static object InputOrder(string room)
    {
        float currentAimChange, gatheredMouseDeltaX;
        using (var f = new Fixture(true, room))
        {
            var p = f.Player; f.Split(); StartBurst(f); Unfold(f);
            for (int step = 1; step <= 3; step++) f.Step(new Input(Attack: true));
            Check(p.DroneFireState == 2 && p.DroneFireFrame == 3 && p.DroneShots == 0,
                "aim-order regression starts immediately before first scheduled fire-step4 volley");
            // Ordinary local keyboard aim buttons do not populate the native
            // button-aim accumulator. Use the actual relative mouse producer,
            // as AndroidInput.MovePointer/MomentumBalanceSceneTests do, without
            // writing canonical aim or gathered player input.
            PointerInput.StylusMode = false; PointerInput.Reset(); PointerDevice.Reset();
            p.Controls.MouseAim = true; p.Controls.KeyboardAim = false;
            Check(!PointerDevice.Active && !PointerInput.StylusMode && InputSettings.MouseSensitivity == 1 && !InputSettings.InvertMouseX,
                "aim-order fixture selects ordinary native relative mouse input");
            var mouse = (MouseState)typeof(Scene).GetField("_mouseState", Hidden)!.GetValue(f.Scene)!;
            var input = (PlayerEntity.PlayerInput)typeof(PlayerEntity).GetProperty("Input", Hidden)!.GetValue(p)!;
            var setPosition = typeof(MouseState).GetProperty("Position", Hidden | BindingFlags.Public)!.SetMethod!
                .CreateDelegate<Action<MouseState, Vector2>>();
            Vector2 pointer = mouse.Position;
            Vector3 previousAim = ReadCanonicalAim(p);
            pointer.X += 16; setPosition(mouse, pointer);
            f.Step(new Input(Attack: true));
            Vector3 currentAim = ReadCanonicalAim(p);
            gatheredMouseDeltaX = input.MouseDeltaX;
            Check(gatheredMouseDeltaX == 16 && input.MouseDeltaY == 0,
                "first scheduled volley step consumes exact actual raw16px native mouse delta");
            currentAimChange = (currentAim - previousAim).Length;
            Check(currentAimChange > .0001f, "real native aim changed during the scheduled volley step");
            var emitted = p.DroneBeams.Where(beam => ReferenceEquals(beam.Owner, p) && beam.Lifespan > 0).ToArray();
            Check(p.DroneShots == 2 && emitted.Length == 2, "first scheduled aim-change step emits exactly two actual native bolts");
            foreach (var beam in emitted)
            {
                Check(!beam.Flags.TestFlag(BeamFlags.Collided) && (beam.Direction - currentAim).Length < .0001f,
                    "newly spawned bolt Direction matches CURRENT canonical native aim on volley step");
                Check((beam.Direction - previousAim).Length > .0001f,
                    "newly spawned bolt cannot use previous simulation step aim");
            }
        }
        using (var f = new Fixture(true, room))
        {
            var p = f.Player; f.Split(); StartBurst(f); Unfold(f);
            for (int step = 1; step <= 3; step++) f.Step(new Input(Attack: true));
            Check(p.DroneFireState == 2 && p.DroneFireFrame == 3 && p.DroneShots == 0,
                "morph-order regression starts immediately before first scheduled fire-step4 volley");
            int bursts = p.DroneBursts;
            f.Step(new Input(Morph: true, Attack: true));
            Check(p.Controls.Morph.IsPressed && p.Controls.AltAttack.IsDown,
                "scheduled firing step observes actual native Morph edge while attack remains held");
            Check(!p.IsAltForm && !p.Flags2.TestFlag(PlayerFlags2.Halfturret),
                "same scheduled firing step enters actual native merge owner");
            Check(p.DroneShots == 0 && p.DroneBursts == bursts && p.DroneBeams.All(beam => beam.Lifespan <= 0),
                "Morph edge wins over scheduled volley without emitting either extra bolt");
            Check(p.DroneFireState == 0 && p.DroneFireFrame == 0 && p.DroneCooldown == 120,
                "scheduled-step merge cancels fire and retains complete120 cooldown on its entry tick");
            f.Step();
            Check(p.DroneCooldown == 119 && p.DroneShots == 0,
                "merge cooldown first decrements on the subsequent native simulation tick");
        }
        return new { actualRelativeMouseOnFireStep = 4, requestedRawMouseDeltaX = 16, gatheredMouseDeltaX, currentAimChange, boltsUseCurrentAim = 2,
            actualMorphEdgeOnFireStep = 4, extraMorphStepBolts = 0, mergeEntryCooldown = 120, nextTickCooldown = 119 };
    }

    private static object ReleaseDuringUnfold(string room)
    {
        using var f = new Fixture(true, room); var p = f.Player; f.Split();
        int shots = p.DroneShots; StartBurst(f);
        for (int step = 1; step <= 32; step++)
        {
            f.Step(new Input(Attack: step <= 5));
            Check(p.DroneShots == shots && p.DroneFireState == (step < 32 ? 1 : 3)
                && p.DroneFireFrame == (step < 32 ? step : 0), "release during unfold finishes32 steps then folds without entering fire", verbose: false);
        }
        for (int i = 0; i < 24; i++) f.Step();
        Check(p.DroneFireState == 0 && p.DroneCooldown == 120 && p.DroneShots == shots, "released unfolding completes fold without manufactured shots");
        return new { releasedAtUnfoldStep = 6, completedUnfoldSteps = 32, foldSteps = 24, newShots = 0 };
    }

    private static object Cancellation(string room)
    {
        var results = new List<object>();
        foreach (string cause in new[] { "freeze", "merge", "death" })
        {
            using var f = new Fixture(true, room); var p = f.Player; f.Split(); StartBurst(f); Unfold(f);
            for (int i = 0; i < 8; i++) f.Step(new Input(Attack: true));
            Check(p.DroneShots == 2, cause + " cancellation arm begins after real first twin volley");
            int shots = p.DroneShots;
            if (cause == "freeze")
            {
                // Explicit synthetic native freeze input, without writing drone fields.
                typeof(PlayerEntity).GetField("_frozenTimer", Hidden)!.SetValue(p, (ushort)90);
                for (int i = 0; i < 8; i++) f.Step(new Input(Attack: true));
                Check(p.DroneFireState != 2 && p.DroneShots == shots, "native freeze promptly stops an active drone firing phase");
            }
            else if (cause == "merge")
            {
                var emitted = p.DroneBeams.Where(beam => ReferenceEquals(beam.Owner, p)).ToArray();
                f.Step(new Input(Morph: true));
                Check(p.DroneFireState == 0 && p.DroneCooldown == 120, "actual merge cancels burst and starts full120 cooldown");
                for (int i = 0; i < 120 && (p.IsAltForm || p.IsUnmorphing); i++) f.Step();
                Check(!p.IsAltForm && !p.IsUnmorphing, "cancelled drone returns through native merge owner");
                for (int i = 0; i < 32; i++)
                {
                    f.Step(new Input(Attack: true));
                    Check(emitted.All(beam => beam.Lifespan <= 0 || ReferenceEquals(beam.Owner, p)),
                        "already-fired bolts keep valid native owner until impact/expiry after merge", verbose: false);
                }
                Check(p.DroneFireState == 0 && p.DroneShots == shots,
                    "native merge cancels later emission while existing bolts retain normal native flight policy");
            }
            else
            {
                p.TakeDamage(9999, DamageFlags.Death | DamageFlags.IgnoreInvuln | DamageFlags.NoDmgInvuln | DamageFlags.NoSfx, null, null);
                for (int i = 0; i < 8; i++) f.Step(new Input(Attack: true), requireAlive: false);
                Check(p.Health == 0 && p.DroneFireState == 0 && p.DroneShots == shots && p.DroneBeams.All(beam => beam.Lifespan <= 0),
                    "actual native death owner cancels drone state and retires its flights");
            }
            results.Add(new { cause, shotsBeforeCancellation = shots, shotsAfterCancellation = p.DroneShots, state = p.DroneFireState });
        }
        return results;
    }

    private static bool BodyFired(Fixture f)
        => f.Player.EquipInfo.Beams.Any(beam => ReferenceEquals(beam.Owner, f.Player.Halfturret));
    private static void PlaceTarget(Fixture f)
    {
        var p = f.Player; var target = f.Target ?? throw new InvalidOperationException("Missing actual native target owner.");
        Vector3 origin = p.Halfturret.Position.AddY(.4f);
        foreach (Vector3 heading in new[] { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitZ, -Vector3.UnitZ })
        {
            Vector3 candidate = p.Halfturret.Position + heading * 5;
            var floor = new CollisionResult();
            if (!CollisionDetection.CheckBetweenPoints(candidate.AddY(5), candidate.AddY(-20), TestFlags.Players, f.Scene, ref floor)
                || floor.Plane.Y < .5f) continue;
            candidate.Y = floor.Position.Y - target.ModBipedBottom + .05f;
            var obstruction = new CollisionResult();
            if (CollisionDetection.CheckBetweenPoints(origin, candidate.AddY(1), TestFlags.Beams, f.Scene, ref obstruction)) continue;
            target.Spawn(candidate, -heading, Vector3.UnitY, p.NodeRef, respawn: false);
            target.IsBot = false; target.Health = target.HealthMax; target.TeamIndex = p.TeamIndex + 1;
            Check(target.ModIsInPlay && target.Health > 0 && (target.Position - origin).Length < 15,
                "explicit fixture-only public Spawn places actual inert opposing native Samus within clear turret range");
            return;
        }
        throw new InvalidOperationException("Actual arena spawn lacks a clear floor/LOS target placement for meaningful body firing control.");
    }
    private static object RetiringBodyCannotEmitBeforePlayer(string room)
    {
        using var f = new Fixture(true, room, target: true); var p = f.Player;
        f.Split(); PlaceTarget(f);
        var body = p.Halfturret; var target = f.Target!;
        // Explicit synthetic target/cooldown fixture, using real initialized
        // native owners and their APIs. No manufactured projectile or drone
        // timer/state is injected. Existing native positive-control coverage
        // separately proves that this actual target placement can be fired at.
        body.ModEnhancedTarget(target);
        CallVolley(p); // actual private native pair to qualify retirement too
        p.TimeSinceShot = 600; // synthetic elapsed native turret firing gate
        Check(ReferenceEquals(body.Target, target) && body.Health > 0 && p.Flags2.TestFlag(PlayerFlags2.Halfturret)
            && SceneContains(f.Scene, body) && target.Health > 0 && target.ModIsInPlay,
            "body-first retirement fixture has actual live planted body and explicit eligible native target");
        Check((int)EntityType.Halfturret < (int)EntityType.Player,
            "native Scene type ordering can process planted body before its player owner");
        var normalPool = p.EquipInfo.Beams;
        var privatePool = p.DroneBeams.ToArray();
        var ordinaryBefore = normalPool.Select(beam => CaptureFlight(f, beam)).ToArray();
        int privateShotsBefore = p.DroneShots;
        Check(normalPool.All(beam => beam.Lifespan <= 0) && privatePool.Count(beam => beam.Lifespan > 0) == 2,
            "retirement control begins with no ordinary flight and one actual private native pair");
        ulong frame = f.Scene.FrameCount;
        // Deliberately do not read ScoutDroneEnabled/ModCharacterGameplay or
        // process the player between conflict injection and native body.Process.
        // The body must be the first observer of this lost gameplay owner.
        f.Scene.GameState.EnhancedHunters = true;
        try
        {
            bool bodyProcessResult = body.Process();
            int ordinaryLiveAfter = normalPool.Count(beam => beam.Lifespan > 0);
            int privateLiveAfter = privatePool.Count(beam => beam.Lifespan > 0);
            int retainedSceneFlights = normalPool.Count(beam => SceneContains(f.Scene, beam))
                + privatePool.Count(beam => SceneContains(f.Scene, beam));
            Console.WriteLine("SCOUT DRONE BODY-FIRST RETIREMENT TRACE " + JsonSerializer.Serialize(new
            {
                scheduling = "synthetic native body.Process before player; explicit eligible target/600-step elapsed cooldown",
                bodyProcessResult, frameBefore = frame, frameAfter = f.Scene.FrameCount,
                ordinaryLiveBefore = 0, ordinaryLiveAfter, privateLiveBefore = 2, privateLiveAfter,
                privateShotsBefore, privateShotsAfter = p.DroneShots, retainedSceneFlights,
                bodyHealth = body.Health, bodySceneMember = SceneContains(f.Scene, body),
                nativeHalfFlag = p.Flags2.TestFlag(PlayerFlags2.Halfturret), timeSinceShotAfter = p.TimeSinceShot,
                resultingRule = p.ModCharacterGameplay.ToString(), f.Scene.PrivateCharacterGameplayUnsupported
            }));
            Check(f.Scene.FrameCount == frame, "body-first retirement arm never processes player or advances simulation frame");
            Check(ordinaryLiveAfter == 0 && p.DroneShots == privateShotsBefore
                && ordinaryBefore.SequenceEqual(normalPool.Select(beam => CaptureFlight(f, beam))),
                "rules-owner retirement observed by body cannot fall through into native/private emission");
            Check(retainedSceneFlights == 0 && privatePool.All(beam => beam.Owner == null && beam.Lifespan <= 0),
                "body-first owner retirement expires/removes exact private pair and leaves no native/private Scene flight");
            Check(body.Health == 0 && !p.Flags2.TestFlag(PlayerFlags2.Halfturret) && !SceneContains(f.Scene, body)
                && !p.ScoutDroneEnabled && p.ModCharacterGameplay == CharacterGameplayRules.Native
                && f.Scene.PrivateCharacterGameplayUnsupported,
                "body-first invalidation performs exact native body/capability retirement and keeps replay exclusion latched");
            return new { scheduling = "synthetic native body.Process before player", explicitTarget = true,
                syntheticNativeCooldownElapsed = 600, ordinaryLiveAfter, privateLiveAfter, retainedSceneFlights,
                privateShotsBefore, privateShotsAfter = p.DroneShots, bodyRetired = true, replayExclusionLatched = true };
        }
        finally { f.Scene.GameState.EnhancedHunters = false; }
    }

    private static object PlantedBodyControl(string room)
    {
        bool nativeFired = false, droneFired = false;
        foreach (bool drone in new[] { false, true })
        {
            using var f = new Fixture(drone, room, target: true); f.Split(); PlaceTarget(f);
            bool fired = false;
            for (int i = 0; i < 240; i++)
            {
                f.Step(); fired |= BodyFired(f);
                if (!drone && fired) break;
            }
            if (drone) droneFired = fired; else nativeFired = fired;
            Check(drone ? !fired : fired, drone ? "drone planted native sphere/model body never auto-fires despite actual eligible LOS target"
                : "positive default native Weavel turret auto-fires at actual eligible LOS target");
        }
        Check(nativeFired && !droneFired, "native positive control makes prototype standing-body no-fire assertion non-vacuous");
        return new { fixtureOnlySecondNativeActor = true, nativeTurretFired = nativeFired, dronePlantedBodyFired = droneFired,
            hitboxScope = "native turret sphere; donor standing cylinder/headshots remain unqualified" };
    }

    private static object OrdinaryPoolIndependence(string room)
    {
        using var f = new Fixture(true, room); var p = f.Player; var normalPool = p.EquipInfo.Beams;
        f.Step(new Input(Shoot: true)); f.Step();
        var live = normalPool.FirstOrDefault(beam => ReferenceEquals(beam.Owner, p) && beam.Lifespan > 0 && !beam.Flags.TestFlag(BeamFlags.Collided));
        Check(live != null, "actual biped Shoot control creates ordinary native live projectile before split");
        f.Split(); StartBurst(f); Unfold(f);
        bool overlap = false;
        for (int i = 0; i < 4; i++)
        {
            bool beforeLive = live!.Lifespan > 0 && !live.Flags.TestFlag(BeamFlags.Collided);
            float beforeAge = live.Age;
            f.Step(new Input(Attack: true));
            Check(ReferenceEquals(p.EquipInfo.Beams, normalPool) && !p.DroneBeams.Any(beam => ReferenceEquals(beam, live)),
                "actual burst allocation leaves ordinary projectile pool and object independent", verbose: false);
            if (p.DroneShots > 0 && beforeLive && live.Lifespan > 0 && !live.Flags.TestFlag(BeamFlags.Collided))
            {
                Check(ReferenceEquals(live.Owner, p) && live.Beam == BeamType.PowerBeam && live.Age >= beforeAge,
                    "live ordinary native shot retains identity/age across actual drone volley allocation");
                overlap = true;
            }
        }
        Check(p.DroneShots == 2, "pool control observes actual first drone twin volley");
        if (!overlap) Console.WriteLine("SCOUT DRONE SCENE UNQUALIFIED live ordinary-flight overlap: real room collision expired the control shot before first volley; pool identities remain verified.");
        return new { ordinaryPoolSize = normalPool.Length, dronePoolSize = p.DroneBeams.Count, actualNativeShot = true, liveOrdinaryFlightOverlapQualified = overlap };
    }

    private static bool SceneContains(Scene scene, EntityBase entity)
    {
        // Native transient projectiles use Id=-1 and intentionally have no
        // entry in the scene's authored-ID lookup. Membership is reference
        // identity in the actual simulation owner list, for bodies and flights.
        foreach (EntityBase member in scene.Entities) if (ReferenceEquals(member, entity)) return true;
        return false;
    }
    private readonly record struct Flight(BeamProjectileEntity Beam, EntityBase? Owner, float Lifespan, float Age,
        Vector3 Position, Vector3 Velocity, Vector3 BackPosition, Vector3 SpawnPosition, bool SceneMember);
    private static Flight CaptureFlight(Fixture f, BeamProjectileEntity beam)
        => new(beam, beam.Owner, beam.Lifespan, beam.Age, beam.Position, beam.Velocity, beam.BackPosition, beam.SpawnPosition,
            SceneContains(f.Scene, beam));
    private static void CallVolley(PlayerEntity p) => typeof(PlayerEntity).GetMethod("ScoutDroneVolley", Hidden)!.Invoke(p, null);
    private static void ReleaseFlight(Fixture f, BeamProjectileEntity beam)
    {
        beam.Destroy();
        if (SceneContains(f.Scene, beam)) f.Scene.RemoveEntity(beam);
    }
    private static object DirectHookCapacityAndRetirement(string room)
    {
        using var f = new Fixture(true, room); var p = f.Player;
        // A real biped input shot supplies an ordinary-pool control. Its flight
        // is never prolonged, repositioned or otherwise manufactured.
        f.Step(new Input(Shoot: true)); f.Step(); f.Split();
        var normalPool = p.EquipInfo.Beams;
        var ordinaryBefore = normalPool.Select(beam => CaptureFlight(f, beam)).ToArray();
        var pool = p.DroneBeams.ToArray();
        Check(pool.Length == 40 && pool.All(beam => beam.Lifespan <= 0), "direct-hook capacity arm begins with40 genuinely unused private native slots");
        ulong frame = f.Scene.FrameCount; int shots = p.DroneShots;
        for (int pair = 0; pair < 20; pair++) CallVolley(p);
        Check(f.Scene.FrameCount == frame && p.DroneShots - shots == 40
            && pool.All(beam => ReferenceEquals(beam.Owner, p) && beam.Lifespan > 0 && !beam.Flags.TestFlag(BeamFlags.Collided)),
            "synthetic direct production-hook scheduling fills40 actual native flights without advancing simulation");
        Check(pool.All(beam => SceneContains(f.Scene, beam)), "all40 actual native flights are members of initialized Scene by reference identity");
        var full = pool.Select(beam => CaptureFlight(f, beam)).ToArray();
        CallVolley(p);
        Check(p.DroneShots - shots == 40 && full.SequenceEqual(pool.Select(beam => CaptureFlight(f, beam))),
            "21st direct volley cannot evict or partially spawn when actual pool is full");
        ReleaseFlight(f, pool[0]);
        var oneFree = pool.Select(beam => CaptureFlight(f, beam)).ToArray();
        CallVolley(p);
        Check(p.DroneShots - shots == 40 && oneFree.SequenceEqual(pool.Select(beam => CaptureFlight(f, beam))),
            "one genuinely destroyed/removed slot cannot admit partial pair or evict39 actual flights");
        ReleaseFlight(f, pool[1]);
        var survivors = pool.Skip(2).Select(beam => CaptureFlight(f, beam)).ToArray();
        CallVolley(p);
        Check(p.DroneShots - shots == 42 && pool.All(beam => ReferenceEquals(beam.Owner, p) && beam.Lifespan > 0)
            && survivors.SequenceEqual(pool.Skip(2).Select(beam => CaptureFlight(f, beam))),
            "two genuinely released slots admit one twin pair without changing38 surviving flights");
        Check(f.Scene.FrameCount == frame && ordinaryBefore.SequenceEqual(normalPool.Select(beam => CaptureFlight(f, beam))),
            "synthetic capacity exercise never touches ordinary native flight state or advances gameplay time");
        p.ModRetireCharacterGameplay();
        Check(!p.ScoutDroneEnabled && p.ModCharacterGameplay == CharacterGameplayRules.Native && f.Scene.PrivateCharacterGameplayUnsupported,
            "actual owner retirement clears finite drone capability while keeping replay exclusion latched");
        Check(pool.All(beam => beam.Owner == null && beam.Lifespan <= 0
            && !SceneContains(f.Scene, beam)),
            "actual owner retirement leaves every captured private flight ownerless/expired/unregistered");
        Check(ReferenceEquals(p.EquipInfo.Beams, normalPool) && ordinaryBefore.SequenceEqual(normalPool.Select(beam => CaptureFlight(f, beam))),
            "actual drone owner retirement preserves entire ordinary pool and existing native flight state");
        return new { scheduling = "synthetic direct production-hook calls; no simulation advancement", actualCapacity = 40,
            fullPoolRefused = true, oneFreeRefused = true, twoFreeAccepted = 2, ordinaryPoolUnchanged = true,
            privateFlightsRetired = true, replayExclusionLatched = true };
    }

    private static object RuntimeFences(string room)
    {
        var results = new List<string>();
        foreach (string fence in new[] { "balanced", "enhanced", "momentum", "quake3", "side-scene" })
        {
            using var f = new Fixture(true, room); var p = f.Player; f.Split(); CallVolley(p);
            var flights = p.DroneBeams.Where(beam => beam.Lifespan > 0).ToArray();
            Check(flights.Length == 2, fence + " gate control starts with real private native flights");
            switch (fence)
            {
                case "balanced": f.Scene.GameState.BalancedMode = true; break;
                case "enhanced": f.Scene.GameState.EnhancedHunters = true; break;
                case "momentum": f.Scene.GameState.MovementModel = MovementModel.Momentum; break;
                case "quake3": f.Scene.GameState.MovementModel = MovementModel.Quake3; break;
                case "side-scene": f.Scene.SideScene = true; break;
            }
            Check(!p.ScoutDroneEnabled && p.ModCharacterGameplay == CharacterGameplayRules.Native
                && flights.All(beam => beam.Owner == null && beam.Lifespan <= 0),
                fence + " actual runtime owner fence retires drone capability and actual flights");
            f.Scene.GameState.BalancedMode = false; f.Scene.GameState.EnhancedHunters = false;
            f.Scene.GameState.MovementModel = MovementModel.Native; f.Scene.SideScene = false;
            int shots = p.DroneShots; CallVolley(p);
            Check(!p.ScoutDroneEnabled && p.DroneShots == shots && f.Scene.PrivateCharacterGameplayUnsupported
                && !p.ModTryBindCharacterGameplay(CharacterGameplayRules.TrooperDroneV1),
                fence + " restored settings cannot revive/rebind retired gameplay capability");
            results.Add(fence);
        }
        return results;
    }

    private static object NativeWeavel(string room)
    {
        using var f = new Fixture(false, room); var p = f.Player; int health = p.Health; f.Split();
        Check(p.Health + p.Halfturret.Health == health && !p.ScoutDroneEnabled && p.ModCharacterGameplay == CharacterGameplayRules.Native,
            "default Native Weavel keeps existing split health/rule owner");
        f.Step(new Input(Attack: true));
        Check(p.Controls.AltAttack.IsPressed && p.Flags2.TestFlag(PlayerFlags2.AltAttack), "default Native Weavel still enters native alt lunge on actual press");
        for (int i = 0; i < 60; i++) f.Step();
        Check(p.DroneShots == 0 && p.DroneBursts == 0 && p.DroneFireState == 0, "native lunge never acquires drone burst mechanics");
        f.Merge();
        Check(!p.ScoutDroneEnabled && !f.Scene.PrivateCharacterGameplayUnsupported, "native split/lunge/merge never sets private drone replay exclusion");
        return new { ordinarySplit = true, nativeAltAttack = true, droneShots = 0, droneBursts = 0, ordinaryMerge = true };
    }
}
