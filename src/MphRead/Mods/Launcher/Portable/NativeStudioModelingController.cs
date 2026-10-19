using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;

namespace MphRead.Mods.Launcher;

/// <summary>Native Studio modeling controller. Rows and drafts retain the ticket presented to the user.</summary>
public sealed class NativeStudioModelingController
{
    public const int PageSize = 8;
    private readonly INativeStudioEditorModeling? _owner;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private NativeStudioModelingSnapshot _snapshot;
    private IReadOnlyList<NativeStudioComponentRow> _rows = Array.Empty<NativeStudioComponentRow>();
    private NativeStudioModelingTicket _configurationTicket, _transformTicket, _uvTicket;
    private bool _configurationDirty, _transformDirty, _uvDirty;

    public NativeStudioModelingSnapshot Snapshot => _snapshot;
    public IReadOnlyList<NativeStudioComponentRow> Rows => _rows;
    public int Offset { get; private set; }
    public int Page => Offset / PageSize;
    public int Pages => Math.Max(1, (_snapshot.Components.Count - 1) / PageSize + 1);
    public bool HasPrevious => Offset > 0;
    public bool HasNext => (long)Offset + PageSize < _snapshot.Components.Count;
    public string Status { get; private set; }
    public long Revision { get; private set; }
    public long DraftRevision { get; private set; }

    public string GridSnapDraft { get; private set; } = "0";
    public string AngleSnapDraft { get; private set; } = "0";
    public string ScaleSnapDraft { get; private set; } = "0";
    public string CursorXDraft { get; private set; } = "0";
    public string CursorYDraft { get; private set; } = "0";
    public string CursorZDraft { get; private set; } = "0";
    public string TransformXDraft { get; private set; } = "0";
    public string TransformYDraft { get; private set; } = "0";
    public string TransformZDraft { get; private set; } = "0";
    public string TransformAngleDraft { get; private set; } = "0";
    public string TransformScaleDraft { get; private set; } = "1";
    public string UvScaleUDraft { get; private set; } = "1";
    public string UvScaleVDraft { get; private set; } = "1";
    public string UvOffsetUDraft { get; private set; } = "0";
    public string UvOffsetVDraft { get; private set; } = "0";
    public string UvAngleDraft { get; private set; } = "0";
    public string UvDensityDraft { get; private set; } = "16";

    // The optional interface keeps an older editor session usable without pretending modeling is alive.
    public NativeStudioModelingController(object? owner)
    {
        _owner = owner as INativeStudioEditorModeling;
        _snapshot = _owner == null ? Unavailable() : Freeze(_owner.CaptureModeling());
        Status = _snapshot.Status;
        PresentRows();
        ResetDrafts();
    }

    private static NativeStudioModelingSnapshot Unavailable() => new(0,
        new(default, 0, Guid.Empty, "", ""), false,
        NativeStudioElementMode.Object, NativeStudioTransformTool.Move, NativeStudioAxes.Free,
        NativeStudioSelectionMode.Replace, NativeStudioPivot.Individual, false, 0, 0, 0,
        Vector3.Zero, Array.Empty<NativeStudioComponentRow>(), "NO COMPONENT SELECTION", false, false,
        "MODELING CONTROLS ARE UNAVAILABLE IN THIS EDITOR SESSION");

    private static NativeStudioModelingSnapshot Freeze(NativeStudioModelingSnapshot snapshot)
        => snapshot with { Components = Array.AsReadOnly(snapshot.Components.ToArray()) };

    public void Refresh()
    {
        Check();
        if (_owner == null) return;
        var next = _owner.CaptureModeling();
        bool changed = next.Revision != _snapshot.Revision || next.Ticket != _snapshot.Ticket
            || next.Alive != _snapshot.Alive || next.Mode != _snapshot.Mode || next.Tool != _snapshot.Tool
            || next.Axes != _snapshot.Axes || next.SelectionMode != _snapshot.SelectionMode
            || next.Pivot != _snapshot.Pivot || next.Local != _snapshot.Local || next.GridSnap != _snapshot.GridSnap
            || next.AngleSnap != _snapshot.AngleSnap || next.ScaleSnap != _snapshot.ScaleSnap
            || next.Cursor != _snapshot.Cursor || next.Selection != _snapshot.Selection
            || next.GestureActive != _snapshot.GestureActive || next.Checker != _snapshot.Checker
            || next.Status != _snapshot.Status || !next.Components.SequenceEqual(_snapshot.Components);
        if (!changed) return;
        _snapshot = Freeze(next);
        Offset = Math.Min(Offset, Math.Max(0, (_snapshot.Components.Count - 1) / PageSize) * PageSize);
        PresentRows();
        // Clean drafts may follow a new presentation; edited drafts keep their original owner ticket.
        if (!_configurationDirty) { ResetConfigurationDraftCore(); DraftRevision++; }
        if (!_transformDirty) _transformTicket = _snapshot.Ticket;
        if (!_uvDirty) _uvTicket = _snapshot.Ticket;
        Revision++;
    }

