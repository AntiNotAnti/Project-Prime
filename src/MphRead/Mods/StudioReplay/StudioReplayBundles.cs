#if !ANDROID && !MPHREAD_SERVER
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.MapGen;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;

namespace MphRead.Mods.StudioReplay;

public sealed partial class StudioReplayPlayer
{
    /// <summary>Carry the exact immutable custom package, or an explicit required-hash reference.</summary>
    public Task<string> ExportPortableAsync(string destination, bool includeCustomMap = true, CancellationToken cancellation = default)
    {
        RequireOwner();
        if (_player?.Current.Session.Metadata is not { } metadata || _playbackPath == null)
            throw new InvalidOperationException("Prepare the replay before exporting its portable package.");
        string source = _playbackPath, logical = LogicalPath;
        MapContentIdentity? identity = metadata.CustomMapIdentity;
        string? exactPackage = identity is { } required
            ? Path.Combine(_cacheRoot, "packages", required.PackageHash.ToString(), "source.ppmap") : null;
        var keys = CameraKeys;
        byte[] cameraState = ExportCameraSidecarState();
        var reels = ReplayReels.Segments(logical).ToArray();
        cancellation.ThrowIfCancellationRequested();
        IDisposable resources = RetainJobResources();
        return Task.Run(() =>
        {
            using var retainedResources = resources;
            string staging = Path.GetFullPath(destination) + "." + Guid.NewGuid().ToString("N") + ".staging";
            string work = Path.Combine(_cacheRoot, "portable", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                cancellation.ThrowIfCancellationRequested();
                string replay = Path.Combine(work, "replay.ppdemo");
                CopyChecked(source, replay, cancellation);
                // Validate the copied source through the canonical detached reader, without installing any map.
                using var parsed = PreparedReplaySource.File(replay, cancellation: cancellation, prepareMap: false);
                if (parsed.Session.Metadata?.CustomMapIdentity is { } copied && (identity == null || !copied.Matches(identity.Value)))
                    throw new InvalidDataException("Replay map identity changed while preparing the portable package.");
                if (File.Exists(logical + ReplayAnnotations.Extension))
                    CopyChecked(logical + ReplayAnnotations.Extension, replay + ReplayAnnotations.Extension, cancellation);
                if (reels.Length != 0) File.WriteAllText(replay + ReplayReels.Extension,
                    JsonSerializer.Serialize(new ReplayReelDocument { SourceReplay = "replay.ppdemo", Segments = reels.ToList() }));
                string? mapHash = null;
                if (identity is { } required)
                {
                    mapHash = required.PackageHash.ToString();
                    if (includeCustomMap)
                    {
                        if (exactPackage == null || !File.Exists(exactPackage) || !MapContentIdentity.FromPackage(exactPackage).Matches(required))
                            throw new InvalidDataException("The exact recorded custom-map package is unavailable; a same-name/latest version cannot substitute.");
                        string target = Path.Combine(work, "map.ppmap"); CopyChecked(exactPackage, target, cancellation);
                        if (!MapContentIdentity.FromPackage(target).Matches(required)) throw new InvalidDataException("Portable map bytes changed during copying.");
                    }
                }
                File.WriteAllText(Path.Combine(work, "manifest.json"), JsonSerializer.Serialize(new
                {
                    Version = 1, Replay = "replay.ppdemo", ReplayHash = FileHash(replay),
                    Map = includeCustomMap && identity.HasValue ? "map.ppmap" : null,
                    RequiredMapId = identity?.MapId, RequiredRoom = identity?.RoomKey,
                    RequiredContentHash = identity?.ContentHash.ToString(), RequiredPackageHash = mapHash,
                    CameraKeys = keys, CameraState = cameraState
                }, new JsonSerializerOptions { IncludeFields = true }));
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
                using (var zip = ZipFile.Open(staging, ZipArchiveMode.Create))
                    foreach (string file in Directory.GetFiles(work))
                    { cancellation.ThrowIfCancellationRequested(); zip.CreateEntryFromFile(file, Path.GetFileName(file), CompressionLevel.Optimal); }
                cancellation.ThrowIfCancellationRequested(); File.Move(staging, destination, overwrite: true); return destination;
            }
            finally { if (File.Exists(staging)) File.Delete(staging); if (Directory.Exists(work)) Directory.Delete(work, recursive: true); }
        });
    }

