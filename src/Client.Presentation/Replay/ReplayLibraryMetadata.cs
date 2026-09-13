using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MphRead.Mods.Network;

public enum ReplayRecoveryStatus : byte
{
    Complete,
    Recovered,
    Damaged
}

public enum ReplayCompatibilityStatus : byte
{
    Ready,
    Legacy,
    PartialCompatibility,
    Unsupported,
    Damaged
}

public sealed record ReplayLibraryMetadata(
    string ReplayId,
    string FilePath,
    string FileName,
    string MapKey,
    string MapName,
    GameMode Mode,
    DateTime RecordedAt,
    TimeSpan Duration,
    long FileSize,
    byte ReplayFormat,
    byte ReplayProtocol,
    int PlayerCount,
    int HighlightCount,
    ReplayRecoveryStatus RecoveryStatus,
    ReplayCompatibilityStatus CompatibilityStatus)
{
    public uint DurationFrames => (uint)Math.Clamp(
        Math.Round(Duration.TotalSeconds * 60, MidpointRounding.AwayFromZero),
        0, uint.MaxValue);
}

/// <summary>
/// Owns the small derived sidecar next to a replay. Replay bytes remain the
/// authority; every sidecar is validated against the source length and write
/// stamp and can be discarded and regenerated at any time.
/// </summary>
public sealed class ReplayLibraryMetadataService
{
    public const int SidecarSchemaVersion = 1;
    public const int MaximumSidecarBytes = 64 * 1024;
    private const int MaximumDecodedRecords = 4_000_000;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        MaxDepth = 8,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static string SidecarPath(string replayPath) => replayPath + ".meta";

    public bool TryRead(string replayPath, out ReplayLibraryMetadata? metadata)
    {
        metadata = null;
        try
        {
            var replay = new FileInfo(replayPath);
            var sidecar = new FileInfo(SidecarPath(replayPath));
            if (!replay.Exists || !sidecar.Exists || sidecar.Length is < 2 or > MaximumSidecarBytes)
                return false;
            SidecarDocument? document = JsonSerializer.Deserialize<SidecarDocument>(
                File.ReadAllBytes(sidecar.FullName), JsonOptions);
            if (document is null || document.Schema != SidecarSchemaVersion
                || document.SourceLength != replay.Length
                || document.SourceWriteUtcTicks != replay.LastWriteTimeUtc.Ticks
                || document.Metadata is null
                || !String.Equals(Path.GetFullPath(document.Metadata.FilePath),
                    replay.FullName, PathComparison())
                || document.Metadata.FileSize != replay.Length
                || document.Metadata.ReplayId.Length != 64)
                return false;
            metadata = document.Metadata;
            return true;
        }
        catch (Exception error) when (IsMetadataFailure(error)) { return false; }
    }

    public ReplayLibraryMetadata GetOrCreate(string replayPath,
        ReplayHighlightMetadata? highlights = null,
        System.Threading.CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (TryRead(replayPath, out ReplayLibraryMetadata? cached) && cached is not null
            && (highlights is null || cached.ReplayId == highlights.ReplayFingerprint
                && cached.HighlightCount == highlights.Highlights.Count))
            return cached;

        ReplayLibraryMetadata metadata = ReadAuthoritative(replayPath, highlights,
            cancellationToken);
        TryWrite(replayPath, metadata);
        return metadata;
    }

