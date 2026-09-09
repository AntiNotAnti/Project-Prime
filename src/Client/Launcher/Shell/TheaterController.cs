using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher;
using MphRead.Mods.Network;

namespace MphRead.Mods.Launcher.Gui;

public sealed record PrimeDemoEntry(string Id, string Path, string FileName, string Room,
    DateTime Recorded, long Bytes)
{
    public string Size => Bytes >= 1024 * 1024
        ? $"{Bytes / (1024d * 1024d):0.0} MB"
        : Bytes >= 1024 ? $"{Bytes / 1024d:0} KB" : $"{Bytes} bytes";
}

public sealed record TheaterState(IReadOnlyList<PrimeDemoEntry> Demos,
    PrimeDemoEntry? Selected, bool Loading, string? Error)
{
    public static TheaterState Initial => new(Array.Empty<PrimeDemoEntry>(), null, false, null);
}

public interface IPrimeDemoLibrary
{
    string Directory { get; }
    bool SupportsImport { get; }
    bool SupportsExport { get; }
    bool SupportsRename { get; }
    IReadOnlyList<PrimeDemoEntry> List();
    bool Delete(PrimeDemoEntry demo);
    bool Rename(PrimeDemoEntry demo, string fileName);
    bool Import(string sourcePath, out PrimeDemoEntry? imported);
    bool Export(PrimeDemoEntry demo, string destinationPath);
}

internal sealed class DemoLibraryAdapter : IPrimeDemoLibrary
{
    public string Directory => DemoLibrary.Directory;
    public bool SupportsImport => true;
    public bool SupportsExport => true;
    public bool SupportsRename => true;

    public IReadOnlyList<PrimeDemoEntry> List()
        => DemoLibrary.List().Select(ToEntry).ToArray();

