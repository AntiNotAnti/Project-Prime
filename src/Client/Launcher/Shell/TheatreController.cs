using System;
using System.Collections.Generic;
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
    IReadOnlyList<PrimeReplayEntry> List();
    bool Delete(PrimeReplayEntry replay);
    bool Rename(PrimeReplayEntry replay, string fileName);
    bool Import(string sourcePath, out PrimeReplayEntry? imported);
    bool Export(PrimeReplayEntry replay, string destinationPath);
}

internal sealed class ReplayLibraryAdapter : IPrimeReplayLibrary
{
    public string Directory => ReplayLibrary.Directory;
    public bool SupportsImport => true;
    public bool SupportsExport => true;
    public bool SupportsRename => true;

    public IReadOnlyList<PrimeReplayEntry> List()
        => ReplayLibrary.List().Select(ToEntry).ToArray();

    public bool Delete(PrimeReplayEntry replay)
    {
        if (!IsLocal(replay)) return false;
        try { File.Delete(replay.Path); return !File.Exists(replay.Path); }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    public bool Rename(PrimeReplayEntry replay, string fileName)
    {
        if (!IsLocal(replay) || fileName is not { Length: > 0 and <= 128 }
            || fileName.Any(c => c is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*')
            || !fileName.EndsWith(ReplayFile.Extension, StringComparison.OrdinalIgnoreCase)) return false;
        string destination = Path.Combine(ReplayLibrary.Directory, fileName);
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
            ReplayLibraryAdapter.EnsureDirectory();
            string name = Path.GetFileName(sourcePath);
            if (!name.EndsWith(ReplayFile.Extension, StringComparison.OrdinalIgnoreCase)) return false;
            string destination = Path.Combine(ReplayLibrary.Directory, name);
            if (File.Exists(destination))
                destination = Path.Combine(ReplayLibrary.Directory,
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
        if (!IsLocal(replay) || destinationPath is not { Length: > 0 }) return false;
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

    private static PrimeReplayEntry ToEntry(ReplayRecording replay)
        => new(replay.Path, replay.Path, replay.FileName, replay.Room, replay.Recorded, replay.Bytes);

    private static bool IsLocal(PrimeReplayEntry replay)
    {
        string root = Path.GetFullPath(ReplayLibrary.Directory) + Path.DirectorySeparatorChar;
        string path;
        try { path = Path.GetFullPath(replay.Path); }
        catch (Exception) { return false; }
        return path.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureDirectory() => System.IO.Directory.CreateDirectory(ReplayLibrary.Directory);
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
