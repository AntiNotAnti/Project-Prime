using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MphRead.Mods.Network;

public readonly record struct ReplayHighlightIdentity(uint StartFrame,
    uint FocusFrame, uint EndFrame, CombatActor Focus, HighlightKind Kind)
{
    public static ReplayHighlightIdentity From(in ReplayHighlight highlight)
        => new(highlight.StartFrame, highlight.FocusFrame, highlight.EndFrame,
            highlight.Focus, highlight.Kind);
}

public sealed record ReplayUserClip(Guid Id, string ReplayFingerprint,
    uint StartFrame, uint EndFrame, CombatActor? Focus, string Label,
    bool Favorite = false)
{
    public const int MaximumLabelLength = 64;
}

public sealed record ReplayUserLibrarySelection(string ReplayFingerprint,
    IReadOnlyList<ReplayUserClip> Clips, bool ReplayFavorite,
    IReadOnlyList<ReplayHighlightIdentity> FavoriteHighlights,
    bool SourceIsStale)
{
    public static ReplayUserLibrarySelection Empty(string fingerprint,
        bool stale = false) => new(fingerprint, Array.Empty<ReplayUserClip>(), false,
            Array.Empty<ReplayHighlightIdentity>(), stale);
}

/// <summary>
/// Durable, user-owned replay annotations. The source replay remains immutable;
/// source length and write time prevent annotations from silently attaching to
/// different bytes that later reuse the same path.
/// </summary>
public sealed class ReplayUserLibraryMetadataStore
{
    public const int SchemaVersion = 1;
    public const int MaximumDocumentBytes = 2 * 1024 * 1024;
    private readonly object _sync = new();
    private readonly string _path;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        MaxDepth = 12,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public ReplayUserLibraryMetadataStore(string? dataRoot = null)
    {
        string root = System.IO.Path.GetFullPath(dataRoot
            ?? ReplayApplicationPaths.UserDataRoot);
        _path = System.IO.Path.Combine(root, "replay-library.json");
    }

    public string Path => _path;

    public ReplayUserLibrarySelection Load(string replayPath, string fingerprint)
    {
        ValidateSource(replayPath, fingerprint);
        lock (_sync)
        {
            UserDocument document = ReadDocument();
            SourceEntry? byFingerprint = document.Replays.FirstOrDefault(value =>
                String.Equals(value.ReplayFingerprint, fingerprint,
                    StringComparison.Ordinal));
            if (byFingerprint is null)
            {
                bool stale = document.Replays.Any(value => SamePath(value.SourcePath,
                    replayPath));
                return ReplayUserLibrarySelection.Empty(fingerprint, stale);
            }
            if (!byFingerprint.MatchesSource(replayPath))
                return ReplayUserLibrarySelection.Empty(fingerprint, stale: true);
            return new ReplayUserLibrarySelection(fingerprint,
                byFingerprint.Clips.ToArray(), byFingerprint.ReplayFavorite,
                byFingerprint.FavoriteHighlights.ToArray(), SourceIsStale: false);
        }
    }

    public ReplayUserLibrarySelection SaveClip(string replayPath, string fingerprint,
        uint startFrame, uint endFrame, CombatActor? focus, string label)
    {
        if (endFrame <= startFrame)
            throw new ArgumentException("Clip Out must be after Clip In.");
        if (focus is { } actor && !actor.IsValid)
            throw new ArgumentException("A clip focus must identify an exact actor life.");
        string normalizedLabel = NormalizeLabel(label);
        lock (_sync)
        {
            UserDocument document = ReadDocument();
            SourceEntry entry = CurrentEntry(document, replayPath, fingerprint);
            if (entry.Clips.Count >= 1024)
                throw new IOException("This replay already has the maximum number of clips.");
            entry.Clips.Add(new ReplayUserClip(Guid.NewGuid(), fingerprint,
                startFrame, endFrame, focus, normalizedLabel));
            WriteDocument(document);
            return Selection(entry);
        }
    }

