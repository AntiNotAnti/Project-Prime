using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;
using OpenTK.Windowing.Common;

// Opt-in fixture in the existing native-render-check window. RenderWindow owns
// every FrameTiming.Advance, simulation step, draw and swap. This partial only
// prepares existing scripted input and observes/captures the completed frame.
sealed partial class CheckWindow
{
    private const BindingFlags PosePrivate = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly bool _poseCheck;
    private readonly bool _posePixels;
    private readonly string _poseScenario;
    private readonly string _poseScheduleId;
    private readonly int? _poseRequestedFov;
    private int _posePriorFieldOfView;
    private int? _poseSelectedFieldOfView;
    private double PoseElapsed => DisplayPosePlan.Elapsed(_poseScheduleId, _cap, _poseOutput);
    private bool _poseInputIsolationInitialized, _posePriorGamepadFocused;
    private DisplayPoseInputIsolation? _poseGamepadIsolation;
    private bool _posePhysicalControllerConnectedBefore, _poseControllerIsolationVerified;
    private string? _posePreviousSupplemental;
    internal void RestoreDisplayPoseInputIsolation()
    {
        if (!_poseInputIsolationInitialized) return;
        try { _poseGamepadIsolation?.Dispose(); }
        finally
        {
            _poseGamepadIsolation = null;
            GamepadContexts.Focused = _posePriorGamepadFocused;
            RenderOptions.FieldOfView = _posePriorFieldOfView;
            _poseInputIsolationInitialized = false;
        }
    }
    private readonly string _poseFocusControl;
    private bool _poseFocusInjected, _poseInjectingFocus;
    private readonly List<object> _poseFocusEvents = new();
    private void ObservePoseFocus(bool focused, string source) => _poseFocusEvents.Add(new
    {
        focused, source, outputFrame = _poseOutput, sceneFrame = Scene?.FrameCount,
        wallSeconds = _wall.Elapsed.TotalSeconds
    });
    private bool _poseGunPixelsVerified, _poseChargePixelsVerified;
    private readonly List<object> _poseSchedule = new(), _poseSteps = new(), _posePictures = new(), _poseFences = new(), _poseInputs = new(), _poseDrawDiagnostics = new();
    private readonly HashSet<int> _poseCaptured = new();
    private int _poseOutput, _poseExpectedSteps, _poseZeroStepChecks, _poseRepeatedChecks, _poseCarryFrames;
    private long _poseStepsBefore;
    private ulong _poseFrameBefore;
    private string? _posePreviousProjection;
    private bool _poseReset, _poseDied, _poseRespawned, _poseAlt, _poseAttack, _poseScope, _poseCharge, _poseEffect, _poseChargeEffect;
    private bool _poseWasDead;
    private float _poseScopeMin = float.PositiveInfinity, _poseScopeMax = float.NegativeInfinity;
    private readonly Vector3[] _poseOrigins = new Vector3[2];
    private readonly List<Keys> _poseHeldKeys = new();
    private readonly List<MouseButton> _poseHeldButtons = new();
    private KeyboardState _poseKeyboard = null!;
    private MouseState _poseMouse = null!;
    private Action<KeyboardState, Keys, bool> _poseSetKey = null!;
    private Action<MouseState, MouseButton, bool> _poseSetButton = null!;
    private string _poseInput = "";
    private static readonly string[] PoseCameraFields = { "_cameraMode", "_cameraPosition", "_cameraFacing", "_cameraRight", "_cameraUp", "_freeCam", "_inputMode", "_cameraFov", "_viewModelFov" };

