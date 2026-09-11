using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MphRead.Mods.Network;

public enum ReplayHighlightMetadataStatus : byte
{
    Ready = 1,
    RecoveredReplay,
    InvalidReplay,
    UnsupportedReplay
}

public sealed record ReplayHighlightMetadata(
    ReplayHighlightMetadataStatus Status,
    string ReplayFingerprint,
    int AnalyzerVersion,
    byte ReplayProtocol,
    uint DurationFrames,
    IReadOnlyList<ReplayHighlight> Highlights,
    bool FromCache,
    string? Error)
{
    public bool IsAvailable => Status is ReplayHighlightMetadataStatus.Ready
        or ReplayHighlightMetadataStatus.RecoveredReplay;
}

/// <summary>
/// Reads authoritative replay facts and owns a separate, replaceable metadata
/// cache. Replay bytes are opened read-only and are never rewritten.
/// </summary>
public sealed class ReplayHighlightMetadataService
{
    public const int CacheSchemaVersion = 1;
    public const int MaximumCacheBytes = 64 * 1024;
    private const int MaximumDecodedRecords = 4_000_000;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        MaxDepth = 8,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private readonly HighlightAnalyzer _analyzer;

    public ReplayHighlightMetadataService(string applicationRoot,
        HighlightAnalyzer? analyzer = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationRoot);
        CacheDirectory = Path.Combine(Path.GetFullPath(applicationRoot), "cache",
            "replay-highlights");
        _analyzer = analyzer ?? new HighlightAnalyzer();
    }

    public string CacheDirectory { get; }

    public ReplayHighlightMetadata Get(string replayPath)
    {
        if (String.IsNullOrWhiteSpace(replayPath))
            return Failure(ReplayHighlightMetadataStatus.InvalidReplay,
                "Replay path is empty.");
        string fingerprint;
        try
        {
            var file = new FileInfo(replayPath);
            if (!file.Exists || file.Length is < ReplayFile.HeaderSize
                or > ReplayArchive.MaximumFileBytes)
            {
                return Failure(ReplayHighlightMetadataStatus.InvalidReplay,
                    "Replay file is missing or exceeds its supported bounds.");
            }
            using FileStream stream = new(replayPath, FileMode.Open, FileAccess.Read,
                FileShare.Read);
            fingerprint = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }
        catch (Exception error) when (IsLocalIoFailure(error))
        {
            return Failure(ReplayHighlightMetadataStatus.InvalidReplay,
                "Replay file could not be fingerprinted.");
        }

        using ReplayReader? reader = ReplayReader.Open(replayPath);
        if (reader is null)
            return Failure(ReplayHighlightMetadataStatus.InvalidReplay,
                "Replay format is damaged or unsupported.", fingerprint);
        if (reader.ProtocolVersion < 8 || reader.ProtocolVersion > NetHeader.Version)
        {
            return Failure(ReplayHighlightMetadataStatus.UnsupportedReplay,
                $"Replay protocol {reader.ProtocolVersion} has no supported highlight event stream.",
                fingerprint, reader.ProtocolVersion);
        }

        string cachePath = CachePath(fingerprint);
        if (TryReadCache(cachePath, fingerprint, reader.ProtocolVersion,
                out ReplayHighlightMetadata? cached) && cached is not null) return cached;

        if (!TryDecode(reader, out List<ReplayHighlightEvent> events,
                out List<ReplayHighlightTimelineAnchor> timeline,
                out string? decodeError))
        {
            return Failure(ReplayHighlightMetadataStatus.InvalidReplay,
                decodeError ?? "Replay event stream exceeds its analysis bounds.",
                fingerprint, reader.ProtocolVersion);
        }

        uint duration = reader.LastFrame;
        IReadOnlyList<ReplayHighlight> highlights;
        try { highlights = _analyzer.Analyze(events, timeline, duration); }
        catch (ArgumentException)
        {
            return Failure(ReplayHighlightMetadataStatus.InvalidReplay,
                "Replay highlight facts exceed their supported bounds.", fingerprint,
                reader.ProtocolVersion);
        }
        ReplayHighlightMetadataStatus status = reader.RecoveredTail
            ? ReplayHighlightMetadataStatus.RecoveredReplay
            : ReplayHighlightMetadataStatus.Ready;
        var metadata = new ReplayHighlightMetadata(status, fingerprint,
            HighlightAnalyzer.Version, reader.ProtocolVersion, duration,
            highlights, FromCache: false,
            reader.RecoveredTail ? "The damaged replay tail was ignored." : null);
        TryWriteCache(cachePath, metadata);
        return metadata;
    }

    public string CachePath(string replayFingerprint)
    {
        if (!ValidFingerprint(replayFingerprint))
            throw new ArgumentException("Replay fingerprint is invalid.",
                nameof(replayFingerprint));
        return Path.Combine(CacheDirectory,
            $"v{HighlightAnalyzer.Version}-{replayFingerprint}.json");
    }

    private static bool TryDecode(ReplayReader reader,
        out List<ReplayHighlightEvent> events,
        out List<ReplayHighlightTimelineAnchor> timeline, out string? error)
    {
        events = new List<ReplayHighlightEvent>();
        timeline = new List<ReplayHighlightTimelineAnchor>();
        error = null;
        var lastTimelineFrame = new Dictionary<uint, uint>();
        var snapshotPlayers = new SnapshotPlayer[8];
        int records = 0;
        while (reader.ReadNext() is ReplayRecord record)
        {
            if (++records > MaximumDecodedRecords)
            {
                error = "Replay contains too many decoded records.";
                return false;
            }
            if (record.Data.Length < 1) continue;
            ReadOnlySpan<byte> body = record.Data.AsSpan(1);
            switch ((ReplayRecordKind)record.Data[0])
            {
                case ReplayRecordKind.Match:
                    if (ReplayTimelineTickReader.TryRead(record.Data, 0,
                            out uint matchTick, reader.ProtocolVersion)
                        && body.Length >= 8)
                        AddTimeline(record.Frame, matchTick,
                            BinaryPrimitives.ReadUInt32LittleEndian(body), timeline,
                            lastTimelineFrame);
                    break;
                case ReplayRecordKind.Snapshot:
                    if (SnapshotPacket.TryRead(body, snapshotPlayers,
                            out SnapshotPacket snapshot, out _))
                        AddTimeline(record.Frame, snapshot.ServerTick,
                            snapshot.MatchId, timeline, lastTimelineFrame);
                    break;
                case ReplayRecordKind.World:
                    if (body.Length >= WorldPacket.HeaderSize)
                    {
                        uint matchId = BinaryPrimitives.ReadUInt32LittleEndian(body);
                        if (WorldPacket.TryValidate(body, matchId))
                            AddTimeline(record.Frame,
                                BinaryPrimitives.ReadUInt32LittleEndian(body[8..]),
                                matchId, timeline, lastTimelineFrame);
                    }
                    break;
                case ReplayRecordKind.Event:
                    if (TryDecodeEvent(record.Frame, reader.ProtocolVersion, body,
                            out ReplayHighlightEvent highlightEvent))
                    {
                        if (events.Count == HighlightAnalyzer.MaximumEvents)
                        {
                            error = "Replay contains too many highlight events.";
                            return false;
                        }
                        events.Add(highlightEvent);
                    }
                    break;
            }
        }
        return true;
    }

    private static void AddTimeline(uint frame, uint tick, uint matchId,
        List<ReplayHighlightTimelineAnchor> timeline,
        Dictionary<uint, uint> lastTimelineFrame)
    {
        if (matchId == 0 || timeline.Count == HighlightAnalyzer.MaximumTimelinePoints)
            return;
        // Twenty-Hz snapshots do not need twenty-Hz anchors for a one-to-one
        // fixed-tick mapping. One point each half second keeps a twelve-hour
        // recording inside the hard bound while retaining nearby anchors.
        if (lastTimelineFrame.TryGetValue(matchId, out uint previous)
            && frame >= previous && frame - previous < 30) return;
        timeline.Add(new ReplayHighlightTimelineAnchor(frame, tick, matchId));
        lastTimelineFrame[matchId] = frame;
    }

    private static bool TryDecodeEvent(uint frame, byte protocol,
        ReadOnlySpan<byte> body, out ReplayHighlightEvent value)
    {
        value = default;
        if (body.Length < 5) return false;
        uint matchId = BinaryPrimitives.ReadUInt32LittleEndian(body);
        ReliableEventType type = (ReliableEventType)body[4];
        ReadOnlySpan<byte> payload = body[5..];
        if (type == ReliableEventType.Kill && protocol >= 8
            && KillEvent.TryRead(payload, out KillEvent kill)
            && kill.MatchId == matchId)
        {
            bool headshot = (kill.Flags & KillEventFlags.Headshot) != 0;
            ReplayMarker markers = ReplayMarker.Kill
                | (headshot ? ReplayMarker.Headshot : ReplayMarker.None);
            CombatActor focus = kill.Killer.IsValid ? kill.Killer : kill.Victim;
            value = new ReplayHighlightEvent(frame, kill.Tick, matchId, kill.Id,
                kill.Id, focus, headshot ? HighlightKind.Headshot
                    : HighlightKind.Kill, markers, ReplayHighlightSourceKind.Kill);
            return true;
        }
        if (type == ReliableEventType.MatchAward && protocol >= 9
            && MatchAwardPacket.TryRead(payload, out MatchAwardPacket awardPacket)
            && awardPacket.MatchId == matchId
            && MatchAwardPacketConversion.TryToAward(awardPacket,
                out MatchAward award)
            && TryMapAward(award.Kind, out HighlightKind awardKind,
                out ReplayMarker awardMarkers))
        {
            value = new ReplayHighlightEvent(frame, award.Tick, matchId,
                award.AwardId, award.SourceEventId, award.Subject, awardKind,
                ReplayMarker.Award | awardMarkers,
                ReplayHighlightSourceKind.MatchAward);
            return true;
        }
        if (type == ReliableEventType.MatchSemantic && protocol >= 9
            && MatchSemanticEventPacket.TryRead(payload,
                out MatchSemanticEventPacket semanticPacket)
            && semanticPacket.MatchId == matchId
            && MatchSemanticEventPacketConversion.TryToEvent(semanticPacket,
                out MatchEvent semantic)
            && TryMapSemantic(semantic.Kind, out HighlightKind semanticKind,
                out ReplayMarker semanticMarkers))
        {
            value = new ReplayHighlightEvent(frame, semantic.Tick, matchId,
                semantic.Id, semantic.Id, semantic.Subject, semanticKind,
                semanticMarkers, ReplayHighlightSourceKind.MatchSemantic);
            return true;
        }
        return false;
    }

    private static bool TryMapAward(MatchAwardKind kind,
        out HighlightKind highlight, out ReplayMarker markers)
    {
        markers = ReplayMarker.None;
        highlight = kind switch
        {
            MatchAwardKind.FirstHunt => HighlightKind.FirstHunt,
            MatchAwardKind.DoubleKill => HighlightKind.DoubleKill,
            MatchAwardKind.TripleKill => HighlightKind.TripleKill,
            MatchAwardKind.Interceptor => HighlightKind.Interceptor,
            MatchAwardKind.Defender => HighlightKind.Defender,
            MatchAwardKind.PrimeSlayer => HighlightKind.PrimeSlayer,
            MatchAwardKind.Capture => HighlightKind.ObjectiveCapture,
            MatchAwardKind.Assist => HighlightKind.Assist,
            _ => default
        };
        if (kind is MatchAwardKind.DoubleKill or MatchAwardKind.TripleKill)
            markers |= ReplayMarker.MultiKill;
        return HighlightScoringPolicy.IsSupported(highlight);
    }

    private static bool TryMapSemantic(MatchEventKind kind,
        out HighlightKind highlight, out ReplayMarker markers)
    {
        markers = kind switch
        {
            MatchEventKind.ObjectiveCaptured => ReplayMarker.FlagCapture,
            MatchEventKind.NodeCaptured => ReplayMarker.NodeCapture,
            MatchEventKind.PrimeChanged => ReplayMarker.PrimeChange,
            MatchEventKind.MatchPointReached => ReplayMarker.MatchPoint,
            MatchEventKind.OvertimeStarted => ReplayMarker.Overtime,
            MatchEventKind.MatchEnded => ReplayMarker.MatchEnd,
            _ => ReplayMarker.None
        };
        highlight = kind switch
        {
            MatchEventKind.ObjectiveCaptured => HighlightKind.ObjectiveCapture,
            MatchEventKind.NodeCaptured => HighlightKind.NodeCapture,
            MatchEventKind.ObjectiveDefended => HighlightKind.Defender,
            MatchEventKind.PrimeChanged => HighlightKind.PrimeChange,
            MatchEventKind.MatchPointReached => HighlightKind.MatchPoint,
            MatchEventKind.OvertimeStarted => HighlightKind.Overtime,
            MatchEventKind.MatchEnded => HighlightKind.MatchEnd,
            _ => default
        };
        return HighlightScoringPolicy.IsSupported(highlight);
    }

    private bool TryReadCache(string path, string fingerprint, byte protocol,
        out ReplayHighlightMetadata? metadata)
    {
        metadata = null;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length is < 2 or > MaximumCacheBytes) return false;
            CacheDocument? document = JsonSerializer.Deserialize<CacheDocument>(
                File.ReadAllBytes(path), JsonOptions);
            if (document is null || document.Schema != CacheSchemaVersion
                || document.AnalyzerVersion != HighlightAnalyzer.Version
                || document.ReplayFingerprint != fingerprint
                || document.ReplayProtocol != protocol
                || document.DurationFrames > ReplayArchive.MaximumFrame
                || document.Highlights is null
                || document.Highlights.Length > HighlightAnalyzer.MaximumHighlights)
                return false;
            var highlights = new ReplayHighlight[document.Highlights.Length];
            for (int i = 0; i < highlights.Length; i++)
            {
                if (document.Highlights[i] is not CacheHighlight cachedHighlight)
                    return false;
                highlights[i] = cachedHighlight.ToDomain();
                if (highlights[i].EndFrame > document.DurationFrames
                    || i > 0 && CompareSelectionOrder(highlights[i - 1],
                        highlights[i]) > 0)
                    return false;
            }
            ReplayHighlightMetadataStatus status = document.RecoveredReplay
                ? ReplayHighlightMetadataStatus.RecoveredReplay
                : ReplayHighlightMetadataStatus.Ready;
            metadata = new ReplayHighlightMetadata(status, fingerprint,
                HighlightAnalyzer.Version, protocol, document.DurationFrames,
                Array.AsReadOnly(highlights), FromCache: true,
                document.RecoveredReplay ? "The damaged replay tail was ignored." : null);
            return true;
        }
        catch (Exception error) when (IsCacheFailure(error)) { return false; }
    }

    private void TryWriteCache(string path, ReplayHighlightMetadata metadata)
    {
        string? temporary = null;
        try
        {
            Directory.CreateDirectory(CacheDirectory);
            var document = CacheDocument.From(metadata);
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
            if (json.Length > MaximumCacheBytes) return;
            temporary = Path.Combine(CacheDirectory,
                $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
            using (var stream = new FileStream(temporary, FileMode.CreateNew,
                FileAccess.Write, FileShare.None))
            {
                stream.Write(json);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
            temporary = null;
        }
        catch (Exception error) when (IsCacheFailure(error)) { }
        finally
        {
            if (temporary is not null)
            {
                try { File.Delete(temporary); }
                catch (Exception error) when (IsCacheFailure(error)) { }
            }
        }
    }

    private static ReplayHighlightMetadata Failure(
        ReplayHighlightMetadataStatus status, string error,
        string fingerprint = "", byte protocol = 0)
        => new(status, fingerprint, HighlightAnalyzer.Version, protocol, 0,
            Array.Empty<ReplayHighlight>(), FromCache: false, error);

    private static bool ValidFingerprint(string value)
        => value.Length == 64 && value.All(character => character is >= '0' and <= '9'
            or >= 'a' and <= 'f');

    private static int CompareSelectionOrder(ReplayHighlight left,
        ReplayHighlight right)
    {
        int value = right.Score.CompareTo(left.Score);
        if (value != 0) return value;
        value = left.FocusFrame.CompareTo(right.FocusFrame);
        if (value != 0) return value;
        value = left.Kind.CompareTo(right.Kind);
        if (value != 0) return value;
        value = left.Focus.Slot.CompareTo(right.Focus.Slot);
        if (value != 0) return value;
        value = left.Focus.ConnectionId.CompareTo(right.Focus.ConnectionId);
        if (value != 0) return value;
        value = left.Focus.Life.CompareTo(right.Focus.Life);
        if (value != 0) return value;
        value = left.StartFrame.CompareTo(right.StartFrame);
        if (value != 0) return value;
        value = left.EndFrame.CompareTo(right.EndFrame);
        if (value != 0) return value;
        value = left.AuthoritativeTick.CompareTo(right.AuthoritativeTick);
        if (value != 0) return value;
        value = left.Markers.CompareTo(right.Markers);
        return value != 0 ? value : StringComparer.Ordinal.Compare(left.Label,
            right.Label);
    }

    private static bool IsLocalIoFailure(Exception error)
        => error is IOException or UnauthorizedAccessException or SecurityException
            or ArgumentException or NotSupportedException;

    private static bool IsCacheFailure(Exception error)
        => IsLocalIoFailure(error) || error is JsonException;

    private sealed class CacheDocument
    {
        public int Schema { get; set; }
        public int AnalyzerVersion { get; set; }
        public string ReplayFingerprint { get; set; } = "";
        public byte ReplayProtocol { get; set; }
        public uint DurationFrames { get; set; }
        public bool RecoveredReplay { get; set; }
        public CacheHighlight[]? Highlights { get; set; }

        public static CacheDocument From(ReplayHighlightMetadata metadata) => new()
        {
            Schema = CacheSchemaVersion,
            AnalyzerVersion = HighlightAnalyzer.Version,
            ReplayFingerprint = metadata.ReplayFingerprint,
            ReplayProtocol = metadata.ReplayProtocol,
            DurationFrames = metadata.DurationFrames,
            RecoveredReplay = metadata.Status
                == ReplayHighlightMetadataStatus.RecoveredReplay,
            Highlights = metadata.Highlights.Select(CacheHighlight.From).ToArray()
        };
    }

    private sealed class CacheHighlight
    {
        public uint StartFrame { get; set; }
        public uint FocusFrame { get; set; }
        public uint EndFrame { get; set; }
        public uint AuthoritativeTick { get; set; }
        public byte FocusSlot { get; set; }
        public ulong FocusConnectionId { get; set; }
        public uint FocusLife { get; set; }
        public HighlightKind Kind { get; set; }
        public int Score { get; set; }
        public ReplayMarker Markers { get; set; }
        public string Label { get; set; } = "";

        public ReplayHighlight ToDomain() => new(StartFrame, FocusFrame, EndFrame,
            AuthoritativeTick,
            new CombatActor(FocusSlot, FocusConnectionId, FocusLife), Kind, Score,
            Markers, Label);

        public static CacheHighlight From(ReplayHighlight value) => new()
        {
            StartFrame = value.StartFrame,
            FocusFrame = value.FocusFrame,
            EndFrame = value.EndFrame,
            AuthoritativeTick = value.AuthoritativeTick,
            FocusSlot = value.Focus.Slot,
            FocusConnectionId = value.Focus.ConnectionId,
            FocusLife = value.Focus.Life,
            Kind = value.Kind,
            Score = value.Score,
            Markers = value.Markers,
            Label = value.Label
        };
    }
}
