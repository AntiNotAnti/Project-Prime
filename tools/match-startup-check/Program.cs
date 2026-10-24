using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.Launcher;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;

string Value(string key, string? fallback = null)
{
    int i = Array.IndexOf(args, key);
    return i >= 0 && i + 1 < args.Length ? args[i + 1]
        : fallback ?? throw new ArgumentException("Required argument: " + key);
}
string room = Value("--room");
string output = Path.GetFullPath(Value("--output"));
string profile = Environment.GetEnvironmentVariable("PROJECT_PRIME_USER_DATA")
    ?? throw new ArgumentException("Set PROJECT_PRIME_USER_DATA to an isolated profile with configured owned assets.");
if (!Path.IsPathFullyQualified(profile)) throw new ArgumentException("Use an absolute isolated profile.");
bool linear = bool.Parse(Value("--linear", "true"));
typeof(Scene).Assembly.GetType("MphRead.ConsoleSetup")!.GetMethod("Run")!.Invoke(null, null);
GameFiles.Root = profile;
LauncherPrefs.Directory = profile;
LauncherPrefs.Load();
GameFiles.ApplyPaths();
if (!GameFiles.Ready) throw new InvalidOperationException("Configured owned game files are required.");
GraphicsBackendPolicy.LoadPreference();

var rows = new List<object>();
Exception? failure = null;
string stage = "window-construction";
bool cleaned = false;
StartupWindow? window = null;
try
{
    window = new StartupWindow(room, linear, rows, value => stage = value);
    window.Run();
    failure = window.Failure;
    if (!window.Completed && failure == null) failure = new InvalidOperationException("Window exited before both matches completed.");
}
catch (Exception ex) { failure = window?.Failure ?? ex; }
finally
{
    if (window != null)
    {
        try { if (window.HasScene) window.EndScene(); window.Dispose(); cleaned = true; }
        catch (Exception ex) { failure ??= ex; }
    }
    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    var assembly = typeof(Scene).Assembly;
    File.WriteAllText(output, JsonSerializer.Serialize(new
    {
        schema = 1, passed = failure == null && window?.Completed == true,
        scope = "Actual persistent RenderWindow(shell:true), MatchStart.Begin, zero-tick first presentation, then 120 native fixed steps and same-window teardown/rematch. No forced spawn, camera/actor writes, or Scene.Update before first draw.",
        room, linearColor = linear, processId = Environment.ProcessId,
        client = new { path = assembly.Location, version = assembly.GetName().Version?.ToString(),
            mvid = assembly.ManifestModule.ModuleVersionId,
            sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))) },
        harnessSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Assembly.GetExecutingAssembly().Location))),
        failureStage = failure == null ? null : stage, failure = failure?.ToString(),
        sceneAndWindowCleanupCompleted = cleaned, matches = rows
    }, new JsonSerializerOptions { WriteIndented = true }));
}
Console.WriteLine($"MATCH STARTUP {(failure == null && window?.Completed == true ? "PASS" : "FAIL")} {output}");
if (failure != null) Console.Error.WriteLine(failure);
return failure == null && window?.Completed == true ? 0 : 1;

