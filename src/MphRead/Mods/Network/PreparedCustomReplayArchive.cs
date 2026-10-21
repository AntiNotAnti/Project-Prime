using System;
using System.IO;
using System.Threading;
using MphRead.Mods.CustomWeapons.Packages;
using MphRead.Mods.Replay;

namespace MphRead.Mods.Network;

// Prepare on a worker before constructing a Scene. The reader retains its
// read-only file handle; preparation transfers it exactly once to the session.
internal sealed class PreparedCustomReplayArchive : IDisposable
{
    internal const uint MaximumReconstructionFrames = 60 * 60 * 10;
    private readonly object _gate = new();
    private DemoReader? _reader;
    private ReplayWorldCheckpoint? _world;
    internal PreparedReplayWeaponPackage Resources { get; }
    internal string Path { get; }
    internal byte FormatVersion { get; }
    internal uint TargetFrame { get; }
    private PreparedCustomReplayArchive(string path, DemoReader reader, ReplayWorldCheckpoint world,
        PreparedReplayWeaponPackage resources, uint target)
    { Path = path; FormatVersion = reader.FormatVersion; _reader = reader; _world = world; Resources = resources; TargetFrame = target; }
    internal static PreparedCustomReplayArchive Prepare(string path, WeaponPackageStore store, uint? targetFrame = null,
        CancellationToken cancellation = default) => PrepareCore(path, store, targetFrame, cancellation, allowPostWorld: true);

    // Explicit inspection shares the exact historical preparation used by ordinary
    // format7/8 playback; specialized Scene factories retain their origin checks.
    internal static PreparedCustomReplayArchive PrepareInspection(string path, WeaponPackageStore store, uint? targetFrame = null,
        CancellationToken cancellation = default) => PrepareCore(path, store, targetFrame, cancellation, allowPostWorld: true);

