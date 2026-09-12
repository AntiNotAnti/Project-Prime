using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher;
using MphRead.Mods.Network;

namespace MphRead.Mods.Launcher.Gui;

public sealed record PrimeReplayEntry(string Id, string Path, string FileName, string Room,
    DateTime Recorded, long Bytes, ReplayLibraryMetadata? Metadata = null)
{
    public string Size => Bytes >= 1024 * 1024
        ? $"{Bytes / (1024d * 1024d):0.0} MB"
        : Bytes >= 1024 ? $"{Bytes / 1024d:0} KB" : $"{Bytes} bytes";
}

public enum TheatreErrorKind : byte
{
    Library,
    Playback,
    Analysis,
    Import,
    Export,
    Rename,
    Delete
}

public sealed record TheatreError(TheatreErrorKind Kind, string Title, string Detail);

public enum TheatreSortOrder : byte
{
    Newest,
    Oldest,
    Duration,
    FileSize,
    HighlightCount
}

public sealed record TheatreFilters(string Search = "", string? Map = null,
    GameMode? Mode = null, DateTime? DateFrom = null, DateTime? DateTo = null,
    bool? HasHighlights = null, ReplayRecoveryStatus? Recovery = null,
    TheatreSortOrder Sort = TheatreSortOrder.Newest);

public sealed record TheatreState(IReadOnlyList<PrimeReplayEntry> Replays,
    PrimeReplayEntry? Selected, bool Loading, string? Error,
    ReplayHighlightMetadata? HighlightMetadata = null, int SelectedHighlight = -1,
    TheatreError? StructuredError = null,
    TheatreFilters? Filters = null,
    ReplayUserLibrarySelection? UserMetadata = null,
    uint? ClipInFrame = null, uint? ClipOutFrame = null,
    IReadOnlyList<ReplayEventTimelineMarker>? Timeline = null)
{
    public static TheatreState Initial => new(Array.Empty<PrimeReplayEntry>(), null, false, null);
    public IReadOnlyList<ReplayHighlight> Highlights
        => HighlightMetadata?.Highlights ?? Array.Empty<ReplayHighlight>();
    public int HighlightCount => Highlights.Count;
    public IReadOnlyList<ReplayUserClip> UserClips
        => UserMetadata?.Clips ?? Array.Empty<ReplayUserClip>();
    public IReadOnlyList<ReplayEventTimelineMarker> EventTimeline
        => Timeline ?? Array.Empty<ReplayEventTimelineMarker>();
}

public interface IPrimeReplayLibrary
{
    string Directory { get; }
    bool SupportsImport { get; }
    bool SupportsExport { get; }
    bool SupportsRename { get; }
    bool SupportsReveal { get; }
    IReadOnlyList<PrimeReplayEntry> List();
    bool Delete(PrimeReplayEntry replay);
    bool Rename(PrimeReplayEntry replay, string fileName);
    bool Import(string sourcePath, out PrimeReplayEntry? imported);
    bool Export(PrimeReplayEntry replay, string destinationPath);
    bool Reveal(PrimeReplayEntry replay);
}

internal sealed class ReplayLibraryAdapter : IPrimeReplayLibrary
{
    private readonly string? _directoryOverride;
    private readonly ReplayLibraryMetadataService _metadata = new();
    private string? _directory;

    internal ReplayLibraryAdapter(string? directory = null)
    {
        // Do not touch Paths.Export while the launcher is being constructed.
        // The default replay directory depends on game path initialization,
        // which is not guaranteed for title/settings-only shell instances.
        _directoryOverride = directory;
    }

    public string Directory => ResolveDirectory();
    public bool SupportsImport => true;
    public bool SupportsExport => true;
    public bool SupportsRename => true;
    public bool SupportsReveal => ReplayReveal.CurrentPlatform != ReplayRevealPlatform.Unsupported;

    public IReadOnlyList<PrimeReplayEntry> List()
        => ReplayLibrary.List(ResolveDirectory()).Select(ToEntry).ToArray();

