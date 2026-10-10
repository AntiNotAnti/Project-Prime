using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;

if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsWindows())
    throw new PlatformNotSupportedException("This QA campaign permits only macOS and Windows.");
// The client may start a preview worker when an end panel opens. This entry
// point is a diagnostic, so reject that command rather than recursively
// starting another diagnostic with its default room.
if (args.Contains("-thumbnail")) { Console.WriteLine("NATIVERENDER: thumbnail workers require the client entry point."); return 2; }
string Value(string name, string fallback) => Array.IndexOf(args, name) is int i && i >= 0 && i + 1 < args.Length
    ? args[i + 1] : fallback;
string room = Value("-room", "UNIT4_RM1");
string output = Path.GetFullPath(Value("-output", "native-render-result.json"));
string? shots = args.Contains("-shots") ? Path.GetFullPath(Value("-shots", "native-render-shots")) : null;
int cap = int.Parse(Value("-hz", "60"));
int seconds = int.Parse(Value("-seconds", "24"));
int players = int.Parse(Value("-players", "8"));
Hunter hunter = Enum.Parse<Hunter>(Value("-hunter", "Samus"), true);
PlayerOutlineStyle outlines = Enum.Parse<PlayerOutlineStyle>(Value("-outlines", "Off"), true);
Vector3? VectorArg(string name)
{
    if (!args.Contains(name)) return null;
    var parts = Value(name, "").Split(',');
    if (parts.Length != 3) throw new ArgumentException(name + " requires x,y,z");
    return new Vector3(float.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
        float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture),
        float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture));
}
Vector3? camera = VectorArg("-camera"), facing = VectorArg("-facing");
if (args.Contains("-outlinecheck") && (players < 2 || outlines == PlayerOutlineStyle.Off || shots == null))
    throw new ArgumentException("-outlinecheck requires -players 2..8, enabled -outlines and -shots.");
if (players < 1 || players > 8) throw new ArgumentOutOfRangeException("Use -players 1..8.");
if ((cap != -1 && cap < 30) || cap > 500 || seconds < 8 || seconds > 120)
    throw new ArgumentOutOfRangeException("Use -hz -1 or 30..500, -seconds 8..120.");
ShadowQuality shadows = Enum.Parse<ShadowQuality>(Value("-shadows", "Off"), true);
string[] dimensions = Value("-size", "1920x1080").Split('x');
var size = new Vector2i(int.Parse(dimensions[0]), int.Parse(dimensions[1]));
if (size.X < 320 || size.Y < 180 || size.X > 4096 || size.Y > 2160)
    throw new ArgumentOutOfRangeException("Use -size 320x180 through 4096x2160.");
