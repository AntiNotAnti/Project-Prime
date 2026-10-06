#if !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.Numerics;

namespace MphRead.Mods.StudioReplay;

public enum StudioReplayCameraMode { Player, Free, Chase, Orbit, Authored, Overview }
public enum StudioReplayCameraInterpolation : byte { Linear, Smooth, Spline, Hold, Bezier }
public enum StudioReplayCameraEase : byte { None, In, Out, InOut }
public sealed record StudioReplayView(int Width, int Height, StudioReplayCameraMode Camera = StudioReplayCameraMode.Player,
    int PlayerSlot = 0, Vector3 Position = default, Quaternion Rotation = default, float Fov = 78,
    bool GameHud = true, bool ReplayOverlay = false, double? PresentationFrame = null, float? PresentationAlpha = null,
    bool ConstantSpeed = false, bool CollisionAvoidance = true, StudioReplayCombat? Combat = null, bool CombatRays = false, bool ShooterView = true);
public sealed record StudioReplayCameraKey(uint Frame, Vector3 Position, Quaternion Rotation, float Fov = 78,
    int LookAtSlot = -1, float Roll = 0, StudioReplayCameraInterpolation Interpolation = StudioReplayCameraInterpolation.Spline,
    StudioReplayCameraEase Ease = StudioReplayCameraEase.InOut,
    Vector3? IncomingTangent = null, Vector3? OutgoingTangent = null,
    float FovIncomingTangent = 0, float FovOutgoingTangent = 0, float RollIncomingTangent = 0, float RollOutgoingTangent = 0);
public sealed record StudioReplayPerformance(uint SeekRestoreFrame, int SeekSimulationSteps, double? SeekMilliseconds,
    int RejectedCheckpoints, string CheckpointSource, double? AdvanceMilliseconds, double? RenderMilliseconds,
    double? CheckpointCaptureMilliseconds = null);
public sealed record StudioReplayWorldSnapshot(uint Frame, string GameplayHash, string PresentationHash,
    string FullGraphHash, IReadOnlyList<StudioReplayPlayerPose> Players);
public sealed record StudioReplayPlayerPose(int Slot, Vector3 Position, Vector3 Facing, bool Active);
public sealed record StudioReplayMarker(Guid Id, uint StartFrame, uint EndFrame, string Name, string Track);
public sealed record StudioReplayPlayerInfo(int Slot, string Name, int Kills, int Deaths, int Damage);
public sealed record StudioReplayEvent(uint Frame, string Type, int Actor, int Target, int Value);
public sealed record StudioReplayStatus(bool Ready, bool Preparing, uint Frame, uint DurationFrames, string State,
    float Rate, uint? ClipIn, uint? ClipOut, int CheckpointCount, long CheckpointBytes, string? Error,
    string? Room, string? CustomMapRoot);
public sealed record StudioReplayCombat(uint RecordingFrame, uint FireFrame, uint ShotId, ushort DamageEventId,
    int Shooter, int Target, uint ServerTick, uint SourceFrame, Vector3 Muzzle, Vector3 Aim, Vector3 Direction,
    Vector3 Impact, int Damage, bool Headshot, bool Lethal, double? RewindFrame, Vector3? RewindTarget, string Explanation,
    bool HasAuthoredPose = true, float? SettlementSeconds = null, Vector3? DamageDirection = null);
public sealed record StudioReplayCapture(int Width, int Height, byte[] Rgba);
public sealed record StudioReplayExportRequest(string Directory, uint StartFrame, uint EndFrame, int Width = 1920,
    int Height = 1080, int Fps = 60, string? Encoder = null, string OutputName = "replay.mp4", bool GameHud = false,
    bool ReplayOverlay = false, StudioReplayCameraMode Camera = StudioReplayCameraMode.Authored, StudioReplayAudioOptions? Audio = null,
    StudioReplayView? View = null);
public sealed record StudioReplayExportStatus(Guid Id, string State, long Frames, long TotalFrames, string? Error, string Directory);
public sealed record StudioReplayPortableImport(string ReplayPath, IReadOnlyList<string> PackageDirectories);
public sealed record StudioAudioEventBinding(string EventType, string WaveFile, StudioAudioBus Bus = StudioAudioBus.Game, int? Value = null, float Gain = 1);
public sealed record StudioReplayAudioOptions(bool Enabled = true, StudioAudioVolumes? Volumes = null,
    string? MusicFile = null, IReadOnlyList<StudioAudioEventBinding>? Bindings = null, bool CombatFeedback = true, bool GameEvents = true);
public sealed record StudioReplayExportTicket(Guid Id, string ReplayPath, string CacheRoot,
    StudioReplayExportRequest Request, string StatusFile, string CancelFile,
    IReadOnlyDictionary<string,string> RuntimePaths, IReadOnlyList<StudioReplayCameraKey> CameraKeys,
    IReadOnlyList<string> PackageDirectories, string MphKey, string FhKey);

/// <summary>Invoked only by the native graphics owner while its context is active.</summary>
public interface IStudioReplayGraphicsSession
{
    void OnGraphicsInitialize(int width, int height);
    void OnGraphicsFrame(TimeSpan elapsed, StudioReplayView view);
    void OnGraphicsDeinitialize(bool nativeReleaseEligible);
    StudioReplayCapture? Capture(StudioReplayView view) => null;
}

#endif
