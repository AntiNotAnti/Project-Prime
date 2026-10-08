using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Launcher.Core;

public sealed record SetupRelease(string Tag, string Version, string Asset, string Notes, string Page, bool CanInstall, bool Downgrade);
public sealed record SetupFiles(bool Ready, string Description, string Root, string Revision, bool CanPickRom, bool CanRenderPreviews, bool UpdatesDisabled);
public sealed record SetupResult(bool Success, string Message, IReadOnlyList<SetupRelease>? Releases = null, bool Prepared = false);
public sealed record SetupSnapshot(long Revision, SetupFiles Files, bool Busy, string Status, string Error,
    string Progress, string Log, string Rom, IReadOnlyList<SetupRelease> Releases, int SelectedRelease, bool ConfirmingRom,
    bool ConfirmingUpdate, bool Prepared, bool WaitingForInstaller, bool CanLeave);

/// <summary>UI-independent setup workflow. All completion publication and final installation happen in Tick/on the owner thread.</summary>
public interface ISetupBackend
{
    SetupFiles Inspect();
    Task<string?> PickRom(CancellationToken cancel);
    SetupResult Extract(string path, Action<string> report);
    SetupResult ConfigureExtractedPath(string revision, string path);
    SetupResult VerifyInstallation();
    SetupResult RenderPreviews(Action<string> report);
    SetupResult CheckLatest(CancellationToken cancel);
    SetupResult LoadReleases(CancellationToken cancel);
    SetupResult? RequestPreparePermission(int index) => null;
    SetupResult PrepareRelease(int index, Action<float> progress);
    SetupResult InstallPrepared(out bool exitAfterInstall);
    SetupResult OpenRelease(int index);
    SetupResult OpenDataFolder();
    bool TryTakeInstallerResult(out SetupResult result);
}

