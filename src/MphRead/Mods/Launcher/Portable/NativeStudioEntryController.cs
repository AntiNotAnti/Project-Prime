using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Launcher;

public sealed record NativeStudioLibraryRow(string Key, string Name, string Detail,
    bool CanOpen = true, bool CanDuplicate = false, bool CanDelete = false);
public sealed record NativeStudioTemplate(string Id, string Name);
public sealed record NativeStudioDeleteRequest(string Ticket, string Name);
public readonly record struct NativeStudioResult(bool Success, string Message, bool HandedOff = false);
public readonly record struct NativeStudioPickResult(string? Path, string Error = "");

/// <summary>The retained editor alone owns documents, save/recovery writers and editor handoffs.</summary>
public interface INativeStudioLauncher
{
    bool LaunchMapStudio();
    bool PickerAvailable => false;
    IReadOnlyList<NativeStudioTemplate> Templates() => Array.Empty<NativeStudioTemplate>();
    Task<IReadOnlyList<NativeStudioLibraryRow>> ReadLibraryAsync(bool recoveries, CancellationToken token)
        => Task.FromResult<IReadOnlyList<NativeStudioLibraryRow>>(Array.Empty<NativeStudioLibraryRow>());
    Task<NativeStudioPickResult> PickMapAsync(CancellationToken token)
        => Task.FromResult(new NativeStudioPickResult(null, "SYSTEM PICKER UNAVAILABLE // ENTER A PROJECT PATH"));
    NativeStudioResult OpenPath(string path) => new(false, "PROJECT OPEN UNAVAILABLE");
    NativeStudioResult OpenEntry(string key) => new(false, "PROJECT OPEN UNAVAILABLE");
    NativeStudioResult CreateProject(string name, string templateId) => new(false, "PROJECT CREATION UNAVAILABLE");
    NativeStudioResult DuplicateEntry(string key) => new(false, "PROJECT DUPLICATION UNAVAILABLE");
    NativeStudioDeleteRequest? PrepareDelete(string key) => null;
    NativeStudioResult Delete(NativeStudioDeleteRequest request) => new(false, "PROJECT DELETE UNAVAILABLE");
}

/// <summary>Native library state and cancellable reads; never owns a MapDocument or a writer.</summary>
public sealed class NativeStudioEntryController : IDisposable
{
    public const int PageSize = 8;
    private readonly INativeStudioLauncher _launcher;
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly ConcurrentQueue<(int Generation, IReadOnlyList<NativeStudioLibraryRow>? Rows,
        NativeStudioPickResult? Pick, string Error)> _completed = new();
    private IReadOnlyList<NativeStudioLibraryRow> _rows = Array.Empty<NativeStudioLibraryRow>();
    private readonly IReadOnlyList<NativeStudioTemplate> _templates;
    private CancellationTokenSource? _work;
    private NativeStudioDeleteRequest? _delete;
    private Snapshot? _snapshot;
    private int _generation, _page, _selected, _selectedTemplate;
    private bool _disposed, _busy, _recoveries;
    private long _revision = 1;
    private string _status = "STUDIO READY // SELECT A PROJECT OR CONTINUE EDITING";
    public bool Closed { get; private set; }

    public NativeStudioEntryController(INativeStudioLauncher launcher)
    {
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
        _templates = Array.AsReadOnly(launcher.Templates().ToArray());
        Refresh();
        Pump();
    }

    public sealed record Snapshot(long Revision, string Status, bool Closed, bool Busy,
        bool Recoveries, int Page, int PageCount, int SelectedIndex, int TemplateIndex, IReadOnlyList<NativeStudioLibraryRow> Rows,
        IReadOnlyList<NativeStudioTemplate> Templates, string PendingDeleteName, bool PickerAvailable);
    public Snapshot Current()
    {
        Check(); Pump();
        if (_snapshot?.Revision == _revision) return _snapshot;
        return _snapshot = new(_revision, _status, Closed, _busy, _recoveries, _page,
            Math.Max(1, (_rows.Count + PageSize - 1) / PageSize), _selected, _selectedTemplate,
            Array.AsReadOnly(_rows.Skip(_page * PageSize).Take(PageSize).ToArray()), _templates,
            _delete?.Name ?? "", _launcher.PickerAvailable);
    }