    public bool Delete(PrimeDemoEntry demo)
    {
        if (!IsLocal(demo)) return false;
        try { File.Delete(demo.Path); return !File.Exists(demo.Path); }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    public bool Rename(PrimeDemoEntry demo, string fileName)
    {
        if (!IsLocal(demo) || fileName is not { Length: > 0 and <= 128 }
            || fileName.Any(c => c is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*')
            || !fileName.EndsWith(DemoFile.Extension, StringComparison.OrdinalIgnoreCase)) return false;
        string destination = Path.Combine(DemoLibrary.Directory, fileName);
        try { File.Move(demo.Path, destination, overwrite: false); return true; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    public bool Import(string sourcePath, out PrimeDemoEntry? imported)
    {
        imported = null;
        if (sourcePath is not { Length: > 0 } || !File.Exists(sourcePath)) return false;
        try
        {
            DemoLibraryAdapter.EnsureDirectory();
            string name = Path.GetFileName(sourcePath);
            if (!name.EndsWith(DemoFile.Extension, StringComparison.OrdinalIgnoreCase)) return false;
            string destination = Path.Combine(DemoLibrary.Directory, name);
            if (File.Exists(destination))
                destination = Path.Combine(DemoLibrary.Directory,
                    Path.GetFileNameWithoutExtension(name) + "-imported" + DemoFile.Extension);
            File.Copy(sourcePath, destination, overwrite: false);
            imported = List().FirstOrDefault(item => item.Path == destination);
            return imported != null;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    public bool Export(PrimeDemoEntry demo, string destinationPath)
    {
        if (!IsLocal(demo) || destinationPath is not { Length: > 0 }) return false;
        try
        {
            string? parent = Path.GetDirectoryName(destinationPath);
            if (string.IsNullOrEmpty(parent)) return false;
            File.Copy(demo.Path, destinationPath, overwrite: false);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static PrimeDemoEntry ToEntry(DemoRecording demo)
        => new(demo.Path, demo.Path, demo.FileName, demo.Room, demo.Recorded, demo.Bytes);

    private static bool IsLocal(PrimeDemoEntry demo)
    {
        string root = Path.GetFullPath(DemoLibrary.Directory) + Path.DirectorySeparatorChar;
        string path;
        try { path = Path.GetFullPath(demo.Path); }
        catch (Exception) { return false; }
        return path.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureDirectory() => System.IO.Directory.CreateDirectory(DemoLibrary.Directory);
}

/// <summary>Local replay library; playback emits the existing Demo LaunchPlan.</summary>
public sealed class TheaterController : IDisposable
{
    private readonly IPrimeDemoLibrary _library;
    private readonly CancellationTokenSource _lifetime = new();
    private TheaterState _state = TheaterState.Initial;
    private int _disposed;

    public TheaterController(IPrimeDemoLibrary? library = null)
        => _library = library ?? new DemoLibraryAdapter();

    public TheaterState State => _state;
    public IPrimeDemoLibrary Library => _library;
    public event EventHandler? Changed;
    public event EventHandler<LaunchPlan>? Launch;

    public Task LoadAsync(CancellationToken cancellationToken = default)
        => LoadCoreAsync(cancellationToken);

    public void Select(PrimeDemoEntry? demo)
    {
        if (demo != null && !_state.Demos.Any(entry => entry.Id == demo.Id)) return;
        Publish(_state with { Selected = demo });
    }

    public async Task<bool> PlayAsync(PrimeDemoEntry? demo = null,
        CancellationToken cancellationToken = default)
    {
        demo ??= _state.Selected;
        if (demo == null) throw new InvalidOperationException("Choose a replay first.");
        if (!File.Exists(demo.Path)) throw new FileNotFoundException("The replay is no longer available.");
        bool opened = await Task.Run(() => DemoPlayback.Join(demo.Path), cancellationToken)
            .ConfigureAwait(false);
        if (!opened)
        {
            Publish(_state with { Error = DemoPlayback.LastError ?? "Replay could not be opened." });
            return false;
        }
        (string RoomKey, GameMode Mode)? room = NetLaunch.ServerRoom();
        LaunchPlan plan = new()
        {
            Kind = LaunchKind.Demo,
            DemoPath = demo.Path,
            RoomKey = room?.RoomKey ?? demo.Room,
            Mode = room?.Mode ?? GameMode.Battle,
            Hunter = Hunter.Samus,
            PlayerName = LauncherPrefs.PlayerName
        };
        Launch?.Invoke(this, plan);
        return true;
    }

    public async Task<bool> DeleteAsync(PrimeDemoEntry? demo = null)
    {
        demo ??= _state.Selected;
        if (demo == null) return false;
        bool deleted = await Task.Run(() => _library.Delete(demo)).ConfigureAwait(false);
        if (deleted) await LoadAsync().ConfigureAwait(false);
        return deleted;
    }

    public async Task<bool> RenameAsync(string fileName, PrimeDemoEntry? demo = null)
    {
        if (!_library.SupportsRename) return false;
        demo ??= _state.Selected;
        if (demo == null) return false;
        bool renamed = await Task.Run(() => _library.Rename(demo, fileName)).ConfigureAwait(false);
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
        PrimeDemoEntry? demo = null, CancellationToken cancellationToken = default)
    {
        if (!_library.SupportsExport) return false;
        demo ??= _state.Selected;
        if (demo == null) return false;
        return await Task.Run(() => _library.Export(demo, destinationPath), cancellationToken)
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
            IReadOnlyList<PrimeDemoEntry> demos = await Task.Run(_library.List, cancellationToken)
                .ConfigureAwait(false);
            if (Volatile.Read(ref _disposed) != 0) return;
            PrimeDemoEntry? selected = _state.Selected is { } old
                ? demos.FirstOrDefault(item => item.Id == old.Id) : demos.FirstOrDefault();
            Publish(new TheaterState(demos, selected, false, null));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception error) { Publish(_state with { Loading = false, Error = error.Message }); }
    }

    private void Publish(TheaterState state)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        _state = state;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
