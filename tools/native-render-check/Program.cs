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
bool diagnoseSolo = args.Contains("-diagnose-solo");
if (diagnoseSolo && shots == null)
    throw new ArgumentException("-diagnose-solo needs -shots; diagnostic readbacks must never run during a benchmark.");
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
using var window = new CheckWindow(room, seconds, cap, shadows, shots, size, players,
    args.Contains("-idle"), args.Contains("-windowcycle"), diagnoseSolo);
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
    private readonly bool _diagnoseSolo;
    private readonly Vector2i _requestedPixels;
    private readonly List<object> _windowEvents = new();
    private int _windowStage;
    public bool Complete { get; private set; }

    internal CheckWindow(string room, int seconds, int cap, ShadowQuality shadows, string? shots,
        Vector2i pixels, int players, bool idle, bool windowCycle, bool diagnoseSolo)
    {
        _room = room; _seconds = seconds; _cap = cap; _shadows = shadows; _shots = shots;
        _diagnoseSolo = diagnoseSolo;
        _windowCycle = windowCycle; _requestedPixels = pixels;
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
        for (int i = 0; i < players; i++) AddPlayer((Hunter)(i % 7));
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
            // All diagnostic samples are taken after the same complete world/
            // outline/HUD path. Only -diagnose-solo performs extra GL readback,
            // and ordinary performance runs are not affected.
            object? solo = _diagnoseSolo ? SoloDiagnosticSnapshot() : null;
            _captures.Add(new { index = _capture, simulationFrame = Scene.FrameCount,
                camera = Scene.CameraPosition.ToString(), width = Scene.Size.X, height = Scene.Size.Y,
                shadow = ShadowSnapshot(_capture == 0), pickups = PickupSnapshot(),
                soloDiagnostic = solo });
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

    protected override void OnFocusedChanged(FocusedChangedEventArgs e)
    {
        base.OnFocusedChanged(e);
        if (_windowCycle) _windowEvents.Add(new { focusChanged = e.IsFocused, wallSeconds = _wall.Elapsed.TotalSeconds });
    }


    private object SoloDiagnosticSnapshot()
    {
        const BindingFlags instance = BindingFlags.Instance
            | BindingFlags.Public | BindingFlags.NonPublic;
        object? SceneField(string name) => typeof(Scene).GetField(name, instance)?.GetValue(Scene);
        var room = Scene.Room ?? throw new InvalidOperationException("Solo diagnostic requires a loaded room.");
        object? RoomField(string name) => room.GetType().GetField(name, instance)?.GetValue(room);
        var current = Scene.Players.Main.CameraInfo.NodeRef;
        bool[] activeParts = (bool[]?)RoomField("_activeRoomParts") ?? Array.Empty<bool>();
        bool fallbackAllParts = RoomField("_partVisInfoHead") == null || Scene.ShowAllNodes;
        var opaque = (IReadOnlyList<RenderItem>?)SceneField("_nonDecalItems")
            ?? Array.Empty<RenderItem>();
        var decals = (IReadOnlyList<RenderItem>?)SceneField("_decalItems")
            ?? Array.Empty<RenderItem>();
        var translucent = (IReadOnlyList<RenderItem>?)SceneField("_translucentItems")
            ?? Array.Empty<RenderItem>();
        var camera = Scene.CameraPosition;
        // This is intentional evidence: 1 and 2 players should share the
        // Battle-mode room layer and LOD. If not, compare the actual setup
        // before making conclusions about GL state.
        int count = Scene.Players.PlayerCount;
        int nodeLayer = SceneSetup.GetNodeLayer(Scene.GameState.Mode, room.Meta.NodeLayer,
            count);
        int entityLayer = SceneSetup.GetMultiplayerEntityLayer(
            Scene.GameState.Mode, count, MphRead.Mods.Multiplayer.MatchWorldProfile.Resolve(count).Resources);
        int bots = Scene.Players.Items.Count(player => player.IsBot
            && player.LoadFlags.TestFlag(LoadFlags.Active));

        object LightingVector(Vector3 value) => new[] { value.X, value.Y, value.Z };
        object GetLightSnapshot() => new
        {
            light1Direction = LightingVector(Scene.Light1Vector),
            light1Color = LightingVector(Scene.Light1Color),
            light2Direction = LightingVector(Scene.Light2Vector),
            light2Color = LightingVector(Scene.Light2Color)
        };

        return new
        {
            schema = 1,
            room = room.Meta.Name,
            mode = Scene.GameState.Mode.ToString(),
            matchState = Scene.GameState.MatchState.ToString(),
            playerCount = count,
            activeBots = bots,
            nodeLayer,
            entityLayer,
            cameraMode = Scene.CameraMode.ToString(),
            camera = new[] { camera.X, camera.Y, camera.Z },
            cameraNode = new
            {
                room = current.RoomName,
                part = current.PartIndex,
                node = current.NodeIndex,
                model = current.ModelIndex
            },
            cullingFallbackAllParts = fallbackAllParts,
            visiblePartCount = activeParts.Count(enabled => enabled),
            roomOwnedOpaquePackets = opaque.Count(item => item.RetainedRoomOwned),
            opaquePackets = opaque.Count,
            decalPackets = decals.Count,
            translucentPackets = translucent.Count,
            outlinedPackets = opaque.Count(item => item.PlayerOutlineColor.HasValue)
                + translucent.Count(item => item.PlayerOutlineColor.HasValue),
            lighting = GetLightSnapshot(),
            depth = WorldDepthSnapshot(),
            gl = new
            {
                framebuffer = GL.GetInteger(GetPName.FramebufferBinding),
                program = GL.GetInteger(GetPName.CurrentProgram),
                depthTest = GL.IsEnabled(EnableCap.DepthTest),
                depthWrite = GL.GetInteger(GetPName.DepthWritemask) != 0,
                stencilTest = GL.IsEnabled(EnableCap.StencilTest),
                alphaTest = GL.IsEnabled(EnableCap.AlphaTest),
                blend = GL.IsEnabled(EnableCap.Blend)
            }
        };
    }

    private object WorldDepthSnapshot()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        int target = (int)(typeof(Scene).GetField("_frameBuffer", flags)?.GetValue(Scene) ?? 0);
        Vector2i size = (Vector2i)(typeof(Scene).GetField("_targetSize", flags)?.GetValue(Scene)
            ?? Vector2i.Zero);
        if (target == 0 || size.X < 32 || size.Y < 32)
            throw new InvalidOperationException("Scene depth attachment missing during solo diagnostics.");

        // Sample bounded regions on the actual world depth attachment.
        // This is deliberately readback-only, and captures do not count as
        // performance samples. The GL read framebuffer must be restored.
        int previousRead = GL.GetInteger(GetPName.ReadFramebufferBinding);
        int[] centersX = { size.X / 4, size.X / 2, size.X * 3 / 4 };
        int[] centersY = { size.Y / 4, size.Y / 2, size.Y * 3 / 4 };
        float[] patch = new float[32 * 32];
        int near = 0, far = 0;
        float min = 1f, max = 0f;
        try
        {
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, target);
            for (int y = 0; y < centersY.Length; y++)
            {
                for (int x = 0; x < centersX.Length; x++)
                {
                    GL.ReadPixels(centersX[x] - 16, centersY[y] - 16,
                        32, 32, PixelFormat.DepthComponent, PixelType.Float, patch);
                    foreach (float value in patch)
                    {
                        if (!float.IsFinite(value) || value < 0 || value > 1)
                            throw new InvalidOperationException("Invalid depth sample from scene framebuffer");
                        if (value >= 0.999999f) far++;
                        else near++;
                        min = Math.Min(min, value);
                        max = Math.Max(max, value);
                    }
                }
            }
        }
        finally
        {
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, previousRead);
        }
        return new
        {
            nearSamples = near,
            farSamples = far,
            nearFraction = near / (double)Math.Max(1, near + far),
            minimum = min,
            maximum = max,
            targetWidth = size.X,
            targetHeight = size.Y
        };
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
            stalls = FrameTiming.Stalls, captures = _captures, windowEvents = _windowEvents, complete = Complete };
    }
}