    public bool Delete(PrimeReplayEntry replay)
    {
        if (!IsLocal(replay)) return false;
        try
        {
            File.Delete(replay.Path);
            if (File.Exists(replay.Path)) return false;
            File.Delete(ReplayLibraryMetadataService.SidecarPath(replay.Path));
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    public bool Rename(PrimeReplayEntry replay, string fileName)
    {
        if (!IsLocal(replay)
            || !ReplayFileNamePolicy.TryNormalize(fileName, out string normalized))
            return false;
        string destination;
        try
        {
            string directory = ResolveDirectory();
            destination = Path.GetFullPath(Path.Combine(directory, normalized));
            if (!ReplayFileNamePolicy.IsWithinDirectory(directory, destination)) return false;
        }
        catch (ArgumentException) { return false; }
        catch (NotSupportedException) { return false; }
        try
        {
            File.Move(replay.Path, destination, overwrite: false);
            string oldSidecar = ReplayLibraryMetadataService.SidecarPath(replay.Path);
            string newSidecar = ReplayLibraryMetadataService.SidecarPath(destination);
            try
            {
                if (File.Exists(oldSidecar))
                    File.Move(oldSidecar, newSidecar, overwrite: true);
            }
            catch (Exception error) when (IsDerivedMetadataFailure(error)) { }
            RefreshSidecarBestEffort(destination);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    public bool Import(string sourcePath, out PrimeReplayEntry? imported)
    {
        imported = null;
        if (sourcePath is not { Length: > 0 } || !File.Exists(sourcePath)) return false;
        try
        {
            EnsureDirectory();
            string name = Path.GetFileName(sourcePath);
            if (!name.EndsWith(ReplayFile.Extension, StringComparison.OrdinalIgnoreCase)) return false;
            string directory = ResolveDirectory();
            string destination = Path.Combine(directory, name);
            if (File.Exists(destination))
                destination = Path.Combine(directory,
                    Path.GetFileNameWithoutExtension(name) + "-imported" + ReplayFile.Extension);
            File.Copy(sourcePath, destination, overwrite: false);
            RefreshSidecarBestEffort(destination);
            imported = List().FirstOrDefault(item => item.Path == destination);
            return imported != null;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    public bool Export(PrimeReplayEntry replay, string destinationPath)
    {
        if (!IsLocal(replay) || destinationPath is not { Length: > 0 }
            || !File.Exists(replay.Path)) return false;
        try
        {
            string? parent = Path.GetDirectoryName(destinationPath);
            if (string.IsNullOrEmpty(parent)) return false;
            File.Copy(replay.Path, destinationPath, overwrite: false);
            RefreshSidecarBestEffort(destinationPath);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    public bool Reveal(PrimeReplayEntry replay)
    {
        if (!SupportsReveal || !IsLocal(replay) || !File.Exists(replay.Path)) return false;
        ProcessStartInfo? start = ReplayReveal.CreateStartInfo(
            replay.Path, ReplayReveal.CurrentPlatform);
        if (start == null) return false;
        try { return Process.Start(start) != null; }
        catch (System.ComponentModel.Win32Exception) { return false; }
        catch (Exception ex) when (ex is InvalidOperationException
            or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private PrimeReplayEntry ToEntry(ReplayRecording replay)
    {
        _metadata.TryRead(replay.Path, out ReplayLibraryMetadata? metadata);
        return new(replay.Path, replay.Path, replay.FileName, replay.Room,
            replay.Recorded, replay.Bytes, metadata);
    }

    private bool IsLocal(PrimeReplayEntry replay)
    {
        return File.Exists(replay.Path)
            && ReplayFileNamePolicy.IsWithinDirectory(ResolveDirectory(), replay.Path);
    }

    private void EnsureDirectory() => System.IO.Directory.CreateDirectory(ResolveDirectory());

    private void RefreshSidecarBestEffort(string replayPath)
    {
        try { _metadata.GetOrCreate(replayPath); }
        catch (Exception error) when (IsDerivedMetadataFailure(error)) { }
    }

    private static bool IsDerivedMetadataFailure(Exception error)
        => error is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException or System.Security.Cryptography.CryptographicException;

    private string ResolveDirectory()
    {
        if (_directory is { } resolved) return resolved;
        resolved = Path.GetFullPath(_directoryOverride ?? ReplayLibrary.Directory);
        _directory = resolved;
        return resolved;
    }
}

internal enum ReplayRevealPlatform
{
    Windows,
    MacOS,
    Linux,
    Unsupported
}

/// <summary>
/// Builds platform-specific file-manager launches without shell parsing.
/// The path is always one argument, so replay names cannot become commands.
/// </summary>
internal static class ReplayReveal
{
    internal static ReplayRevealPlatform CurrentPlatform
        => OperatingSystem.IsAndroid() ? ReplayRevealPlatform.Unsupported
        : OperatingSystem.IsWindows() ? ReplayRevealPlatform.Windows
        : OperatingSystem.IsMacOS() ? ReplayRevealPlatform.MacOS
        : OperatingSystem.IsLinux() ? ReplayRevealPlatform.Linux
        : ReplayRevealPlatform.Unsupported;

    internal static ProcessStartInfo? CreateStartInfo(string replayPath,
        ReplayRevealPlatform platform)
    {
        if (String.IsNullOrWhiteSpace(replayPath)) return null;
        string path;
        try { path = Path.GetFullPath(replayPath); }
        catch (ArgumentException) { return null; }
        catch (NotSupportedException) { return null; }
        catch (IOException) { return null; }
        ProcessStartInfo start = platform switch
        {
            ReplayRevealPlatform.Windows => new ProcessStartInfo("explorer.exe")
            {
                UseShellExecute = false
            },
            ReplayRevealPlatform.MacOS => new ProcessStartInfo("open")
            {
                UseShellExecute = false
            },
            ReplayRevealPlatform.Linux => new ProcessStartInfo("xdg-open")
            {
                UseShellExecute = false
            },
            _ => null!
        };
        if (platform == ReplayRevealPlatform.Unsupported) return null;

        if (platform == ReplayRevealPlatform.Windows)
        {
            start.ArgumentList.Add($"/select,{path}");
        }
        else if (platform == ReplayRevealPlatform.MacOS)
        {
            start.ArgumentList.Add("-R");
            start.ArgumentList.Add(path);
        }
        else
        {
            string? directory = Path.GetDirectoryName(path);
            if (String.IsNullOrEmpty(directory)) return null;
            start.ArgumentList.Add(directory);
        }
        return start;
    }
}

/// <summary>Local replay library; playback emits the existing Replay LaunchPlan.</summary>
public sealed class TheatreController : IDisposable
{
    private readonly IPrimeReplayLibrary _library;
    private readonly ReplayAnalysisCoordinator _analysis;
    private readonly ReplayUserLibraryMetadataStore _userMetadata;
    private readonly CancellationTokenSource _lifetime = new();
    private IReadOnlyList<PrimeReplayEntry> _allReplays = Array.Empty<PrimeReplayEntry>();
    private TheatreState _state = TheatreState.Initial;
    private int _disposed;

    public TheatreController(IPrimeReplayLibrary? library = null,
        ReplayHighlightMetadataService? highlights = null,
        ReplayAnalysisCoordinator? analysis = null,
        ReplayUserLibraryMetadataStore? userMetadata = null)
    {
        _library = library ?? new ReplayLibraryAdapter();
        _analysis = analysis ?? new ReplayAnalysisCoordinator(highlights);
        _userMetadata = userMetadata ?? new ReplayUserLibraryMetadataStore();
    }

    public TheatreState State => _state;
    public IPrimeReplayLibrary Library => _library;
    public event EventHandler? Changed;
    public event EventHandler<LaunchPlan>? Launch;

    public Task LoadAsync(CancellationToken cancellationToken = default)
        => LoadCoreAsync(cancellationToken);

    public void Select(PrimeReplayEntry? replay)
    {
        if (replay != null && !_state.Replays.Any(entry => entry.Id == replay.Id)) return;
        _analysis.CancelSelection();
        Publish(_state with { Selected = replay, HighlightMetadata = null,
            SelectedHighlight = -1, Error = null, StructuredError = null,
            UserMetadata = null, ClipInFrame = null, ClipOutFrame = null,
            Timeline = null });
        if (replay != null) _ = LoadHighlightsAsync(replay, _lifetime.Token);
    }

    public void ApplyFilters(TheatreFilters filters)
    {
        ArgumentNullException.ThrowIfNull(filters);
        IReadOnlyList<PrimeReplayEntry> visible = FilterAndSort(_allReplays, filters);
        PrimeReplayEntry? selected = _state.Selected is { } old
            ? visible.FirstOrDefault(value => value.Id == old.Id)
            : visible.FirstOrDefault();
        string? previousSelection = _state.Selected?.Id;
        if (selected?.Id != previousSelection) _analysis.CancelSelection();
        Publish(_state with { Replays = visible, Selected = selected,
            Filters = filters, HighlightMetadata = selected?.Id == _state.Selected?.Id
                ? _state.HighlightMetadata : null,
            SelectedHighlight = selected?.Id == _state.Selected?.Id
                ? _state.SelectedHighlight : -1 });
        if (selected is not null && selected.Id != previousSelection)
            _ = LoadHighlightsAsync(selected, _lifetime.Token);
    }

    public void SelectHighlight(int index)
    {
        if ((uint)index >= (uint)_state.HighlightCount) return;
        Publish(_state with { SelectedHighlight = index });
    }

    public void SetClipIn(uint frame)
    {
        uint bounded = BoundSelectedFrame(frame);
        Publish(_state with { ClipInFrame = bounded,
            ClipOutFrame = _state.ClipOutFrame is uint end && end <= bounded
                ? null : _state.ClipOutFrame });
    }

    public void SetClipOut(uint frame)
    {
        uint bounded = BoundSelectedFrame(frame);
        if (_state.ClipInFrame is uint start && bounded <= start)
        {
            SetError(TheatreErrorKind.Analysis, "Clip range is invalid",
                "Clip Out must be after Clip In.");
            return;
        }
        Publish(_state with { ClipOutFrame = bounded });
    }

    public void ResetClipRange()
        => Publish(_state with { ClipInFrame = null, ClipOutFrame = null });

    public async Task<bool> SaveClipAsync(string label,
        CombatActor? focus = null, CancellationToken cancellationToken = default)
    {
        PrimeReplayEntry replay = CurrentReplayWithFingerprint();
        if (_state.ClipInFrame is not uint start || _state.ClipOutFrame is not uint end)
            throw new InvalidOperationException("Set both Clip In and Clip Out first.");
        ReplayUserLibrarySelection selection = await Task.Run(() =>
            _userMetadata.SaveClip(replay.Path, replay.Metadata!.ReplayId,
                start, end, focus, label), cancellationToken).ConfigureAwait(false);
        PublishUserMetadata(replay.Id, selection);
        return true;
    }

    public async Task SetReplayFavoriteAsync(bool favorite,
        CancellationToken cancellationToken = default)
    {
        PrimeReplayEntry replay = CurrentReplayWithFingerprint();
        ReplayUserLibrarySelection selection = await Task.Run(() =>
            _userMetadata.SetReplayFavorite(replay.Path, replay.Metadata!.ReplayId,
                favorite), cancellationToken).ConfigureAwait(false);
        PublishUserMetadata(replay.Id, selection);
    }

    public async Task SetSelectedHighlightFavoriteAsync(bool favorite,
        CancellationToken cancellationToken = default)
    {
        PrimeReplayEntry replay = CurrentReplayWithFingerprint();
        int index = _state.SelectedHighlight;
        if ((uint)index >= (uint)_state.HighlightCount)
            throw new InvalidOperationException("Choose a highlight first.");
        ReplayHighlight highlight = _state.Highlights[index];
        ReplayUserLibrarySelection selection = await Task.Run(() =>
            _userMetadata.SetHighlightFavorite(replay.Path,
                replay.Metadata!.ReplayId, highlight, favorite), cancellationToken)
            .ConfigureAwait(false);
        PublishUserMetadata(replay.Id, selection);
    }

    public async Task SetClipFavoriteAsync(Guid clipId, bool favorite,
        CancellationToken cancellationToken = default)
    {
        PrimeReplayEntry replay = CurrentReplayWithFingerprint();
        ReplayUserLibrarySelection selection = await Task.Run(() =>
            _userMetadata.SetClipFavorite(replay.Path, replay.Metadata!.ReplayId,
                clipId, favorite), cancellationToken).ConfigureAwait(false);
        PublishUserMetadata(replay.Id, selection);
    }

    public Task<bool> PlayEventAsync(ReplayEventTimelineMarker marker,
        CancellationToken cancellationToken = default)
    {
        if (!_state.EventTimeline.Contains(marker))
            throw new InvalidOperationException("That replay event is no longer selected.");
        return PlayCoreAsync(_state.Selected, null, cancellationToken,
            BoundSelectedFrame(marker.Frame));
    }

    public Task<bool> PreviewClipAsync(ReplayUserClip clip,
        CancellationToken cancellationToken = default)
    {
        PrimeReplayEntry replay = CurrentReplayWithFingerprint();
        if (!String.Equals(clip.ReplayFingerprint, replay.Metadata!.ReplayId,
            StringComparison.Ordinal) || !_state.UserClips.Any(value => value.Id == clip.Id))
            throw new InvalidOperationException("That clip belongs to a different replay.");
        return PlayCoreAsync(replay, null, cancellationToken, clip.StartFrame,
            clip.EndFrame, clip.Focus);
    }

    public Task<bool> ExportClipAsync(string destinationPath, ReplayUserClip clip,
        CancellationToken cancellationToken = default)
    {
        _ = destinationPath;
        _ = clip;
        _ = cancellationToken;
        SetError(TheatreErrorKind.Export, "Replay clip export is unavailable",
            "This build cannot synthesize an exact checkpoint at an arbitrary Clip In frame. "
            + "The clip reference is saved safely; export the original replay instead.");
        return Task.FromResult(false);
    }

    public async Task<bool> PlayAsync(PrimeReplayEntry? replay = null,
        CancellationToken cancellationToken = default)
        => await PlayCoreAsync(replay, null, cancellationToken).ConfigureAwait(false);

    public async Task<bool> PlayHighlightAsync(int index,
        PrimeReplayEntry? replay = null, CancellationToken cancellationToken = default)
    {
        replay ??= _state.Selected;
        if (replay == null) throw new InvalidOperationException("Choose a replay first.");
        ReplayHighlightMetadata metadata = await GetHighlightsAsync(replay,
            cancellationToken).ConfigureAwait(false);
        if (!metadata.IsAvailable || (uint)index >= (uint)metadata.Highlights.Count)
            return false;
        return await PlayCoreAsync(replay, [metadata.Highlights[index]], cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> PlayHighlightReelAsync(PrimeReplayEntry? replay = null,
        CancellationToken cancellationToken = default)
    {
        replay ??= _state.Selected;
        if (replay == null) throw new InvalidOperationException("Choose a replay first.");
        ReplayHighlightMetadata metadata = await GetHighlightsAsync(replay,
            cancellationToken).ConfigureAwait(false);
        if (!metadata.IsAvailable || metadata.Highlights.Count == 0) return false;
        ReplayHighlight[] ordered = metadata.Highlights.OrderBy(value => value.StartFrame)
            .ThenBy(value => value.FocusFrame).ToArray();
        return await PlayCoreAsync(replay, ordered, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<bool> PlayCoreAsync(PrimeReplayEntry? replay,
        IReadOnlyList<ReplayHighlight>? highlights, CancellationToken cancellationToken,
        uint? startFrame = null, uint? endFrame = null, CombatActor? focus = null)
    {
        replay ??= _state.Selected;
        if (replay == null) throw new InvalidOperationException("Choose a replay first.");
        if (!File.Exists(replay.Path)) throw new FileNotFoundException("The replay is no longer available.");
        bool opened = await Task.Run(() => ReplayPlayback.Prepare(replay.Path), cancellationToken)
            .ConfigureAwait(false);
        if (!opened)
        {
            SetError(TheatreErrorKind.Playback, "Replay could not be opened",
                ReplayPlayback.LastError ?? "The replay player rejected this file.");
            return false;
        }
        (string RoomKey, GameMode Mode)? room = NetLaunch.ServerRoom();
        LaunchPlan plan = new()
        {
            Kind = LaunchKind.Replay,
            ReplayPath = replay.Path,
            RoomKey = room?.RoomKey ?? replay.Room,
            Mode = room?.Mode ?? GameMode.Battle,
            Hunter = Hunter.Samus,
            PlayerName = LauncherPrefs.PlayerName,
            ReplayHighlights = highlights,
            ReplayStartFrame = highlights is null ? startFrame : null,
            ReplayEndFrame = highlights is null ? endFrame : null,
            ReplayFocus = highlights is null ? focus : null
        };
        Launch?.Invoke(this, plan);
        return true;
    }

    public async Task<bool> DeleteAsync(PrimeReplayEntry? replay = null)
    {
        replay ??= _state.Selected;
        if (replay == null) return false;
        bool deleted = await Task.Run(() => _library.Delete(replay)).ConfigureAwait(false);
        if (deleted) await LoadAsync().ConfigureAwait(false);
        else SetError(TheatreErrorKind.Delete, "Replay could not be deleted",
            "The replay may be in use or the file is no longer writable.");
        return deleted;
    }

    public async Task<bool> RenameAsync(string fileName, PrimeReplayEntry? replay = null)
    {
        if (!_library.SupportsRename) return false;
        replay ??= _state.Selected;
        if (replay == null) return false;
        bool renamed = await Task.Run(() => _library.Rename(replay, fileName)).ConfigureAwait(false);
        if (renamed) await LoadAsync().ConfigureAwait(false);
        else SetError(TheatreErrorKind.Rename, "Replay could not be renamed",
            "Choose a unique file name without path characters.");
        return renamed;
    }

    public async Task<bool> ImportAsync(string sourcePath,
        CancellationToken cancellationToken = default)
    {
        if (!_library.SupportsImport) return false;
        bool imported = await Task.Run(() => _library.Import(sourcePath, out _), cancellationToken)
            .ConfigureAwait(false);
        if (imported) await LoadAsync(cancellationToken).ConfigureAwait(false);
        else SetError(TheatreErrorKind.Import, "Replay could not be imported",
            "Choose a readable Project Prime replay file.");
        return imported;
    }

    public async Task<bool> ExportAsync(string destinationPath,
        PrimeReplayEntry? replay = null, CancellationToken cancellationToken = default)
    {
        if (!_library.SupportsExport) return false;
        replay ??= _state.Selected;
        if (replay == null) return false;
        bool exported = await Task.Run(() => _library.Export(replay, destinationPath),
            cancellationToken).ConfigureAwait(false);
        if (!exported) SetError(TheatreErrorKind.Export,
            "Replay could not be exported",
            "The destination must be writable and must not already exist.");
        return exported;
    }

    public async Task<bool> RevealAsync(PrimeReplayEntry? replay = null,
        CancellationToken cancellationToken = default)
    {
        if (!_library.SupportsReveal) return false;
        replay ??= _state.Selected;
        if (replay == null) return false;
        return await Task.Run(() => _library.Reveal(replay), cancellationToken)
            .ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _analysis.Dispose();
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    private async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        Publish(_state with { Loading = true, Error = null, StructuredError = null });
        try
        {
            IReadOnlyList<PrimeReplayEntry> replays = await Task.Run(_library.List, cancellationToken)
                .ConfigureAwait(false);
            if (Volatile.Read(ref _disposed) != 0) return;
            _allReplays = replays;
            TheatreFilters filters = _state.Filters ?? new TheatreFilters();
            IReadOnlyList<PrimeReplayEntry> visible = FilterAndSort(replays, filters);
            PrimeReplayEntry? selected = _state.Selected is { } old
                ? visible.FirstOrDefault(item => item.Id == old.Id) : visible.FirstOrDefault();
            Publish(new TheatreState(visible, selected, false, null,
                Filters: filters));
            if (selected != null) await LoadHighlightsAsync(selected, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception error)
        {
            SetError(TheatreErrorKind.Library, "Replay library could not be loaded",
                error.Message, loading: false);
        }
    }

    private void Publish(TheatreState state)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        _state = state;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task<ReplayHighlightMetadata> GetHighlightsAsync(
        PrimeReplayEntry replay, CancellationToken cancellationToken)
    {
        if (_state.Selected?.Id == replay.Id && _state.HighlightMetadata is { } current)
            return current;
        return (await _analysis.SelectAsync(replay.Path, cancellationToken)
            .ConfigureAwait(false)).Highlights;
    }

    private async Task LoadHighlightsAsync(PrimeReplayEntry replay,
        CancellationToken cancellationToken)
    {
        try
        {
            ReplayAnalysisResult result = await _analysis.SelectAsync(replay.Path,
                cancellationToken).ConfigureAwait(false);
            ReplayHighlightMetadata metadata = result.Highlights;
            if (Volatile.Read(ref _disposed) == 0 && _state.Selected?.Id == replay.Id)
            {
                var enriched = replay with { Metadata = result.Library,
                    Room = String.IsNullOrWhiteSpace(result.Library.MapKey)
                        ? replay.Room : result.Library.MapKey };
                _allReplays = _allReplays.Select(value => value.Id == replay.Id
                    ? enriched : value).ToArray();
                IReadOnlyList<PrimeReplayEntry> visible = FilterAndSort(_allReplays,
                    _state.Filters ?? new TheatreFilters());
                PrimeReplayEntry? selected = visible.FirstOrDefault(value => value.Id == replay.Id);
                Publish(_state with { Replays = visible, Selected = selected,
                    HighlightMetadata = metadata,
                    SelectedHighlight = metadata.Highlights.Count == 0 ? -1 : 0,
                    UserMetadata = LoadUserMetadata(enriched),
                    Timeline = ReplayEventTimeline.Read(enriched.Path),
                    Error = metadata.IsAvailable ? null : metadata.Error,
                    StructuredError = metadata.IsAvailable ? null : new TheatreError(
                        TheatreErrorKind.Analysis, "Highlights are unavailable",
                        metadata.Error ?? "No compatible authoritative event stream was found.") });
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (_state.Selected?.Id == replay.Id)
                SetError(TheatreErrorKind.Analysis, "Replay analysis failed",
                    error.Message);
        }
    }

    private void SetError(TheatreErrorKind kind, string title, string detail,
        bool? loading = null)
    {
        Publish(_state with { Error = detail,
            StructuredError = new TheatreError(kind, title, detail),
            Loading = loading ?? _state.Loading });
    }

    private ReplayUserLibrarySelection? LoadUserMetadata(PrimeReplayEntry replay)
    {
        if (replay.Metadata?.ReplayId is not { Length: 64 } fingerprint) return null;
        try { return _userMetadata.Load(replay.Path, fingerprint); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException) { return null; }
    }

    private PrimeReplayEntry CurrentReplayWithFingerprint()
    {
        PrimeReplayEntry replay = _state.Selected
            ?? throw new InvalidOperationException("Choose a replay first.");
        if (replay.Metadata?.ReplayId is not { Length: 64 })
            throw new InvalidOperationException("Wait for replay analysis to finish.");
        return replay;
    }

    private uint BoundSelectedFrame(uint frame)
    {
        PrimeReplayEntry replay = CurrentReplayWithFingerprint();
        return Math.Min(frame, replay.Metadata!.DurationFrames);
    }

    private void PublishUserMetadata(string replayId,
        ReplayUserLibrarySelection selection)
    {
        if (_state.Selected?.Id == replayId)
            Publish(_state with { UserMetadata = selection,
                Error = null, StructuredError = null });
    }

    internal static IReadOnlyList<PrimeReplayEntry> FilterAndSort(
        IReadOnlyList<PrimeReplayEntry> source, TheatreFilters filters)
    {
        IEnumerable<PrimeReplayEntry> query = source;
        if (!String.IsNullOrWhiteSpace(filters.Search))
        {
            string search = filters.Search.Trim();
            query = query.Where(value => value.FileName.Contains(search,
                    StringComparison.OrdinalIgnoreCase)
                || value.Room.Contains(search, StringComparison.OrdinalIgnoreCase)
                || value.Metadata?.MapName.Contains(search,
                    StringComparison.OrdinalIgnoreCase) == true);
        }
        if (!String.IsNullOrWhiteSpace(filters.Map))
            query = query.Where(value => String.Equals(value.Metadata?.MapKey
                ?? value.Room, filters.Map, StringComparison.OrdinalIgnoreCase));
        if (filters.Mode is { } mode)
            query = query.Where(value => value.Metadata?.Mode == mode);
        if (filters.DateFrom is { } from)
            query = query.Where(value => value.Recorded >= from);
        if (filters.DateTo is { } to)
            query = query.Where(value => value.Recorded <= to);
        if (filters.HasHighlights is { } hasHighlights)
            query = query.Where(value => (value.Metadata?.HighlightCount > 0)
                == hasHighlights);
        if (filters.Recovery is { } recovery)
            query = query.Where(value => value.Metadata?.RecoveryStatus == recovery);

        query = filters.Sort switch
        {
            TheatreSortOrder.Oldest => query.OrderBy(value => value.Recorded),
            TheatreSortOrder.Duration => query.OrderByDescending(value =>
                value.Metadata?.Duration ?? TimeSpan.Zero)
                .ThenByDescending(value => value.Recorded),
            TheatreSortOrder.FileSize => query.OrderByDescending(value => value.Bytes)
                .ThenByDescending(value => value.Recorded),
            TheatreSortOrder.HighlightCount => query.OrderByDescending(value =>
                value.Metadata?.HighlightCount ?? 0)
                .ThenByDescending(value => value.Recorded),
            _ => query.OrderByDescending(value => value.Recorded)
        };
        return query.ToArray();
    }
}