    private static ReplayLibraryMetadata ReadAuthoritative(string replayPath,
        ReplayHighlightMetadata? highlights,
        System.Threading.CancellationToken cancellationToken)
    {
        var file = new FileInfo(replayPath);
        if (!file.Exists)
            return Damaged(file, highlights?.ReplayFingerprint ?? "");
        string replayId = highlights?.ReplayFingerprint ?? Fingerprint(replayPath,
            cancellationToken);
        if (file.Length is < ReplayFile.HeaderSize or > ReplayArchive.MaximumFileBytes)
            return Damaged(file, replayId);
        using ReplayReader? reader = ReplayReader.Open(replayPath);
        if (reader is null) return Damaged(file, replayId);
        string mapKey = "";
        GameMode mode = GameMode.None;
        int playerCount = 0;
        uint durationFrames = reader.LastFrame;
        int records = 0;
        var roster = new NetRosterEntry[8];
        while (reader.ReadNext() is ReplayRecord record)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++records > MaximumDecodedRecords)
                return Damaged(file, replayId, reader.FormatVersion,
                    reader.ProtocolVersion);
            durationFrames = Math.Max(durationFrames, record.Frame);
            if (record.Data.Length < 2) continue;
            ReadOnlySpan<byte> body = record.Data.AsSpan(1);
            switch ((ReplayRecordKind)record.Data[0])
            {
                case ReplayRecordKind.Match:
                    if (MatchTransitionPacket.TryRead(body,
                            out MatchTransitionPacket match))
                    {
                        mapKey = match.Room;
                        mode = match.Mode;
                    }
                    break;
                case ReplayRecordKind.Roster:
                    int count = 0;
                    bool rosterValid = body.Length >= 4
                        && (reader.ProtocolVersion >= 19
                            ? SessionRosterPacket.TryRead(body[4..], roster, out _, out count)
                            : reader.ProtocolVersion >= 8
                                ? Protocol18ReplayRoster.TryRead(body[4..], roster, out _, out count)
                                : Protocol7ReplayRoster.TryRead(body[4..], roster, out _, out count));
                    if (rosterValid)
                        playerCount = Math.Max(playerCount, count);
                    break;
            }
        }

        ReplayRecoveryStatus recovery = reader.RecoveredTail
            ? ReplayRecoveryStatus.Recovered : ReplayRecoveryStatus.Complete;
        ReplayCompatibilityStatus compatibility = Compatibility(reader.ProtocolVersion,
            reader.RecoveredTail);
        return new ReplayLibraryMetadata(replayId, file.FullName, file.Name,
            mapKey, mapKey, mode, file.LastWriteTime, TimeSpan.FromSeconds(
                durationFrames / 60d), file.Length, reader.FormatVersion,
            reader.ProtocolVersion, playerCount, highlights?.Highlights.Count ?? 0,
            recovery, compatibility);
    }

    private static ReplayCompatibilityStatus Compatibility(byte protocol,
        bool recovered)
    {
        if (!ReplayFile.IsSupportedProtocol(protocol))
            return ReplayCompatibilityStatus.Unsupported;
        if (protocol < 8) return ReplayCompatibilityStatus.Legacy;
        if (protocol < NetHeader.Version)
            return ReplayCompatibilityStatus.PartialCompatibility;
        return recovered ? ReplayCompatibilityStatus.PartialCompatibility
            : ReplayCompatibilityStatus.Ready;
    }

    private static ReplayLibraryMetadata Damaged(FileInfo file, string replayId,
        byte format = 0, byte protocol = 0)
        => new(replayId, file.FullName, file.Name, "", "", GameMode.None,
            file.Exists ? file.LastWriteTime : default, TimeSpan.Zero,
            file.Exists ? file.Length : 0, format, protocol, 0, 0,
            ReplayRecoveryStatus.Damaged, ReplayCompatibilityStatus.Damaged);

    private static string Fingerprint(string path,
        System.Threading.CancellationToken cancellationToken)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read,
            FileShare.Read);
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[128 * 1024];
        int count;
        while ((count = stream.Read(buffer)) != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            hash.AppendData(buffer, 0, count);
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void TryWrite(string replayPath, ReplayLibraryMetadata metadata)
    {
        string destination = SidecarPath(replayPath);
        string? temporary = null;
        try
        {
            var replay = new FileInfo(replayPath);
            var document = new SidecarDocument
            {
                Schema = SidecarSchemaVersion,
                SourceLength = replay.Length,
                SourceWriteUtcTicks = replay.LastWriteTimeUtc.Ticks,
                Metadata = metadata
            };
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
            if (json.Length > MaximumSidecarBytes) return;
            temporary = destination + $".{Guid.NewGuid():N}.tmp";
            using (var stream = new FileStream(temporary, FileMode.CreateNew,
                FileAccess.Write, FileShare.None))
            {
                stream.Write(json);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, destination, overwrite: true);
            temporary = null;
        }
        catch (Exception error) when (IsMetadataFailure(error)) { }
        finally
        {
            if (temporary is not null)
            {
                try { File.Delete(temporary); }
                catch (Exception error) when (IsMetadataFailure(error)) { }
            }
        }
    }

    private static bool IsMetadataFailure(Exception error)
        => error is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException or JsonException or CryptographicException;

    private static StringComparison PathComparison()
        => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private sealed class SidecarDocument
    {
        public int Schema { get; set; }
        public long SourceLength { get; set; }
        public long SourceWriteUtcTicks { get; set; }
        public ReplayLibraryMetadata? Metadata { get; set; }
    }
}

/// <summary>Serial background publication used only after a recorder closes.</summary>
internal static class ReplaySidecarGenerationQueue
{
    private static readonly System.Collections.Concurrent.ConcurrentQueue<string> Pending = new();
    private static int _worker;

    internal static void Queue(string path)
    {
        if (String.IsNullOrWhiteSpace(path)) return;
        Pending.Enqueue(path);
        if (System.Threading.Interlocked.CompareExchange(ref _worker, 1, 0) == 0)
            _ = System.Threading.Tasks.Task.Run(Drain);
    }

    private static void Drain()
    {
        try
        {
            var service = new ReplayLibraryMetadataService();
            while (Pending.TryDequeue(out string? path))
                service.GetOrCreate(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or InvalidDataException or CryptographicException or ArgumentException
            or NotSupportedException) { }
        finally
        {
            System.Threading.Volatile.Write(ref _worker, 0);
            if (!Pending.IsEmpty
                && System.Threading.Interlocked.CompareExchange(ref _worker, 1, 0) == 0)
                _ = System.Threading.Tasks.Task.Run(Drain);
        }
    }
}
