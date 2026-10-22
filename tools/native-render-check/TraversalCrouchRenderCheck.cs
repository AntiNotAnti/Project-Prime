using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using MphRead;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.Movement;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

// Narrow Windows GL acceptance inside the existing native RenderWindow loop.
// Only camera/input bindings and frozen render-only HideModel controls are fixture
// seams. No actor position, pose matrix, stance, velocity or jump state is written.
sealed partial class CheckWindow
{
    private const BindingFlags CrouchPrivate = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly bool _crouchCheck;
    private readonly List<object> _crouchPictures = new();
    private readonly List<object> _crouchFocusEvents = new();
    private readonly List<TransitionSample> _crouchTransitionSamples = new();
    private readonly HashSet<(int Stage, ulong Ticks)> _crouchTransitionCaptures = new();
    private sealed record TransitionSample(int Stage, int CompletedTicks, float VisualAmount,
        float CanonicalEyeOffset, int BodyHeight);
    private void ObserveCrouchFocus(bool focused, string source) => _crouchFocusEvents.Add(new
    {
        focused, source, sceneFrame = Scene?.FrameCount, stage = _crouchStage,
        wallSeconds = _wall.Elapsed.TotalSeconds
    });
    private KeyboardState _crouchKeyboard = null!;
    private Action<KeyboardState, Keys, bool> _crouchKey = null!;
    private ulong? _crouchSpawnFrame;
    private long? _crouchContinuousGeneration;
    private static readonly PropertyInfo CrouchGeneration = typeof(FrameTiming).GetProperty(
        "PresentationGeneration", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
    private static long CrouchPresentationGeneration => (long)CrouchGeneration.GetValue(null)!;
    private ulong _crouchStageFrame;
    private int _crouchStage;
    private Vector3 _crouchActorOrigin, _crouchCamera, _crouchAim;
    private Silhouette? _crouchStanding, _crouchLow, _crouchReleased;
    private sealed record Silhouette(int Pixels, int Left, int Bottom, int Right, int Top)
    { public int Height => Top - Bottom + 1; }
    private static object? CrouchGet(object value, string name)
        => value.GetType().GetProperty(name, CrouchPrivate)?.GetValue(value)
            ?? value.GetType().GetField(name, CrouchPrivate)?.GetValue(value);
    private static void CrouchSet(object value, string name, object state)
    {
        if (value.GetType().GetField(name, CrouchPrivate) is { } field) field.SetValue(value, state);
        else (value.GetType().GetProperty(name, CrouchPrivate) ?? throw new MissingMemberException(name)).SetValue(value, state);
    }
    private static void CrouchRequire(bool good, string label)
    { if (!good) throw new InvalidDataException("Crouch GL: " + label); }
    private static float[] CrouchVector(Vector3 value) => new[] { value.X, value.Y, value.Z };

    private void InitializeCrouchRenderCheck()
    {
        CrouchRequire(OperatingSystem.IsWindows() && !Headless.Active && !NetSession.Active && !DemoPlayback.IsActive,
            "native Windows offline renderer required");
        CrouchRequire(_shots != null && !Directory.Exists(_shots), "choose a fresh PNG directory");
        Directory.CreateDirectory(_shots!);
        CrouchRequire(Scene.GameState.MovementModel == MovementModel.Traversal && Scene.Players.PlayerCount == 1,
            "explicit Traversal match and one genuine local Hunter");
        PlayerEntity player = Scene.Players.Main;
        CrouchRequire(!player.IsBot, "local keyboard owner must not be a bot");
        foreach (var bind in player.Controls.All) { bind.Type = ButtonType.Key; bind.Key = Keys.Unknown; }
        player.Controls.Shoot.Key = Keys.Enter; player.Controls.Jump.Key = Keys.Space;
        player.Controls.Crouch.Key = Keys.LeftControl;
        player.Controls.MouseAim = false; player.Controls.KeyboardAim = true;
        _crouchKeyboard = (KeyboardState)CrouchGet(Scene, "_keyboardState")!;
        _crouchKey = typeof(KeyboardState).GetMethod("SetKeyState", CrouchPrivate)!
            .CreateDelegate<Action<KeyboardState, Keys, bool>>();
        ObserveCrouchFocus(IsFocused, "initial-observation");
        RenderOptions.ShowCustomCosmetics = false;
        Title = "Traversal crouch renderer check";
    }

    private void BeforeCrouchRenderFrame()
    {
        CrouchRequire(_wall.Elapsed.TotalSeconds < 120, "capture wall timeout");
        CrouchRequire(!GamepadManager.ActiveState.Connected && !NetSession.Active,
            "synthetic local keyboard remains the sole gameplay input source");
        GamepadContexts.Focused = true;
        var inputMode = typeof(Scene).GetField("_inputMode", CrouchPrivate)!;
        inputMode.SetValue(Scene, Enum.Parse(inputMode.FieldType, "All"));
        _crouchKey(_crouchKeyboard, Keys.Enter, !Scene.Players.Main.LoadFlags.TestFlag(LoadFlags.Spawned));
        _crouchKey(_crouchKeyboard, Keys.LeftControl, _crouchStage == 1);
        // Use RenderWindow's continuous FrameTiming.Advance branch. Debug
        // single-step resets the presentation generation every picture and must
        // retain its production snap fence, rather than qualify a transition.
        CrouchRequire(!Scene.FrameAdvance, "continuous native scheduler required; debug single-step is inapplicable");
        if (_crouchStanding != null) SetCrouchCamera();
    }

    private void AfterCrouchRenderFrame()
    {
        PlayerEntity player = Scene.Players.Main;
        if (!player.LoadFlags.TestFlag(LoadFlags.Spawned)) return;
        if (_crouchSpawnFrame == null)
        {
            _crouchSpawnFrame = Scene.FrameCount;
            Scene.CameraSequences.Current?.End();
            return;
        }
        if (Scene.FrameCount - _crouchSpawnFrame.Value < 120) return;
        CrouchRequire(player.Health > 0 && !player.IsAltForm && !player.IsMorphing && !player.IsUnmorphing,
            "native living biped remains in play");
        CrouchRequire((bool)CrouchGet(player, "ModMomentumEligible")!, "actual admitted local rule owns movement");
        if (_crouchStage == 0)
        {
            _crouchActorOrigin = player.Position;
            ChooseCrouchCamera(); SetCrouchCamera();
            CrouchRequire(!player.Controls.Crouch.IsDown && !(bool)CrouchGet(player, "ModCrouched")!,
                "standing capture precedes any Ctrl input");
            _crouchStanding = CaptureCrouchSilhouette("standing");
            _crouchStage = 1; _crouchStageFrame = Scene.FrameCount;
        }
        else if (_crouchStage is 1 or 2)
        {
            ulong completedTicks = Scene.FrameCount - _crouchStageFrame;
            CrouchRequire(completedTicks <= 8, "transition capture must not skip its completed-tick endpoint");
            if (completedTicks is 2 or 4 or 6 or 8
                && _crouchTransitionCaptures.Add((_crouchStage, completedTicks)))
            {
                bool entering = _crouchStage == 1;
                CrouchRequire(player.Controls.Crouch.IsDown == entering
                    && (bool)CrouchGet(player, "ModCrouched")! == entering,
                    entering ? "production Ctrl hold earns each captured crouched stance"
                        : "released Ctrl permits native standing clearance at each transition capture");
                string label = entering ? "crouched" : "released-standing";
                if (completedTicks != 8) label += "-tick-" + completedTicks;
                Silhouette picture = CaptureCrouchSilhouette(label, checked((int)completedTicks));
                if (completedTicks == 8 && entering)
                {
                    _crouchLow = picture;
                    CrouchRequire(_crouchLow.Height < _crouchStanding!.Height - 2,
                        "native rendered crouch silhouette must be shorter than standing");
                    _crouchStage = 2; _crouchStageFrame = Scene.FrameCount;
                }
                else if (completedTicks == 8)
                {
                    _crouchReleased = picture;
                    CrouchRequire(_crouchReleased.Height > _crouchLow!.Height + 2,
                        "released native renderer restores standing silhouette height");
                    CheckCrouchTransitionProgress();
                    Complete = true; Close();
                }
            }
        }
        CrouchRequire((player.Position - _crouchActorOrigin).Length < .005f,
            "the stationary actor was not repositioned or moved between stance captures");
        if (GL.GetError() is var error && error != OpenTK.Graphics.OpenGL.ErrorCode.NoError)
            throw new InvalidOperationException("Crouch GL error: " + error);
    }

    private void ChooseCrouchCamera()
    {
        PlayerEntity player = Scene.Players.Main;
        _crouchAim = player.Position + Vector3.UnitY * .3f;
        Vector3 forward = player.FacingVector.WithY(0).Normalized();
        Vector3 right = Vector3.Cross(forward, Vector3.UnitY).Normalized();
        foreach (Vector3 direction in new[] { (forward + right).Normalized(), (forward - right).Normalized(),
            right, -right, forward, -forward })
        {
            Vector3 candidate = _crouchAim + direction * 4 + Vector3.UnitY * .25f;
            CollisionResult hit = default;
            if (CollisionDetection.CheckBetweenPoints(_crouchAim, candidate, TestFlags.Beams, Scene, ref hit)) continue;
            _crouchCamera = candidate; return;
        }
        throw new InvalidDataException("Crouch GL: no clear external view of the native spawn; choose another room.");
    }

    private void SetCrouchCamera()
    {
        Scene.SetFreeCamera(true); Scene.ShowAllNodes = true;
        Vector3 forward = (_crouchAim - _crouchCamera).Normalized();
        Vector3 right = Vector3.Cross(forward, Vector3.UnitY).Normalized();
        CrouchSet(Scene, "_cameraPosition", _crouchCamera); CrouchSet(Scene, "_cameraFacing", forward);
        CrouchSet(Scene, "_cameraRight", right); CrouchSet(Scene, "_cameraUp", Vector3.Cross(right, forward));
        CrouchSet(Scene, "_cameraFov", MathHelper.DegreesToRadians(60));
        CrouchSet(Scene, "_viewModelFov", MathHelper.DegreesToRadians(60));
        var mode = typeof(Scene).GetField("_inputMode", CrouchPrivate)!;
        mode.SetValue(Scene, Enum.Parse(mode.FieldType, "All"));
    }

    private Silhouette CaptureCrouchSilhouette(string label, int completedTicks = 0)
    {
        PlayerEntity player = Scene.Players.Main; ulong frame = Scene.FrameCount;
        long generation = CrouchPresentationGeneration;
        CrouchRequire(FrameTiming.Active && !Scene.FrameAdvance, "capture must follow the native continuous scheduler");
        if (_crouchContinuousGeneration.HasValue)
            CrouchRequire(generation == _crouchContinuousGeneration.Value,
                "presentation generation changed during the measured transition; production reset fence remains authoritative");
        else _crouchContinuousGeneration = generation;
        Scene.OnDrawFrame(); CrouchRequire(Scene.OnRenderFrame(), "external native body view initializes");
        PlayerFlags2 flags = player.Flags2;
        float visualAmount = (float)CrouchGet(player, "ModCrouchVisualAmount")!;
        float canonicalEye = (float)CrouchGet(player, "ModBipedEyeOffset")!;
        CrouchRequire(float.IsFinite(visualAmount) && visualAmount is >= 0 and <= 1
            && float.IsFinite(canonicalEye), "finite visual blend and canonical eye samples");
        (byte[] Pixels, int Width, int Height, object Metadata) Capture(string suffix)
        {
            Scene.OnDrawFrame(); CrouchRequire(Scene.OnRenderFrame(), "native renderer stopped");
            byte[] pixels = Scene.ReadSceneTarget(out int width, out int height)
                ?? throw new InvalidDataException("Crouch GL readback unavailable");
            CrouchRequire(width > 0 && height > 0 && pixels.Length == width * height * 3, "valid RGB framebuffer");
            string file = Path.Combine(_shots!, label + suffix + ".png");
            CrouchRequire(!File.Exists(file) && ScreenCapture.Save(Scene, file), "fresh PNG readback failed");
            return (pixels, width, height, new { file = Path.GetFileName(file), width, height,
                rgbSha256 = Convert.ToHexString(SHA256.HashData(pixels)),
                pngSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))) });
        }
        var enabled = Capture("");
        (byte[] Pixels, int Width, int Height, object Metadata) hidden;
        try
        {
            // Frozen render-only negative control isolates actual body submission;
            // it never crosses a simulation step or changes physical stance.
            CrouchSet(player, nameof(PlayerEntity.Flags2), flags | PlayerFlags2.HideModel);
            hidden = Capture("-body-hidden");
        }
        finally { CrouchSet(player, nameof(PlayerEntity.Flags2), flags); }
        var repeat = Capture("-repeat");
        CrouchRequire(generation == CrouchPresentationGeneration, "frozen extra draws retain the native presentation generation");
        CrouchRequire(frame == Scene.FrameCount && flags == player.Flags2,
            "frozen body control restores flags without advancing simulation");
        CrouchRequire(enabled.Pixels.SequenceEqual(repeat.Pixels), "same-frame body readback is stable");
        CrouchRequire(visualAmount == (float)CrouchGet(player, "ModCrouchVisualAmount")!
            && canonicalEye == (float)CrouchGet(player, "ModBipedEyeOffset")!,
            "frozen extra draws cannot advance the visual transition or canonical eye");
        int left = enabled.Width, bottom = enabled.Height, right = -1, top = -1, count = 0;
        for (int pixel = 0; pixel < enabled.Width * enabled.Height; pixel++)
        {
            int index = pixel * 3;
            if (Math.Abs(enabled.Pixels[index] - hidden.Pixels[index]) <= 2
                && Math.Abs(enabled.Pixels[index + 1] - hidden.Pixels[index + 1]) <= 2
                && Math.Abs(enabled.Pixels[index + 2] - hidden.Pixels[index + 2]) <= 2) continue;
            int x = pixel % enabled.Width, y = pixel / enabled.Width;
            left = Math.Min(left, x); right = Math.Max(right, x);
            bottom = Math.Min(bottom, y); top = Math.Max(top, y); count++;
        }
        CrouchRequire(count >= 20, "positive native Hunter body pixels are required");
        var silhouette = new Silhouette(count, left, bottom, right, top);
        _crouchTransitionSamples.Add(new(_crouchStage, completedTicks, visualAmount, canonicalEye, silhouette.Height));
        _crouchPictures.Add(new { label, completedTicks, transitionStage = _crouchStage,
            presentationGeneration = generation, clockActive = FrameTiming.Active,
            schedulerStepsThisFrame = FrameTiming.StepsThisFrame, schedulerTotalSteps = FrameTiming.TotalSteps,
            debugFrameAdvance = Scene.FrameAdvance,
            visualAmount, canonicalEyeOffset = canonicalEye, nativeCameraPosition = CrouchVector(player.CameraInfo.Position),
            simulationFrame = frame, hunter = player.Hunter.ToString(),
            ctrlHeld = player.Controls.Crouch.IsDown, crouched = CrouchGet(player, "ModCrouched"),
            collisionCrouched = CrouchGet(player, "ModCollisionCrouched"), actualPosition = CrouchVector(player.Position),
            bottom = CrouchGet(player, "ModBipedBottom"), top = CrouchGet(player, "ModBipedTop"),
            silhouette, enabled = enabled.Metadata, hidden = hidden.Metadata, repeat = repeat.Metadata,
            unstablePixels = 0, camera = CrouchVector(_crouchCamera) });
        return silhouette;
    }

    private int CrouchBodyJitterPixels => Math.Max(3, (int)Math.Ceiling((_crouchStanding?.Height ?? 0) * .02));

    private void CheckCrouchTransitionProgress()
    {
        TransitionSample[] entering = _crouchTransitionSamples.Where(sample => sample.Stage == 1)
            .OrderBy(sample => sample.CompletedTicks).ToArray();
        TransitionSample[] released = _crouchTransitionSamples.Where(sample => sample.Stage == 2)
            .OrderBy(sample => sample.CompletedTicks).ToArray();
        int[] expectedTicks = { 2, 4, 6, 8 };
        CrouchRequire(entering.Select(sample => sample.CompletedTicks).SequenceEqual(expectedTicks)
            && released.Select(sample => sample.CompletedTicks).SequenceEqual(expectedTicks),
            "both phases require completed 2/4/6 tick samples and their existing 8 tick endpoints");
        CrouchRequire(entering[0].VisualAmount > 0 && entering[0].VisualAmount < entering[1].VisualAmount
            && entering[1].VisualAmount < 1 && MathF.Abs(entering[2].VisualAmount - 1) <= .00001f
            && MathF.Abs(entering[3].VisualAmount - 1) <= .00001f,
            "held crouch visual blend progresses through intermediate values and completes after six ticks");
        CrouchRequire(released[0].VisualAmount < 1 && released[0].VisualAmount > released[1].VisualAmount
            && released[1].VisualAmount > 0 && MathF.Abs(released[2].VisualAmount) <= .00001f
            && MathF.Abs(released[3].VisualAmount) <= .00001f,
            "released crouch visual blend reverses through intermediate values and completes after six ticks");
        int previousHeight = _crouchStanding!.Height;
        foreach (TransitionSample sample in entering)
        {
            CrouchRequire(sample.BodyHeight <= previousHeight + CrouchBodyJitterPixels,
                "rendered body height decreases through crouch, allowing declared native idle-animation pixel jitter");
            CrouchRequire(sample.CanonicalEyeOffset == entering[^1].CanonicalEyeOffset,
                "canonical eye stays exactly crouched while the rendered pose blends");
            previousHeight = sample.BodyHeight;
        }
        previousHeight = _crouchLow!.Height;
        foreach (TransitionSample sample in released)
        {
            CrouchRequire(sample.BodyHeight + CrouchBodyJitterPixels >= previousHeight,
                "rendered body height increases through release, allowing declared native idle-animation pixel jitter");
            CrouchRequire(sample.CanonicalEyeOffset == _crouchTransitionSamples[0].CanonicalEyeOffset,
                "canonical eye returns exactly to standing while the rendered pose blends");
            previousHeight = sample.BodyHeight;
        }
        CrouchRequire(entering[0].BodyHeight > _crouchLow.Height + 2
            && released[0].BodyHeight < _crouchReleased!.Height - 2,
            "intermediate rendered silhouettes must differ from their final pose rather than snap immediately");
    }

    internal object CrouchRenderResult() => new { schema = 1, status = Complete ? "PASS" : "FAIL",
        scope = "Windows native renderer and actual Traversal Ctrl input; native spawn and intro skip; fixed external camera and frozen render-only body-hidden controls; no actor position/stance/pose/velocity writes; image capture only; OS focus observed with isolated frozen synthetic input, no OS-focus, frame timing or physical-device qualification",
        focusPolicy = "OS focus callbacks are recorded without invoking the production focus-loss handler; only this diagnostic owns the frozen synthetic keyboard",
        focusEvents = _crouchFocusEvents,
        scheduler = "native continuous RenderWindow FrameTiming.Advance at configured 60 Hz; no debug single-step or generation override",
        continuousPresentationGeneration = _crouchContinuousGeneration,
        model = Scene.GameState.MovementModel.ToString(), hunter = Scene.Players.Main.Hunter.ToString(),
        standing = _crouchStanding, crouched = _crouchLow, released = _crouchReleased,
        transitionCompletedTicks = new[] { 2, 4, 6, 8 }, bodyHeightJitterPixels = CrouchBodyJitterPixels,
        transitionSamples = _crouchTransitionSamples,
        glVendor = _glVendor, glRenderer = _glRenderer, glVersion = _glVersion,
        clientMvid = typeof(Scene).Module.ModuleVersionId, room = _room, pictures = _crouchPictures };
}
