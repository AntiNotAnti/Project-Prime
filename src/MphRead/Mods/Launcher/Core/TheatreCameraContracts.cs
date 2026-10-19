#if !MPHREAD_SERVER
using System;
using System.Collections.Immutable;

namespace MphRead.Mods.Launcher.Core;

public sealed record TheatreCameraVector(float X,float Y,float Z);
public sealed record TheatreCameraHandles(TheatreCameraVector? Incoming,TheatreCameraVector? Outgoing,float FovIn,float FovOut,float RollIn,float RollOut);
public sealed record TheatreCameraPose(float X, float Y, float Z, float Qx, float Qy, float Qz, float Qw,
    float Fov, float Roll, int LookAt, int Interpolation, int Ease,TheatreCameraHandles? Handles=null);
public sealed record TheatreCameraKey(uint Frame, TheatreCameraPose Pose);
public sealed record TheatreCameraContext(long SceneGeneration, object Transport, object PresentationScene,
    long SeekGeneration, string LogicalPath, string PlaybackPath, string RecordingRevision, string CameraRevision,
    uint Frame, uint Duration, bool SettledPaused, TheatreCameraPose Pose, ImmutableArray<TheatreCameraKey> Keys,
    bool TrackPlaying)
{
    public long PresentationRevision { get; init; }
}
public enum TheatreCameraChange { Add, Replace, Remove }
public sealed record TheatreCameraEdit(TheatreCameraChange Change, TheatreCameraContext Context,
    TheatreCameraKey Key);
public sealed class TheatreCameraStaleException(string message) : InvalidOperationException(message) { }
public sealed record TheatreCameraSnapshot(TheatreCameraContext Context, int SelectedIndex,
    TheatreCameraKey? Selected, TheatreCameraEdit? Pending, bool Current, string Status, string Error);

/// <summary>Presentation edits use the existing replay and camera track owners.</summary>
public interface ITheatreCameraBackend
{
    TheatreCameraContext Capture();
    bool IsCurrent(TheatreCameraContext context);
    void SetFreeCamera(TheatreCameraContext context);
    void AdjustLens(TheatreCameraContext context, float fov, float roll);
    void Preview(TheatreCameraContext context, TheatreCameraKey key);
    void ToggleTrack(TheatreCameraContext context);
    void Commit(TheatreCameraEdit exact);
}
#endif