    private void PresentRows()
        => _rows = Array.AsReadOnly(_snapshot.Components.Skip(Offset).Take(PageSize).ToArray());

    public void Previous() => PageBy(-1);
    public void Next() => PageBy(1);
    private void PageBy(int direction)
    {
        Check();
        if (direction == -1 && HasPrevious) Offset -= PageSize;
        else if (direction == 1 && HasNext) Offset += PageSize;
        else return;
        PresentRows();
        Revision++;
    }

    public bool SelectComponent(int slot)
    {
        Check();
        // Resolve only the last presented row and ticket. Capture here would retarget a delayed click.
        if (!Available() || slot < 0 || slot >= PageSize || slot >= _rows.Count) return false;
        var ticket = _snapshot.Ticket;
        var row = _rows[slot];
        return Complete(_owner!.SelectComponent(ticket, row));
    }

    public bool Configure(NativeStudioElementMode? mode = null, NativeStudioTransformTool? tool = null,
        NativeStudioAxes? axes = null, bool? local = null, NativeStudioSelectionMode? selection = null,
        NativeStudioPivot? pivot = null, float? gridSnap = null, float? angleSnap = null,
        float? scaleSnap = null, Vector3? cursor = null)
    {
        Check();
        if (!Available()) return false;
        if (!ValidConfiguration(mode, tool, axes, selection, pivot, gridSnap, angleSnap, scaleSnap, cursor))
            return Fail("ENTER VALID MODELING SETTINGS: GRID 0..100000, ANGLE 0..180, SCALE 0..1000, CURSOR +/-1000000");
        return Complete(_owner!.ConfigureModeling(_snapshot.Ticket, mode, tool, axes, local, selection, pivot,
            gridSnap, angleSnap, scaleSnap, cursor));
    }

    public bool Execute(NativeStudioModelingOperation operation, NativeStudioModelingValues? values = null)
    {
        Check();
        if (!Available()) return false;
        values ??= new();
        if (!Enum.IsDefined(operation) || !ValidValues(operation, values))
            return Fail("ENTER FINITE MODELING VALUES WITHIN THE DISPLAYED LIMITS");
        return Complete(_owner!.ExecuteModeling(_snapshot.Ticket, operation, values));
    }

    public void SetConfigurationDraft(string gridSnap, string angleSnap, string scaleSnap,
        string cursorX, string cursorY, string cursorZ)
    {
        Check();
        if (!_configurationDirty) _configurationTicket = _snapshot.Ticket;
        _configurationDirty = true;
        GridSnapDraft = DraftText(gridSnap); AngleSnapDraft = DraftText(angleSnap); ScaleSnapDraft = DraftText(scaleSnap);
        CursorXDraft = DraftText(cursorX); CursorYDraft = DraftText(cursorY); CursorZDraft = DraftText(cursorZ);
        DraftChanged();
    }

    public void SetTransformDraft(string x, string y, string z, string angle, string scale)
    {
        Check();
        if (!_transformDirty) _transformTicket = _snapshot.Ticket;
        _transformDirty = true;
        TransformXDraft = DraftText(x); TransformYDraft = DraftText(y); TransformZDraft = DraftText(z);
        TransformAngleDraft = DraftText(angle); TransformScaleDraft = DraftText(scale);
        DraftChanged();
    }