public sealed class SetupController : IDisposable
{
    private readonly ISetupBackend _backend;
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private readonly bool _inGame;
    private readonly bool _required;
    private readonly ConcurrentQueue<(string? Line, float? Progress)> _reports = new();
    private readonly CancellationTokenSource _lifetime = new();
    private Task<SetupResult>? _operation;
    private SetupFiles _files;
    private IReadOnlyList<SetupRelease> _releases = Array.Empty<SetupRelease>();
    private string _status = "", _error = "", _progress = "", _log = "", _rom = "";
    private int _selected = -1;
    private long _revision = 1;
    private bool _disposed, _confirmRom, _confirmUpdate, _prepared, _waiting, _exit;
    public SetupController(ISetupBackend backend, bool inGame = false, bool required = false)
    { _backend = backend ?? throw new ArgumentNullException(nameof(backend)); _inGame = inGame; _required = required; _files = backend.Inspect(); }
    public bool Busy { get { VerifyOwner(); return _operation != null || _waiting; } }
    public bool ExitRequested { get { VerifyOwner(); return _exit; } }
    public bool CanLeave => !Busy && (!_required || _files.Ready);
    public SetupSnapshot Snapshot()
    { VerifyOwner(); return new(_revision, _files, Busy, _status, _error, _progress, _log, _rom,
        _releases, _selected, _confirmRom, _confirmUpdate, _prepared, _waiting, CanLeave); }
    private void VerifyOwner()
    { if(Environment.CurrentManagedThreadId != _ownerThread) throw new InvalidOperationException("Setup must be accessed on its owner thread."); }
    private bool Available()
    { VerifyOwner(); if (_disposed || Busy) return false; _error = ""; return true; }
    private void Result(SetupResult result)
    {
        if (result.Success) { _status = result.Message; _error = ""; }
        else { _error = result.Message; _status = ""; }
        if (result.Releases != null) { _releases = new List<SetupRelease>(result.Releases).AsReadOnly(); _selected = _releases.Count > 0 ? 0 : -1; _prepared = false; }
        if (result.Prepared) _prepared = true;
        _revision++;
    }
    private bool Start(Func<SetupResult> work, string status)
    {
        if (!Available()) return false;
        _status = status; _progress = ""; _confirmRom = _confirmUpdate = false; _revision++;
        _operation = Task.Run(work); return true;
    }
    public bool RefreshFiles()
    {
        if (!Available()) return false;
        try { _files = _backend.Inspect(); Result(new(true, _files.Description)); return true; }
        catch (Exception ex) { Result(new(false, ex.Message)); return false; }
    }
    public bool PickRom()
    {
        if (!Available() || _inGame || !_files.CanPickRom) return false;
        _status = "Choose your Metroid Prime Hunters ROM dump."; _revision++;
        _operation = Pick(); return true;
        async Task<SetupResult> Pick()
        {
            string? path = await _backend.PickRom(_lifetime.Token).ConfigureAwait(false);
            // Only immutable completion data crosses back to the owner thread.
            return new(path != null, path == null ? "ROM selection cancelled." : "rom:" + path);
        }
    }
    public bool RequestRom(string path)
    {
        if (!Available() || _inGame) return false;
        if (string.IsNullOrWhiteSpace(path) || path.Length > 32767 || path.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0)
        { Result(new(false, "Choose a readable .nds file or enter its full path.")); return false; }
        _rom = path.Trim(); _confirmRom = true; _confirmUpdate = false; _prepared = false;
        _status = "Install the selected ROM into the game-data folder? Existing files for this revision will be replaced."; _revision++; return true;
    }
    public bool ConfirmRom()
    {
        VerifyOwner(); if (!_confirmRom || _inGame) return false;
        string path = _rom; _log = "";
        return Start(() => _backend.Extract(path, line => _reports.Enqueue((line, null))), "Setting up game files…");
    }
    public bool ConfigurePath(string revision, string path)
    {
        if (!Available() || _inGame) return false;
        try { var result = _backend.ConfigureExtractedPath(revision, path); Result(result); if (result.Success) _files = _backend.Inspect(); return result.Success; }
        catch (Exception ex) { Result(new(false, ex.Message)); return false; }
    }
    public bool Verify() => Start(_backend.VerifyInstallation, "Verifying application files…");
    public bool RenderPreviews()
    { VerifyOwner(); return !_inGame && _files.Ready && _files.CanRenderPreviews
        && Start(() => _backend.RenderPreviews(line => _reports.Enqueue((line, null))), "Rendering missing map previews…"); }
    public bool CheckLatest()
    { VerifyOwner(); return !_files.UpdatesDisabled && Start(() => _backend.CheckLatest(_lifetime.Token), "Checking published releases…"); }
    public bool LoadReleases()
    { VerifyOwner(); return !_files.UpdatesDisabled && Start(() => _backend.LoadReleases(_lifetime.Token), "Loading published versions…"); }
    public bool SelectRelease(int index)
    {
        if (!Available() || index < 0 || index >= _releases.Count) return false;
        _selected = index; _prepared = false; _confirmUpdate = false; _revision++; return true;
    }
    public bool PresentPublishedReleases(IReadOnlyList<SetupRelease> releases, string status)
    {
        if (!Available()) return false;
        // Accept only a detached bounded snapshot supplied by the authoritative backend.
        if (releases.Count > 30) return false;
        Result(new(true, status, new List<SetupRelease>(releases))); return true;
    }
    public bool RequestPrepare()
    {
        if (!Available() || _inGame || _selected < 0 || _selected >= _releases.Count) return false;
        _confirmUpdate = true; _confirmRom = false; _revision++; return true;
    }
    public bool ConfirmPrepare()
    {
        VerifyOwner(); if (!_confirmUpdate || _inGame || _selected < 0 || !Available()) return false;
        try { if (_backend.RequestPreparePermission(_selected) is { } permission) { _confirmUpdate = false; Result(permission); return false; } }
        catch (Exception ex) { _confirmUpdate = false; Result(new(false, ex.Message)); return false; }
        int index = _selected; _prepared = false;
        return Start(() => _backend.PrepareRelease(index, progress => _reports.Enqueue((null, progress))), "Preparing the selected release…");
    }
    /// <summary>Explicit final action after the prepared package is visible. Never called by a worker or an automatic check.</summary>
    public bool InstallPrepared()
    {
        if (!Available() || _inGame || !_prepared) return false;
        try
        {
            SetupResult result = _backend.InstallPrepared(out bool exit); Result(result);
            if (result.Success) { _exit = exit; _waiting = !exit; _prepared = false; }
            return result.Success;
        }
        catch (Exception ex) { Result(new(false, ex.Message)); return false; }
    }
    public bool OpenRelease()
    {
        if (!Available() || _selected < 0) return false;
        try { Result(_backend.OpenRelease(_selected)); return true; }
        catch (Exception ex) { Result(new(false, ex.Message)); return false; }
    }
    public bool OpenFolder()
    {
        if (!Available()) return false;
        try { Result(_backend.OpenDataFolder()); return true; }
        catch (Exception ex) { Result(new(false, ex.Message)); return false; }
    }
    public bool CancelConfirmation()
    { if (!Available()) return false; _confirmRom = _confirmUpdate = false; _revision++; return true; }
    public void Tick()
    {
        VerifyOwner(); if (_disposed) return;
        while (_reports.TryDequeue(out var report))
        {
            if (report.Line != null) { _progress = report.Line; _log += report.Line + "\n"; if (_log.Length > 3600) _log = _log[^3600..]; }
            if (report.Progress is float p) _progress = p < 0 ? "Downloading package…" : $"Downloading package… {Math.Clamp((int)(p * 100), 0, 100)}%";
            _revision++;
        }
        if (_operation?.IsCompleted == true)
        {
            var operation = _operation; _operation = null;
            try
            {
                var result = operation.GetAwaiter().GetResult();
                if (result.Success && result.Message.StartsWith("rom:", StringComparison.Ordinal)) RequestRom(result.Message[4..]);
                else { Result(result); _files = _backend.Inspect(); }
            }
            catch (Exception ex) { Result(new(false, ex.GetBaseException().Message)); }
        }
        if (_backend.TryTakeInstallerResult(out var finished)) { _waiting = false; Result(finished); }
    }
    public void Dispose()
    { VerifyOwner(); if (_disposed) return; _disposed = true; _lifetime.Cancel(); _lifetime.Dispose(); }
}
