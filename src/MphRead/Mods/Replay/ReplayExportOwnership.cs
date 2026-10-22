using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace MphRead.Mods.Replay;

/// <summary>An accepted job keeps its immutable reviewed inputs independently of any editor page.</summary>
internal sealed record ReplayExportPolicy(Func<string?> ValidateStart, Func<string?> ValidateFrame,
    ReplayExportPresentation Presentation)
{
    public string? LogicalPath { get; init; }
}
internal sealed record ReplayExportReceipt(Guid Id, ReplayVideoExportManifest Manifest);
internal sealed record ReplayExportQueueSnapshot(long Revision, ImmutableArray<ReplayExportReceipt> Pending,
    ReplayExportReceipt? Active, ReplayExportReceipt? Retry, string Status, ImmutableArray<string> Failures);

internal sealed record ReplayExportPresentation(ReplayCameraMode Mode, ReplayPresentationProfile Profile,
    float Fov, float Roll, float Distance, float Height, int LookAt, bool Director, bool Track, bool Speed, bool Collision,
    ReplayCameraInterpolation Interpolation, ReplayCameraEase Ease, ImmutableArray<ReplayCameraKeyframe> Keys)
{
    public static ReplayExportPresentation Capture() => new(ReplayCamera.Mode, ReplayCamera.Profile,
        ReplayCamera.FieldOfView, ReplayCamera.Roll, ReplayCamera.Distance, ReplayCamera.Height, ReplayCamera.LookAtSlot,
        ReplayCamera.Director, ReplayCamera.PlayTrack, ReplayCamera.TrackConstantSpeed, ReplayCamera.TrackCollisionAvoidance,
        ReplayCamera.TrackInterpolation, ReplayCamera.TrackEase, ReplayCamera.Track.Keys.ToImmutableArray());
    public bool Matches() => (Director || ReplayCamera.Mode == Mode) && ReplayCamera.Profile == Profile
        && ReplayCamera.FieldOfView == Fov && ReplayCamera.Roll == Roll && ReplayCamera.Distance == Distance
        && ReplayCamera.Height == Height && ReplayCamera.LookAtSlot == LookAt && ReplayCamera.Director == Director
        && ReplayCamera.PlayTrack == Track && ReplayCamera.TrackConstantSpeed == Speed && ReplayCamera.TrackCollisionAvoidance == Collision
        && ReplayCamera.TrackInterpolation == Interpolation && ReplayCamera.TrackEase == Ease
        && ReplayCamera.Track.Keys.SequenceEqual(Keys);
    public void Apply()
    {
        ReplayCamera.SetProfile(Profile); ReplayCamera.SetMode(Mode);
        ReplayCamera.FieldOfView = Fov; ReplayCamera.Roll = Roll; ReplayCamera.Distance = Distance; ReplayCamera.Height = Height;
        ReplayCamera.LookAtSlot = LookAt; ReplayCamera.Director = Director; ReplayCamera.PlayTrack = Track;
        ReplayCamera.TrackConstantSpeed = Speed; ReplayCamera.TrackCollisionAvoidance = Collision;
        ReplayCamera.TrackInterpolation = Interpolation; ReplayCamera.TrackEase = Ease;
        ReplayCamera.Track.Clear(); foreach (var key in Keys) ReplayCamera.Track.Put(key);
    }
}