// Reuse the client's user-data/culture setup without exposing it as a runtime API.
typeof(Scene).Assembly.GetType("MphRead.ConsoleSetup")!.GetMethod("Run")!.Invoke(null, null);
// A standalone diagnostic must not run the client's thumbnail workers: their
// child command assumes ProjectPrime's entry point, not this tool's entry point.
GraphicsBackendPolicy.LoadPreference();
Paths.UpdatePaths(); Paths.ChooseMphPath(); Paths.ChooseFhPath();
MphRead.Mods.MapGen.CustomRooms.GenerateMissing(room);
using var window = new CheckWindow(room, seconds, cap, shadows, shots, size, players, args.Contains("-idle"), args.Contains("-windowcycle"), hunter, outlines, camera, facing, args.Contains("-outlinecheck"));
try
{
    window.Run();
    var result = JsonSerializer.SerializeToNode(window.Result())!;
    result["cleanup"] = JsonSerializer.SerializeToNode(window.CleanupAndInspect());
    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    File.WriteAllText(output, result.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
    Console.WriteLine("NATIVERENDER " + (window.Complete ? "PASS " : "FAIL ") + output);
    return window.Complete ? 0 : 1;
}
catch (Exception ex) { Console.WriteLine("NATIVERENDER FAIL " + ex); return 1; }
finally
{
    // Run's unload handles gameplay; explicitly release GL before its context dies.
    window.EndScene();
}

sealed class CheckWindow : RenderWindow
{
    private readonly int _seconds, _cap;
    private readonly ShadowQuality _shadows;
    private readonly string? _shots;
    private readonly List<double> _intervals = new();
    private readonly Stopwatch _wall = new();
    private long _last, _measureStarted, _allocated;
    private int[] _collections = new int[3];
    private bool _measuring;
    private int _capture;
    private readonly string _room;
    private readonly List<object> _captures = new();
    private readonly bool _windowCycle;
    private readonly PlayerOutlineStyle _outlines;
    private readonly Vector3? _camera, _facing;
    private readonly bool _outlineCheck;
    private object? _outlineEvidence;
    private readonly Vector2i _requestedPixels;
    private readonly List<object> _windowEvents = new();
    private int _windowStage;
    public bool Complete { get; private set; }

    internal CheckWindow(string room, int seconds, int cap, ShadowQuality shadows, string? shots, Vector2i pixels, int players, bool idle, bool windowCycle, Hunter hunter, PlayerOutlineStyle outlines, Vector3? camera, Vector3? facing, bool outlineCheck)
    {
        _room = room; _seconds = seconds; _cap = cap; _shadows = shadows; _shots = shots;
        _windowCycle = windowCycle; _requestedPixels = pixels;
        _outlines = outlines; _camera = camera; _facing = facing; _outlineCheck = outlineCheck;
        MinimumSize = new Vector2i(160, 90);
        double sx = FramebufferSize.X / (double)ClientSize.X, sy = FramebufferSize.Y / (double)ClientSize.Y;
        ClientSize = new Vector2i((int)Math.Round(pixels.X / sx), (int)Math.Round(pixels.Y / sy));
        Title = "Project Prime native renderer QA";
        // Frozen input prevents keyboard/mouse activity from changing the workload.
        typeof(Scene).GetField("_keyboardState", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(Scene, SyntheticInput.CreateKeyboard());
        typeof(Scene).GetField("_mouseState", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(Scene, SyntheticInput.CreateMouse());
        Scene.Random.SetRng1(12345); Scene.Random.SetRng2(98765);
        PlayerEntity.MaxPlayers = players;
        for (int i = 0; i < players; i++) AddPlayer(i == 0 ? hunter : (Hunter)(i % 7));
        PlayerEntity.PlayerCount = players;
        PlayerEntity.MainPlayerIndex = 0;
        foreach (var player in PlayerEntity.Players) { player.IsBot = true; player.BotLevel = 1; }
        if (idle) Scene.Players.Main.IsBot = false;
        typeof(MphRead.Mods.Network.MapAudit).GetProperty("ForceEveryone")!.SetValue(null, true);
        AddRoom(room, GameMode.Battle, playerCount: players);
    }

    protected override void OnLoad()
    {
        base.OnLoad();
        RenderOptions.ApplyGraphicsPreset(GraphicsPreset.Original);
        RenderOptions.Shadows = _shadows;
        RenderOptions.PlayerOutline = _outlines;
        ApplyCheckCamera();
        RenderOptions.ResolutionScale = 100; RenderOptions.ShowFps = false;
        RenderOptions.TextureFiltering = false; RenderOptions.TextureMipmaps = false;
        RenderOptions.TextureAnisotropy = 1;
        FrameTiming.FrameRateCap = _cap;
        Scene.GameState.PointGoal = 0; Scene.GameState.MatchTime = -1;
        FrameTiming.Reset(); FrameTiming.ResetDiagnostics();
        CursorState = CursorState.Normal;
        Console.WriteLine($"NATIVERENDER context room={_room} pixels={FramebufferSize} cap={_cap} shadows={_shadows}"
            + $" vendor={GL.GetString(StringName.Vendor)} renderer={GL.GetString(StringName.Renderer)} gl={GL.GetString(StringName.Version)}"
            + $" mvid={typeof(Scene).Module.ModuleVersionId:D}");
        _wall.Start();
    }

    private void ApplyCheckCamera()
    {
        if (_camera is Vector3 position)
        {
            Scene.SetFreeCamera(true);
            Vector3 forward = (_facing ?? -Vector3.UnitZ).Normalized();
            Vector3 right = Vector3.Cross(forward, Vector3.UnitY).Normalized();
            void Set(string name, Vector3 value) => typeof(Scene)
                .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Scene, value);
            Set("_cameraPosition", position); Set("_cameraFacing", forward);
            Set("_cameraRight", right); Set("_cameraUp", Vector3.Cross(right, forward));
        }
    }

    protected override void OnRenderFrame(FrameEventArgs args)
    {
        if (_windowCycle && Scene.FrameCount >= (ulong)(300 + _windowStage * 120) && _windowStage < 4)
        {
            // Real native window transitions, separate from benchmark runs.
            // Hiding then showing the owned window exercises focus loss/recovery;
            // this does not assert OS keyboard Alt-Tab behavior.
            switch (_windowStage)
            {
                case 0: WindowState = WindowState.Fullscreen; break;
                case 1:
                    WindowState = WindowState.Normal;
                    double scale = FramebufferSize.X / (double)ClientSize.X;
                    ClientSize = new Vector2i((int)Math.Round(_requestedPixels.X / scale), (int)Math.Round(_requestedPixels.Y / scale));
                    break;
                case 2: IsVisible = false; break;
                case 3: IsVisible = true; Focus(); break;
            }
            _windowEvents.Add(new { stage = _windowStage++, simulationFrame = Scene.FrameCount,
                state = WindowState.ToString(), visible = IsVisible, focused = IsFocused,
                framebuffer = new[] { FramebufferSize.X, FramebufferSize.Y } });
        }
        if (_shots != null)
        {
            // Capture-only runs step exactly once per picture, so paired arms
            // use identical simulation frames even if their draw costs differ.
            typeof(Scene).GetField("_frameAdvanceOn", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(Scene, true);
            typeof(Scene).GetField("_advanceOneFrame", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(Scene, true);
        }
        ApplyCheckCamera();
        // Actual client pacing, simulation, render and SwapBuffers all run here.
        base.OnRenderFrame(args);
        long now = Stopwatch.GetTimestamp();
        if (!_measuring && Scene.FrameCount >= 240)
        {
            _measuring = true; _measureStarted = now;
            _allocated = GC.GetTotalAllocatedBytes(false);
            _collections = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray();
            FrameTiming.ResetDiagnostics();
            Console.WriteLine("NATIVERENDER measurement-start (240 simulation steps warmup excluded)");
        }
        else if (_measuring && _last != 0)
            _intervals.Add(Stopwatch.GetElapsedTime(_last, now).TotalMilliseconds);
        _last = now;
        if (_shots != null && _capture < 4 && Scene.FrameCount >= (ulong)(240 + _capture * 300))
        {
            // Capture runs are separate from performance runs. A fresh draw places
            // HUD pixels in the unswapped back buffer, as SaveWindow requires.
            Scene.OnDrawFrame(); Scene.OnRenderFrame();
            Directory.CreateDirectory(_shots);
            bool world = ScreenCapture.Save(Scene, Path.Combine(_shots, $"frame-{_capture}-world.png"));
            bool hud = ScreenCapture.SaveWindow(Scene, Path.Combine(_shots, $"frame-{_capture}-hud.png"));
            if (!world || !hud) throw new InvalidDataException("Native capture readback failed.");
            if (_capture == 0)
            {
                Scene.OnDrawFrame(); Scene.OnRenderFrame();
                if (!ScreenCapture.Save(Scene, Path.Combine(_shots, "frame-0-world-repeat.png")))
                    throw new InvalidDataException("Repeated native readback failed.");
            }
            _captures.Add(new { index = _capture, simulationFrame = Scene.FrameCount,
                camera = Scene.CameraPosition.ToString(), width = Scene.Size.X, height = Scene.Size.Y,
                shadow = ShadowSnapshot(_capture == 0), pickups = PickupSnapshot() });
            if (_capture == 0 && _outlineCheck) CheckOutlineOcclusion();
            _capture++;
        }
        if (GL.GetError() is var error && error != ErrorCode.NoError)
            throw new InvalidOperationException("Native renderer GL error: " + error);
        if (Scene.FrameCount >= (ulong)(_seconds * 60))
        {
            if (_windowCycle && (_windowStage != 4 || FramebufferSize.X <= 0 || FramebufferSize.Y <= 0 || !IsVisible))
                throw new InvalidOperationException("Native window cycle did not recover a visible framebuffer.");
            Complete = true; Close();
        }
        if (_wall.Elapsed.TotalSeconds > (_shots == null ? _seconds + 45 : _seconds * 5 + 45))
            throw new TimeoutException("Native renderer did not finish its fixed-step workload.");
    }

    // Diagnostic input is frozen, including native mouse callbacks.
    protected override void OnMouseMove(MouseMoveEventArgs e) { }

    private void CheckOutlineOcclusion()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Field(string name) => typeof(Scene).GetField(name, flags)!.GetValue(Scene);
        void Set(string name, object value) => typeof(Scene).GetField(name, flags)!.SetValue(Scene, value);
        if (PlayerEntity.PlayerCount < 2 || _outlines == PlayerOutlineStyle.Off)
            throw new InvalidOperationException("-outlinecheck requires two players and enabled outlines.");
        // Use an actual animated hunter and the production outline mask/composite.
        // Replacing world depth with a near wall must occlude every outline pixel;
        // far depth is the positive control proving that the body was submitted.
        var names = new[] { "_cameraMode", "_cameraPosition", "_cameraFacing", "_cameraRight", "_cameraUp",
            "_freeCam", "_inputMode", "_cameraFov", "_viewModelFov" };
        var saved = names.Select(Field).ToArray();
        void CheckGl(string stage)
        {
            var error = GL.GetError();
            if (error != ErrorCode.NoError) throw new InvalidOperationException("Outline check " + stage + ": " + error);
        }
        CheckGl("entry");
        try
        {
            Scene.SetFreeCamera(true);
            Vector3 target = Scene.Players.Items[1].Position + Vector3.UnitY;
            Set("_cameraPosition", target + Vector3.UnitZ * 5);
            Set("_cameraFacing", -Vector3.UnitZ);
            Set("_cameraRight", Vector3.UnitX); Set("_cameraUp", Vector3.UnitY);
            Scene.OnDrawFrame(); Scene.OnRenderFrame();
            CheckGl("camera draw");
            var draw = typeof(Scene).GetMethod("DrawPlayerOutlines", flags)!;
            int framebuffer = (int)Field("_frameBuffer")!;
            var size = (Vector2i)Field("_targetSize")!;
            int Pixels(double depth)
            {
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
                GL.Viewport(0, 0, size.X, size.Y);
                GL.ColorMask(true, true, true, true); GL.DepthMask(true);
                GL.Disable(EnableCap.ScissorTest);
                GL.ClearColor(0, 0, 0, 0); GL.ClearDepth(depth);
                GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                CheckGl("depth setup");
                draw.Invoke(Scene, null);
                CheckGl("outline draw");
                var pixels = new byte[size.X * size.Y * 4];
                GL.ReadPixels(0, 0, size.X, size.Y, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
                CheckGl("readback");
                int count = 0;
                for (int i = 0; i < pixels.Length; i += 4)
                    if (pixels[i] != 0 || pixels[i + 1] != 0 || pixels[i + 2] != 0) count++;
                return count;
            }
            int occludedPixels = Pixels(0), visiblePixels = Pixels(1);
            if (occludedPixels != 0 || visiblePixels == 0)
                throw new InvalidOperationException($"Outline occlusion failed: hidden={occludedPixels}, visible={visiblePixels}");
            _outlineEvidence = new { occludedPixels, visiblePixels };
            Console.WriteLine($"NATIVERENDER outline occlusion PASS hidden={occludedPixels} visible={visiblePixels}");
        }
        finally
        {
            // Use the GL2.1 double overload, not the newer glClearDepthf entry point.
            GL.ClearDepth(1.0);
            for (int i = 0; i < names.Length; i++) Set(names[i], saved[i]!);
            CheckGl("restore state");
            Scene.OnDrawFrame(); CheckGl("restore prepare");
            Scene.OnRenderFrame(); CheckGl("restore draw");
        }
    }

    protected override void OnFocusedChanged(FocusedChangedEventArgs e)
    {
        base.OnFocusedChanged(e);
        if (_windowCycle) _windowEvents.Add(new { focusChanged = e.IsFocused, wallSeconds = _wall.Elapsed.TotalSeconds });
    }

    private object[] PickupSnapshot()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var visible = typeof(ItemInstanceEntity).GetMethod("IsPickupVisuallyVisible", flags);
        var blocked = typeof(ItemInstanceEntity).GetMethod("PickupSampleBlocked", flags);
        var items = new List<ItemInstanceEntity>();
        foreach (var entity in Scene.Entities) if (entity is ItemInstanceEntity item) items.Add(item);
        return items.Select(item =>
        {
            Vector3 right = Vector3.Cross(item.Position - Scene.CameraPosition, Vector3.UnitY);
            right = right.LengthSquared > .0001f ? right.Normalized() : Vector3.UnitX;
            Vector3[] samples = { item.Position, item.Position + right * .27f, item.Position - right * .27f,
                item.Position + Vector3.UnitY * .27f, item.Position - Vector3.UnitY * .27f };
            bool[]? obstruction = blocked == null ? null : samples.Select(p =>
                (bool)blocked.Invoke(item, new object[] { Scene.CameraPosition, p })!).ToArray();
            return (object)new { type = item.ItemType.ToString(), position = item.Position.ToString(),
                visibleByPickupRule = visible?.Invoke(item, null), blockedSamples = obstruction };
        }).ToArray();
    }

    private object ShadowSnapshot(bool saveDepth)
    {
        object? Field(string name) => typeof(Scene).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(Scene);
        int size = (int)(Field("_shadowTargetSize") ?? 0), texture = (int)(Field("_shadowDepthTexture") ?? 0);
        string? depthHash = null;
        float? minimum = null, maximum = null;
        if (saveDepth && texture != 0 && size > 0)
        {
            int active = GL.GetInteger(GetPName.ActiveTexture);
            GL.ActiveTexture(TextureUnit.Texture0);
            int binding = GL.GetInteger(GetPName.TextureBinding2D);
            try
            {
                var depth = new float[size * size];
                GL.BindTexture(TextureTarget.Texture2D, texture);
                GL.GetTexImage(TextureTarget.Texture2D, 0, PixelFormat.DepthComponent, PixelType.Float, depth);
                byte[] bytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(depth.AsSpan()).ToArray();
                File.WriteAllBytes(Path.Combine(_shots!, "shadow-depth-float32.bin"), bytes);
                depthHash = Convert.ToHexString(SHA256.HashData(bytes));
                minimum = depth.Min(); maximum = depth.Max();
            }
            finally { GL.BindTexture(TextureTarget.Texture2D, binding); GL.ActiveTexture((TextureUnit)active); }
        }
        return new { ready = Field("_shadowReady"), size, depthTexture = texture,
            lightSpaceRoomDraws = typeof(Scene).GetProperty("LightSpaceRoomShadowDraws", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(Scene),
            depthSha256 = depthHash, minimumDepth = minimum, maximumDepth = maximum };
    }

    internal object CleanupAndInspect()
    {
        var scene = Scene;
        int Field(string name) => (int)(typeof(Scene).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(scene) ?? 0);
        int shadowFbo = Field("_shadowFramebuffer"), shadowTexture = Field("_shadowDepthTexture");
        Type retained = typeof(Scene).Assembly.GetType("MphRead.Mods.Render.DesktopRetainedGeometry")!;
        object? Counter(string name) => retained.GetProperty(name, BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null);
        object? retainedBefore = Counter("RetainedListCount");
        long combinedBefore = (long)(retained.GetField("_combinedBytes", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) ?? 0L);
        EndScene();
        bool shadowReleased = (shadowFbo == 0 || !GL.IsFramebuffer(shadowFbo))
            && (shadowTexture == 0 || !GL.IsTexture(shadowTexture)) && Field("_shadowFramebuffer") == 0 && Field("_shadowDepthTexture") == 0;
        object? retainedAfter = Counter("RetainedListCount");
        long combinedAfter = (long)(retained.GetField("_combinedBytes", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) ?? 0L);
        if (!shadowReleased || retainedAfter is int count && count != 0 || combinedAfter != 0 || GL.GetError() != ErrorCode.NoError)
            throw new InvalidOperationException("Scene unload retained live GL resources or an error.");
        return new { shadowReleased, retainedListsBefore = retainedBefore, retainedListsAfter = retainedAfter,
            combinedBytesBefore = combinedBefore, combinedBytesAfter = combinedAfter };
    }

    internal object Result()
    {
        double P(double percentile) => _intervals.Count == 0 ? 0
            : _intervals.Order().ElementAt(Math.Clamp((int)Math.Ceiling(_intervals.Count * percentile / 100) - 1, 0, _intervals.Count - 1));
        return new { schema = 1, room = _room, cap = _cap, shadows = _shadows.ToString(),
            effectiveCap = FrameTiming.FrameRateCap, softwareFrequency = UpdateFrequency, vsync = VSync.ToString(),
            assemblyMvid = typeof(Scene).Module.ModuleVersionId, framebuffer = new[] { FramebufferSize.X, FramebufferSize.Y },
            wallSeconds = _wall.Elapsed.TotalSeconds, measurementSeconds = Stopwatch.GetElapsedTime(_measureStarted).TotalSeconds,
            simulationFrames = Scene.FrameCount, samples = _intervals.Count,
            meanCompletedFrameFps = _intervals.Count == 0 ? 0 : 1000 / _intervals.Average(),
            frameIntervalP50Ms = P(50), frameIntervalP95Ms = P(95), frameIntervalP99Ms = P(99),
            onePercentLowEstimateFps = P(99) > 0 ? 1000 / P(99) : 0,
            pointOnePercentLowEstimateFps = _intervals.Count >= 1000 && P(99.9) > 0 ? (double?)(1000 / P(99.9)) : null,
            allocatedBytes = GC.GetTotalAllocatedBytes(false) - _allocated,
            gcCollections = Enumerable.Range(0, 3).Select(i => GC.CollectionCount(i) - _collections[i]).ToArray(),
            managedBytes = GC.GetTotalMemory(false), workingSetBytes = Environment.WorkingSet,
            simulationHz = FrameTiming.MeasuredSimulationHz, droppedSimulationSteps = FrameTiming.DroppedSteps,
            stalls = FrameTiming.Stalls, captures = _captures, outlineOcclusion = _outlineEvidence, windowEvents = _windowEvents, complete = Complete };
    }
}