    public ReplayUserLibrarySelection SetReplayFavorite(string replayPath,
        string fingerprint, bool favorite)
    {
        lock (_sync)
        {
            UserDocument document = ReadDocument();
            SourceEntry entry = CurrentEntry(document, replayPath, fingerprint);
            entry.ReplayFavorite = favorite;
            WriteDocument(document);
            return Selection(entry);
        }
    }

    public ReplayUserLibrarySelection SetHighlightFavorite(string replayPath,
        string fingerprint, in ReplayHighlight highlight, bool favorite)
    {
        ReplayHighlightIdentity identity = ReplayHighlightIdentity.From(highlight);
        lock (_sync)
        {
            UserDocument document = ReadDocument();
            SourceEntry entry = CurrentEntry(document, replayPath, fingerprint);
            entry.FavoriteHighlights.RemoveAll(value => value == identity);
            if (favorite)
            {
                if (entry.FavoriteHighlights.Count >= 1024)
                    throw new IOException(
                        "This replay already has the maximum number of favorite highlights.");
                entry.FavoriteHighlights.Add(identity);
            }
            WriteDocument(document);
            return Selection(entry);
        }
    }

    public ReplayUserLibrarySelection SetClipFavorite(string replayPath,
        string fingerprint, Guid clipId, bool favorite)
    {
        lock (_sync)
        {
            UserDocument document = ReadDocument();
            SourceEntry entry = CurrentEntry(document, replayPath, fingerprint);
            int index = entry.Clips.FindIndex(value => value.Id == clipId);
            if (index < 0) throw new ArgumentException("That clip no longer exists.");
            entry.Clips[index] = entry.Clips[index] with { Favorite = favorite };
            WriteDocument(document);
            return Selection(entry);
        }
    }

    private SourceEntry CurrentEntry(UserDocument document, string replayPath,
        string fingerprint)
    {
        ValidateSource(replayPath, fingerprint);
        var source = new FileInfo(replayPath);
        document.Replays.RemoveAll(value => SamePath(value.SourcePath, replayPath)
            && !String.Equals(value.ReplayFingerprint, fingerprint,
                StringComparison.Ordinal));
        SourceEntry? entry = document.Replays.FirstOrDefault(value =>
            String.Equals(value.ReplayFingerprint, fingerprint,
                StringComparison.Ordinal));
        if (entry is null)
        {
            entry = new SourceEntry
            {
                ReplayFingerprint = fingerprint,
                SourcePath = source.FullName,
                SourceLength = source.Length,
                SourceWriteUtcTicks = source.LastWriteTimeUtc.Ticks
            };
            document.Replays.Add(entry);
        }
        else
        {
            // The fingerprint is the identity. A rename or import may change
            // the path/write stamp without changing replay bytes.
            entry.SourcePath = source.FullName;
            entry.SourceLength = source.Length;
            entry.SourceWriteUtcTicks = source.LastWriteTimeUtc.Ticks;
        }
        return entry;
    }

    private UserDocument ReadDocument()
    {
        try
        {
            var file = new FileInfo(_path);
            if (!file.Exists) return new UserDocument();
            if (file.Length is < 2 or > MaximumDocumentBytes) return new UserDocument();
            UserDocument? document = JsonSerializer.Deserialize<UserDocument>(
                File.ReadAllBytes(_path), JsonOptions);
            if (document is null || document.Schema != SchemaVersion
                || document.Replays is null || document.Replays.Count > 4096)
                return new UserDocument();
            document.Sanitize();
            return document;
        }
        catch (Exception error) when (IsStorageFailure(error))
        {
            return new UserDocument();
        }
    }

