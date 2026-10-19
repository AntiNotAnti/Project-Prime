using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace MphRead.Mods.Launcher;

public enum NativeStudioCreateKind { Box, Wedge, Prism, Convex, Mesh, Spawn, Pickup, JumpPad, NavigationLink }
public enum NativeStudioPropertyKind { Text, Number, Integer, Boolean, Choice }
public sealed record NativeStudioPropertyField(string Id, string Label, NativeStudioPropertyKind Kind, string Value,
    int MaxLength, double? Min, double? Max, IReadOnlyList<string>? Choices);
public readonly record struct NativeStudioPropertyTicket(NativeStudioEditTicket Owner, string Schema);
public sealed record NativeStudioPropertySnapshot(NativeStudioPropertyTicket Ticket, IReadOnlyList<NativeStudioPropertyField> Fields);
public sealed record NativeStudioObjectDelete(NativeStudioEditTicket Owner, Guid Confirmation, string Label);
public interface INativeStudioEditorObjects
{
    NativeStudioResult CreateObject(NativeStudioEditTicket ticket, NativeStudioCreateKind kind);
    NativeStudioResult DuplicateObjects(NativeStudioEditTicket ticket);
    NativeStudioObjectDelete? PrepareObjectDelete(NativeStudioEditTicket ticket);
    NativeStudioResult ConfirmObjectDelete(NativeStudioObjectDelete confirmation);
    void CancelObjectDelete();
}
public interface INativeStudioEditorProperties
{
    NativeStudioPropertySnapshot ReadProperties(NativeStudioEditTicket ticket);
    NativeStudioResult ApplyProperties(NativeStudioPropertyTicket ticket, IReadOnlyDictionary<string,string> patch);
}

/// <summary>Eight visible schema slots; all draft fields stay pinned to their original owner/schema ticket.</summary>
public sealed class NativeStudioEditorDetails
{
    private readonly INativeStudioEditorSession _session;
    private readonly INativeStudioEditorObjects? _objects;
    private readonly INativeStudioEditorProperties? _properties;
    private NativeStudioPropertySnapshot? _schema;
    private readonly Dictionary<string,string> _draft = new(StringComparer.Ordinal);
    private int _page;
    public long Revision { get; private set; }
    public string Status { get; private set; } = "";
    public NativeStudioObjectDelete? Delete { get; private set; }
    public int PageCount => Math.Max(1, ((_schema?.Fields.Count ?? 0) + 7) / 8);
    public int PageIndex => _page;
    public sealed record Row(NativeStudioPropertyField Field, string Value);
    public IReadOnlyList<Row> Rows => Array.AsReadOnly((_schema?.Fields ?? Array.Empty<NativeStudioPropertyField>()).Skip(_page * 8).Take(8)
        .Select(f => new Row(f, _draft.GetValueOrDefault(f.Id, f.Value))).ToArray());
    public NativeStudioEditorDetails(INativeStudioEditorSession session)
    { _session = session; _objects = session as INativeStudioEditorObjects; _properties = session as INativeStudioEditorProperties; Reset(); }
    public void Reset()
    {
        _schema = _properties?.ReadProperties(_session.Capture().Ticket); _draft.Clear(); _page = 0;
        if (_schema != null) foreach(var field in _schema.Fields) _draft[field.Id] = field.Value;
        CancelDelete(); Revision++;
    }
    public void SetDraft(int slot, string value)
    {
        var rows = Rows;
        if (slot < 0 || slot >= rows.Count || rows[slot].Field.Kind is NativeStudioPropertyKind.Boolean or NativeStudioPropertyKind.Choice) return;
        var field = rows[slot].Field;
        if (value.Length <= 4096 && _draft.GetValueOrDefault(field.Id) != value) { _draft[field.Id] = value; Revision++; }
    }
    public void Cycle(int slot)
    {
        var rows = Rows; if(slot < 0 || slot >= rows.Count) return;
        var row = rows[slot];
        var choices = row.Field.Kind == NativeStudioPropertyKind.Boolean ? new[] {"false","true"} : row.Field.Choices;
        if(choices == null || choices.Count == 0) return;
        int index = choices.ToList().FindIndex(c => c == row.Value);
        _draft[row.Field.Id] = choices[(index + 1) % choices.Count]; Revision++; CancelDelete();
    }
    public void Page(int direction)
    { if(direction is not(-1 or 1))return; int next=_page+direction;if(next>=0&&next<PageCount){_page=next;Revision++;} CancelDelete(); }
    public bool Apply()
    {
        CancelDelete(); if(_schema == null || _properties == null) return false;
        var changed = _schema.Fields.Where(f => _draft[f.Id] != f.Value).ToDictionary(f => f.Id, f => _draft[f.Id], StringComparer.Ordinal);
        var result = _properties.ApplyProperties(_schema.Ticket, new ReadOnlyDictionary<string,string>(changed));
        bool success = Result(result); if(success) Reset(); return success;
    }
    public bool Create(NativeStudioEditTicket ticket, NativeStudioCreateKind kind)
    { CancelDelete(); bool result = Result(_objects?.CreateObject(ticket, kind) ?? new(false,"OBJECT CREATION UNAVAILABLE")); if(result)Reset();return result; }
    public bool Duplicate(NativeStudioEditTicket ticket)
    { CancelDelete(); bool result = Result(_objects?.DuplicateObjects(ticket) ?? new(false,"DUPLICATION UNAVAILABLE"));if(result)Reset();return result; }
    public void RequestDelete(NativeStudioEditTicket ticket)
    { CancelDelete();Delete=_objects?.PrepareObjectDelete(ticket);Status=Delete==null?"SELECT AN UNLOCKED OBJECT TO DELETE":"CONFIRM DELETE OF "+Delete.Label;Revision++; }
    public bool ConfirmDelete()
    {
        var request=Delete;Delete=null;Revision++;
        if(request==null||_objects==null)return false;
        bool result=Result(_objects.ConfirmObjectDelete(request));if(result)Reset();return result;
    }
    public void CancelDelete() { Delete=null;_objects?.CancelObjectDelete();Revision++; }
    private bool Result(NativeStudioResult result){Status=result.Message;Revision++;return result.Success;}
}
