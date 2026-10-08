using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;

namespace MphRead.Mods.Launcher.Core;

/// <summary>Uses existing replay files, annotations, archive validation, storage jobs and Studio IPC.</summary>
public sealed class TheatreEngineBackend : ITheatreBackend
{
    private readonly Func<CancellationToken, Task<string?>>? _import;
    private readonly Func<string, CancellationToken, Task<string?>>? _export;
    public TheatreEngineBackend(Func<CancellationToken, Task<string?>>? import = null,
        Func<string, CancellationToken, Task<string?>>? export = null) => (_import, _export) = (import, export);

    public bool DesktopActions => !OperatingSystem.IsAndroid();
    public bool CanLaunch(out string reason)
    {
        reason = DemoPlayback.IsActive ? "Close the current replay before opening another replay."
            : NetSession.Active ? "Leave the current multiplayer session before opening a replay."
            : GameFiles.Problem() ?? "";
        return reason.Length == 0;
    }

    public Task<ImmutableArray<TheatreEntry>> Scan(bool applyStoragePolicy, CancellationToken cancellation)
        => ReplayStorageJobs.Run(() =>
        {
            cancellation.ThrowIfCancellationRequested();
            if (applyStoragePolicy) ApplyStoragePolicy();
            var recordings = DemoLibrary.List();
            var sources = recordings.ToDictionary(d => d.Path, StringComparer.OrdinalIgnoreCase);
            var entries = ImmutableArray.CreateBuilder<TheatreEntry>();
            foreach (DemoRecording recording in recordings)
            {
                cancellation.ThrowIfCancellationRequested();
                string people = recording.Metadata == null ? "" : String.Join(" ", recording.Metadata.Players.Select(p => p.Name));
                bool clip = recording.Metadata?.Type == ReplayType.Clip || recording.FileName.Contains("_clip_", StringComparison.OrdinalIgnoreCase);
                bool interrupted = recording.Path.EndsWith(".part", StringComparison.OrdinalIgnoreCase);
                TheatreEntry entry = Entry(recording.Path, recording.DisplayName, recording.Room, people,
                    recording.Metadata?.Mode.ToString() ?? "", recording.Recorded, recording.DurationFrames,
                    clip, false, recording.Favorite, interrupted || recording.Compatibility == ReplayOpenResult.Truncated, interrupted);
                entries.Add(entry with
                {
                    Detail = DemoLibrary.Describe(recording),
                    Metadata = DemoLibrary.Describe(recording) + "\n" + DemoLibrary.Details(recording) + AnnotationSummary(entry) + OrganizationSummary(entry),
                    Hero = $"{(recording.Metadata?.Type == ReplayType.Clip ? "CLIP" : "FULL REPLAY")} // {Time(recording.DurationFrames)}"
                        + (recording.Room.Length > 0 ? $" // {recording.Room}" : "") + $" // {recording.Recorded:g}"
                });
            }
            foreach (string path in ReplayVirtualClips.List())
            {
                cancellation.ThrowIfCancellationRequested();
                if (!ReplayVirtualClips.TryLoad(path, out ReplayVirtualClipDocument? clip) || clip == null) continue;
                sources.TryGetValue(clip.SourceReplay, out DemoRecording source);
                string room = source.Path != null ? source.Room : "";
                string people = source.Metadata == null ? "" : String.Join(" ", source.Metadata.Players.Select(p => p.Name));
                uint duration = clip.EndFrame - clip.StartFrame;
                TheatreEntry entry = Entry(path, clip.Name, room, people, source.Metadata?.Mode.ToString() ?? "",
                    clip.CreatedUtc.ToLocalTime(), duration, true, true, ReplayVirtualClips.IsFavorite(path), false, false,
                    clip.SourceReplay);
                entries.Add(entry with
                {
                    Detail = $"virtual clip / {Time(duration)}",
                    Metadata = $"VIRTUAL CLIP / {Time(duration)}\n{Time(clip.StartFrame)} – {Time(clip.EndFrame)}\nSOURCE {Path.GetFileName(clip.SourceReplay)}"
                        + AnnotationSummary(entry) + OrganizationSummary(entry),
                    Hero = $"VIRTUAL CLIP // {Time(duration)}" + (room.Length > 0 ? $" // {room}" : "")
                });
            }
            return entries.ToImmutable();
        }, cancellation);