    /// <summary>Validates an immutable portable recording and exact custom identity in a new private cache directory.</summary>
    public static Task<StudioReplayPortableImport> ImportPortableAsync(string bundle, string privateImportRoot,
        CancellationToken cancellation = default) => Task.Run(() =>
    {
        string root = Path.Combine(Path.GetFullPath(privateImportRoot), Guid.NewGuid().ToString("N"));
        bool accepted = false;
        try
        {
            StudioPortableArchive.Extract(bundle, root, cancellation);
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "manifest.json")));
            var data = manifest.RootElement;
            if (data.GetProperty("Version").GetInt32() != 1 || data.GetProperty("Replay").GetString() != "replay.ppdemo")
                throw new InvalidDataException("Unsupported portable replay manifest.");
            string replay = Path.Combine(root, "replay.ppdemo");
            if (!StringComparer.Ordinal.Equals(data.GetProperty("ReplayHash").GetString(), FileHash(replay)))
                throw new InvalidDataException("Portable replay content hash does not match its manifest.");
            using var parsed = PreparedReplaySource.File(replay, cancellation: cancellation, prepareMap: false);
            MapContentIdentity? required = parsed.Session.Metadata?.CustomMapIdentity;
            string? expected = data.GetProperty("RequiredPackageHash").GetString();
            string? member = data.GetProperty("Map").GetString();
            if (required is { } map)
            {
                if (!StringComparer.Ordinal.Equals(expected, map.PackageHash.ToString())
                    || !StringComparer.Ordinal.Equals(data.GetProperty("RequiredContentHash").GetString(), map.ContentHash.ToString())
                    || !StringComparer.OrdinalIgnoreCase.Equals(data.GetProperty("RequiredRoom").GetString(), map.RoomKey)
                    || data.GetProperty("RequiredMapId").GetGuid() != map.MapId)
                    throw new InvalidDataException("Portable replay map reference differs from the recorded exact identity.");
                if (member != null && (member != "map.ppmap" || !MapContentIdentity.FromPackage(Path.Combine(root, member)).Matches(map)))
                    throw new InvalidDataException("Portable replay carries a different custom-map package.");
            }
            else if (member != null || expected != null) throw new InvalidDataException("Portable replay declares an unrecorded custom map.");
            if (File.Exists(Path.Combine(root, "map.ppmap")) && member == null) throw new InvalidDataException("Portable replay carries an undeclared custom map.");
            if (data.TryGetProperty("CameraState", out var stateData) && stateData.ValueKind != JsonValueKind.Null)
            {
                byte[] state = stateData.Deserialize<byte[]>() ?? throw new InvalidDataException("Portable camera state is missing.");
                var camera = new ReplayCameraTrack();
                if (!camera.ImportState(state)) throw new InvalidDataException(camera.LastError);
                if (camera.WindowDuration is { } duration && duration != parsed.Session.LastFrame)
                    throw new InvalidDataException("Portable camera window differs from the exact recording duration.");
                if (!camera.Save(replay)) throw new IOException(camera.LastError);
            }
            else if (data.TryGetProperty("CameraKeys", out var cameraData))
            {
                var keys = cameraData.Deserialize<StudioReplayCameraKey[]>(new JsonSerializerOptions { IncludeFields = true }) ?? Array.Empty<StudioReplayCameraKey>();
                if (keys.Length > ReplayCameraTrack.MaxKeys) throw new InvalidDataException("Portable replay camera keys exceed the canonical limit.");
                var camera = new ReplayCameraTrack();
                foreach (var key in keys)
                {
                    if (!camera.Put(new(key.Frame, Tk(key.Position), Tk(key.Rotation), key.Fov * MathF.PI / 180,
                        checked((sbyte)key.LookAtSlot), key.Roll * MathF.PI / 180, (ReplayCameraInterpolation)key.Interpolation,
                        (ReplayCameraEase)key.Ease, key.IncomingTangent is { } incoming ? Tk(incoming) : null,
                        key.OutgoingTangent is { } outgoing ? Tk(outgoing) : null, key.FovIncomingTangent * MathF.PI / 180,
                        key.FovOutgoingTangent * MathF.PI / 180, key.RollIncomingTangent * MathF.PI / 180, key.RollOutgoingTangent * MathF.PI / 180)))
                        throw new InvalidDataException("Portable replay contains an invalid camera key.");
                }
                if (keys.Length != 0 && !camera.Save(replay)) throw new IOException(camera.LastError);
            }
            cancellation.ThrowIfCancellationRequested(); accepted = true;
            return new StudioReplayPortableImport(replay, member == null ? Array.Empty<string>() : new[] { root });
        }
        finally { if (!accepted && Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }, cancellation);

    /// <summary>Export allowlisted diagnostics; credential stores, environment, packet bytes and arbitrary logs are excluded.</summary>
    public Task<string> ExportDiagnosticBundleAsync(string destination, string renderBackend,
        CancellationToken cancellation = default)
    {
        RequireOwner();
        var state = Status;
        var metadata = _player?.Current.Session.Metadata;
        var data = new
        {
            Version = 1,
            Build = typeof(StudioReplayPlayer).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            Replay = metadata == null ? null : new { metadata.FormatVersion, metadata.ProtocolVersion, metadata.TickRate,
                metadata.BuildVersion, metadata.DurationFrames, metadata.Integrity, metadata.RoomKey,
                MapId = metadata.CustomMapIdentity?.MapId, PackageHash = metadata.CustomMapIdentity?.PackageHash.ToString(),
                ContentHash = metadata.CustomMapIdentity?.ContentHash.ToString() },
            Checkpoints = new { state.CheckpointCount, state.CheckpointBytes },
            Seek = new { state.Frame, state.DurationFrames, state.State, Performance },
            VerificationFailure = Redact(state.Error),
            RenderBackend = Redact(renderBackend),
            Combat = _player?.CanPresent == true ? CombatAt(state.Frame) : Array.Empty<StudioReplayCombat>(),
            Camera = CameraKeys,
            Markers = Markers.Select(m => m with { Name = Redact(m.Name) ?? "" }).ToArray(),
            Exports = Exports.Select(e => new { e.Id, e.State, e.Frames, e.TotalFrames, Error = Redact(e.Error) }).ToArray()
        };
        string json = StudioDiagnosticRedaction.Serialize(data);
        return Task.Run(() =>
        {
            string staging = Path.GetFullPath(destination) + "." + Guid.NewGuid().ToString("N") + ".staging";
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
            try
            {
                cancellation.ThrowIfCancellationRequested();
                using (var archive = ZipFile.Open(staging, ZipArchiveMode.Create))
                using (var writer = new StreamWriter(archive.CreateEntry("diagnostics.json").Open())) writer.Write(json);
                cancellation.ThrowIfCancellationRequested(); File.Move(staging, destination, overwrite: true); return destination;
            }
            finally { if (File.Exists(staging)) File.Delete(staging); }
        }, cancellation);
    }
    private static string? Redact(string? text)
        => StudioDiagnosticRedaction.Redact(text);
    private static string FileHash(string file) { using var input = File.OpenRead(file); return Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant(); }
    private static void CopyChecked(string source, string destination, CancellationToken cancellation)
    {
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        byte[] buffer = new byte[65536]; int count;
        while ((count = input.Read(buffer)) != 0) { cancellation.ThrowIfCancellationRequested(); output.Write(buffer, 0, count); }
        output.Flush(flushToDisk: true);
    }
}

#endif