    public bool Launch()
    {
        Check(); if (!Ready()) return false;
        _delete = null;
        try
        {
            if (!_launcher.LaunchMapStudio()) return Fail("STUDIO UNAVAILABLE // CHECK DESKTOP GAME FILES");
            Closed = true; _revision++; return true;
        }
        catch (Exception error) when (Recoverable(error)) { return Fail("STUDIO LAUNCH FAILED // EXISTING EDITOR RETAINED"); }
    }
    public bool OpenPath(string path)
    {
        Check(); if (!Ready()) return false;
        _delete = null;
        if (string.IsNullOrWhiteSpace(path) || path.Length > 32767 || path.Contains('\0'))
            return Fail("ENTER AN EXISTING .JSON PROJECT OR .PPMAP PACKAGE PATH");
        return Command(() => _launcher.OpenPath(path));
    }
    public bool OpenSelected(int index)
    {
        Check(); var row = Selected(index);
        if (row == null) return false;
        return row.CanOpen ? Command(() => _launcher.OpenEntry(row.Key)) : Fail("SELECTED ENTRY IS UNREADABLE // ORIGINAL FILE RETAINED");
    }
    public bool Create(string name, string templateId)
    {
        Check(); if (!Ready()) return false;
        _delete = null;
        if (string.IsNullOrWhiteSpace(name) || name.Length > 40 || !_templates.Any(t => t.Id == templateId))
            return Fail("ENTER A RUNTIME NAME (1–40 CHARACTERS) AND SELECT A CREATION TEMPLATE");
        return Command(() => _launcher.CreateProject(name, templateId));
    }
    public bool Duplicate(int index)
    {
        Check(); var row = Selected(index);
        if (row == null) return false;
        return row.CanDuplicate ? Command(() => _launcher.DuplicateEntry(row.Key)) : Fail("THIS ENTRY CANNOT BE DUPLICATED");
    }
    public bool SelectEntry(int index)
    {
        Check(); if (Selected(index) == null) return false;
        _selected = index; _delete = null; _revision++; return true;
    }
    public bool SelectTemplate(int index)
    {
        Check(); if (!Ready() || index < 0 || index >= _templates.Count) return false;
        _selectedTemplate = index; _delete = null; _revision++; return true;
    }
    public bool RequestDelete(int index)
    {
        Check(); _delete = null; var row = Selected(index);
        if (row == null || !row.CanDelete) return Fail("ONLY A WRITABLE USER PROJECT WITHOUT A LIVE EDITOR OWNER CAN BE DELETED");
        try { _delete = _launcher.PrepareDelete(row.Key); }
        catch (Exception error) when (Recoverable(error)) { return Fail("PROJECT CHANGED OR DELETE IS UNAVAILABLE // REFRESH LIBRARY"); }
        if (_delete == null) return Fail("PROJECT CHANGED OR HAS AN EDITOR OWNER // REFRESH LIBRARY");
        _status = "CONFIRM DELETE OF THE SELECTED SAVED PROJECT FILE; ASSETS ARE RETAINED"; _revision++; return true;
    }
    public bool ConfirmDelete()
    {
        Check(); if (!Ready() || _delete == null) return false;
        var request = _delete; _delete = null;
        bool success = Command(() => _launcher.Delete(request));
        if (success) Refresh(_recoveries);
        return success;
    }
    public void CancelDelete() { Check(); _delete = null; _revision++; }
    public void ShowStatus(string message) { Check(); _status = message; _revision++; }
    public bool Page(int direction)
    {
        Check(); if (!Ready() || direction is not (-1 or 1)) return false;
        int next = _page + direction;
        if (next < 0 || next * PageSize >= _rows.Count) return false;
        _page = next; _selected = 0; _delete = null; _revision++; return true;
    }
    public bool Refresh(bool recoveries = false)
    {
        Check(); if (!Ready()) return false;
        if (_recoveries != recoveries) { _rows = Array.Empty<NativeStudioLibraryRow>(); _page = _selected = 0; }
        _recoveries = recoveries; _delete = null;
        Begin("READING LOCAL " + (recoveries ? "RECOVERY FILES" : "PROJECT LIBRARY"));
        _ = ReadAsync(_generation, recoveries, _work!.Token); return true;
    }
    public bool Pick()
    {
        Check(); if (!Ready()) return false;
        if (!_launcher.PickerAvailable) return Fail("SYSTEM PICKER UNAVAILABLE // ENTER A PROJECT PATH");
        Begin("CHOOSE A .JSON MAP PROJECT IN THE SYSTEM FILE PICKER");
        _ = PickAsync(_generation, _work!.Token); return true;
    }
    public void Back()
    {
        Check();
        if (_busy) { RetireWork(); _status = "SELECTION CANCELLED // LATE RESULTS WILL BE IGNORED"; _revision++; return; }
        _delete = null; Closed = true; _revision++;
    }
    private void Begin(string status)
    {
        RetireWork(); _delete = null; _work = new(); _busy = true; _status = status; _revision++;
    }
    private async Task ReadAsync(int generation, bool recoveries, CancellationToken token)
    {
        try
        {
            var rows = await _launcher.ReadLibraryAsync(recoveries, token).ConfigureAwait(false);
            if (!token.IsCancellationRequested) _completed.Enqueue((generation, rows.ToArray(), null, ""));
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (Recoverable(error))
        { if (!token.IsCancellationRequested) _completed.Enqueue((generation, null, null, "LOCAL LIBRARY COULD NOT BE READ // EXISTING EDITOR RETAINED")); }
    }
    private async Task PickAsync(int generation, CancellationToken token)
    {
        try
        {
            var pick = await _launcher.PickMapAsync(token).ConfigureAwait(false);
            if (!token.IsCancellationRequested) _completed.Enqueue((generation, null, pick, ""));
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (Recoverable(error))
        { if (!token.IsCancellationRequested) _completed.Enqueue((generation, null, null, "SYSTEM PICKER FAILED // ENTER A PROJECT PATH")); }
    }
    private void Pump()
    {
        while (_completed.TryDequeue(out var result))
        {
            if (result.Generation != _generation || !_busy || Closed) continue;
            _work?.Dispose(); _work = null; _busy = false;
            if (result.Error.Length > 0) { Fail(result.Error); continue; }
            if (result.Rows != null)
            {
                _rows = result.Rows; _page = Math.Min(_page, Math.Max(0, (_rows.Count - 1) / PageSize)); _selected = 0;
                _status = _rows.Count == 0 ? (_recoveries ? "NO LOCAL RECOVERY FILES" : "NO LOCAL PROJECTS // CREATE OR OPEN A PROJECT")
                    : $"{_rows.Count} LOCAL " + (_recoveries ? "RECOVERY FILES" : "PROJECTS");
                _revision++;
            }
            else if (result.Pick is { } pick)
            {
                if (pick.Error.Length > 0) Fail(pick.Error);
                else if (pick.Path == null) { _status = "NO FILE SELECTED // EXISTING EDITOR RETAINED"; _revision++; }
                else OpenPath(pick.Path);
            }
        }
    }
    private NativeStudioLibraryRow? Selected(int index)
    {
        if (!Ready()) return null;
        int at = _page * PageSize + index;
        if (index < 0 || index >= PageSize || at >= _rows.Count) { Fail("SELECT A PROJECT FROM THE CURRENT PAGE"); return null; }
        return _rows[at];
    }
    private bool Command(Func<NativeStudioResult> run)
    {
        if (!Ready()) return false;
        _delete = null;
        try
        {
            var result = run(); _status = result.Message; _revision++;
            if (result.Success && result.HandedOff) Closed = true;
            return result.Success;
        }
        catch (Exception error) when (Recoverable(error)) { return Fail("STUDIO COMMAND FAILED // EXISTING EDITOR RETAINED"); }
    }
    private bool Ready() => !Closed && !_busy;
    private bool Fail(string message) { _status = message; _revision++; return false; }
    private void RetireWork()
    {
        _generation++; var work = _work; _work = null; _busy = false;
        if (work != null) { try { work.Cancel(); } catch (AggregateException) { } finally { work.Dispose(); } }
    }
    private static bool Recoverable(Exception error) => error is not OutOfMemoryException and not StackOverflowException;
    private void Check()
    {
        if (Environment.CurrentManagedThreadId != _owner)
            throw new InvalidOperationException("Studio navigation belongs to its owner thread.");
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
    public void Dispose()
    {
        if (_disposed) return; Check(); _disposed = true; RetireWork();
        while (_completed.TryDequeue(out _)) { } _delete = null;
    }
}