    private static TheatreEntry Entry(string path, string title, string room, string people, string mode,
        DateTime recorded, uint duration, bool clip, bool virtualClip, bool favorite, bool recoverable,
        bool interrupted, string? thumbnailSource = null)
    {
        var bookmarks = ReplayAnnotations.Bookmarks(path);
        var highlights = ReplayAnnotations.Highlights(path);
        string annotations = String.Join(" ", bookmarks.Select(b => b.Name).Concat(highlights.Select(h => h.Name)));
        string tags = String.Join(", ", ReplayAnnotations.Tags(path));
        string collections = String.Join(", ", ReplayAnnotations.Collections(path));
        var previews = ReplayVideoExporter.Thumbnails(thumbnailSource ?? path).Take(3).ToImmutableArray();
        if (previews.IsEmpty && room.Length > 0)
        {
            try
            {
                string fallback = ThumbnailGenerator.PathFor(room);
                if (File.Exists(fallback)) previews = ImmutableArray.Create(fallback);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
        }
        return new()
        {
            Path = path, Title = title, Room = room, Players = people, Recorded = recorded,
            DurationFrames = duration, IsClip = clip, VirtualClip = virtualClip, Favorite = favorite,
            Recoverable = recoverable, Interrupted = interrupted, Annotated = annotations.Length > 0,
            Organized = tags.Length > 0 || collections.Length > 0, BookmarkCount = bookmarks.Count,
            HighlightCount = highlights.Count, Tags = tags, Collections = collections, PreviewPaths = previews,
            SearchText = $"{title} {room} {mode} {people} {annotations} {tags} {collections} {Path.GetFileName(thumbnailSource ?? path)}"
        };
    }

    public Task<TheatreLaunchResult> PreparePlayback(string path, CancellationToken cancellation)
        => ReplayStorageJobs.Run(() =>
        {
            cancellation.ThrowIfCancellationRequested();
            string source = path;
            if (Virtual(path))
            {
                source = ReplayVirtualClips.ResolveForPlayback(path, out ReplayOpenResult opened, cancellation) ?? "";
                if (source.Length == 0) return new TheatreLaunchResult(null, $"Could not prepare virtual clip: {opened}");
            }
            cancellation.ThrowIfCancellationRequested();
            using DemoReader? reader = DemoReader.Open(source, out ReplayOpenResult result, metadataOnly: true);
            if (reader == null) return new TheatreLaunchResult(null, $"Cannot open replay: {result}.");
            if (!ReplayIdentityCompatibility.Supports(reader.ProtocolVersion))
                return new TheatreLaunchResult(null, $"This replay uses network protocol {reader.ProtocolVersion}. "
                    + $"This build can replay archived protocols {ReplayIdentityCompatibility.OldestReplayProtocol}-{NetConfig.ProtocolVersion}.");
            if (reader.Metadata is ReplayMetadata metadata)
            {
                result = ReplayMapIdentity.Validate(metadata);
                if (result == ReplayOpenResult.MapHashMismatch && metadata.CustomMapIdentity == null
                    && reader.ProtocolVersion < NetConfig.ProtocolVersion) result = ReplayOpenResult.Success;
                if (result != ReplayOpenResult.Success) return new TheatreLaunchResult(null, $"Cannot load replay map: {result}.");
            }
            return new TheatreLaunchResult(new LaunchPlan
            {
                Kind = LaunchKind.Demo, DemoPath = source, Hunter = Hunter.Samus, PlayerName = "", RoomKey = ""
            }, "");
        }, cancellation);

    public async Task<TheatreOperationResult> Execute(TheatreOperation operation, ImmutableArray<TheatreEntry> targets,
        string name, string tags, string collections, CancellationToken cancellation)
    {
        if (operation == TheatreOperation.Export && _export != null)
        {
            string source = await Resolve(targets[0].Path, cancellation).ConfigureAwait(false);
            string? destination = await _export(source, cancellation).ConfigureAwait(false);
            return new(destination == null ? "EXPORT CANCELLED" : $"EXPORTED {destination}", Reload: false);
        }
        return await ReplayStorageJobs.Run(() =>
        {
            cancellation.ThrowIfCancellationRequested();
            TheatreEntry entry = targets[0];
            switch (operation)
            {
                case TheatreOperation.Favorite:
                    ToggleFavorite(entry.Path); return new TheatreOperationResult(entry.Favorite ? "UNFAVORITED" : "FAVORITED", entry.Path);
                case TheatreOperation.Rename:
                    if (Virtual(entry.Path)) ReplayVirtualClips.Rename(entry.Path, name); else DemoLibrary.Rename(entry.Path, name);
                    return new TheatreOperationResult("RENAMED", entry.Path);
                case TheatreOperation.Organize:
                    ReplayAnnotations.SetOrganization(entry.Path, SplitLabels(tags), SplitLabels(collections));
                    return new TheatreOperationResult("ORGANIZATION SAVED", entry.Path);
                case TheatreOperation.Delete:
                    if (Virtual(entry.Path)) ReplayVirtualClips.Delete(entry.Path); else DemoLibrary.Delete(entry.Path);
                    return new TheatreOperationResult("DELETED");
                case TheatreOperation.Validate:
                    return new TheatreOperationResult($"INTEGRITY {Validate(entry.Path, cancellation)}".ToUpperInvariant(), entry.Path);
                case TheatreOperation.Recover:
                    ReplayArchive.Recover(entry.Path, out string? recovered, out ReplayOpenResult recoveredResult, cancellation);
                    return new TheatreOperationResult(recovered == null ? $"RECOVERY FAILED {recoveredResult}".ToUpperInvariant()
                        : $"RECOVERED {Path.GetFileName(recovered)}".ToUpperInvariant(), recovered ?? entry.Path);
                case TheatreOperation.Export:
                    if (OperatingSystem.IsAndroid()) throw new InvalidOperationException("An Android replay export provider is unavailable.");
                    string source = ResolveSync(entry.Path, cancellation);
                    string directory = Path.Combine(DemoLibrary.Directory, "exports");
                    string destination = Path.Combine(directory, Path.GetFileNameWithoutExtension(entry.Path) + $"_{Guid.NewGuid():N}{DemoFile.Extension}");
                    cancellation.ThrowIfCancellationRequested();
                    Directory.CreateDirectory(directory); File.Copy(source, destination, overwrite: false);
                    return new TheatreOperationResult($"EXPORTED {destination}", Reload: false);
                case TheatreOperation.FavoriteFiltered:
                    int changed = 0;
                    foreach (TheatreEntry target in targets)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        bool favorite = Virtual(target.Path) ? ReplayVirtualClips.IsFavorite(target.Path) : File.Exists(target.Path + ".favorite");
                        if (!favorite) { ToggleFavorite(target.Path); changed++; }
                    }
                    return new TheatreOperationResult($"FAVORITED {changed} FILTERED ITEM{(changed == 1 ? "" : "S")}");
                case TheatreOperation.ValidateFiltered:
                    int healthy = 0, issues = 0;
                    foreach (TheatreEntry target in targets)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        if (Validate(target.Path, cancellation) == ReplayOpenResult.Success) healthy++; else issues++;
                    }
                    return new TheatreOperationResult(issues == 0 ? $"BATCH INTEGRITY // {healthy} HEALTHY"
                        : $"BATCH INTEGRITY // {healthy} HEALTHY / {issues} NEED ATTENTION");
                default: throw new ArgumentOutOfRangeException(nameof(operation));
            }
        }, cancellation).ConfigureAwait(false);
    }

    public async Task<string?> PickImport(CancellationToken cancellation)
    {
        if (_import != null) return await _import(cancellation).ConfigureAwait(false);
        if (!NativeFilePicker.Available)
            throw new InvalidOperationException(OperatingSystem.IsAndroid()
                ? "An Android replay import provider is unavailable. Enter a local replay path."
                : "No desktop file dialog is available. Install zenity or kdialog.");
        return await NativeFilePicker.OpenFile("Import replay", $"{Branding.Name} replay", DemoFile.Extension.TrimStart('.'))
            .WaitAsync(cancellation).ConfigureAwait(false);
    }

    public bool OpenStudio(string path, out string? error)
    {
#if !ANDROID && !MPHREAD_SERVER
        return StudioIntegration.StudioApplicationLauncher.TryOpen(path, false, out error);
#else
        error = "Project Prime Studio is unavailable on this platform.";
        return false;
#endif
    }
    public void Reveal(string path)
    {
        Process.Start(new ProcessStartInfo(Path.GetDirectoryName(Path.GetFullPath(path))!) { UseShellExecute = true })?.Dispose();
    }

    private static Task<string> Resolve(string path, CancellationToken cancellation)
        => ReplayStorageJobs.Run(() => ResolveSync(path, cancellation), cancellation);
    private static string ResolveSync(string path, CancellationToken cancellation)
    {
        if (!Virtual(path)) return path;
        return ReplayVirtualClips.ResolveForPlayback(path, out ReplayOpenResult result, cancellation)
            ?? throw new InvalidDataException(result.ToString());
    }
    private static ReplayOpenResult Validate(string path, CancellationToken cancellation)
    {
        string source = path;
        if (Virtual(path))
        {
            source = ReplayVirtualClips.ResolveForPlayback(path, out ReplayOpenResult result, cancellation) ?? "";
            if (source.Length == 0) return result;
        }
        ReplayOpenResult validation = ReplayArchive.Validate(source);
        if (!Virtual(path)) DemoLibrary.NoteValidation(path, validation);
        return validation;
    }
    private static void ToggleFavorite(string path)
    { if (Virtual(path)) ReplayVirtualClips.ToggleFavorite(path); else DemoLibrary.ToggleFavorite(path); }
    private static bool Virtual(string path) => path.EndsWith(ReplayVirtualClips.Extension, StringComparison.OrdinalIgnoreCase);
    private static IEnumerable<string> SplitLabels(string value)
        => value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    private static string Time(uint frames) => ReplayHud.Time(frames);
    private static string AnnotationSummary(TheatreEntry entry) => entry.BookmarkCount + entry.HighlightCount == 0 ? ""
        : $"\n{entry.BookmarkCount} bookmark{(entry.BookmarkCount == 1 ? "" : "s")} / {entry.HighlightCount} named highlight{(entry.HighlightCount == 1 ? "" : "s")}";
    private static string OrganizationSummary(TheatreEntry entry)
        => !entry.Organized ? "" : "\n" + (entry.Tags.Length > 0 ? "TAGS " + entry.Tags : "")
            + (entry.Tags.Length > 0 && entry.Collections.Length > 0 ? "\n" : "")
            + (entry.Collections.Length > 0 ? "COLLECTIONS " + entry.Collections : "");
    private static void ApplyStoragePolicy()
    {
        if (!LauncherPrefs.ReplayAutoPrune || LauncherPrefs.ReplayStorageLimitGb <= 0) return;
        try
        {
            ReplayStorageManager.Apply(new ReplayStoragePolicy(LauncherPrefs.ReplayStorageLimitGb * 1024L * 1024L * 1024L,
                DeleteFullMatches: true, DeleteMaterializedClips: LauncherPrefs.ReplayDeleteClips, DeleteVirtualClips: false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Console.WriteLine($"[replay] storage management skipped: {ex.Message}"); }
    }
}