sealed class StartupWindow : RenderWindow
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private readonly string _room;
    private readonly bool _linear;
    private readonly List<object> _rows;
    private readonly Action<string> _stage;
    private readonly Stopwatch _timeout = Stopwatch.StartNew();
    private int _match;
    private bool _began;
    private object? _firstDraw;
    public Exception? Failure { get; private set; }
    public bool Completed { get; private set; }

    internal StartupWindow(string room, bool linear, List<object> rows, Action<string> stage) : base(shell: true)
    {
        _room = room; _linear = linear; _rows = rows; _stage = stage;
        ClientSize = new Vector2i(640, 360);
        WindowState = WindowState.Normal;
        IsVisible = true;
        Title = "Project Prime normal match startup regression";
    }

    protected override void OnRenderFrame(FrameEventArgs args)
    {
        try
        {
            if (_timeout.Elapsed.TotalSeconds > 120) throw new TimeoutException("Two-match startup check timed out.");
            if (!_began)
            {
                _stage($"match-{_match}-begin");
                // Same settings loaders as Shell.ShowFrontScreen, with explicit
                // diagnostic appearance/format choices; never persist them.
                MenuSettings settings = GameState.LoadSettings();
                settings.LinearColor = _linear ? "on" : "off";
                settings.ThirdPersonCamera = "off";
                GameSettings.Apply(settings);
                RenderOptions.LinearColor = _linear;
                FrameTiming.FrameRateCap = FrameTiming.Unlimited;
                var plan = new LaunchPlan { Kind = LaunchKind.Offline, RoomKey = _room,
                    Mode = GameMode.CaptureTheFlag, Hunter = Hunter.Samus, Bots = 7,
                    BotLevel = 1, LocalTeam = 0, PlayerName = "STARTUP CHECK",
                    MatchRules = new MatchDefinition { RoomKey = _room, Mode = GameMode.CaptureTheFlag,
                        Format = MatchFormat.FourVsFour, PointGoal = 5, TimeLimitSeconds = 600 } };
                if (!MatchStart.Begin(this, settings, plan)) throw new InvalidOperationException(MatchStart.LastError);
                _began = true;
                ulong before = Scene.FrameCount;
                var cameraBefore = CameraSnapshot(Scene.Players.Main.CameraInfo, invert: false);
                _stage($"match-{_match}-first-draw-zero-steps");
                _rows.Add(new { match = _match, stage = "before-first-draw", frameCount = before, camera = cameraBefore });
                Console.WriteLine($"MATCH STARTUP first draw room={_room} match={_match} frameCount={before}");
                FrameTiming.Reset();
                // Do not pre-check/invert the camera or update the Scene here.
                // The baseline must reach the real renderer's material-camera failure.
                base.OnRenderFrame(new FrameEventArgs(0));
                CheckGl();
                Require(before == 0 && Scene.FrameCount == 0 && FrameTiming.StepsThisFrame == 0,
                    "The first successful native draw must precede every fixed step.");
                Require(Scene.CameraMode == CameraMode.Player, "Normal player camera is required.");
                bool requested = (bool)typeof(Scene).GetField("SceneColorRequested", Hidden)!.GetValue(Scene)!;
                Require(!_linear || requested, "The linear-color material-camera path must actually be requested.");
                _firstDraw = new { frameCountBefore = before, frameCountAfter = Scene.FrameCount,
                    fixedSteps = FrameTiming.StepsThisFrame, sceneColorRequested = requested,
                    cameraMode = Scene.CameraMode.ToString(), camera = CameraSnapshot(Scene.Players.Main.CameraInfo, invert: true) };
                return;
            }
            _stage($"match-{_match}-fixed-step-render");
            base.OnRenderFrame(new FrameEventArgs(FrameTiming.StepSeconds));
            CheckGl();
            if (Scene.FrameCount < 120) return;
            var players = Scene.Players.Items.Take(8).ToArray();
            int bots = players.Count(p => p.IsBot);
            int readyBots = players.Count(p => p.IsBot && BotReady(p));
            if ((bots != 7 || readyBots != 7) && Scene.FrameCount < 600) return;
            Require(bots == 7 && readyBots == 7, "All seven capture bots must retain enabled navigation/objective bindings.");
            Require(players.Count(p => p.TeamIndex == 0) == 4 && players.Count(p => p.TeamIndex == 1) == 4,
                "The normal launch must populate the requested 4v4 teams.");
            _rows.Add(new { match = _match, stage = "completed", room = Scene.Room?.Meta.Name,
                mode = Scene.GameState.Mode.ToString(), simulationFrames = Scene.FrameCount, bots, readyBots,
                firstDraw = _firstDraw, camera = CameraSnapshot(Scene.Players.Main.CameraInfo, invert: true),
                players = players.Select(p => new { p.SlotIndex, p.IsBot, p.TeamIndex, p.Health, loadFlags = p.LoadFlags.ToString() }).ToArray() });
            _stage($"match-{_match}-teardown");
            EndScene();
            Require(!HasScene, "Teardown must clear the previous scene before rematch.");
            if (++_match == 2) { Completed = true; Close(); }
            else _began = false;
        }
        catch (Exception ex) { Failure = ex; Close(); }
    }

    private static bool BotReady(PlayerEntity player)
    {
        object ai = typeof(PlayerEntity).GetProperty("AiData", Hidden)?.GetValue(player)
            ?? typeof(PlayerEntity).GetField("AiData", Hidden)!.GetValue(player)!;
        Type type = ai.GetType();
        return player.TeamIndex is 0 or 1
            && type.GetField("_forceDisable", Hidden)?.GetValue(ai) is false
            && type.GetField("_nodeData", Hidden)?.GetValue(ai) != null
            && new[] { "_octolithFlagCC", "_octolithFlagD4", "_octolithFlagDC", "_flagBaseD0", "_flagBaseD8", "_flagBaseE0" }
                .All(name => type.GetField(name, Hidden)?.GetValue(ai) != null);
    }

    private static object CameraSnapshot(CameraInfo camera, bool invert)
    {
        Matrix4 view = camera.ViewMatrix;
        float[] V(Vector3 value) => new[] { value.X, value.Y, value.Z };
        float[] M(Matrix4 value) => Enumerable.Range(0, 4).SelectMany(r => Enumerable.Range(0, 4).Select(c => value[r, c])).ToArray();
        Vector3? eye = null;
        if (invert)
        {
            Matrix4 inverse = Matrix4.Invert(view);
            eye = inverse.Row3.Xyz;
            Require(M(view).All(float.IsFinite) && M(inverse).All(float.IsFinite)
                && (eye.Value - camera.Position).Length < .001f, "Finite inverse camera eye must match its current position.");
        }
        return new { position = V(camera.Position), target = V(camera.Target), up = V(camera.UpVector),
            view = M(view), inverseEye = eye.HasValue ? V(eye.Value) : null };
    }
    private static void CheckGl()
    {
        ErrorCode error = GL.GetError();
        Require(error == ErrorCode.NoError, "Native frame GL error: " + error);
    }
    private static void Require(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
}