internal static partial class ReplayExportQueue
{
    private static long _ownershipRevision;
    private static ReplayVideoExportManifest? _nativeRetry;
    private static readonly ConditionalWeakTable<ReplayVideoExportManifest, ReplayExportPolicy> Policies = new();
    private static readonly ConditionalWeakTable<ReplayVideoExportManifest, ReplayExportReceipt> Receipts = new();
    private static ReplayExportReceipt Receipt(ReplayVideoExportManifest job)
        => Receipts.GetValue(job, static value => new(Guid.NewGuid(), value));
    internal static bool EnqueueOwned(ReplayVideoExportManifest job, ReplayExportPolicy policy)
    {
        lock (Gate)
        {
            if (!CheckCapacityLocked()) return false;
            if (Policies.TryGetValue(job, out _)) throw new InvalidOperationException("This exact export has already been accepted.");
            Policies.Add(job, policy);
            if (Enqueue(job)) return true;
            Policies.Remove(job); return false;
        }
    }
    internal static string? ValidateOwnedStart(ReplayVideoExportManifest job)
    {
        if (!Policies.TryGetValue(job, out var policy)) return null;
        try { return policy.ValidateStart(); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { return error.Message; }
    }
    internal static string? ValidateOwnedFrame(ReplayVideoExportManifest job)
    {
        if (!Policies.TryGetValue(job, out var policy)) return null;
        try { return policy.ValidateFrame(); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { return error.Message; }
    }
    internal static ReplayExportPresentation? PresentationFor(ReplayVideoExportManifest job)
        => Policies.TryGetValue(job, out var policy) ? policy.Presentation : null;
    internal static string LogicalPathFor(ReplayVideoExportManifest job)
        => Policies.TryGetValue(job, out var policy) ? policy.LogicalPath ?? job.Replay : job.Replay;
    internal static void NoteCancelled(ReplayVideoExportManifest? job)
    { if (job != null) lock (Gate) { _nativeRetry = job; _ownershipRevision++; } }
    internal static ReplayExportQueueSnapshot OwnerSnapshot()
    {
        // Poll completion first so a queued encoder success cannot be mistaken for a cancellable job.
        _ = ReplayVideoExporter.Active;
        lock (Gate)
        {
            var active = ReplayVideoExporter.OwnedManifest;
            var retry = _nativeRetry ?? _failed ?? (ReplayVideoExporter.State == ReplayExportState.Cancelled ? _last : null);
            return new(_ownershipRevision, Pending.Select(Receipt).ToImmutableArray(), active == null ? null : Receipt(active),
                retry == null ? null : Receipt(retry), _status, _failureSnapshot.ToImmutableArray());
        }
    }
    internal static bool CancelExact(ReplayExportReceipt exact, long revision)
    {
        _ = ReplayVideoExporter.Active;
        lock (Gate)
        {
            if (_ownershipRevision != revision || !ReferenceEquals(ReplayVideoExporter.OwnedManifest, exact.Manifest)) return false;
        }
        if (!ReplayVideoExporter.CancelOwnedExact(exact.Manifest)) return false;
        lock (Gate)
        {
            _ownershipRevision++;
            _status = $"Cancellation requested for {Path.GetFileName(exact.Manifest.SuggestedOutput)} · {Pending.Count} still queued.";
        }
        return true;
    }
    internal static bool RetryExact(ReplayExportReceipt exact, long revision)
    {
        _ = ReplayVideoExporter.Active;
        lock (Gate)
        {
            var retry = _nativeRetry ?? _failed ?? (ReplayVideoExporter.State == ReplayExportState.Cancelled ? _last : null);
            if (_ownershipRevision != revision || !ReferenceEquals(retry, exact.Manifest)) return false;
            return Enqueue(exact.Manifest);
        }
    }
    internal static bool ClearExact(ImmutableArray<ReplayExportReceipt> exact, long revision)
    {
        lock (Gate)
        {
            if (_ownershipRevision != revision || Pending.Count != exact.Length
                || !Pending.Zip(exact, static (job, receipt) => ReferenceEquals(job, receipt.Manifest)).All(static same => same)) return false;
            ClearPending(); return true;
        }
    }
}

internal static partial class ReplayVideoExport
{
    internal static void WriteReviewedManifest(ReplayVideoExportManifest manifest)
    {
        string directory = Path.GetDirectoryName(manifest.FramePattern) ?? throw new IOException("The export directory is unavailable.");
        File.WriteAllText(Path.Combine(directory, "render.json"), JsonSerializer.Serialize(manifest,
            new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(directory, "encode.txt"), "ffmpeg " + manifest.FfmpegArguments + Environment.NewLine);
    }
}