    public void SetUvDraft(string scaleU, string scaleV, string offsetU, string offsetV, string density, string angle = "0")
    {
        Check();
        if (!_uvDirty) _uvTicket = _snapshot.Ticket;
        _uvDirty = true;
        UvScaleUDraft = DraftText(scaleU); UvScaleVDraft = DraftText(scaleV); UvOffsetUDraft = DraftText(offsetU);
        UvOffsetVDraft = DraftText(offsetV); UvDensityDraft = DraftText(density); UvAngleDraft = DraftText(angle);
        DraftChanged();
    }

    public bool ApplyConfiguration()
    {
        Check();
        if (!Available()) return false;
        // Parse the entire draft before invoking the owner: a bad final field must apply nothing.
        if (!Number(GridSnapDraft, 0, 100000, out var grid)
            || !Number(AngleSnapDraft, 0, 180, out var angle)
            || !Number(ScaleSnapDraft, 0, 1000, out var scale)
            || !Number(CursorXDraft, -1000000, 1000000, out var x)
            || !Number(CursorYDraft, -1000000, 1000000, out var y)
            || !Number(CursorZDraft, -1000000, 1000000, out var z))
            return Fail("ENTER DOT DECIMALS: GRID 0..100000, ANGLE 0..180, SCALE 0..1000, CURSOR +/-1000000");
        return Complete(_owner!.ConfigureModeling(_configurationTicket, gridSnap: grid,
            angleSnap: angle, scaleSnap: scale, cursor: new Vector3(x, y, z)));
    }

    public bool ApplyTransform()
    {
        Check();
        if (!Available()) return false;
        if (!Number(TransformXDraft, -1000000, 1000000, out var x)
            || !Number(TransformYDraft, -1000000, 1000000, out var y)
            || !Number(TransformZDraft, -1000000, 1000000, out var z)
            || !Number(TransformAngleDraft, -3600, 3600, out var angle)
            || !Number(TransformScaleDraft, .001f, 1000, out var scale))
            return Fail("ENTER DOT DECIMALS: MOVE +/-1000000, ANGLE +/-3600, SCALE 0.001..1000");
        return Complete(_owner!.ExecuteModeling(_transformTicket, NativeStudioModelingOperation.ApplyTransform,
            new(X: x, Y: y, Z: z, Angle: angle, Scale: scale)));
    }

    public bool ApplyUv() => ExecuteUv(NativeStudioModelingOperation.ApplyUv);
    public bool MatchDensity() => ExecuteUv(NativeStudioModelingOperation.MatchDensity);
    public bool ExecuteUv(NativeStudioModelingOperation operation)
    {
        Check();
        if (!Available()) return false;
        if (operation is not (NativeStudioModelingOperation.ApplyUv or NativeStudioModelingOperation.MatchDensity))
            return Fail("CHOOSE APPLY UV OR MATCH DENSITY FOR THE UV DRAFT");
        if (!Number(UvScaleUDraft, -1000, 1000, out var u) || Math.Abs(u) < .001f
            || !Number(UvScaleVDraft, -1000, 1000, out var v) || Math.Abs(v) < .001f
            || !Number(UvOffsetUDraft, -1000000, 1000000, out var offsetU)
            || !Number(UvOffsetVDraft, -1000000, 1000000, out var offsetV)
            || !Number(UvAngleDraft, -3600, 3600, out var angle)
            || !Number(UvDensityDraft, .001f, 65536, out var density))
            return Fail("ENTER DOT DECIMALS: UV SCALE MAGNITUDE 0.001..1000, OFFSET +/-1000000, ANGLE +/-3600, DENSITY 0.001..65536");
        return Complete(_owner!.ExecuteModeling(_uvTicket, operation,
            new(Angle: angle, Scale: u, ScaleV: v, OffsetU: offsetU, OffsetV: offsetV, Density: density)));
    }

    public void ResetDrafts()
    {
        Check();
        ResetConfigurationDraftCore();
        _transformTicket = _snapshot.Ticket;
        _transformDirty = false;
        TransformXDraft = TransformYDraft = TransformZDraft = TransformAngleDraft = "0";
        TransformScaleDraft = "1";
        ResetUvDraftCore();
        DraftChanged();
    }

    public void ResetUvDraft()
    {
        Check();
        ResetUvDraftCore();
        DraftChanged();
    }