    private static Type EngineType(string name) => typeof(Scene).Assembly.GetType(name, throwOnError: true)!;
    private static object? PoseGet(object owner, string name)
        => owner.GetType().GetField(name, PosePrivate)?.GetValue(owner)
            ?? owner.GetType().GetProperty(name, PosePrivate)?.GetValue(owner);
    private static void PoseSet(object owner, string name, object value)
    {
        if (owner.GetType().GetField(name, PosePrivate) is { } field) field.SetValue(owner, value);
        else (owner.GetType().GetProperty(name, PosePrivate) ?? throw new MissingMemberException(name)).SetValue(owner, value);
    }
    private static object? PoseCall(object owner, string name, params object?[] values)
        => (owner.GetType().GetMethod(name, PosePrivate) ?? throw new MissingMethodException(name)).Invoke(owner, values);
    private static float[] PoseVector(Vector3 v) => new[] { v.X, v.Y, v.Z };
    private static string PoseDigest(byte[] value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
    private string PoseProjection()
    {
        uint tick = checked((uint)Scene.FrameCount);
        string gameplay = (string)EngineType("MphRead.Mods.Network.ReplayStateHash")
            .GetMethod("Compute", PosePrivate, null, new[] { typeof(Scene), typeof(uint) }, null)!.Invoke(null, new object[] { Scene, tick })!;
        string presentation = (string)typeof(Scene).GetMethod("ReplayPresentationHash", PosePrivate)!.Invoke(Scene, new object[] { tick })!;
        return gameplay + ":" + presentation;
    }
    // Fixture-local observations, independent of the archival hash contract.
    // These never add fields to ReplayStateHash or ReplayPresentationHash.
    private string PoseCombatWitness() => PoseDigest(JsonSerializer.SerializeToUtf8Bytes(new
    {
        players = Scene.Players.Items.Where(p => p != null).Select(p => new
        {
            p.SlotIndex, p.Health, weapon = (int)p.CurrentWeapon, p.TimeSinceShot,
            p.RespawnTimer, p.DeathCountdown, p.IsAltForm, p.IsMorphing, p.IsUnmorphing,
            points = Scene.GameState.Points[p.SlotIndex], kills = Scene.GameState.Kills[p.SlotIndex],
            deaths = Scene.GameState.Deaths[p.SlotIndex]
        }).ToArray(),
        projectiles = Scene.Entities.WhereEnumerable().OfType<BeamProjectileEntity>().Select(b => new
        {
            owner = b.Owner is PlayerEntity owner ? owner.SlotIndex : (b.Owner as HalfturretEntity)?.Owner.SlotIndex,
            beam = (int)b.Beam, flags = (int)b.Flags,
            position = PoseVector(b.Position), origin = PoseVector(b.SpawnPosition),
            direction = PoseVector(b.Direction), velocity = PoseVector(b.Velocity),
            b.Damage, b.HeadshotDamage, b.SplashDamage, b.Age, b.Lifespan
        }).ToArray(),
        bombs = Scene.Entities.WhereEnumerable().OfType<BombEntity>().Select(b => new
        {
            owner = b.Owner?.SlotIndex, kind = (int)b.BombType, flags = (int)b.Flags,
            position = PoseVector(b.Position), b.Countdown, b.BombIndex, b.Damage, b.EnemyDamage
        }).ToArray()
    }));
    private string PoseSourceWitness() => PoseDigest(JsonSerializer.SerializeToUtf8Bytes(
        Scene.Players.Items.Take(2).Select(p => new
        {
            p.SlotIndex, position = PoseVector(p.Position), facing = PoseVector(p.FacingVector),
            gunVector = PoseVector((Vector3)PoseGet(p, "ModGunVector")!),
            muzzle = PoseVector((Vector3)PoseGet(p, "ModMuzzlePos")!),
            controls = p.Controls.All.Select(b => new { b.IsDown, b.IsPressed, b.IsReleased }).ToArray()
        }).ToArray()));
    private string PoseRngWitness() => PoseDigest(JsonSerializer.SerializeToUtf8Bytes(new
    {
        sceneRng1 = Scene.Random.Rng1, sceneRng2 = Scene.Random.Rng2,
        globalRng1 = Rng.Rng1, globalRng2 = Rng.Rng2
    }));
    private string PoseSupplementalWitness() => PoseCombatWitness() + ":" + PoseSourceWitness() + ":" + PoseRngWitness();
    private static float[] PoseMatrix(Matrix4 value)
        => Enumerable.Range(0, 16).Select(i => value[i / 4, i % 4]).ToArray();
    private object PoseAnchorObservation()
    {
        var actors = new List<object>();
        foreach (PlayerEntity player in Scene.Players.Items.Take(2))
        {
            var fields = new Dictionary<string, object?>
            {
                ["slot"] = player.SlotIndex,
                ["modelTransform"] = PoseMatrix((Matrix4)PoseGet(player, "_modelTransform")!),
                ["cameraPosition"] = PoseVector(player.CameraInfo.Position),
                ["cameraTarget"] = PoseVector(player.CameraInfo.Target),
                ["cameraFov"] = player.CameraInfo.Fov
            };
            foreach (string key in new[] { "_kandenSegMtx", "_bipedIceTransforms" })
                fields[key] = PoseGet(player, key) is IEnumerable matrices
                    ? matrices.Cast<Matrix4>().Select(PoseMatrix).ToArray() : null;
            fields["_kandenSegPos"] = PoseGet(player, "_kandenSegPos") is IEnumerable positions
                ? positions.Cast<Vector3>().Select(PoseVector).ToArray() : null;
            foreach (string key in new[] { "_chargeEffect", "_muzzleEffect" })
            {
                object? effect = PoseGet(player, key);
                fields[key + "Present"] = effect != null;
                fields[key + "ElementOwnTransforms"] = effect != null && PoseGet(effect, "Elements") is IEnumerable elements
                    ? elements.Cast<object>().Select(element => PoseMatrix((Matrix4)PoseGet(element, "OwnTransform")!)).ToArray()
                    : null;
            }
            actors.Add(fields);
        }
        return actors;
    }
    private string PoseAnchorHash() => PoseDigest(JsonSerializer.SerializeToUtf8Bytes(PoseAnchorObservation()));
    private void PoseRequire(bool value, string message)
    {
        if (!value) throw new InvalidDataException("Display pose: " + message);
    }
    private void InitializeDisplayPose()
    {
        PoseRequire(!Headless.Active && !NetSession.Active && !Scene.FrameAdvance, "fresh native offline scene required");
        PoseRequire(_shots != null && !Directory.Exists(_shots), "fresh private capture directory required");
        Directory.CreateDirectory(_shots!);
        Features.ProHud = false; Features.FixedCrosshair = true; Features.FixedWeapon = false;
        Scene.SetFreeCamera(false);
        _poseKeyboard = (KeyboardState)PoseGet(Scene, "_keyboardState")!;
        _poseMouse = (MouseState)PoseGet(Scene, "_mouseState")!;
        _poseSetKey = (Action<KeyboardState, Keys, bool>)typeof(KeyboardState).GetMethod("SetKeyState", PosePrivate)!
            .CreateDelegate(typeof(Action<KeyboardState, Keys, bool>));
        _poseSetButton = (Action<MouseState, MouseButton, bool>)typeof(MouseState).GetProperty("Item", PosePrivate)!.SetMethod!
            .CreateDelegate(typeof(Action<MouseState, MouseButton, bool>));
        NetTestScript.Reset();
        _posePriorGamepadFocused = GamepadContexts.Focused;
        _posePriorFieldOfView = RenderOptions.FieldOfView;
        if (_poseRequestedFov.HasValue) RenderOptions.FieldOfView = _poseRequestedFov.Value;
        _poseSelectedFieldOfView = RenderOptions.FieldOfView;
        _poseInputIsolationInitialized = true;
        _poseGamepadIsolation = new DisplayPoseInputIsolation();
        _posePhysicalControllerConnectedBefore = _poseGamepadIsolation.PriorConnected;
    }
    private object PoseFovObservation()
    {
        float world = (float)PoseGet(Scene, "_cameraFov")!;
        float viewmodel = (float)PoseGet(Scene, "_viewModelFov")!;
        bool policyMatch = MathF.Abs(world - MathHelper.DegreesToRadians(
            RenderOptions.ScaleCameraFov(MathHelper.RadiansToDegrees(viewmodel)))) < .00001f;
        if (Scene.FrameCount > DisplayPosePlan.WarmupTicks)
            PoseRequire(policyMatch, "world/viewmodel FOV scaling policy changed");
        return new { selectedDegrees = RenderOptions.FieldOfView, authoredCameraDegrees = Scene.Players.Main.CameraInfo.Fov,
            worldRadians = world, viewmodelRadians = viewmodel, scaledWorldPolicyMatch = policyMatch };
    }
    private void BeforeDisplayPoseFrame()
    {
        PoseRequire(_wall.Elapsed.TotalSeconds < 240, "bounded capture wall timeout");
        PoseRequire(!Scene.FrameAdvance && !NetSession.Active, "normal offline clock ownership changed");
        PoseRequire(_poseGamepadIsolation?.Isolated == true, "physical controller polling is not isolated from frozen input");
        _poseControllerIsolationVerified = true;
        // Only the diagnostic's synthetic device is admitted. Native focus is
        // observed separately; these captures do not qualify OS focus behavior.
        GamepadContexts.Focused = true;
        int next = checked((int)Scene.FrameCount + 1);
        if (next == 245 && !_poseFocusInjected && _poseFocusControl != "none")
        {
            _poseFocusInjected = true;
            foreach (bool focused in new[] { false, true })
            {
                if (_poseFocusControl == "legacy")
                {
                    ObservePoseFocus(focused, "injected-legacy-base");
                    base.OnFocusedChanged(new FocusedChangedEventArgs(focused));
                }
                else
                {
                    // Use the ordinary callback, labeling the injected observation.
                    _poseInjectingFocus = true;
                    try { OnFocusedChanged(new FocusedChangedEventArgs(focused)); }
                    finally { _poseInjectingFocus = false; }
                }
            }
        }
        if (next == 1390 && !_poseReset)
        {
            _poseReset = true; FrameTiming.Reset();
            CheckPoseFence("clock-reset-immediate", Scene.Players.Items[1]);
            var camera = Scene.Players.Main.CameraInfo;
            float drawFov = (float)camera.GetType().GetMethod("ModGetDrawFov", PosePrivate)!.Invoke(camera, new object[] { .5, true })!;
            PoseRequire(drawFov == camera.Fov, "responsive scope reused FOV before clock reset");
        }
        double accumulator = (double)typeof(FrameTiming).GetField("_accumulator", PosePrivate)!.GetValue(null)!;
        _poseExpectedSteps = DisplayPosePlan.PreviewSteps(accumulator, PoseElapsed, FrameTiming.StepSeconds);
        _poseStepsBefore = FrameTiming.TotalSteps; _poseFrameBefore = Scene.FrameCount;
        if (_poseExpectedSteps == 1) PrepareDisplayPoseInput(next);
        _poseProjectionBeforeRender = PoseProjection();
        _poseFieldsBeforeDraw = _poseExpectedSteps == 0 && Scene.FrameCount is >= 360 and <= 400
            ? PosePresentationFields() : null;
    }
    private void PrepareDisplayPoseInput(int tick)
    {
        if (tick == 241)
            for (int slot = 0; slot < 2; slot++)
            {
                var player = Scene.Players.Items[slot];
                PoseRequire(player.Health > 0 && player.LoadFlags.TestFlag(LoadFlags.Spawned), "actor failed warmup spawn");
                _poseOrigins[slot] = player.Position; player.Health = 999;
            }
        if (tick is 361 or 1321)
            foreach (var player in Scene.Players.Items.Take(2))
            {
                PoseCall(player, tick == 361 ? "ModArmWeapon" : "ModArmZoomWeapon",
                    tick == 361 ? new object?[] { BeamType.PowerBeam } : Array.Empty<object?>());
                player.Health = 999;
            }
        for (int slot = 0; slot < 2; slot++)
        {
            PlayerEntity player = Scene.Players.Items[slot];
            switch (DisplayPosePlan.Phase(tick))
            {
                case "warmup":
                    NetTestScript.Rest(player, wantBiped: true);
                    // Rest asks initially unspawned actors to respawn with fire.
                    // Keep the first synthetic hardware baseline neutral before
                    // native focus/first-input admission; initial spawn is still
                    // owned by the ordinary engine step, not by this fixture.
                    if (tick == 1) player.Controls.ClearAll();
                    break;
                case "biped":
                    NetTestScript.WalkForward(player);
                    player.Controls.MoveLeft.IsDown = tick % 60 < 30;
                    player.Controls.MoveRight.IsDown = tick % 60 >= 30;
                    PoseCall(player, "ModApplyScriptAim", .5f, MathF.Sin(tick / 25f) * .2f);
                    break;
                case "charge":
                    NetTestScript.HoldFire(player, tick < 460);
                    if (DisplayPosePlan.RetreatsDuringCharge(_poseScenario, tick))
                        player.Controls.MoveDown.IsDown = true;
                    break;
                case "alt-lifecycle":
                    typeof(Scene).Assembly.GetType("MphRead.Mods.Fidelity.FidelityContentScenarios")!
                        .GetMethod("DriveAlt", PosePrivate)!.Invoke(null, new object[] { player, Scene.Players.Items[1 - slot], _poseOrigins[slot], tick - 480 });
                    break;
                default:
                    NetTestScript.Rest(player, wantBiped: true);
                    bool zoom = !player.EquipInfo.Zoomed && tick % 8 == 0;
                    player.Controls.Zoom.IsDown = zoom; player.Controls.Zoom.IsPressed = zoom;
                    if (zoom) PoseCall(player, "ModNoteInput");
                    break;
            }
        }
        if (tick is 1382 or 1384)
        {
            var target = Scene.Players.Items[1];
            PoseCall(target, "ModPlaceAt", tick == 1382 ? _poseOrigins[1] + Vector3.UnitX * 6 : _poseOrigins[1]);
            CheckPoseFence("uncaptured-teleport-" + tick, target);
        }
        // The actual local ProcessInput still consumes a keyboard/mouse snapshot.
        // Mirror the existing script's held binds, preserving alias keys (jump /
        // boost and shoot / alt attack) exactly as a real input backend does.
        foreach (Keys key in _poseHeldKeys) _poseSetKey(_poseKeyboard, key, false);
        foreach (MouseButton button in _poseHeldButtons) _poseSetButton(_poseMouse, button, false);
        _poseHeldKeys.Clear(); _poseHeldButtons.Clear();
        foreach (Keybind bind in Scene.Players.Main.Controls.All.Where(b => b.IsDown))
            if (bind.Type == ButtonType.Key && !_poseHeldKeys.Contains(bind.Key))
            { _poseSetKey(_poseKeyboard, bind.Key, true); _poseHeldKeys.Add(bind.Key); }
            else if (bind.Type == ButtonType.Mouse && !_poseHeldButtons.Contains(bind.MouseButton))
            { _poseSetButton(_poseMouse, bind.MouseButton, true); _poseHeldButtons.Add(bind.MouseButton); }
        _poseInput = InputWitness();
    }
    private string InputWitness()
    {
        var rows = Scene.Players.Items.Take(2).Select(p => new { slot = p.SlotIndex,
            down = String.Join(',', p.Controls.All.Select((b, i) => b.IsDown ? i.ToString() : "").Where(v => v.Length > 0)),
            pressed = String.Join(',', p.Controls.All.Select((b, i) => b.IsPressed ? i.ToString() : "").Where(v => v.Length > 0)),
            released = String.Join(',', p.Controls.All.Select((b, i) => b.IsReleased ? i.ToString() : "").Where(v => v.Length > 0)) }).ToArray();
        return JsonSerializer.Serialize(rows);
    }
    private (Vector3 Position, Vector3 Facing) SampleActor(PlayerEntity player)
    {
        object?[] args = { null, null };
        typeof(PlayerEntity).GetMethod("ModGetDrawActorPose", PosePrivate)!.Invoke(player, args);
        return ((Vector3)args[0]!, (Vector3)args[1]!);
    }
    private void CheckPoseFence(string reason, PlayerEntity player)
    {
        var sampled = SampleActor(player);
        PoseRequire(sampled.Position == player.Position, reason + " carried previous-life/teleport position");
        _poseFences.Add(new { reason, simulationFrame = Scene.FrameCount, actor = player.SlotIndex,
            health = player.Health, position = PoseVector(player.Position), sampledPosition = PoseVector(sampled.Position) });
    }
    private void AfterDisplayPoseFrame()
    {
        double scheduledElapsed = PoseElapsed;
        _poseOutput++;
        long actualSteps = FrameTiming.TotalSteps - _poseStepsBefore;
        PoseRequire(actualSteps == _poseExpectedSteps && Scene.FrameCount - _poseFrameBefore == (ulong)actualSteps,
            "normal RenderWindow steps differ from the observed accumulator/scheduled elapsed");
        PoseRequire(FrameTiming.Active && FrameTiming.DroppedSteps == 0 && FrameTiming.Stalls == 0, "clock stalled/reset outside declared fence");
        PoseRequire(Scene.CheckRenderInvariants() && GL.GetError() == OpenTK.Graphics.OpenGL.ErrorCode.NoError, "native frame invariant/GL failure");
        ObserveDisplayParticleSamples();
        string projection = PoseProjection();
        string supplemental = PoseSupplementalWitness();
        if (actualSteps == 0 && _posePreviousProjection != null)
        {
            if (projection != _posePreviousProjection) RecordPoseCanonicalFailure(projection, actualSteps);
            PoseRequire(projection == _posePreviousProjection, "zero-step draw changed established canonical simulation/replay projection");
            PoseRequire(supplemental == _posePreviousSupplemental, "zero-step draw changed combat/source-input/RNG witness");
            _poseZeroStepChecks++;
        }
        var target = Scene.Players.Items[1];
        var pose = SampleActor(target);
        if (pose.Position != target.Position) _poseCarryFrames++;
        int tick = checked((int)Scene.FrameCount);
        _poseSchedule.Add(new { outputFrame = _poseOutput, scheduledElapsed,
            sceneFrame = tick, actualSteps, alpha = FrameTiming.Alpha,
            presentationAlpha = FrameTiming.PresentationAlpha, active = FrameTiming.Active,
            drawAnchorDiagnosticSha256 = PoseAnchorHash() });
        if (actualSteps == 1)
        {
            bool dead = target.Health == 0;
            if (dead) { _poseDied = true; CheckPoseFence("dead", target); }
            if (_poseWasDead && !dead) { _poseRespawned = true; CheckPoseFence("respawn-first-frame", target); }
            _poseWasDead = dead;
            _poseAlt |= target.IsAltForm;
            _poseAttack |= target.Flags2.TestFlag(PlayerFlags2.AltAttack)
                || Scene.Entities.WhereEnumerable().OfType<BombEntity>().Any(b => b.Owner == target);
            _poseScope |= Scene.Players.Main.EquipInfo.Zoomed;
            _poseCharge |= (int)PoseGet(Scene.Players.Main, "ModChargeLevel")! > 0;
            _poseEffect |= ((IEnumerable)PoseGet(Scene, "_activeElements")!).Cast<object>().Any();
            _poseChargeEffect |= PoseGet(Scene.Players.Main, "_chargeEffect") != null;
            if (tick >= 1321)
            {
                _poseScopeMin = Math.Min(_poseScopeMin, Scene.Players.Main.CameraInfo.Fov);
                _poseScopeMax = Math.Max(_poseScopeMax, Scene.Players.Main.CameraInfo.Fov);
            }
            // Tick 361 arms the diagnostic weapon and enters held fire. Permit
            // that one admission boundary; every subsequent charge tick must
            // consume the script exactly, including its release at tick 460.
            if (tick is >= 362 and <= 480)
                PoseRequire(Scene.Players.Main.Controls.Shoot.IsDown == (tick < 460),
                    "main charge input was suppressed or changed at tick " + tick);
            string consumed = InputWitness();
            _poseInputs.Add(new { tick, preparedInput = _poseInput, consumedInput = consumed });
            _poseSteps.Add(new { tick, phase = DisplayPosePlan.Phase(tick), canonicalProjection = projection,
                preparedInput = _poseInput, consumedInput = consumed, health = target.Health,
                position = PoseVector(target.Position), facing = PoseVector(target.FacingVector),
                alt = target.IsAltForm, morphing = target.IsMorphing, unmorphing = target.IsUnmorphing,
                zoomed = Scene.Players.Main.EquipInfo.Zoomed, fov = Scene.Players.Main.CameraInfo.Fov,
                rng1 = Scene.Random.Rng1, rng2 = Scene.Random.Rng2,
                combatWitness = PoseCombatWitness(), sourceInputWitness = PoseSourceWitness(), rngWitness = PoseRngWitness(),
                fovObservation = PoseFovObservation() });
        }
        if (DisplayPosePlan.Captures.TryGetValue(tick, out string? label) && _poseCaptured.Add(tick))
            CaptureDisplayPose(label, tick);
        _posePreviousProjection = PoseProjection();
        _posePreviousSupplemental = PoseSupplementalWitness();
        if (tick >= DisplayPosePlan.TotalTicks)
        {
            PoseRequire(_poseCaptured.Count == DisplayPosePlan.Captures.Count && _poseDied && _poseRespawned
                && _poseAlt && _poseAttack && _poseScope && _poseCharge && _poseEffect && _poseChargeEffect && _poseScopeMax > _poseScopeMin,
                "required native lifecycle/scope/charge/effect states were not exercised");
            PoseRequire(_cap == 60 ? _poseCarryFrames == 0 : _poseCarryFrames > 0 && _poseZeroStepChecks > 0,
                "requested high-refresh/current-state draw path was not exercised");
            PoseRequire(!_posePixels || _poseGunPixelsVerified && _poseChargePixelsVerified,
                "requested native gun/charge pixel controls were not exercised");
            PoseRequire(_poseParticleSamples > 0 && (_cap == 60 || _poseParticleMotionSamples > 0),
                "native completed particle samples did not exercise the requested presentation policy");
            PoseRequire(_poseBeamDraws > 0 && (_cap == 60 || _poseBeamSamples > 0),
                "native beam drawings did not exercise the requested presentation policy");
            Complete = _poseFocusControl != "legacy"; Close();
        }
    }
    private void DrawPoseRepeated(string label)
    {
        string before = PoseProjection(); ulong tick = Scene.FrameCount;
        string supplementalBefore = PoseSupplementalWitness();
        object anchorBefore = PoseAnchorObservation();
        Scene.OnDrawFrame(); PoseRequire(Scene.OnRenderFrame(), "repeat renderer stopped");
        object anchorAfter = PoseAnchorObservation();
        _poseDrawDiagnostics.Add(new { label, tick, outputFrame = _poseOutput,
            presentationAlpha = FrameTiming.PresentationAlpha, anchorBefore, anchorAfter,
            sameDisplayTimeAnchorMatch = PoseDigest(JsonSerializer.SerializeToUtf8Bytes(anchorBefore))
                == PoseDigest(JsonSerializer.SerializeToUtf8Bytes(anchorAfter)) });
        PoseRequire(Scene.FrameCount == tick && PoseProjection() == before,
            label + " repeated draw changed established canonical simulation/replay projection");
        PoseRequire(PoseSupplementalWitness() == supplementalBefore, label + " repeated draw changed combat/source-input/RNG witness");
        PoseRequire(Scene.CheckRenderInvariants() && GL.GetError() == OpenTK.Graphics.OpenGL.ErrorCode.NoError, label + " repeat GL invariant");
        _poseRepeatedChecks++;
    }
    private byte[] PosePixels() => Scene.ReadSceneTarget(out _, out _)
        ?? throw new InvalidDataException("Display pose: scene readback unavailable");
    private static int PoseDifferent(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) throw new InvalidDataException("Display pose readback dimensions changed");
        int count = 0;
        for (int i = 0; i < a.Length; i += 3)
            if (a[i] != b[i] || a[i + 1] != b[i + 1] || a[i + 2] != b[i + 2]) count++;
        return count;
    }
    private object PoseImage(string name, byte[] pixels)
    {
        string path = Path.Combine(_shots!, name + ".png");
        PoseRequire(!File.Exists(path) && ScreenCapture.Save(Scene, path), "PNG readback failed or would overwrite evidence");
        return new { file = Path.GetFileName(path), sha256 = PoseDigest(File.ReadAllBytes(path)),
            pixelSha256 = PoseDigest(pixels), width = Scene.RenderSize.X, height = Scene.RenderSize.Y };
    }
    private void CaptureDisplayPose(string label, int tick)
    {
        string transactionEntryProjection = PoseProjection();
        string transactionEntrySupplemental = PoseSupplementalWitness();
        var transactionEntryFields = PosePresentationFields();
        string prefix = tick + "-" + label;
        DrawPoseRepeated(prefix + "-first-person");
        byte[] first = PosePixels(); var firstImage = PoseImage(prefix + "-first-person", first);
        DrawPoseRepeated(prefix + "-first-person-repeat");
        PoseRequire(first.SequenceEqual(PosePixels()), prefix + " unchanged first-person frame changed pixels");
        object? firstPersonGunPixels = null, chargeEffectPixels = null;
        if (_posePixels && tick == 300)
        {
            firstPersonGunPixels = CaptureFirstPersonGunPixels(prefix);
            _poseGunPixelsVerified = true;
        }
        if (_posePixels && tick == 420)
        {
            chargeEffectPixels = CaptureChargeEffectPixels(prefix);
            _poseChargePixelsVerified = true;
        }
        object worldDepth = CaptureWorldDepth(tick);
        PoseRequire(ScreenCapture.SaveWindow(Scene, Path.Combine(_shots!, prefix + "-hud.png")), "HUD readback failed");
        var target = Scene.Players.Items[1];
        bool eligible = target.Health > 0 && target.LoadFlags.TestFlag(LoadFlags.Spawned)
            && !target.Flags2.TestFlag(PlayerFlags2.HideModel);
        object third = new { status = "NOT_APPLICABLE_CANONICAL_HIDDEN_OR_DEAD" };
        object?[] saved = PoseCameraFields.Select(n => PoseGet(Scene, n)).ToArray();
        bool all = Scene.ShowAllNodes;
        PlayerFlags2 flags = target.Flags2;
        try
        {
            Scene.ShowAllNodes = true;
            Vector3 aim = target.Position + Vector3.UnitY * (target.IsAltForm ? .3f : .9f);
            Vector3 eye = ClearPoseCamera(aim);
            PoseSet(Scene, "_cameraMode", CameraMode.Roam); PoseSet(Scene, "_freeCam", true);
            PoseSet(Scene, "_cameraPosition", eye);
            Vector3 facing = (aim - eye).Normalized(), right = Vector3.Cross(facing, Vector3.UnitY).Normalized();
            PoseSet(Scene, "_cameraFacing", facing); PoseSet(Scene, "_cameraRight", right);
            PoseSet(Scene, "_cameraUp", Vector3.Cross(right, facing));
            PoseSet(Scene, "_cameraFov", MathHelper.DegreesToRadians(78));
            PoseSet(Scene, "_viewModelFov", MathHelper.DegreesToRadians(78));
            Scene.OnDrawFrame(); PoseRequire(Scene.OnRenderFrame(), "external camera renderer stopped");
            DrawPoseRepeated(prefix + "-third-person");
            byte[] enabled = PosePixels(); object image = PoseImage(prefix + "-third-person", enabled);
            if (eligible)
            {
                // HideModel suppresses the native body after DrawShadow, so
                // the positive-control difference cannot pass on shadow alone.
                PoseSet(target, nameof(PlayerEntity.Flags2), target.Flags2 | PlayerFlags2.HideModel);
                Scene.OnDrawFrame(); PoseRequire(Scene.OnRenderFrame(), "hidden-body renderer stopped");
                byte[] hidden = PosePixels(); object hiddenImage = PoseImage(prefix + "-body-hidden", hidden);
                PoseSet(target, nameof(PlayerEntity.Flags2), flags);
                Scene.OnDrawFrame(); PoseRequire(Scene.OnRenderFrame(), "restored-body renderer stopped");
                int changed = PoseDifferent(enabled, hidden), unstable = PoseDifferent(enabled, PosePixels());
                PoseRequire(changed > 0 && unstable == 0, prefix + " body vanished or same-frame pixels were unstable");
                third = new { status = "PASS", changedBodyPixels = changed, unstablePixels = unstable,
                    camera = PoseVector(eye), image, hiddenImage };
            }
            else third = new { status = "NOT_APPLICABLE_CANONICAL_HIDDEN_OR_DEAD", image };
        }
        finally
        {
            PoseSet(target, nameof(PlayerEntity.Flags2), flags); Scene.ShowAllNodes = all;
            for (int i = 0; i < PoseCameraFields.Length; i++) PoseSet(Scene, PoseCameraFields[i], saved[i]!);
            Scene.OnDrawFrame(); PoseRequire(Scene.OnRenderFrame(), "restored first-person renderer stopped");
            string transactionExitProjection = PoseProjection();
            string transactionExitSupplemental = PoseSupplementalWitness();
            var transactionExitFields = PosePresentationFields();
            _poseCameraTransactions.Add(new { label, tick, outputFrame = _poseOutput,
                entryProjection = transactionEntryProjection, exitProjection = transactionExitProjection,
                exactEstablishedProjectionMatch = transactionEntryProjection == transactionExitProjection,
                supplementalEntry = transactionEntrySupplemental, supplementalExit = transactionExitSupplemental,
                differences = transactionEntryFields.Keys.Union(transactionExitFields.Keys)
                    .Where(key => transactionEntryFields.GetValueOrDefault(key) != transactionExitFields.GetValueOrDefault(key))
                    .Select(key => new { field = key, before = transactionEntryFields.GetValueOrDefault(key),
                        after = transactionExitFields.GetValueOrDefault(key) }).ToArray() });
            PoseRequire(transactionEntryProjection == transactionExitProjection,
                prefix + " camera transaction changed established canonical simulation/replay projection");
            PoseRequire(transactionEntrySupplemental == transactionExitSupplemental,
                prefix + " camera transaction changed combat/source-input/RNG witness");
        }
        _posePictures.Add(new { label, tick, outputFrame = _poseOutput, alpha = FrameTiming.PresentationAlpha,
            hunter = target.Hunter.ToString(), targetHealth = target.Health, alt = target.IsAltForm,
            zoomed = Scene.Players.Main.EquipInfo.Zoomed, firstImage, worldDepth, third,
            firstPersonGunPixels, chargeEffectPixels });
    }
    private Vector3 ClearPoseCamera(Vector3 target)
    {
        foreach (Vector3 direction in new[] { Vector3.UnitZ, -Vector3.UnitZ, Vector3.UnitX, -Vector3.UnitX })
        {
            Vector3 candidate = target + direction * 4 + Vector3.UnitY * .4f;
            CollisionResult result = default;
            if (!CollisionDetection.CheckBetweenPoints(target, candidate, TestFlags.Beams, Scene, ref result)) return candidate;
        }
        throw new InvalidDataException("Display pose: no collision-clear external body camera");
    }
    private static unsafe int? PosePrimaryDisplayHz()
    {
        var monitor = GLFW.GetPrimaryMonitor();
        if (monitor == null) return null;
        var mode = GLFW.GetVideoMode(monitor);
        return mode == null ? null : mode->RefreshRate;
    }
    internal object DisplayPoseResult() => new
    {
        schema = 1, mode = "full-world-display-pose", platform = OperatingSystem.IsWindows() ? "Windows" : "Mac", scenarioId = _poseScenario,
        status = Complete ? "PASS" : "FAIL_OR_INCOMPLETE", complete = Complete,
        room = _room, hunter = Scene.Players.Main.Hunter.ToString(), requestedOutputScheduleHz = _cap,
        scheduleId = _poseScheduleId, scheduledElapsedSeconds = _poseScheduleId == "steady" ? (double?)(1.0 / _cap) : null,
        simulationHz = 60, sampleScope = "normal RenderWindow FrameTiming loop with deterministic elapsed; existing scripted controls; native world draws",
        primaryDisplayRefreshHz = PosePrimaryDisplayHz(), panelIdentityScope = "GLFW primary monitor mode; actual panel scanout not measured",
        panelScanoutAcceptance = "NOT_CLAIMED", performanceAcceptance = "NOT_CLAIMED_READBACK_FIXTURE",
        actualWallSeconds = _wall.Elapsed.TotalSeconds, outputFrames = _poseOutput,
        assemblyMvid = typeof(Scene).Module.ModuleVersionId, vendor = _glVendor, renderer = _glRenderer, glVersion = _glVersion,
        framebuffer = new[] { FramebufferSize.X, FramebufferSize.Y }, renderSize = new[] { Scene.RenderSize.X, Scene.RenderSize.Y },
        responsiveCamera = Features.ResponsiveAimCamera, fixedWeapon = Features.FixedWeapon,
        requestedFieldOfView = _poseRequestedFov, selectedFieldOfView = _poseSelectedFieldOfView,
        syntheticInput = true, physicalControllerRejected = false,
        physicalControllerIsolation = new { connectedBefore = _posePhysicalControllerConnectedBefore,
            scopedDisconnected = _poseControllerIsolationVerified, fixedAndPresentationPollingBlocked = _poseControllerIsolationVerified,
            physicalDeviceAcceptance = "NOT_CLAIMED" }, osFocusAcceptance = "NOT_CLAIMED",
        focusControl = new { mode = _poseFocusControl, injectionTick = 245,
            injected = _poseFocusInjected, qualificationEligible = _poseFocusControl != "legacy",
            events = _poseFocusEvents },
        canonicalWitness = "unchanged production ReplayStateHash/schema4 + Scene.ReplayPresentationHash only",
        supplementalWitness = "fixture-local exact combat/source-input/scene-and-global-RNG; no archival schema changes",
        cameraControlAcceptance = "STRICT_ENTRY_EXIT_CANONICAL_REQUIRED",
        pixelControlAcceptance = _posePixels ? "ISOLATED_NATIVE_GUN_AND_CHARGE_REQUIRED" : "NOT_REQUESTED",
        pixelControlDiagnostics = _posePixelDiagnostics,
        beamSampling = new { draws = _poseBeamDraws, sampled = _poseBeamSamples },
        particleSampling = new { samples = _poseParticleSamples, motionSamples = _poseParticleMotionSamples,
            appearanceSamples = _poseParticleAppearanceSamples, examples = _poseParticleSampleExamples,
            scope = "values consumed by actual native draws; current at 60, completed-history sampling above 60" },
        drawAnchorDiagnosticScope = "raw model/ice/Kanden/effect/CameraInfo observations only; neither canonical/cross-rate nor different-alpha equality gate",
        replayPlaybackAcceptance = "NOT_RUN_EXISTING_PROJECTIONS_ONLY", canonicalTolerances = "EXACT_NO_RELAXATION",
        zeroStepCanonicalChecks = _poseZeroStepChecks, repeatedDrawCanonicalChecks = _poseRepeatedChecks, sampledActorCarryFrames = _poseCarryFrames,
        coverage = new { died = _poseDied, respawned = _poseRespawned, alt = _poseAlt, altAttack = _poseAttack,
            scope = _poseScope, charge = _poseCharge, effects = _poseEffect, chargeEffectAttached = _poseChargeEffect,
            scopeFovMin = float.IsFinite(_poseScopeMin) ? (float?)_poseScopeMin : null,
            scopeFovMax = float.IsFinite(_poseScopeMax) ? (float?)_poseScopeMax : null, clockReset = _poseReset },
        inputTimelineSha256 = PoseDigest(JsonSerializer.SerializeToUtf8Bytes(_poseInputs)),
        schedule = _poseSchedule, steps = _poseSteps, pictures = _posePictures, lifetimeFences = _poseFences,
        drawAnchorDiagnostics = _poseDrawDiagnostics, canonicalFailure = _poseCanonicalFailure,
        cameraControlTransactions = _poseCameraTransactions
    };
}