    private void WriteDocument(UserDocument document)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
        if (bytes.Length > MaximumDocumentBytes)
            throw new IOException("Replay user metadata is too large.");
        string directory = System.IO.Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        string temporary = _path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew,
                FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception error) when (IsStorageFailure(error)) { }
        }
    }

    private static ReplayUserLibrarySelection Selection(SourceEntry entry)
        => new(entry.ReplayFingerprint, entry.Clips.ToArray(), entry.ReplayFavorite,
            entry.FavoriteHighlights.ToArray(), SourceIsStale: false);

    private static void ValidateSource(string replayPath, string fingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(replayPath);
        if (!File.Exists(replayPath)) throw new FileNotFoundException(
            "The replay is no longer available.", replayPath);
        if (fingerprint.Length != 64 || fingerprint.Any(value => !Uri.IsHexDigit(value)))
            throw new ArgumentException("Replay fingerprint is invalid.", nameof(fingerprint));
    }

    private static string NormalizeLabel(string label)
    {
        string value = (label ?? "").Trim();
        if (value.Length is < 1 or > ReplayUserClip.MaximumLabelLength
            || value.Any(char.IsControl))
            throw new ArgumentException("Clip label must be 1-64 visible characters.");
        return value;
    }

    private static bool SamePath(string left, string right)
    {
        try
        {
            return String.Equals(System.IO.Path.GetFullPath(left),
                System.IO.Path.GetFullPath(right), OperatingSystem.IsWindows()
                    || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal);
        }
        catch (Exception error) when (error is ArgumentException
            or NotSupportedException) { return false; }
    }

    private static bool IsStorageFailure(Exception error)
        => error is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException or JsonException;

    private sealed class UserDocument
    {
        public int Schema { get; set; } = SchemaVersion;
        public List<SourceEntry> Replays { get; set; } = new();

        public void Sanitize()
        {
            Replays.RemoveAll(value => value is null
                || value.ReplayFingerprint is null
                || value.ReplayFingerprint.Length != 64
                || value.Clips is null || value.Clips.Count > 1024
                || value.FavoriteHighlights is null
                || value.FavoriteHighlights.Count > 1024);
        }
    }

    private sealed class SourceEntry
    {
        public string ReplayFingerprint { get; set; } = "";
        public string SourcePath { get; set; } = "";
        public long SourceLength { get; set; }
        public long SourceWriteUtcTicks { get; set; }
        public bool ReplayFavorite { get; set; }
        public List<ReplayHighlightIdentity> FavoriteHighlights { get; set; } = new();
        public List<ReplayUserClip> Clips { get; set; } = new();

        public bool MatchesSource(string path)
        {
            var source = new FileInfo(path);
            return source.Exists && source.Length == SourceLength
                && source.LastWriteTimeUtc.Ticks == SourceWriteUtcTicks;
        }
    }
}

public sealed record ReplayEventTimelineMarker(uint Frame, ReplayMarker Markers,
    string Label);

public static class ReplayEventTimeline
{
    public static IReadOnlyList<ReplayEventTimelineMarker> Read(string replayPath)
    {
        using ReplayReader? reader = ReplayReader.Open(replayPath);
        if (reader is null) return Array.Empty<ReplayEventTimelineMarker>();
        return reader.Index.Where(value => !value.Keyframe
                && value.Marker != ReplayMarker.None)
            .Select(value => new ReplayEventTimelineMarker(value.Frame,
                value.Marker, Label(value.Marker))).ToArray();
    }

    public static string Label(ReplayMarker marker)
    {
        if ((marker & ReplayMarker.MatchEnd) != 0) return "MATCH END";
        if ((marker & ReplayMarker.Overtime) != 0) return "OVERTIME";
        if ((marker & ReplayMarker.MatchPoint) != 0) return "MATCH POINT";
        if ((marker & ReplayMarker.PrimeChange) != 0) return "PRIME CHANGE";
        if ((marker & ReplayMarker.FlagCapture) != 0) return "CAPTURE";
        if ((marker & ReplayMarker.NodeCapture) != 0) return "NODE CAPTURE";
        if ((marker & ReplayMarker.MultiKill) != 0) return "MULTI KILL";
        if ((marker & ReplayMarker.Headshot) != 0) return "HEADSHOT";
        if ((marker & ReplayMarker.Kill) != 0) return "KILL";
        if ((marker & ReplayMarker.Award) != 0) return "AWARD";
        return "EVENT";
    }
}
