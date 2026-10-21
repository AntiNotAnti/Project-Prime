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
    internal uint TargetFrame { get; }
    private PreparedCustomReplayArchive(string path, DemoReader reader, ReplayWorldCheckpoint world,
        PreparedReplayWeaponPackage resources, uint target)
    { Path = path; _reader = reader; _world = world; Resources = resources; TargetFrame = target; }
    internal static PreparedCustomReplayArchive Prepare(string path, WeaponPackageStore store, uint? targetFrame = null,
        CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        string fullPath = System.IO.Path.GetFullPath(path);
        DemoReader? reader = null; ReplayWorldCheckpoint? world = null; PreparedReplayWeaponPackage? resources = null;
        try
        {
            reader = DemoReader.Open(fullPath, out var result) ?? throw new InvalidDataException("Cannot prepare custom archive: " + result);
            if (reader.ProtocolVersion != 83 || reader.Metadata is not { FormatVersion: 7, CustomWeapons: not null, LeadInFrames: 0 } metadata
                || metadata.WorldCheckpoint.Length == 0)
                throw new InvalidDataException("Custom file inspection requires an exact format7 world12 baseline without a lead-in.");
            ReplayFormatV3.ValidateCustomArchiveMetadata(metadata);
            world = ReplayWorldCheckpoint.FromCustomBytes(metadata.WorldCheckpoint, metadata.BuildId);
            uint last = checked(metadata.OriginRecordingFrame + reader.DurationFrames);
            uint target = targetFrame ?? metadata.OriginRecordingFrame;
            if (target < metadata.OriginRecordingFrame || target > last) throw new ArgumentOutOfRangeException(nameof(targetFrame));
            // A captured Scene baseline is a completed recording frame. A
            // packet suffix at relative frame0 is ambiguous and stays refused.
            int initialRecords = 0;
            var first = reader.ReadNext();
            while (first is { Frame: 0 } record)
            {
                cancellation.ThrowIfCancellationRequested();
                if (++initialRecords > 4096 || reader.DurationFrames != 0 || record.Data.Length != 1 || record.Data[0] != 255)
                    throw new InvalidDataException("Custom Scene archives require a complete-frame baseline; frame0 packet suffixes are unsupported.");
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
    internal ReplayWorldCheckpoint CopyWorld()
    {
        lock (_gate)
            return _world != null && _reader != null ? ReplayWorldCheckpoint.FromCustomBytes(_world.Bytes, _reader.Metadata?.BuildId)
                : throw new ObjectDisposedException(nameof(PreparedCustomReplayArchive));
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