    private void ResetUvDraftCore()
    {
        _uvTicket = _snapshot.Ticket;
        _uvDirty = false;
        UvScaleUDraft = UvScaleVDraft = "1";
        UvOffsetUDraft = UvOffsetVDraft = UvAngleDraft = "0";
        UvDensityDraft = "16";
    }

    private void ResetConfigurationDraftCore()
    {
        _configurationTicket = _snapshot.Ticket;
        _configurationDirty = false;
        GridSnapDraft = Format(_snapshot.GridSnap); AngleSnapDraft = Format(_snapshot.AngleSnap);
        ScaleSnapDraft = Format(_snapshot.ScaleSnap); CursorXDraft = Format(_snapshot.Cursor.X);
        CursorYDraft = Format(_snapshot.Cursor.Y); CursorZDraft = Format(_snapshot.Cursor.Z);
    }

    private static string Format(float value) => value.ToString("R", CultureInfo.InvariantCulture);
    private void DraftChanged() { DraftRevision++; Revision++; }
    private string DraftText(string? value)
    {
        if (value == null) return "";
        if (value.Length <= 64) return value;
        Fail("NUMERIC DRAFTS ARE LIMITED TO 64 CHARACTERS");
        // Preserve an invalid draft sentinel instead of leaving an older valid value ready to apply.
        return value.Substring(0, 65);
    }

    private bool Complete(NativeStudioResult result)
    {
        Status = result.Message;
        Revision++;
        // Failure leaves text and tickets intact so a stale or invalid draft cannot silently retarget.
        if (result.Success) { Refresh(); ResetDrafts(); }
        return result.Success;
    }

    private bool Available()
    {
        if (_owner != null && _snapshot.Alive) return true;
        return Fail(_owner == null ? Unavailable().Status : "MODELING SESSION IS CLOSED OR UNAVAILABLE; REFRESH THE EDITOR");
    }
    private bool Fail(string message) { Status = message; Revision++; return false; }

    private static bool ValidConfiguration(NativeStudioElementMode? mode, NativeStudioTransformTool? tool,
        NativeStudioAxes? axes, NativeStudioSelectionMode? selection, NativeStudioPivot? pivot,
        float? grid, float? angle, float? scale, Vector3? cursor)
        => (!mode.HasValue || Enum.IsDefined(mode.Value)) && (!tool.HasValue || Enum.IsDefined(tool.Value))
        && (!axes.HasValue || Enum.IsDefined(axes.Value)) && (!selection.HasValue || Enum.IsDefined(selection.Value))
        && (!pivot.HasValue || Enum.IsDefined(pivot.Value)) && (!grid.HasValue || Bounded(grid.Value, 0, 100000))
        && (!angle.HasValue || Bounded(angle.Value, 0, 180)) && (!scale.HasValue || Bounded(scale.Value, 0, 1000))
        && (!cursor.HasValue || (Bounded(cursor.Value.X, -1000000, 1000000)
            && Bounded(cursor.Value.Y, -1000000, 1000000) && Bounded(cursor.Value.Z, -1000000, 1000000)));

    private static bool ValidValues(NativeStudioModelingOperation operation, NativeStudioModelingValues values)
        => Bounded(values.X, -1000000, 1000000) && Bounded(values.Y, -1000000, 1000000)
        && Bounded(values.Z, -1000000, 1000000) && Bounded(values.Angle, -3600, 3600)
        && Bounded(values.Scale, operation == NativeStudioModelingOperation.ApplyTransform ? .001f : -1000, 1000)
        && Math.Abs(values.Scale) >= .001f && Bounded(values.ScaleV, -1000, 1000) && Math.Abs(values.ScaleV) >= .001f
        && Bounded(values.OffsetU, -1000000, 1000000) && Bounded(values.OffsetV, -1000000, 1000000)
        && Bounded(values.Density, .001f, 65536);

    private static bool Bounded(float value, float min, float max) => float.IsFinite(value) && value >= min && value <= max;
    private static bool Number(string value, float min, float max, out float number)
    {
        number = 0;
        return value != null && value.Length <= 64 && float.TryParse(value,
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent,
            CultureInfo.InvariantCulture, out number) && Bounded(number, min, max);
    }

    private void Check()
    {
        if (Environment.CurrentManagedThreadId != _thread)
            throw new InvalidOperationException("Modeling controls belong to the editor owner thread.");
    }
}
