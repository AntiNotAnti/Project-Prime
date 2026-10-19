using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Launcher.Core;

internal sealed record NativeGameFilesState(bool Ready, string Description, string Root,
    bool PickerAvailable, bool CanRenderPreviews, int MissingPreviews, bool SetupBusy = false);
internal sealed record NativeGameFilesPick(string Path, string Name, string Label, string Error = "");
internal sealed record NativeGameFilesResult(bool Success, string Message);
internal interface INativeGameFilesAuthority
{
    NativeGameFilesState Read();
    Task<NativeGameFilesPick?> PickAsync(CancellationToken token);
    Task<NativeGameFilesResult> InstallAsync(string path, string expectedRoot, Action<string> report, CancellationToken token);
    Task<NativeGameFilesResult> PreviewsAsync(Action<string> report, CancellationToken token);
}
internal sealed record NativeGameFilesView(long Revision, NativeGameFilesState Files, bool Busy,
    bool Confirming, string SelectedName, string Status, string Log, string Stage, double Fraction,
    bool Closed, bool CanBack);

// All mutable UI state stays on the captured owner. A worker publishes only
// bounded immutable messages into its own operation; retirement drains it.
internal sealed class NativeGameFilesController : IDisposable
{
    private sealed class Operation(string kind, CancellationToken lifetime)
    {
        public readonly string Kind = kind;
        public readonly CancellationTokenSource Cancel = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        public readonly object Publication = new();
        public readonly Queue<string> Lines = new();
        public readonly SetupProgress Progress = new();
        public Task<object?> Task = System.Threading.Tasks.Task.FromResult<object?>(null);
        public bool Retired;
        public string ExpectedRoot = "";
    }
    private readonly INativeGameFilesAuthority _authority;
    private readonly Func<bool> _scope;
    private readonly Func<bool> _canStartImport;
    private readonly CancellationToken _lifetime;
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private NativeGameFilesState _files;
    private NativeGameFilesPick? _pick;
    private Operation? _operation;
    private string _status = "Choose your own Metroid Prime Hunters cartridge dump.", _log = "", _stage = "";
    private double _fraction;
    private long _revision = 1;
    private sealed record InstalledReceipt(string ExpectedRoot);
    private InstalledReceipt? _installed;
    private bool _closed, _disposed;
    public NativeGameFilesController(INativeGameFilesAuthority authority, Func<bool> scope, CancellationToken lifetime = default,
        Func<bool>? canStartImport = null)
    { _authority = authority; _scope = scope; _canStartImport = canStartImport ?? (() => true); _lifetime = lifetime; _files = authority.Read(); }
    private void Verify() { if (Environment.CurrentManagedThreadId != _owner) throw new InvalidOperationException("Game Files requires its owner thread."); }
    private bool Current() => !_disposed && !_closed && !_lifetime.IsCancellationRequested && _scope();
    public NativeGameFilesView Snapshot() { Verify(); return new(_revision, _files, _operation != null, _pick != null,
        _pick?.Name ?? "", _status, _log, _stage, _fraction, _closed, _files.Ready && _operation == null); }
    public bool Choose()
    {
        Verify(); if (!Current() || _operation != null || _pick != null || _files.SetupBusy) return false;
        _installed = null; // an explicit newer journey retires the prior receipt
        if (!_files.PickerAvailable) { _status = "This desktop has no file dialog. Install zenity or kdialog and try again."; _revision++; return true; }
        var authority = _authority;
        Start("picker", async op => await authority.PickAsync(op.Cancel.Token)); return true;
    }
    public bool Confirm()
    {
        Verify(); if (!Current() || !_canStartImport() || _operation != null || _pick == null || _files.SetupBusy) return false;
        NativeGameFilesState now = _authority.Read();
        if (!Current() || !_canStartImport())
        { _pick = null; _status = "Setup is no longer current. Choose the cartridge dump again."; _revision++; return false; }
        if (!String.Equals(now.Root, _files.Root, StringComparison.Ordinal) || now.SetupBusy)
        { _pick = null; _files = now; _status = "The game-files destination changed or another setup is working. Choose again."; _revision++; return false; }
        var proposal = _pick; _pick = null;
        // Admission witness is captured before consuming the proposal; never
        // adopt a newly selected root from an asynchronous continuation.
        string root = _files.Root;
        var authority = _authority;
        Start("install", async op => await authority.InstallAsync(proposal.Path, root, line => Report(op, line), op.Cancel.Token)); return true;
    }
    public bool Previews()
    {
        Verify(); if (!Current() || _operation != null || _pick != null || _files.SetupBusy || !_files.Ready
            || !_files.CanRenderPreviews || _files.MissingPreviews <= 0) return false;
        var authority = _authority;
        Start("previews", async op => await authority.PreviewsAsync(line => Report(op, line), op.Cancel.Token)); return true;
    }
    private void Start(string kind, Func<Operation, Task<object?>> start)
    {
        _installed = null;
        var op = new Operation(kind, _lifetime) { ExpectedRoot = _files.Root }; _operation = op;
        _log = ""; _stage = kind == "install" ? "Starting" : ""; _fraction = 0;
        _status = kind == "install" ? "Installing game files. Once extraction starts, leaving cannot undo files already written."
            : kind == "picker" ? "Choose a cartridge dump in the system file dialog." : "Rendering missing map previews.";
        _revision++;
        try { op.Task = start(op); } catch (Exception e) { op.Task = Task.FromException<object?>(e); }
    }
    private static void Report(Operation op, string line)
    {
        lock (op.Publication)
        {
            if (op.Retired || op.Cancel.IsCancellationRequested) return;
            string text = (line ?? "").Replace('\r', ' '); if (text.Length > 1024) text = text[..1024];
            if (op.Lines.Count == 8) op.Lines.Dequeue(); op.Lines.Enqueue(text);
            if (op.Kind == "install") op.Progress.Observe(text);
        }
    }
    public NativeGameFilesView Tick()
    {
        Verify(); if (!Current()) { Retire(); _installed = null; _closed = true; return Snapshot(); }
        var op = _operation;
        if (op == null) return Snapshot();
        lock (op.Publication)
        {
            string log = String.Join("\n", op.Lines);
            if (_log != log || _stage != op.Progress.Stage || _fraction != op.Progress.Fraction)
            { _log = log; _stage = op.Kind == "install" ? op.Progress.Stage : ""; _fraction = op.Progress.Fraction; _revision++; }
        }
        if (!op.Task.IsCompleted) return Snapshot();
        // Selecting a valid ROM is the import action. If another modal covers
        // setup while the OS picker closes, defer its result until setup owns
        // the foreground again; retirement/cancellation still wins above.
        if (op.Kind == "picker" && !_canStartImport()) return Snapshot();
        _operation = null;
        try
        {
            object? result = op.Task.GetAwaiter().GetResult();
            if (op.Cancel.IsCancellationRequested) _status = "Stopped waiting. Check game files for the actual result.";
            else if (op.Kind == "picker")
            {
                NativeGameFilesState current = _authority.Read();
                if (!String.Equals(current.Root, _files.Root, StringComparison.Ordinal) || current.SetupBusy)
                { _files = current; _status = "The game-files destination changed or setup started. Choose again."; }
                else if (result is NativeGameFilesPick picked && picked.Error.Length == 0 && !String.IsNullOrWhiteSpace(picked.Path))
                {
                    _pick = picked;
                    try { Confirm(); }
                    finally { _pick = null; }
                }
                else _status = result is NativeGameFilesPick rejected && rejected.Error.Length > 0 ? rejected.Error : "No cartridge dump selected.";
            }
            else if (result is NativeGameFilesResult finished)
            {
                _files = _authority.Read(); _status = finished.Message;
                if (op.Kind == "install" && finished.Success && _files.Ready && !_files.SetupBusy)
                {
                    if (String.Equals(_files.Root, op.ExpectedRoot, StringComparison.Ordinal))
                    { _installed = new(op.ExpectedRoot); _fraction = 1; _stage = "Ready to play"; }
                    else _status = "Extraction finished for the captured folder; the active destination changed. Check game files.";
                }
            }
        }
        catch (OperationCanceledException) { _status = "Stopped waiting. Check game files for the actual result."; }
        catch { _status = "Game-file work did not finish. Check the selected file and try again."; }
        finally { Retire(op); _revision++; }
        return Snapshot();
    }
    public bool Cancel()
    {
        Verify(); if (!Current()) return false;
        if (_pick != null) { _pick = null; _status = "Installation canceled before extraction."; _revision++; return true; }
        if (_operation != null)
        {
            string kind = _operation.Kind; Retire();
            _status = kind == "install" ? "Stopped waiting. Extraction already started may still finish; check game files."
                : "Stopped waiting. A canceled system picker result will be ignored.";
            // Actual owner lease/readiness is reread on explicit Refresh only.
            _files = _files with { SetupBusy = kind == "install" || _files.SetupBusy };
            _revision++; return true;
        }
        return false;
    }
    public bool Refresh()
    {
        Verify(); if (!Current() || _operation != null || _pick != null) return false;
        _installed = null;
        _files = _authority.Read(); _status = _files.SetupBusy ? "Another setup is still working. Check again when it finishes." : _files.Description; _revision++; return true;
    }
    public bool Back()
    {
        Verify(); if (!Current()) return false;
        if (_pick != null) return Cancel();
        if (!_files.Ready || _operation != null) return false;
        Retire(); _closed = true; _revision++; return true;
    }
    public bool TryTakeInstalled()
    {
        Verify(); var receipt = _installed;
        if (receipt == null || !Current() || _operation != null || _pick != null) return false;
        NativeGameFilesState now;
        try { now = _authority.Read(); }
        catch
        {
            if (ReferenceEquals(_installed, receipt)) _installed = null;
            _status = "Could not verify installed game files. Check game files."; _revision++; return false;
        }
        // The foreground may return long after Tick verified extraction. This
        // receipt belongs to that exact operation/root, never a newer journey.
        if (!Current() || !ReferenceEquals(_installed, receipt) || _operation != null || _pick != null) return false;
        _installed = null; _files = now;
        if (!now.Ready || now.SetupBusy || !String.Equals(now.Root, receipt.ExpectedRoot, StringComparison.Ordinal))
        { _status = "Installed files are no longer current or setup is working. Check game files."; _revision++; return false; }
        return true;
    }
    private static void Retire(Operation op)
    {
        lock (op.Publication) { op.Retired = true; op.Lines.Clear(); }
        op.Cancel.Cancel();
        // Task may be a non-cancelable existing extractor. Observe its failure
        // without retaining any page/controller callback or authority.
        _ = op.Task.ContinueWith(t => { _ = t.Exception; op.Cancel.Dispose(); }, CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
    private void Retire() { var old = _operation; _operation = null; _pick = null; if (old != null) Retire(old); }
    public void Dispose() { Verify(); if (_disposed) return; _disposed = true; Retire(); _installed = null; _revision++; }
}