    private static PreparedCustomReplayArchive PrepareCore(string path, WeaponPackageStore store, uint? targetFrame,
        CancellationToken cancellation, bool allowPostWorld)
    {
        cancellation.ThrowIfCancellationRequested();
        string fullPath = System.IO.Path.GetFullPath(path);
        DemoReader? reader = null; ReplayWorldCheckpoint? world = null; PreparedReplayWeaponPackage? resources = null;
        try
        {
            reader = DemoReader.Open(fullPath, out var result) ?? throw new InvalidDataException("Cannot prepare custom archive: " + result);
            if (reader.ProtocolVersion is not (83 or 84 or 85) || reader.Metadata is not { CustomWeapons: not null } metadata
                || metadata.FormatVersion != 7 && (!allowPostWorld || metadata.FormatVersion != 8) || metadata.WorldCheckpoint.Length == 0)
                throw new InvalidDataException("Custom preparation requires an exact admitted world12/13 baseline.");
            if (metadata.FormatVersion == 8 && metadata.Integrity == ReplayIntegrity.Truncated)
                throw new InvalidDataException("Post-world inspection requires a finalized or recovered archive.");
            ReplayFormatV3.ValidateCustomArchiveMetadata(metadata);
            world = ReplayWorldCheckpoint.FromCustomBytes(metadata.WorldCheckpoint, metadata.BuildId);
            uint visibleOrigin = checked(metadata.OriginRecordingFrame + metadata.LeadInFrames);
            uint last = checked(visibleOrigin + reader.DurationFrames);
            uint target = targetFrame ?? visibleOrigin;
            if (target < visibleOrigin || target > last) throw new ArgumentOutOfRangeException(nameof(targetFrame));
            // A baseline is a completed recording frame. Format7 retains its
            // strict zero-suffix refusal. Format8 explicitly retains post-world0
            // facts for application before the first following fixed step.
            int initialRecords = 0;
            var first = reader.ReadNext();
            while (first is { Frame: 0 } record)
            {
                cancellation.ThrowIfCancellationRequested();
                bool quiet = reader.DurationFrames == 0 && metadata.LeadInFrames == 0 && record.Data.Length == 1 && record.Data[0] == 255;
                bool deferred = metadata.FormatVersion == 8 && record.Data.Length > 0 && record.Data[0] == ReplayPostWorldRecord.Tag;
                if (++initialRecords > ReplayPostWorldFrameState.MaximumRecords || !quiet && !deferred)
                    throw new InvalidDataException("Custom archives require an explicit complete-world baseline phase.");
                first = reader.ReadNext();
            }
            if (reader.LastResult != ReplayOpenResult.Success) throw new InvalidDataException("Custom archive baseline scan failed: " + reader.LastResult);
            ReplayCheckpointIndex? nearest = null;
            foreach (var checkpoint in reader.Checkpoints)
            {
                cancellation.ThrowIfCancellationRequested();
                uint absolute = checked(metadata.OriginRecordingFrame + checkpoint.Frame);
                if (absolute > target) break;
                nearest = checkpoint;
            }
            if (nearest is { } selectedCheckpoint)
            {
                uint absolute = checked(metadata.OriginRecordingFrame + selectedCheckpoint.Frame);
                using var candidate = ReplayWorldCheckpoint.FromCustomBytes(reader.ReadCheckpoint(selectedCheckpoint), metadata.BuildId);
                candidate.ValidateCustomArchive(metadata);
                if (candidate.IndexedFirePresentation != world.IndexedFirePresentation
                    || candidate.HasHeadHistoryContract != world.HasHeadHistoryContract || candidate.HeadHistoryOrigin != world.HeadHistoryOrigin)
                    throw new InvalidDataException("Custom checkpoint firing/head-history contract differs from its archive baseline.");
                if (candidate.Frame != absolute) throw new InvalidDataException("Custom checkpoint index differs from its recorded frame.");
                if (candidate.Frame >= world.Frame)
                { var replacement = ReplayWorldCheckpoint.FromCustomBytes(candidate.Bytes, metadata.BuildId); world.Dispose(); world = replacement; }
            }
            if (world.Frame > target || target - world.Frame > MaximumReconstructionFrames)
                throw new InvalidDataException("Custom archive seek exceeds its bounded reconstruction window; a nearer world checkpoint is required.");
            cancellation.ThrowIfCancellationRequested();
            resources = PreparedReplayWeaponPackage.Prepare(metadata.CustomWeapons, store, cancellation);
            cancellation.ThrowIfCancellationRequested();
            return new(fullPath, reader, world, resources, target);
        }
        catch { resources?.Dispose(); world?.Dispose(); reader?.Dispose(); throw; }
    }
    internal ReplayPlaybackSession CreateRequestSession()
    {
        lock (_gate)
        {
            if (_reader == null || _world == null) throw new ObjectDisposedException(nameof(PreparedCustomReplayArchive));
            var session = new ReplayPlaybackSession(new PassiveReplaySessionHost());
            try
            {
                session.JoinCustomArchiveRequest(_reader.Metadata!, Path, _reader.DurationFrames);
                return session;
            }
            catch { session.Dispose(); throw; }
        }
    }
    internal ReplayWorldCheckpoint CopyWorld()
    {
        lock (_gate)
            return _world != null && _reader != null ? ReplayWorldCheckpoint.FromCustomBytes(_world.Bytes, _reader.Metadata?.BuildId)
                : throw new ObjectDisposedException(nameof(PreparedCustomReplayArchive));
    }
    internal bool MatchesArchiveBaseline(ReplayMetadata expected, uint duration)
    {
        lock (_gate)
        {
            var metadata = _reader?.Metadata;
            return metadata != null && metadata.FormatVersion == expected.FormatVersion
                && _reader!.DurationFrames == duration && metadata.ProtocolVersion == expected.ProtocolVersion
                && metadata.OriginRecordingFrame == expected.OriginRecordingFrame && metadata.RoomKey == expected.RoomKey
                && metadata.LeadInFrames == expected.LeadInFrames
                && metadata.Mode == expected.Mode && metadata.MapHash == expected.MapHash
                && metadata.IndexedFireContinuation == expected.IndexedFireContinuation
                && metadata.TimedShotCertificates == expected.TimedShotCertificates
                && metadata.WorldCheckpoint.AsSpan().SequenceEqual(expected.WorldCheckpoint)
                && metadata.CustomWeapons != null && expected.CustomWeapons != null
                && metadata.CustomWeapons.CopyBytes().AsSpan().SequenceEqual(expected.CustomWeapons.CopyBytes());
        }
    }
    internal DemoReader TakeReader()
    {
        lock (_gate)
        {
            var reader = _reader ?? throw new ObjectDisposedException(nameof(PreparedCustomReplayArchive));
            _reader = null; return reader;
        }
    }
    public void Dispose()
    {
        lock (_gate) { _reader?.Dispose(); _reader = null; _world?.Dispose(); _world = null; }
        Resources.Dispose();
    }
}
