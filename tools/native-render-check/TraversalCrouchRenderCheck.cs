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
    private void ObserveCrouchFocus(bool focused, string source) => _crouchFocusEvents.Add(new
    {
        focused, source, sceneFrame = Scene?.FrameCount, stage = _crouchStage,
        wallSeconds = _wall.Elapsed.TotalSeconds
    });
    private KeyboardState _crouchKeyboard = null!;
    private Action<KeyboardState, Keys, bool> _crouchKey = null!;
    private ulong? _crouchSpawnFrame;
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
        // Reuse the diagnostic's existing one-step-per-picture capture owner.
        // This run produces image evidence and never reports timing acceptance.
        CrouchSet(Scene, "_frameAdvanceOn", true); CrouchSet(Scene, "_advanceOneFrame", true);
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
        else if (_crouchStage == 1 && Scene.FrameCount - _crouchStageFrame >= 8)
        {
            CrouchRequire(player.Controls.Crouch.IsDown && (bool)CrouchGet(player, "ModCrouched")!,
                "production Ctrl hold earns the captured crouched stance");
            _crouchLow = CaptureCrouchSilhouette("crouched");
            CrouchRequire(_crouchLow.Height < _crouchStanding!.Height - 2,
                "native rendered crouch silhouette must be shorter than standing");
            _crouchStage = 2; _crouchStageFrame = Scene.FrameCount;
        }
        else if (_crouchStage == 2 && Scene.FrameCount - _crouchStageFrame >= 8)
        {
            CrouchRequire(!player.Controls.Crouch.IsDown && !(bool)CrouchGet(player, "ModCrouched")!,
                "released Ctrl permits native standing clearance");
            _crouchReleased = CaptureCrouchSilhouette("released-standing");
            CrouchRequire(_crouchReleased.Height > _crouchLow!.Height + 2,
                "released native renderer restores standing silhouette height");
            Complete = true; Close();
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

    private Silhouette CaptureCrouchSilhouette(string label)
    {
        PlayerEntity player = Scene.Players.Main; ulong frame = Scene.FrameCount;
        Scene.OnDrawFrame(); CrouchRequire(Scene.OnRenderFrame(), "external native body view initializes");
        PlayerFlags2 flags = player.Flags2;
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
        CrouchRequire(frame == Scene.FrameCount && flags == player.Flags2,
            "frozen body control restores flags without advancing simulation");
        CrouchRequire(enabled.Pixels.SequenceEqual(repeat.Pixels), "same-frame body readback is stable");
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
        _crouchPictures.Add(new { label, simulationFrame = frame, hunter = player.Hunter.ToString(),
            ctrlHeld = player.Controls.Crouch.IsDown, crouched = CrouchGet(player, "ModCrouched"),
            collisionCrouched = CrouchGet(player, "ModCollisionCrouched"), actualPosition = CrouchVector(player.Position),
            bottom = CrouchGet(player, "ModBipedBottom"), top = CrouchGet(player, "ModBipedTop"),
            silhouette, enabled = enabled.Metadata, hidden = hidden.Metadata, repeat = repeat.Metadata,
            unstablePixels = 0, camera = CrouchVector(_crouchCamera) });
        return silhouette;
    }

    internal object CrouchRenderResult() => new { schema = 1, status = Complete ? "PASS" : "FAIL",
        scope = "Windows native renderer and actual Traversal Ctrl input; native spawn and intro skip; fixed external camera and frozen render-only body-hidden controls; no actor position/stance/pose/velocity writes; image capture only; OS focus observed with isolated frozen synthetic input, no OS-focus, frame timing or physical-device qualification",
        focusPolicy = "OS focus callbacks are recorded without invoking the production focus-loss handler; only this diagnostic owns the frozen synthetic keyboard",
        focusEvents = _crouchFocusEvents,
        model = Scene.GameState.MovementModel.ToString(), hunter = Scene.Players.Main.Hunter.ToString(),
        standing = _crouchStanding, crouched = _crouchLow, released = _crouchReleased,
        glVendor = _glVendor, glRenderer = _glRenderer, glVersion = _glVersion,
        clientMvid = typeof(Scene).Module.ModuleVersionId, room = _room, pictures = _crouchPictures };
}
