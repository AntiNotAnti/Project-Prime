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
    DateTime Recorded, long Bytes)
{
    public string Size => Bytes >= 1024 * 1024
        ? $"{Bytes / (1024d * 1024d):0.0} MB"
        : Bytes >= 1024 ? $"{Bytes / 1024d:0} KB" : $"{Bytes} bytes";
}

public sealed record TheatreState(IReadOnlyList<PrimeReplayEntry> Replays,
    PrimeReplayEntry? Selected, bool Loading, string? Error,
    ReplayHighlightMetadata? HighlightMetadata = null, int SelectedHighlight = -1)
{
    public static TheatreState Initial => new(Array.Empty<PrimeReplayEntry>(), null, false, null);
    public IReadOnlyList<ReplayHighlight> Highlights
        => HighlightMetadata?.Highlights ?? Array.Empty<ReplayHighlight>();
    public int HighlightCount => Highlights.Count;
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
        try { File.Delete(replay.Path); return !File.Exists(replay.Path); }
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
        try { File.Move(replay.Path, destination, overwrite: false); return true; }
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

    private static PrimeReplayEntry ToEntry(ReplayRecording replay)
        => new(replay.Path, replay.Path, replay.FileName, replay.Room, replay.Recorded, replay.Bytes);

    private bool IsLocal(PrimeReplayEntry replay)
    {
        return File.Exists(replay.Path)
            && ReplayFileNamePolicy.IsWithinDirectory(ResolveDirectory(), replay.Path);
    }

    private void EnsureDirectory() => System.IO.Directory.CreateDirectory(ResolveDirectory());

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
    private readonly ReplayHighlightMetadataService _highlights;
    private readonly CancellationTokenSource _lifetime = new();
    private TheatreState _state = TheatreState.Initial;
    private int _disposed;

    public TheatreController(IPrimeReplayLibrary? library = null,
        ReplayHighlightMetadataService? highlights = null)
    {
        _library = library ?? new ReplayLibraryAdapter();
        _highlights = highlights ?? new ReplayHighlightMetadataService(
            Environment.CurrentDirectory);
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
        Publish(_state with { Selected = replay, HighlightMetadata = null,
            SelectedHighlight = -1 });
        if (replay != null) _ = LoadHighlightsAsync(replay, _lifetime.Token);
    }

    public void SelectHighlight(int index)
    {
        if ((uint)index >= (uint)_state.HighlightCount) return;
        Publish(_state with { SelectedHighlight = index });
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
        IReadOnlyList<ReplayHighlight>? highlights, CancellationToken cancellationToken)
    {
        replay ??= _state.Selected;
        if (replay == null) throw new InvalidOperationException("Choose a replay first.");
        if (!File.Exists(replay.Path)) throw new FileNotFoundException("The replay is no longer available.");
        bool opened = await Task.Run(() => ReplayPlayback.Join(replay.Path), cancellationToken)
            .ConfigureAwait(false);
        if (!opened)
        {
            Publish(_state with { Error = ReplayPlayback.LastError ?? "Replay could not be opened." });
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
            ReplayHighlights = highlights
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
        return deleted;
    }

    public async Task<bool> RenameAsync(string fileName, PrimeReplayEntry? replay = null)
    {
        if (!_library.SupportsRename) return false;
        replay ??= _state.Selected;
        if (replay == null) return false;
        bool renamed = await Task.Run(() => _library.Rename(replay, fileName)).ConfigureAwait(false);
        if (renamed) await LoadAsync().ConfigureAwait(false);
        return renamed;
    }

    public async Task<bool> ImportAsync(string sourcePath,
        CancellationToken cancellationToken = default)
    {
        if (!_library.SupportsImport) return false;
        bool imported = await Task.Run(() => _library.Import(sourcePath, out _), cancellationToken)
            .ConfigureAwait(false);
        if (imported) await LoadAsync(cancellationToken).ConfigureAwait(false);
        return imported;
    }

    public async Task<bool> ExportAsync(string destinationPath,
        PrimeReplayEntry? replay = null, CancellationToken cancellationToken = default)
    {
        if (!_library.SupportsExport) return false;
        replay ??= _state.Selected;
        if (replay == null) return false;
        return await Task.Run(() => _library.Export(replay, destinationPath), cancellationToken)
            .ConfigureAwait(false);
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
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    private async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        Publish(_state with { Loading = true, Error = null });
        try
        {
            IReadOnlyList<PrimeReplayEntry> replays = await Task.Run(_library.List, cancellationToken)
                .ConfigureAwait(false);
            if (Volatile.Read(ref _disposed) != 0) return;
            PrimeReplayEntry? selected = _state.Selected is { } old
                ? replays.FirstOrDefault(item => item.Id == old.Id) : replays.FirstOrDefault();
            Publish(new TheatreState(replays, selected, false, null));
            if (selected != null) await LoadHighlightsAsync(selected, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception error) { Publish(_state with { Loading = false, Error = error.Message }); }
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
        return await Task.Run(() => _highlights.Get(replay.Path), cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task LoadHighlightsAsync(PrimeReplayEntry replay,
        CancellationToken cancellationToken)
    {
        try
        {
            ReplayHighlightMetadata metadata = await GetHighlightsAsync(replay,
                cancellationToken).ConfigureAwait(false);
            if (Volatile.Read(ref _disposed) == 0 && _state.Selected?.Id == replay.Id)
                Publish(_state with { HighlightMetadata = metadata,
                    SelectedHighlight = metadata.Highlights.Count == 0 ? -1 : 0 });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (_state.Selected?.Id == replay.Id)
                Publish(_state with { Error = error.Message });
        }
    }
}
