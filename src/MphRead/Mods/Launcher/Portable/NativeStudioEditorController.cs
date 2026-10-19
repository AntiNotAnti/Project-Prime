using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MphRead.Mods.Launcher;

public readonly record struct NativeStudioEditTicket(Guid Lease, ulong State, long Revision, Guid Selection);
public sealed record NativeStudioObjectRow(Guid Id, string Label, bool Locked);
public sealed record NativeStudioEditorSnapshot(long Revision, NativeStudioEditTicket Ticket, bool Alive,
    string Name, string Path, bool Dirty, bool CanUndo, bool CanRedo, string Selection,
    IReadOnlyList<NativeStudioObjectRow> Rows, string Status, bool Busy = false);
public enum NativeStudioEditorOperation { Undo, Redo, Save, Move, Rotate, Scale, FrameAll, FrameSelection, Perspective, Top, Front, Side }
public readonly record struct NativeStudioEditorValues(float X = 0, float Y = 0, float Z = 0, string Path = "");

/// <summary>Borrowed retained-editor owner; never a document or independent save/undo service.</summary>
public interface INativeStudioEditorSession : IDisposable
{
    NativeStudioEditorSnapshot Capture();
    NativeStudioResult Select(NativeStudioEditTicket ticket, Guid objectId);
    NativeStudioResult Execute(NativeStudioEditTicket ticket, NativeStudioEditorOperation operation, NativeStudioEditorValues values);
    NativeStudioResult TryClose();
}
public interface INativeStudioEditorViewport
{
    bool DrawViewport(int x, int y, int width, int height, int targetWidth, int targetHeight);
    NativeStudioResult Pick(NativeStudioEditTicket ticket, double x, double y);
    void Camera(double deltaX, double deltaY, bool orbit, bool pan);
    void Zoom(double steps);
    void CancelInput();
}

/// <summary>Frozen native inspector/row commands, with bounded culture-invariant numeric drafts.</summary>
public sealed class NativeStudioEditorController
{
    public const int PageSize = 8;
    private readonly INativeStudioEditorSession _session;
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private NativeStudioEditorSnapshot _shown;
    private NativeStudioEditTicket _draft;
    private int _page;
    private string _status = "";
    private long _revision;
    public bool Closed { get; private set; }
    public NativeStudioEditorController(INativeStudioEditorSession session)
    { _session = session ?? throw new ArgumentNullException(nameof(session)); _shown = session.Capture(); _draft = _shown.Ticket; }
    public sealed record Snapshot(long Revision, NativeStudioEditorSnapshot Owner, int Page, int PageCount,
        IReadOnlyList<NativeStudioObjectRow> Rows, string Status);
    public Snapshot Current()
    {
        Check(); var current = _session.Capture();
        if (current != _shown) { _shown = current; _revision++; }
        _page = Math.Min(_page, Math.Max(0, (_shown.Rows.Count - 1) / PageSize));
        return new(_revision, _shown, _page, Math.Max(1, (_shown.Rows.Count + PageSize - 1) / PageSize),
            Array.AsReadOnly(_shown.Rows.Skip(_page * PageSize).Take(PageSize).ToArray()),
            _status.Length > 0 ? _status : _shown.Status);
    }
    public void Page(int direction)
    {
        Check(); if (Closed || direction is not (-1 or 1)) return;
        int next = _page + direction;
        if (next >= 0 && next * PageSize < _shown.Rows.Count) { _page = next; _revision++; }
    }
    public bool Select(int slot)
    {
        Check(); int index = _page * PageSize + slot;
        if (Closed || slot < 0 || slot >= PageSize || index >= _shown.Rows.Count) return false;
        var result = _session.Select(_shown.Ticket, _shown.Rows[index].Id);
        if (result.Success) AcceptSelection();
        return Result(result);
    }
    public void AcceptSelection() { Check(); _draft = _session.Capture().Ticket; }
    public void ShowStatus(string message) { Check(); _status = message; _revision++; }
    public bool Apply(NativeStudioEditorOperation operation, string x = "", string y = "", string z = "", string path = "")
    {
        Check(); if (Closed || !_shown.Alive || !Enum.IsDefined(operation)) return false;
        var values = new NativeStudioEditorValues(Path: path);
        if (operation is NativeStudioEditorOperation.Move or NativeStudioEditorOperation.Rotate or NativeStudioEditorOperation.Scale)
        {
            if (!Number(x, out float a) || (operation == NativeStudioEditorOperation.Move && (!Number(y, out _) || !Number(z, out _))))
                return Fail("ENTER FINITE DECIMAL VALUES USING A DOT; MAXIMUM MAGNITUDE 1000000");
            float b = 0, c = 0;
            if (operation == NativeStudioEditorOperation.Move) { Number(y, out b); Number(z, out c); }
            if (operation == NativeStudioEditorOperation.Rotate && Math.Abs(a) > 3600)
                return Fail("ROTATION MUST BE BETWEEN -3600 AND 3600 DEGREES");
            if (operation == NativeStudioEditorOperation.Scale && (a < .001f || a > 1000))
                return Fail("SCALE FACTOR MUST BE BETWEEN 0.001 AND 1000");
            values = new(a, b, c);
        }
        if (operation == NativeStudioEditorOperation.Save && (string.IsNullOrWhiteSpace(path) || path.Length > 32767 || path.Contains('\0')))
            return Fail("ENTER A LOCAL .JSON SAVE PATH");
        bool transform = operation is NativeStudioEditorOperation.Move or NativeStudioEditorOperation.Rotate or NativeStudioEditorOperation.Scale;
        var result = _session.Execute(transform ? _draft : _shown.Ticket, operation, values);
        if (result.Success) _draft = _session.Capture().Ticket;
        return Result(result);
    }
    public bool Close()
    {
        Check(); if (Closed) return true;
        var result = _session.TryClose(); Result(result); Closed = result.Success; return Closed;
    }
    private static bool Number(string value, out float number)
    {
        number = 0;
        return value.Length <= 64 && float.TryParse(value,
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent,
            CultureInfo.InvariantCulture, out number) && float.IsFinite(number) && Math.Abs(number) <= 1000000;
    }
    private bool Result(NativeStudioResult result) { _status = result.Message; _revision++; return result.Success; }
    private bool Fail(string message) => Result(new(false, message));
    private void Check()
    { if (Environment.CurrentManagedThreadId != _owner) throw new InvalidOperationException("Native editor commands belong to the retained editor thread."); }
}
