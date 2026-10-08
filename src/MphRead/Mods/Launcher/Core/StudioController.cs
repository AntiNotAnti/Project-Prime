using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Launcher.Core;

/// <summary>Owner-thread launch commands and cancellable picker results; workers never open Studio or touch a document.</summary>
public sealed class StudioController : IDisposable
{
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private readonly Guid _lifetime = Guid.NewGuid();
    private readonly IStudioEntryBackend _backend;
    private readonly ConcurrentQueue<(int Generation, StudioPickResult Result)> _completed = new();
    private CancellationTokenSource? _pickerCancellation;
    private StudioAvailability _availability;
    private StudioViewSnapshot _snapshot = new();
    private StudioOperation _operation;
    private string _path = "", _status = "", _error = "", _documentName = "";
    private StudioDocumentKind? _documentKind;
    private ulong _version, _pathEpoch;
    private int _generation;
    private bool _disposed;

    public StudioController(IStudioEntryBackend backend)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _availability = CaptureAvailability();
        _status = _availability.Message;
        Publish();
    }

    public StudioViewSnapshot Snapshot { get { VerifyOwner(); return _snapshot; } }
    // Editable paths do not travel in snapshots, native intent packets or logs.
    public string PathDraft { get { VerifyOwner(); return _path; } }

    public StudioActionResult SetPath(string path)
    {
        VerifyOwner();
        if (_disposed) return Reject("This Studio entry page has closed.");
        if (_operation != StudioOperation.None) return Reject("Wait for the current Studio action to finish.");
        if (path == null || path.Length > 32767 || path.Contains('\0')) return Reject("Enter a valid file path of at most 32767 characters.");
        _path = path;
        _pathEpoch++;
        _error = "";
        Publish();
        return new(true);
    }

    public StudioActionResult Launch() => LaunchCore(null, recover: false);
    public StudioActionResult Recover() => LaunchCore(null, recover: true);

    public StudioActionResult OpenPath(string? path = null, bool recover = false)
    {
        VerifyOwner();
        if (path != null)
        {
            StudioActionResult changed = SetPath(path);
            if (!changed.Accepted) return changed;
        }
        return LaunchCore(_path, recover);
    }

    public StudioActionResult PickMap()
    {
        VerifyOwner();
        if (_disposed) return Reject("This Studio entry page has closed.");
        if (_operation != StudioOperation.None) return Reject("A Studio action is already in progress.");
        _availability = CaptureAvailability();
        if (!_availability.DesktopSupported) return Reject(_availability.Message);
        if (!_availability.PickerAvailable)
            return Reject("The system file picker is unavailable. Enter a map project path below, or open Studio and use File → Open Map.");
        CancelPicker();
        _pickerCancellation = new();
        _operation = StudioOperation.PickingMap;
        _status = "Choose a map project in the system file dialog.";
        _error = "";
        int generation = _generation;
        _ = CompletePickerAsync(generation, _pickerCancellation.Token);
        Publish();
        return new(true);
    }

    /// <summary>Call on the render owner while the page remains live. Only Pump may act on an async selection.</summary>
    public void Pump()
    {
        VerifyOwner();
        if (_disposed) return;
        while (_completed.TryDequeue(out var completion))
        {
            if (completion.Generation != _generation || _operation != StudioOperation.PickingMap) continue;
            _pickerCancellation?.Dispose();
            _pickerCancellation = null;
            _operation = StudioOperation.None;
            if (!String.IsNullOrEmpty(completion.Result.Error)) { Reject(completion.Result.Error); continue; }
            if (completion.Result.Path == null)
            {
                _status = "No file selected. Your path draft is retained.";
                _error = "";
                Publish();
                continue;
            }
            _path = completion.Result.Path;
            _pathEpoch++;
            StudioPathResult selected = ValidatePath(_path);
            if (!selected.Valid) { Reject(selected.Error); continue; }
            if (selected.Kind != StudioDocumentKind.Map) { Reject("Choose a .json map project or .ppmap map package."); continue; }
            LaunchCore(_path, recover: false);
        }
    }

    public StudioActionResult Cancel()
    {
        VerifyOwner();
        if (_disposed) return new(false, "This Studio entry page has closed.");
        bool picking = _operation == StudioOperation.PickingMap;
        CancelPicker();
        _operation = StudioOperation.None;
        _status = picking ? "Selection cancelled. Close the system file dialog if it is still open; its result will be ignored."
            : "Studio entry is ready.";
        _error = "";
        Publish();
        return new(true);
    }

    private StudioActionResult LaunchCore(string? path, bool recover)
    {
        VerifyOwner();
        if (_disposed) return Reject("This Studio entry page has closed.");
        if (_operation != StudioOperation.None) return Reject("Wait for the current Studio action to finish.");
        _availability = CaptureAvailability();
        if (!_availability.DesktopSupported) return Reject(_availability.Message);
        string? canonical = null;
        StudioDocumentKind? kind = null;
        if (path != null)
        {
            StudioPathResult validated = ValidatePath(path);
            if (!validated.Valid) return Reject(validated.Error);
            canonical = validated.Path;
            kind = validated.Kind;
        }
        _operation = StudioOperation.Launching;
        _error = "";
        Publish();
        StudioActionResult result;
        try { result = _backend.Launch(canonical, recover); }
        catch (Exception) { result = new(false, "Project Prime Studio could not start. Check the desktop installation and try again."); }
        finally { _operation = StudioOperation.None; }
        if (!result.Accepted) return Reject(!String.IsNullOrWhiteSpace(result.Message) ? result.Message : "Project Prime Studio could not start.");
        _documentName = canonical == null ? "" : Path.GetFileName(canonical);
        _documentKind = kind;
        _status = recover ? "Studio recovery launch requested. Continue restoring the session in Studio."
            : canonical == null ? "Studio launch requested. Create or open a Map or Replay workspace in Studio."
            : "Studio launch requested. Continue opening the selected document in Studio.";
        _error = "";
        Publish();
        return new(true, _status);
    }

    private async Task CompletePickerAsync(int generation, CancellationToken cancellationToken)
    {
        StudioPickResult result;
        try { result = await _backend.PickMapAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { return; }
        catch (Exception) { result = new(Error: "The system file dialog could not open. Enter the map path below or open it in Studio."); }
        if (!cancellationToken.IsCancellationRequested) _completed.Enqueue((generation, result));
    }

    private StudioActionResult Reject(string error)
    {
        _error = String.IsNullOrWhiteSpace(error) ? "This Studio action is unavailable." : error;
        Publish();
        return new(false, _error);
    }

    private void Publish()
    {
        StudioViewSnapshot next = new()
        {
            Lifetime = _lifetime, Version = _version, Availability = _availability,
            Operation = _operation, Status = _status, Error = _error,
            DocumentName = _documentName, DocumentKind = _documentKind, PathEpoch = _pathEpoch, Closed = _disposed
        };
        if (next != _snapshot) _snapshot = next with { Version = ++_version };
    }

    private void CancelPicker()
    {
        _generation++;
        CancellationTokenSource? cancellation = _pickerCancellation;
        _pickerCancellation = null;
        if (cancellation == null) return;
        try { cancellation.Cancel(); }
        catch (AggregateException) { /* Provider callbacks cannot retain a retired request. */ }
        finally { cancellation.Dispose(); }
    }

    private StudioAvailability CaptureAvailability()
    {
        try { var result = _backend.Availability(); return result with { Message = result.Message ?? "" }; }
        catch (Exception) { return new(false, false, false, "Studio availability could not be checked. Check the desktop installation and reopen this page."); }
    }

    private StudioPathResult ValidatePath(string path)
    {
        try { return _backend.ValidatePath(path); }
        catch (Exception) { return new(false, Error: "The document could not be checked. Check the path and file permissions."); }
    }

    private void VerifyOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException("Studio entry commands and completion publication must run on their owner thread.");
    }

    public void Dispose()
    {
        VerifyOwner();
        if (_disposed) return;
        _disposed = true;
        CancelPicker();
        while (_completed.TryDequeue(out _)) { }
        _path = "";
        _operation = StudioOperation.None;
        Publish();
    }
}
