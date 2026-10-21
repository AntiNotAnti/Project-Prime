using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using MphRead;
using MphRead.Entities;
using MphRead.Droid;
using MphRead.Formats;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.Movement;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.NetTest;

// A real UDP client in its own process. DedicatedServer and the other client are externally owned.
internal static class MomentumOnlineSceneTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int _checks;
    private static readonly FieldInfo WalkPresentation = typeof(PlayerEntity).GetField("_modCrouchWalk", Hidden)!;
    private static readonly FieldInfo SlidePresentation = typeof(PlayerEntity).GetField("_modSlidePresentation", Hidden)!;
    private static void ObserveLowTravel(PlayerEntity actor, ref int walking, ref int sliding)
    {
        if (!actor.ModCrouched || actor.Health == 0 || actor.IsAltForm || actor.IsMorphing || actor.IsUnmorphing
            || !actor.LoadFlags.TestFlag(LoadFlags.Spawned) || actor.Speed.Xz.Length <= .00001f) return;
        var walk = ((TraversalWalkPresentation)WalkPresentation.GetValue(actor)!).Sample(1);
        float slide = ((TraversalSlidePresentation)SlidePresentation.GetValue(actor)!).Sample(1);
        Require(float.IsFinite(walk.Amount) && walk.Amount is >= 0 and <= 1
            && float.IsFinite(slide) && slide is >= 0 and <= 1,
            "actual moving low actor has bounded completed cosmetic samples");
        if (walk.Amount > 0) walking++;
        if (slide > 0) sliding++;
    }
    private static void Require(bool condition, string message)
    { _checks++; if (!condition) throw new InvalidOperationException(message); }
    private static float ExpectedTraversalRampLift(Scene scene, PlayerEntity player)
    {
        if (!player.ModMomentumEligible || !player.Flags1.TestFlag(PlayerFlags1.Standing) || scene.FrameCount == 0) return 0;
        ulong supportFrame = (ulong)typeof(PlayerEntity).GetField("_modRampSupportFrame", Hidden)!.GetValue(player)!;
        if (supportFrame != scene.FrameCount - 1) return 0;
        Vector3 support = (Vector3)typeof(PlayerEntity).GetField("_modRampSupport", Hidden)!.GetValue(player)!;
        return TraversalMovementProfileV1.RampLift(player.Speed, support);
    }
    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    internal static int Run(int port, MovementModel expected, string outputPath, int seconds = 12, string? replayPath = null, bool touchModel = false, MomentumOnlineAcceptanceOptions? acceptanceOptions = null)
    {
        if (acceptanceOptions?.Spectator == true)
            return MomentumOnlineAcceptance.RunSpectator(port, expected, outputPath, seconds, acceptanceOptions);
        if (acceptanceOptions?.MinimumTransitions > 0)
            return MomentumOnlineAcceptance.RunTransitions(port, expected, outputPath, seconds, acceptanceOptions);
        using var acceptance = acceptanceOptions == null ? null : new MomentumOnlineAcceptance(acceptanceOptions);
        Scene? scene = null;
        var clock = Stopwatch.StartNew();
        string path = Path.GetFullPath(outputPath);
        int previousRtt = NetLag.RoundTripMs, previousJitter = NetLag.JitterMs;
        var touch = touchModel ? new TouchControls() : null;
        Action<ReplayTimelineRecord>? stanceObserver = null;
        touch?.Layout(1000, 600, 1);
        int touchSamples = 0, touchReleases = 0, touchDirectionalSamples = 0, touchDiagonalSamples = 0;
        bool touchContact = false;
        try
        {
            _checks = 0;
            Require(seconds >= 6 && seconds <= 120 && port is > 0 and <= 65535, "bounded worker duration and UDP port");
            Require(!NetSession.Active && !DemoPlayback.IsActive, "worker starts without a session");
            Require(ServerSim.Available(out string reason), reason); Headless.Enter();
            if (touchModel) Require(NetLag.Configure("200:20"), "actual bounded latency queues configured before Connect");
            string name = "MomentumCPU-" + Environment.ProcessId;
            Require(NetLaunch.Connect("127.0.0.1", port, name, Hunter.Samus, timeoutMs: 15000, color: 0),
                "actual UDP Connect: " + NetLaunch.LastJoinError);
            Require(NetSession.IsClient && !NetSession.IsAuthority, "real UDP client role, no authority impersonation");
            acceptance?.PrepareLobby();
            var wait = Stopwatch.StartNew();
            while (!NetSession.ShouldLoadMatch && wait.Elapsed.TotalSeconds < 40)
            {
                NetSession.Pump(); CheckConnection(); Thread.Sleep(5);
            }
            Require(NetSession.ShouldLoadMatch && NetSession.ServerSession.HasValue && NetSession.ServerMatch.HasValue, "server publishes actual match and frozen session");
            MatchDefinition definition = NetSession.ActiveMatchDefinition!.Value;
            Require(definition.MovementModel == expected, "server frozen match selected expected movement model");
            ushort matchId = NetSession.CurrentMatchId; ulong epoch = NetSession.AuthorityEpoch;
            int localSlot = NetSession.LocalSlot;
            Require(localSlot >= 0 && epoch != 0 && matchId != 0, "actual welcome identifies local incarnation and match");
            NetLaunch.DisableCheatsForMatch();
            var keyboard = SyntheticInput.CreateKeyboard();
            scene = new Scene(new Vector2i(256, 192), keyboard, SyntheticInput.CreateMouse(), _ => { }, () => { }, lifetimeOwned: value => scene = value);
            NetLaunch.BuildPlayers(scene, NetSession.LocalHunter, NetSession.LocalColor, teams: GameState.IsTeamMode(definition.Mode));
            scene.AddRoom(definition.RoomKey, definition.Mode, playerCount: NetLaunch.RoomPlayerCount);
            // The frozen server definition owns this configuration, including after
            // retail room setup installs its own defaults.
            definition.ApplyInitialConfiguration(scene.GameState);
            typeof(Scene).GetField("_developerMomentumDiagnostics", Hidden)!.SetValue(scene, true);
            scene.OnLoad();
            var inputMode = typeof(Scene).GetField("_inputMode", Hidden)!;
            inputMode.SetValue(scene, Enum.Parse(inputMode.FieldType, "All"));
            var player = scene.Players.Main;
            Require(player.SlotIndex == localSlot && !player.IsBot && !NetHooks.IsPuppet(player), "native BuildPlayers installed actual local owner");
            foreach (var control in player.Controls.All) { control.Type = ButtonType.Key; control.Key = Keys.Unknown; }
            player.Controls.MoveUp.Key = Keys.W; player.Controls.MoveDown.Key = Keys.S;
            player.Controls.MoveRight.Key = Keys.D; player.Controls.Jump.Key = Keys.Space; player.Controls.Shoot.Key = Keys.Enter;
            player.Controls.Morph.Key = Keys.Tab;
            if (expected is MovementModel.Traversal or MovementModel.Parkour) player.Controls.Crouch.Key = Keys.LeftControl;
            player.Controls.MouseAim = false; player.Controls.KeyboardAim = true;
            var set = typeof(KeyboardState).GetMethod("SetKeyState", Hidden | BindingFlags.Public)!.CreateDelegate<Action<KeyboardState, Keys, bool>>();
            NetSession.MarkMatchLoaded();
            wait.Restart(); double next = clock.Elapsed.TotalSeconds;
            while ((!NetSession.WorldIsReady || NetSession.FreezeGameplay || player.Health == 0 || !player.ModIsInPlay) && wait.Elapsed.TotalSeconds < 40)
            {
                WaitUntil(clock, ref next);
                scene.OnSimulationFrame(); CheckConnection();
            }
            Require(NetSession.WorldIsReady && NetRoomChange.GameplayReady && !NetSession.FreezeGameplay && player.Health > 0 && player.ModIsInPlay,
                "actual UDP bootstrap and load barrier release to a living owner");
            scene.CameraSequences.Current?.End();
            Require(scene.GameState.MovementModel == expected && NetSession.ActiveMatchDefinition?.MovementModel == expected, "actual world and frozen server rule agree");
            Require(!(bool)typeof(Scene).GetField("_developerMomentumRequested", Hidden)!.GetValue(scene)!, "online adapter does not install private developer opt-in");
            if (MovementModelRules.UsesMomentumSolver(expected)) Require(scene.MomentumMovementActive && scene.MomentumOwnsPlayer(player), "authorized online rule owns real local player");
            if (replayPath != null) Require(DemoRecorder.Start(), "real accepted-fact recording starts after world readiness");

            var remoteSlots = new HashSet<int>(); var previousRemote = new Dictionary<int, Vector3>();
            var samples = new List<object>(); int remoteMoving = 0, inputTicks = 0, impulses = 0, directSamples = 0, airborneTicks = 0, landings = 0;
            int altTicks = 0, airborneMorphTicks = 0, bipedReturnTicks = 0, padCoincidentJumpInputs = 0;
            int ownerCrouchHeldTicks = 0, ownerCrouchedTicks = 0, ownerStandingTicks = 0, ownerStanceChanges = 0;
            int ownerVisualWalkTicks = 0, ownerVisualSlideTicks = 0, ownerPhysicalSlideTicks = 0;
            int remoteVisualWalkTicks = 0, remoteVisualSlideTicks = 0;
            int ownerTuckedTicks = 0, ownerTuckedIntentPackets = 0, ownerCanonicalTuckedSnapshots = 0;
            int remoteCanonicalTuckedSnapshots = 0, remoteTuckedPoseChecks = 0;
            bool traversalLowTravelScript = expected == MovementModel.Traversal && touch == null;
            int ownerHeldIntentPackets = 0, ownerCrouchedIntentPackets = 0, remoteHeldIntentPackets = 0;
            int ownerCanonicalCrouchedSnapshots = 0, ownerCanonicalStandingSnapshots = 0;
            int remoteCanonicalCrouchedSnapshots = 0, remoteCanonicalStandingSnapshots = 0, remoteCrouchedPoseChecks = 0;
            bool previousOwnerCrouched = player.ModCrouched;
            if (expected is MovementModel.Traversal or MovementModel.Parkour)
            {
                stanceObserver = record =>
                {
                    var payload = record.Payload;
                    if (record.Kind == ReplayFactKind.Intent && payload.Length >= 2 + IntentPacket.Size
                        && payload[0] == (byte)PacketType.SlotIntent)
                    {
                        var accepted = IntentPacket.Read(payload[2..]);
                        if (payload[1] == localSlot)
                        {
                            if ((accepted.Buttons & IntentButtons.Crouch) != 0) ownerHeldIntentPackets++;
                            if ((accepted.Buttons & IntentButtons.CrouchedState) != 0) ownerCrouchedIntentPackets++;
                            if ((accepted.Buttons & IntentButtons.TuckedState) != 0) ownerTuckedIntentPackets++;
                        }
                        else if ((accepted.Buttons & IntentButtons.Crouch) != 0) remoteHeldIntentPackets++;
                    }
                    else if (record.Kind == ReplayFactKind.Snapshot && payload.Length >= 1 + SnapshotHeader.Size
                        && payload[0] == (byte)PacketType.Snapshot)
                    {
                        var header = SnapshotHeader.Read(payload[1..]);
                        for (int index = 0; index < header.PlayerCount; index++)
                        {
                            var canonical = PlayerState.Read(payload[(1 + SnapshotHeader.Size + index * PlayerState.Size)..]);
                            if (canonical.Health == 0 || (canonical.Flags & PlayerState.FlagSpawned) == 0) continue;
                            if (canonical.SlotIndex == localSlot)
                            {
                                if (canonical.Crouched) ownerCanonicalCrouchedSnapshots++; else ownerCanonicalStandingSnapshots++;
                                if (canonical.Tucked) ownerCanonicalTuckedSnapshots++;
                            }
                            else if (canonical.SlotIndex < NetSession.SlotIsBot.Length && !NetSession.SlotIsBot[canonical.SlotIndex])
                            {
                                if (canonical.Crouched) remoteCanonicalCrouchedSnapshots++; else remoteCanonicalStandingSnapshots++;
                                if (canonical.Tucked) remoteCanonicalTuckedSnapshots++;
                            }
                        }
                    }
                };
                ReplayCapture.Recorder.Accepted += stanceObserver;
            }
            var excludedPadJumpSamples = new List<object>();
            var acceptedJumpSamples = new List<object>(); var postSeamVerticalChanges = new List<object>();
            int acceptedMomentumJumps = 0, acceptedHeldHops = 0, postSeamVerticalChangeCount = 0, highFinalJumpInputCount = 0;
            var highFinalJumpInputs = new List<object>(); long previousSolverDelta = 0;
            var ownerAnomalies = new List<object>(); int ownerAnomalyCount = 0, ownerAnomalyOverflow = 0;
            Vector3 previousOwnerDisplacement = Vector3.Zero;
            Vector3 origin = player.Position; float previousYSpeed = player.Speed.Y; bool airborne = false;
            long firstSteps = scene.DeveloperMomentumSteps; ulong firstSceneFrame = scene.FrameCount;
            double readyTime = clock.Elapsed.TotalSeconds; int tick = 0;
            while (clock.Elapsed.TotalSeconds - readyTime < seconds)
            {
                WaitUntil(clock, ref next);
                int phase = tick % 240;
                int traversalCycle = tick / 240 % 3;
                bool dedicatedLowTravel = traversalLowTravelScript && traversalCycle != 0;
                if (touch != null)
                {
                    // Actual linked Android model, with the GameView direction ->
                    // keyboard boundary mirrored explicitly. No MotionEvent/View,
                    // physical screen or native absolute alt gesture is simulated.
                    if (phase < 192)
                    {
                        if (!touchContact) { touch.PointerDown(23, 100, 300); touchContact = true; }
                        Vector2 axis = phase < 48 ? new(0, -1) : phase < 96 ? new(1, -1)
                            : phase < 144 ? new(0, 1) : new(-1, -1);
                        touch.PointerMove(23, 100 + axis.X * touch.StickRadius * .8f,
                            300 + axis.Y * touch.StickRadius * .8f, tick * 1000L / 60, tick * 1000L / 60);
                        touchSamples++;
                    }
                    else if (touchContact)
                    {
                        touch.PointerUp(23); touchContact = false; touchReleases++;
                        Require(touch.Direction == TouchControls.Dir.None, "actual model release clears virtual stick");
                    }
                    var direction = touch.Direction;
                    if (direction != TouchControls.Dir.None) touchDirectionalSamples++;
                    bool horizontal = (direction & (TouchControls.Dir.Left | TouchControls.Dir.Right)) != 0;
                    bool vertical = (direction & (TouchControls.Dir.Up | TouchControls.Dir.Down)) != 0;
                    if (horizontal && vertical) touchDiagonalSamples++;
                    set(keyboard, Keys.W, (direction & TouchControls.Dir.Up) != 0);
                    set(keyboard, Keys.S, (direction & TouchControls.Dir.Down) != 0);
                    set(keyboard, Keys.A, (direction & TouchControls.Dir.Left) != 0);
                    set(keyboard, Keys.D, (direction & TouchControls.Dir.Right) != 0);
                }
                else
                {
                    if (dedicatedLowTravel)
                    {
                        // Cycle 1: settle in crouch for 30 ticks, then actually walk low.
                        // Cycle 2: earn ordinary run speed for 60 ticks before fresh Ctrl.
                        set(keyboard, Keys.W, phase < 160 && (traversalCycle == 2 || phase >= 30));
                        set(keyboard, Keys.D, false); set(keyboard, Keys.S, false);
                    }
                    else
                    {
                        set(keyboard, Keys.W, phase < 48); set(keyboard, Keys.D, phase >= 48 && phase < 96);
                        set(keyboard, Keys.S, phase >= 96 && phase < 144);
                    }
                }
                bool heldHopModel = expected is MovementModel.Quake3 or MovementModel.Traversal or MovementModel.Parkour;
                bool heldHopScript = heldHopModel && acceptanceOptions != null;
                set(keyboard, Keys.Space, !dedicatedLowTravel
                    && (heldHopScript ? phase >= 30 && phase < 192 : tick % 90 == 30));
                // Recording acceptance adds real mid-air form transitions; the
                // short protocol/rule worker keeps its original input script.
                set(keyboard, Keys.Tab, replayPath != null && (traversalLowTravelScript
                    ? traversalCycle == 0 && phase is 160 or 220
                    : tick % 600 is 220 or 350));
                if (expected is MovementModel.Traversal or MovementModel.Parkour)
                    set(keyboard, Keys.LeftControl, dedicatedLowTravel
                        ? phase < 160 && (traversalCycle == 1 || phase >= 60)
                        : phase >= 160 && phase < 200);
                if (expected == MovementModel.Parkour && touch == null)
                {
                    set(keyboard, Keys.Space, phase is >= 30 and < 192);
                    set(keyboard, Keys.LeftControl, phase is >= 48 and < 110);
                }
                ushort padMinBefore = acceptanceOptions == null ? (ushort)0
                    : (ushort)typeof(PlayerEntity).GetField("_jumpPadControlLockMin", Hidden)!.GetValue(player)!;
                object? jumpBypassBefore = acceptanceOptions != null && MovementModelRules.UsesMomentumSolver(expected) && tick % 90 == 30
                    ? MomentumBypassEvidence(scene, player) : null;
                long before = scene.DeveloperMomentumSteps; ulong frame = scene.FrameCount;
                Vector3 ownerPositionBefore = player.Position, ownerSpeedBefore = player.Speed;
                float expectedRampLift = expected is MovementModel.Traversal or MovementModel.Parkour
                    ? ExpectedTraversalRampLift(scene, player) : 0;
                float allowedJumpImpulse = Fixed.ToFloat(player.Values.JumpSpeed) + expectedRampLift;
                bool newGroundContactBefore = player.Flags1.TestFlag(PlayerFlags1.Standing)
                    && !player.Flags1.TestFlag(PlayerFlags1.StandingPrevious);
                uint ownerNetFrameBefore = NetSession.NetFrame;
                object? ownerGatesBefore = acceptanceOptions != null ? MomentumBypassEvidence(scene, player) : null;
                scene.OnSimulationFrame(); CheckConnection();
                if (expected is MovementModel.Traversal or MovementModel.Parkour)
                {
                    if (player.Controls.Crouch.IsDown) ownerCrouchHeldTicks++;
                    if (player.ModCrouched) ownerCrouchedTicks++;
                    else if (!player.IsAltForm && !player.IsMorphing && !player.IsUnmorphing) ownerStandingTicks++;
                    if (player.ModTucked) ownerTuckedTicks++;
                    if (player.ModCrouched != previousOwnerCrouched) ownerStanceChanges++;
                    previousOwnerCrouched = player.ModCrouched;
                    if (player.ModSliding) ownerPhysicalSlideTicks++;
                    ObserveLowTravel(player, ref ownerVisualWalkTicks, ref ownerVisualSlideTicks);
                    Require(player.ModCollisionCrouched == player.ModCrouched,
                        "actual owner collision stance follows production crouch input without historical override");
                }
                Vector3 ownerDisplacement = player.Position - ownerPositionBefore;
                float horizontalDisplacement = new Vector2(ownerDisplacement.X, ownerDisplacement.Z).Length;
                Vector3 displacementChange = ownerDisplacement - previousOwnerDisplacement;
                float horizontalDisplacementChange = new Vector2(displacementChange.X, displacementChange.Z).Length;
                if (acceptanceOptions != null && (horizontalDisplacement > .25f || horizontalDisplacementChange > .25f))
                {
                    ownerAnomalyCount++;
                    if (ownerAnomalies.Count < 128)
                    {
                        float[] V(Vector3 value) => new[] { value.X, value.Y, value.Z };
                        ownerAnomalies.Add(new { tick, preNetFrame = ownerNetFrameBefore, postNetFrame = NetSession.NetFrame,
                            epoch = NetSession.AuthorityEpoch, match = NetSession.CurrentMatchId, slot = player.SlotIndex,
                            generation = NetPlayerLifecycle.Generation(player.SlotIndex), life = NetPlayerLifecycle.Get(player.SlotIndex),
                            preSceneFrame = frame, postSceneFrame = scene.FrameCount,
                            prePosition = V(ownerPositionBefore), postPosition = V(player.Position),
                            preNativeSpeed = V(ownerSpeedBefore), postNativeSpeed = V(player.Speed),
                            displacement = V(ownerDisplacement), previousDisplacement = V(previousOwnerDisplacement),
                            horizontalDisplacement, horizontalDisplacementChange,
                            boundary = JumpBoundaryEvidence(scene, player, tick, frame,
                                scene.DeveloperMomentumSteps - before, previousSolverDelta, ownerGatesBefore) });
                    }
                    else ownerAnomalyOverflow++;
                }
                previousOwnerDisplacement = ownerDisplacement;
                acceptance?.Observe(scene);
                Require(NetSession.CurrentMatchId == matchId && NetSession.AuthorityEpoch == epoch,
                    "live fixture remains in its actual frozen match generation");
                Require(NetSession.WorldIsReady && NetRoomChange.GameplayReady && !NetSession.FreezeGameplay,
                    "real baseline readiness remains open");
                Require(scene.GameState.MovementModel == expected && NetSession.ActiveMatchDefinition?.MovementModel == expected,
                    "live server rule remains immutable");
                Require(player.SlotIndex == NetHooks.LocalSlot && !NetHooks.IsPuppet(player) && Finite(player.Position) && Finite(player.Speed), "finite actual local collision body ownership");
                long delta = scene.DeveloperMomentumSteps - before;
                Require(delta is >= 0 and <= 1, "single real local solver application per tick");
                if (expected == MovementModel.Native) Require(delta == 0 && !scene.MomentumMovementActive, "Native match never activates Momentum adapter");
                if (delta == 1)
                {
                    var sample = scene.LastMomentumStep!.Value;
                    Require(sample.Slot == localSlot && sample.SimulationFrame == frame, "diagnostic names actual local slot/frame");
                    Require(BitConverter.SingleToInt32Bits(sample.NativeVelocityBefore.Y) == BitConverter.SingleToInt32Bits(sample.NativeVelocityAfter.Y), "online horizontal adapter preserves native Y bits");
                    Vector3 incoming = new(sample.NativeVelocityBefore.X * 30, 0, sample.NativeVelocityBefore.Z * 30);
                    if (sample.GroundSupport.HasValue)
                        incoming = GroundSlopeMovement.Flatten(sample.NativeVelocityBefore) * 30;
                    var result = expected switch
                    {
                        MovementModel.Traversal => TraversalMovementProfileV5.Step(incoming, sample.Input, sample.Crouched, sample.Sliding),
                        MovementModel.Parkour => ParkourMovementProfileV4.Step(incoming, sample.Input, sample.Crouched, sample.Sliding),
                        MovementModel.Quake3 => Quake3MovementProfileV3.Step(incoming, sample.Input),
                        MovementModel.Momentum => MomentumProfileV1.Step(incoming, sample.Input),
                        _ => throw new InvalidOperationException("Classic must not reach the horizontal solver.")
                    };
                    Require(MathF.Abs(sample.NativeVelocityAfter.X - result.Velocity.X / 30) <= .00001f
                        && MathF.Abs(sample.NativeVelocityAfter.Z - result.Velocity.Z / 30) <= .00001f, "actual online rule uses its immutable horizontal solver");
                    directSamples++;
                }
                bool observedMomentumJump = acceptanceOptions != null && MovementModelRules.UsesMomentumSolver(expected) && delta == 1
                    && scene.LastMomentumStep!.Value.ContactPhase == MomentumContactPhase.LegalJump;
                if (observedMomentumJump)
                {
                    var jump = scene.LastMomentumStep!.Value;
                    bool heldHop = heldHopModel && !player.Controls.Jump.IsPressed
                        && player.Controls.Jump.IsDown && newGroundContactBefore;
                    Require(jump.SimulationFrame == frame && (player.Controls.Jump.IsPressed || heldHop),
                        "actual native jump acceptance names current input/frame");
                    if (heldHop) acceptedHeldHops++;
                    Require(jump.NativeVelocityBefore.Y > .01f
                        && jump.NativeVelocityBefore.Y <= allowedJumpImpulse + .00001f,
                        "accepted jump impulse before gravity/collision stays within native jump plus witnessed Traversal ramp lift");
                    Require(BitConverter.SingleToInt32Bits(jump.NativeVelocityBefore.Y) == BitConverter.SingleToInt32Bits(jump.NativeVelocityAfter.Y),
                        "actual accepted jump Y remains bit-identical across horizontal solver");
                    acceptedMomentumJumps++; impulses++;
                    bool changedUpward = player.Speed.Y > jump.NativeVelocityAfter.Y + .00001f;
                    if (acceptedJumpSamples.Count < 16 || changedUpward)
                    {
                        object evidence = JumpBoundaryEvidence(scene, player, tick, frame, delta, previousSolverDelta, jumpBypassBefore);
                        if (acceptedJumpSamples.Count < 16) acceptedJumpSamples.Add(evidence);
                        if (changedUpward)
                        {
                            postSeamVerticalChangeCount++;
                            if (postSeamVerticalChanges.Count < 16) postSeamVerticalChanges.Add(evidence);
                            if (postSeamVerticalChangeCount <= 4)
                                Console.WriteLine("[momentum-jump-contact] " + JsonSerializer.Serialize(evidence));
                        }
                    }
                }
                if (acceptanceOptions != null && MovementModelRules.UsesMomentumSolver(expected) && player.Controls.Jump.IsPressed
                    && player.Speed.Y > Fixed.ToFloat(player.Values.JumpSpeed) + .00001f)
                {
                    highFinalJumpInputCount++;
                    object evidence = JumpBoundaryEvidence(scene, player, tick, frame, delta, previousSolverDelta, jumpBypassBefore);
                    if (highFinalJumpInputs.Count < 16) highFinalJumpInputs.Add(evidence);
                    if (highFinalJumpInputCount <= 4) Console.WriteLine("[momentum-jump-candidate] " + JsonSerializer.Serialize(evidence));
                }
                if (player.Controls.MoveUp.IsDown || player.Controls.MoveRight.IsDown || player.Controls.MoveDown.IsDown) inputTicks++;
                if (player.Controls.Jump.IsPressed && player.Speed.Y > .01f && player.Speed.Y > previousYSpeed + .01f)
                {
                    ushort padMinAfter = acceptanceOptions == null ? (ushort)0
                        : (ushort)typeof(PlayerEntity).GetField("_jumpPadControlLockMin", Hidden)!.GetValue(player)!;
                    bool nativePadInterval = acceptanceOptions != null && (padMinBefore > 0 || padMinAfter > 0)
                        && player.Flags1.TestFlag(PlayerFlags1.UsedJumpPad);
                    if (nativePadInterval)
                    {
                        padCoincidentJumpInputs++;
                        if (excludedPadJumpSamples.Count < 16) excludedPadJumpSamples.Add(new { tick, y = player.Speed.Y,
                            previousY = previousYSpeed, padMinBefore, padMinAfter, flags1 = player.Flags1.ToString() });
                    }
                    else if (acceptanceOptions != null && MovementModelRules.UsesMomentumSolver(expected))
                    {
                        // The actual native acceptance label above owns jump checks.
                        // A pressed but rejected jump is not a jump impulse.
                    }
                    else
                    {
                        Require(player.Speed.Y <= allowedJumpImpulse + .00001f, $"actual native legal jump retains selected impulse ceiling tick={tick} y={player.Speed.Y:R} previousY={previousYSpeed:R} ceiling={Fixed.ToFloat(player.Values.JumpSpeed):R} health={player.Health} flags1={player.Flags1} flags2={player.Flags2} padLock={typeof(PlayerEntity).GetField("_jumpPadControlLock", Hidden)!.GetValue(player)} padMin={typeof(PlayerEntity).GetField("_jumpPadControlLockMin", Hidden)!.GetValue(player)} damageAge={typeof(PlayerEntity).GetField("_timeSinceDamage", Hidden)!.GetValue(player)} accel={typeof(PlayerEntity).GetField("_accelerationTimer", Hidden)!.GetValue(player)}"); impulses++;
                    }
                }
                previousYSpeed = player.Speed.Y;
                bool grounded = player.Flags1.TestFlag(PlayerFlags1.Grounded);
                if (player.IsAltForm) altTicks++;
                if (!grounded && player.IsMorphing) airborneMorphTicks++;
                if (altTicks > 0 && !player.IsAltForm && !player.IsMorphing && !player.IsUnmorphing) bipedReturnTicks++;
                if (!grounded) { airborne = true; airborneTicks++; }
                else if (airborne) { landings++; airborne = false; }
                foreach (var remote in scene.Players.Items)
                {
                    int slot = remote.SlotIndex;
                    if (slot == localSlot || slot >= NetSession.SlotOccupied.Length || !NetSession.SlotOccupied[slot]
                        || remote.IsBot || NetSession.SlotIsBot[slot] || remote.Health == 0 || !remote.LoadFlags.TestFlag(LoadFlags.Active)) continue;
                    Require(NetHooks.IsPuppet(remote) && !scene.MomentumOwnsPlayer(remote), "actual remote human has no local movement ownership");
                    Require(Finite(remote.Position) && Finite(remote.Speed), "actual remote collision body remains finite");
                    if ((expected is MovementModel.Traversal or MovementModel.Parkour) && NetSession.RemoteStateValid[slot])
                    {
                        var canonical = NetSession.RemoteStates[slot];
                        if (canonical.SlotGeneration == NetPlayerLifecycle.Generation(slot)
                            && canonical.LifeId == NetPlayerLifecycle.Get(slot)
                            && !remote.IsAltForm && !remote.IsMorphing && !remote.IsUnmorphing
                            && (canonical.Flags & PlayerState.FlagAltForm) == 0)
                        {
                            Require(remote.ModCrouched == canonical.Crouched && remote.ModCollisionCrouched == canonical.Crouched,
                                "actual remote biped collision stance equals received authority snapshot");
                            if (canonical.Crouched) remoteCrouchedPoseChecks++;
                            Require(remote.ModTucked == canonical.Tucked, "remote tuck equals authority snapshot");
                            if (canonical.Tucked) remoteTuckedPoseChecks++;
                        }
                    }
                    if (expected is MovementModel.Traversal or MovementModel.Parkour)
                        ObserveLowTravel(remote, ref remoteVisualWalkTicks, ref remoteVisualSlideTicks);
                    remoteSlots.Add(slot);
                    if (previousRemote.TryGetValue(slot, out Vector3 prior) && (remote.Position - prior).Length > .001f) remoteMoving++;
                    previousRemote[slot] = remote.Position;
                }
                if (tick % 60 == 0) samples.Add(new { tick, frame = scene.FrameCount, localSlot, solverSteps = scene.DeveloperMomentumSteps - firstSteps,
                    position = new[] { player.Position.X, player.Position.Y, player.Position.Z }, speed = new[] { player.Speed.X, player.Speed.Y, player.Speed.Z },
                    crouchHeld = player.Controls.Crouch.IsDown, crouched = player.ModCrouched, remoteHumans = remoteSlots.Count });
                previousSolverDelta = delta; tick++;
            }
            if (touch != null)
            {
                if (touchContact) { touch.PointerUp(23); touchReleases++; }
                Require(touchSamples > 0 && touchSamples <= tick && touchDirectionalSamples > 0 && touchDiagonalSamples > 0 && touchReleases > 0,
                    "actual linked touch model produces bounded movement samples, diagonals and releases");
                Require(NetLag.RoundTripMs == 200 && NetLag.JitterMs == 20, "moderate latency remains configured for actual UDP queues");
            }
            Require(inputTicks > 0 && (player.Position - origin).Length > .01f, "actual local input displaces native body");
            Require(impulses > 0 && airborneTicks > 0 && landings > 0, "actual native legal jump, airborne interval and collision landing");
            if (acceptanceOptions != null && MovementModelRules.UsesMomentumSolver(expected))
                Require(acceptedMomentumJumps > 0, "extended match earns actual native accepted jump samples");
            if (acceptanceOptions != null && expected is MovementModel.Quake3 or MovementModel.Traversal or MovementModel.Parkour)
                Require(acceptedHeldHops > 0, "live selected contact-hop model earns held hops from actual new ground contacts");
            Require(remoteSlots.Count > 0 && remoteMoving > 0, "actual other UDP human including late join is observed moving");
            if (expected == MovementModel.Parkour)
                Require(ownerTuckedTicks > 0 && ownerTuckedIntentPackets > 0 && ownerCanonicalTuckedSnapshots > 0
                    && remoteCanonicalTuckedSnapshots > 0 && remoteTuckedPoseChecks > 0,
                    "Parkour tuck crosses real owner input, accepted intents, authority snapshots and remote geometry");
            if (expected is MovementModel.Traversal or MovementModel.Parkour)
                Require(ownerCrouchHeldTicks > 0 && ownerCrouchedTicks > 0 && ownerStandingTicks > 0 && ownerStanceChanges >= 2
                    && ownerHeldIntentPackets > 0 && ownerCrouchedIntentPackets > 0 && remoteHeldIntentPackets > 0
                    && ownerCanonicalCrouchedSnapshots > 0 && ownerCanonicalStandingSnapshots > 0
                    && remoteCanonicalCrouchedSnapshots > 0 && remoteCanonicalStandingSnapshots > 0 && remoteCrouchedPoseChecks > 0,
                    "real Ctrl input produces owner stance, submitted/received UDP intents, canonical owner/remote snapshots and remote low collision shapes");
            // Short protocol workers keep their bounded-duration contract.
            // A full 720-tick cohort must actually earn both motion categories;
            // a key schedule alone is never activation or slide-entitlement proof.
            bool requireLowTravelActivation = traversalLowTravelScript && tick >= 720;
            if (requireLowTravelActivation)
                Require(ownerVisualWalkTicks > 0 && ownerVisualSlideTicks > 0 && ownerPhysicalSlideTicks > 0
                    && remoteVisualWalkTicks > 0 && remoteVisualSlideTicks > 0,
                    $"full real Traversal cohort earns owned/remote cosmetic low travel: ownerWalk={ownerVisualWalkTicks} ownerSlide={ownerVisualSlideTicks} physicalSlide={ownerPhysicalSlideTicks} remoteWalk={remoteVisualWalkTicks} remoteSlide={remoteVisualSlideTicks}");
            Require(expected == MovementModel.Native ? scene.DeveloperMomentumSteps == firstSteps : directSamples > 0, "selected model actually governs local solver execution");
            acceptance?.Complete();
            uint? recordedFrames = null;
            if (replayPath != null)
            {
                Require(altTicks > 0 && airborneMorphTicks > 0 && bipedReturnTicks > 0,
                    "actual recorded keyboard script earns mid-air morph, stable alt form and biped return");
                var timeline = ReplayCapture.Recorder.Timeline;
                Require(timeline.FirstRecordingFrame.HasValue && timeline.LastRecordingFrame.HasValue,
                    "real recorder retains accepted-fact history");
                uint start = timeline.FirstRecordingFrame!.Value, end = timeline.LastRecordingFrame!.Value;
                recordedFrames = end - start;
                Require(recordedFrames >= 1800, "recording spans seek/checkpoint acceptance intervals");
                Require(timeline.TryFreeze(start, end, out var clip) && clip != null,
                    "production timeline freezes actual accepted match facts");
                using (clip) ReplayTimelineArchive.SaveFrozen(clip!, Path.GetFullPath(replayPath));
                Require(ReplayArchive.Validate(replayPath) == ReplayOpenResult.Success,
                    "actual live fixture round-trips through production replay writer and archive validation");
                Require(DemoRecorder.LastError == null && ReplayCapture.WorldCapture.LastError == null,
                    "live canonical world and disk recording report no errors");
                DemoRecorder.Stop();
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new { checks = _checks, model = expected.ToString(), profileVersion = MovementModelRules.ProfileVersion(expected),
                acceptance = acceptance?.Report,
                ownerAnomalyCount, ownerAnomalyOverflow, ownerAnomalies,
                ownerAnomalyScope = "Test-only post-step body displacement/change trigger >0.25 units per simulation call; not a movement acceptance threshold. Maximum128 rows; overflow explicit. Native Speed and positional displacement differ in meaning. Scratch candidates are not a current collision response witness.",
                port, localSlot, matchId, authorityEpoch = epoch, name, seconds, ticks = tick, firstSceneFrame, finalSceneFrame = scene.FrameCount,
                solverSteps = scene.DeveloperMomentumSteps - firstSteps, directSamples, inputTicks, legalJumpImpulses = impulses, acceptedHeldHops, airborneTicks, landings,
                altTicks, airborneMorphTicks, bipedReturnTicks, recordedFrames, padCoincidentJumpInputs, excludedPadJumpSamples, acceptedMomentumJumps, acceptedJumpSamples, postSeamVerticalChangeCount, postSeamVerticalChanges, highFinalJumpInputCount, highFinalJumpInputs,
                ownerCrouchHeldTicks, ownerCrouchedTicks, ownerStandingTicks, ownerStanceChanges,
                requireLowTravelActivation, ownerVisualWalkTicks, ownerVisualSlideTicks, ownerPhysicalSlideTicks,
                remoteVisualWalkTicks, remoteVisualSlideTicks,
                ownerTuckedTicks, ownerTuckedIntentPackets, ownerCanonicalTuckedSnapshots, remoteCanonicalTuckedSnapshots, remoteTuckedPoseChecks,
                ownerHeldIntentPackets, ownerCrouchedIntentPackets, remoteHeldIntentPackets,
                ownerCanonicalCrouchedSnapshots, ownerCanonicalStandingSnapshots,
                remoteCanonicalCrouchedSnapshots, remoteCanonicalStandingSnapshots, remoteCrouchedPoseChecks,
                crouchSchedule = traversalLowTravelScript
                    ? "720 fixed steps repeated: cycle 0 retains W/D/S and hop coverage, Ctrl 160..199; cycle 1 Ctrl 0..159 with W 30..159; cycle 2 W 0..159 with fresh Ctrl 60..159; no jumps in cycles1/2; recording form edges confined to cycle 0 phase160/220; production input only"
                    : expected is MovementModel.Traversal or MovementModel.Parkour ? "Ctrl held at ticks160..199 of each240-tick touch cycle; production input, no stance/shape/velocity writes" : null,
                inputSource = touchModel ? "linked production Android TouchControls + explicit GameView digital keyboard boundary" : "synthetic keyboard",
                touchSamples, touchDirectionalSamples, touchDiagonalSamples, touchReleases,
                touchSchedule = touchModel ? "240 fixed steps: 48 up, 48 up-right, 48 down, 48 up-left, 48 released; native keyboard jump every 90 steps at phase30" : null,
                netLag = new { NetLag.RoundTripMs, NetLag.JitterMs, NetLag.LossPercent, NetLag.ReorderRate, NetLag.DuplicateRate, NetLag.Seed },
                touchScope = touchModel ? "CPU production touch model and real delayed UDP; excludes Android View/MotionEvent, physical screen, native absolute alt gesture" : null,
                remoteSlots = remoteSlots.OrderBy(slot => slot).ToArray(), remoteMovingSamples = remoteMoving, disconnected = false,
                scope = "real localhost UDP dedicated server + independent client processes; CPU/headless Roam; actual bootstrap/late-join/owner movement and optional accepted-fact archive; no rendered/device/bot/replay-seek acceptance", samples }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"MOMENTUM ONLINE SCENE PASS {_checks} assertions model={expected} local={localSlot} steps={scene.DeveloperMomentumSteps - firstSteps} remoteMoving={remoteMoving}");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("MOMENTUM ONLINE SCENE FAIL " + ex); return 1; }
        finally
        {
            if (stanceObserver != null) ReplayCapture.Recorder.Accepted -= stanceObserver;
            NetCampaignEvidence.Write("simulation-client");
            DemoRecorder.Stop();
            try { if (scene != null) MomentumSceneTests.Release(scene); }
            finally
            {
                NetSession.Stop(); touch?.ReleaseEverything();
                if (touchModel) NetLag.Configure(previousRtt + ":" + previousJitter);
            }
        }
    }
    private static object MomentumBypassEvidence(Scene scene, PlayerEntity player)
    {
        object? Read(string name) => typeof(PlayerEntity).GetField(name, Hidden)!.GetValue(player);
        return new { active = scene.MomentumMovementActive, eligible = player.ModMomentumEligible, health = player.Health,
            flags1 = player.Flags1.ToString(), flags2 = player.Flags2.ToString(),
            damageAge = Read("_timeSinceDamage"),
            frozen = Read("_frozenTimer"), field6D0 = Read("_field6D0"), padLock = Read("_jumpPadControlLock"), padMin = Read("_jumpPadControlLockMin"),
            q3Gravity = Read("_q3LaunchGravity"), q3Frames = Read("_q3LaunchFrames"), accelerationTimer = Read("_accelerationTimer"),
            slipperiness = Read("_slipperiness"), standingEntity = Read("_standingEntCol") != null, crestTimer = Read("_spireLedgeCrestTimer"),
            attachedEnemy = player.AttachedEnemy != null, cameraSequence = scene.CameraSequences.Current != null,
            player.IsAltForm, player.IsMorphing, player.IsUnmorphing, player.IsPrimeHunter };
    }
    private static object JumpBoundaryEvidence(Scene scene, PlayerEntity player, int tick, ulong frame,
        long solverDelta, long previousSolverDelta, object? bypassBefore)
    {
        var latest = scene.LastMomentumStep;
        bool sameFrame = latest is { } current && current.SimulationFrame == frame;
        bool currentStep = sameFrame && solverDelta == 1;
        float gravity = (float)typeof(PlayerEntity).GetField("_gravity", Hidden)!.GetValue(player)!;
        var collision = (CollisionResult[])typeof(PlayerEntity).GetField("_collisionScratch", Hidden)!.GetValue(player)!;
        Vector3? afterGravity = currentStep ? latest!.Value.NativeVelocityAfter.AddY(gravity / 2) : null;
        float[]? V(Vector3? value) => value is { } vector ? new[] { vector.X, vector.Y, vector.Z } : null;
        return new { tick, frame, solverDelta, previousSolverDelta, sameFrame, latestSampleFrame = latest?.SimulationFrame,
            contact = currentStep ? latest!.Value.ContactPhase.ToString() : null,
            beforeSolver = V(currentStep ? latest!.Value.NativeVelocityBefore : null),
            afterSolver = V(currentStep ? latest!.Value.NativeVelocityAfter : null),
            finalNativeVelocity = V(player.Speed), gravity, bypassBefore, bypassAfter = MomentumBypassEvidence(scene, player),
            nativeJumpCeiling = Fixed.ToFloat(player.Values.JumpSpeed), flags1 = player.Flags1.ToString(),
            queryScratchCandidates = collision.Where(contact => contact.Plane.Xyz.LengthSquared > .000001f).Take(8).Select(contact =>
            {
                Vector3 normal = contact.Plane.Xyz;
                float? dot = afterGravity is { } velocity ? Vector3.Dot(velocity, normal) : null;
                return new { kind = contact.Field0, normal = new[] { normal.X, normal.Y, normal.Z }, dotAfterGravity = dot,
                    projectedY = dot.HasValue ? afterGravity!.Value.Y + normal.Y * -MathF.Min(dot.Value, 0) : (float?)null };
            }).ToArray(),
            scope = "query scratch is cleared at CheckCollision entry; this diagnostic retains no call/count witness, entries may be unused, and a bypass may leave an earlier call; edge response can derive another normal; not resolved-contact proof" };
    }
    private static void CheckConnection() => Require(NetSession.Active && NetSession.IsClient && !NetSession.IsAuthority
        && !NetSession.Refused && !NetSession.SessionTimedOut && !NetSession.ConnectionLost,
        "actual UDP session remains connected: " + NetSession.RefusedReason);
    private static void WaitUntil(Stopwatch clock, ref double next)
    {
        next += 1d / 60;
        double remaining = next - clock.Elapsed.TotalSeconds;
        if (remaining > .002) Thread.Sleep((int)((remaining - .001) * 1000));
        while (clock.Elapsed.TotalSeconds < next) Thread.SpinWait(32);
        // Bound catch-up work after an asset load or an external scheduling stall.
        if (clock.Elapsed.TotalSeconds - next > .25) next = clock.Elapsed.TotalSeconds;
    }
}
