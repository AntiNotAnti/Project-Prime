using System.Collections.Immutable;
using OpenTK.Mathematics;

namespace MphRead
{
    internal sealed class Scene { public double ReplayPresentationFrame = double.NaN; public long LifetimeGeneration = 1; public bool OwnerExportPreparedForDraw = true; public StubPlayers Players=new(); }
    internal sealed class StubPlayers { public int MainPlayerIndex=2; }
}
namespace MphRead.Mods.Network
{
    internal enum ReplayState { Inactive, Playing, Paused, Seeking, Ended, Error }
    internal static partial class DemoPlayback
    {
        public static bool IsActive = true;
        public static string? PlaybackPath = "/synthetic.ppdemo";
        public static string? LogicalPath = "/synthetic.ppdemo";
        private static object? _player = new();
        private static object _prepared = new();
        public static void ReplaceOwner() => _player = new();
        public static WeakReference PlayerReference() => new(_player);
        public static MphRead.Scene? PresentationScene = new();
        public static MphRead.Scene? Shell;
        public static string? LastError = null;
        public static IReadOnlyList<ReplayEvent> Events=Array.Empty<ReplayEvent>();
        public static StubReplayMetadata? Metadata=new();
        public static bool Owns(MphRead.Scene scene) => ReferenceEquals(scene, PresentationScene);
        public static MphRead.Scene? Presentation(MphRead.Scene shell) => ReferenceEquals(shell, Shell) ? PresentationScene : null;
    }
    internal sealed class StubReplayMetadata { public string RoomKey="SYNTHETIC ROOM";public IReadOnlyList<ReplayPlayerInfo> Players=Array.Empty<ReplayPlayerInfo>(); }
    internal static class ReplayController
    {
        public static uint CurrentFrame = 1, DurationFrames = 100;
        public static uint? ClipIn = 10, ClipOut = 20;
        public static ReplayState State = ReplayState.Paused;
        public static bool IsSeeking => State == ReplayState.Seeking;
        public static void Seek(uint frame, bool resume) { CurrentFrame = frame; State = ReplayState.Seeking; }
        public static int StepCalls;
        public static void StepForward() { StepCalls++; }
    }
    internal static class ReplayFormatV3 { public const uint MaxFrame = 100000000; }
    internal static class DemoLibrary { public static string Directory = Path.GetTempPath(); }
}
namespace MphRead.Mods
{
    internal static class ScreenCapture
    {
        public static int Captures;
        public static MphRead.Scene? LastScene;
        public static bool Save(MphRead.Scene scene, string path) { Captures++; LastScene = scene; return true; }
        public static bool SaveWindow(MphRead.Scene scene, string path) => Save(scene, path);
    }
}
namespace MphRead.Mods.Replay
{
    internal static class ReplayOwnerExport
    {
        public static bool Ready = true;
        public static string? Source;
        public static void Begin(string source) => Source = source;
        public static void End() => Source = null;
    }
    internal enum ReplayCameraMode { FirstPerson, Chase, Free, Orbit }
    internal enum ReplayPresentationProfile { Faithful, Presentation }
    internal static class ReplayCamera
    {
        public static ReplayCameraMode Mode;
        public static ReplayPresentationProfile Profile;
        public static float FieldOfView = 80, Roll, Distance = 4, Height = 1.5f;
        public static int LookAtSlot = -1;
        public static bool Director, PlayTrack, TrackConstantSpeed = true, TrackCollisionAvoidance = true;
        public static ReplayCameraInterpolation TrackInterpolation;
        public static ReplayCameraEase TrackEase;
        public static ReplayCameraTrack Track = new();
        public static void SetMode(ReplayCameraMode mode) { Mode = mode; PlayTrack = false; }
        public static void SetProfile(ReplayPresentationProfile profile) => Profile = profile;
    }
    internal static class ReplayHud { public static string Time(uint frame) => frame.ToString(); }
    internal static partial class ReplayAnnotations { public const string Extension = ".studio.json"; }
    internal static partial class ReplayReels { public const string Extension = ".reel.json"; }
    internal static class ReplayVirtualClips
    {
        public const string Extension = ".ppclip";
        public static bool TryLoad(string path, out ReplayVirtualClipDocument? document)
        { document = System.Text.Json.JsonSerializer.Deserialize<ReplayVirtualClipDocument>(File.ReadAllBytes(path)); return document is { Version: 1 }; }
    }
    internal static class ReplayEncoderCapabilities
    {
        public static Task<IReadOnlyList<string>> Available => Task.FromResult<IReadOnlyList<string>>(new[] { "libx264" });
        public static ReplayVideoExportManifest Apply(ReplayVideoExportManifest job, string encoder) => job;
    }
    internal sealed record ReplayEncodingResult(bool Cancelled, int? ExitCode, string? Error, string Stderr);
    internal sealed class ReplayEncoderJob : IDisposable
    {
        public static ReplayEncoderJob? Last;
        public readonly TaskCompletionSource<ReplayEncodingResult> Result = new();
        public Task<ReplayEncodingResult> Completion => Result.Task;
        public bool Cancelled, Disposed;
        public int Frames => 0;
        public ReplayEncoderJob(string executable, string arguments, string directory) => Last = this;
        public void Cancel() => Cancelled = true;
        public void Dispose() => Disposed = true;
    }
}

namespace MphRead.Mods.Launcher.RmlUi.Render
{
    internal static partial class RmlUiOpenGlSmoke
    {
        private static bool GameplayLeaseCurrent = true;
        private static MphRead.Scene? _gameplayScene = new();
        internal static MphRead.Mods.Launcher.Core.ITheatreExportBackend FixtureBackend() => new CurrentReplayExports();
    }
}
