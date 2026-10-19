#if !MPHREAD_SERVER
using System;
using System.Collections.Immutable;

namespace MphRead.Mods.Launcher.Core;

public sealed record TheatreExportContext(long SceneGeneration, object PlaybackOwner, string LogicalPath,
    string PlaybackPath, string SourceRevision, string CameraRevision, string AnnotationRevision, string ReelRevision,
    uint Duration, uint? MarkIn, uint? MarkOut, int ReelSegments, object Presentation)
{
    public object? AudioSnapshot { get; init; }
    public string AudioRevision { get; init; }="";
    public string AudioSummary { get; init; }="Offline audio settings unavailable.";
}
public sealed record TheatreExportPreset(string Name, int Resolution, int Fps, bool GameHud, bool ReplayOverlay);
public enum TheatreExportSource { Selection, FullReplay, ExistingReel }
public enum TheatreExportCamera { FirstPerson, Chase, Orbit, Director, SavedCameraTrack }
public sealed record TheatreExportOptions(int Preset, int Resolution, int Fps, bool GameHud, bool ReplayOverlay,
    string Encoder, TheatreExportSource Source, TheatreExportCamera Camera,string EncoderExecutable="ffmpeg");
public sealed record TheatreExportJob(Guid Id, object Token, string ReplayName, string OutputPath, string Description);
public sealed record TheatreExportQueueState(long Revision, ImmutableArray<TheatreExportJob> Pending,
    TheatreExportJob? Active, TheatreExportJob? Retry, string State, string Status, float Progress,
    ImmutableArray<string> Failures, string OutputPath);
public enum TheatreExportConfirmationKind { CancelActive, Retry, ClearPending }
public sealed record TheatreExportConfirmation(TheatreExportConfirmationKind Kind, TheatreExportQueueState Queue);
public sealed record TheatreExportSnapshot(TheatreExportContext Context, TheatreExportOptions Options,
    ImmutableArray<TheatreExportPreset> Presets, ImmutableArray<string> Encoders, TheatreExportQueueState Queue,
    TheatreExportConfirmation? Confirmation, bool Current, string Status, string Error)
{
    public int SelectedIndex { get; init; }
}
public sealed class TheatreExportStaleException(string message) : InvalidOperationException(message) { }

public interface ITheatreExportBackend
{
    TheatreExportContext Capture();
    bool IsCurrent(TheatreExportContext context);
    ImmutableArray<TheatreExportPreset> Presets { get; }
    ImmutableArray<string> Encoders();
    TheatreExportQueueState Queue();
    void Enqueue(TheatreExportContext reviewed, TheatreExportOptions options);
    bool CancelExact(TheatreExportQueueState reviewed);
    bool RetryExact(TheatreExportQueueState reviewed);
    bool ClearExact(TheatreExportQueueState reviewed);
    void OpenOutput(string exactOutputPath);
}
#endif
