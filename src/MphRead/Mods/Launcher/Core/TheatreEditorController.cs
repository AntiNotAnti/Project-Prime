#if !MPHREAD_SERVER
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;

namespace MphRead.Mods.Launcher.Core;

public sealed partial class TheatreEditorController : IDisposable
{
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly ITheatreEditorBackend _backend;
    private readonly TheatreEditorContext _original;
    private object _reviewTransport;
    private TheatreEditorContext _context;
    private TheatreEditorTab _tab;
    private TheatreEditorFilter _filter;
    private int _player = -1, _selected;
    private bool _disposed, _authority = true;
    private string _name = "", _status = "Browse recorded events or review a reel edit.", _error = "";
    private string _eventSearch = "", _tags;
    private bool _tagsDirty;
    private TheatreEditorEdit? _pending;
    public TheatreEditorController(ITheatreEditorBackend backend)
    { _backend = backend ?? throw new ArgumentNullException(nameof(backend)); _original = _context = backend.Capture(); _reviewTransport=_context.Transport; _tags=string.Join(", ",_context.Tags); }
    public TheatreEditorSnapshot Snapshot()
    {
        Verify(); var live = _backend.ReadLive(_context);
        if (!live.Current) { _authority = false; CancelSuggestions(); }
        PollSuggestions();
        if (!_authority || _disposed) live = live with { Current = false };
        var rows = Rows(); _selected = Math.Clamp(_selected, 0, Math.Max(0, rows.Length - 1));
        return new(_context,_tab,_filter,_player,rows,_selected,rows.IsEmpty ? null : rows[_selected],_pending,
            live,_name,_status,_error) { EventSearch=_eventSearch,Tags=_tags,TagsDirty=_tagsDirty };
    }
    public bool Refresh() => RefreshReview(false);
    public bool RefreshWorkspace() => RefreshReview(true);
    private bool RefreshReview(bool allowSeekReview) => Run(() =>
    {
        Free(); CancelSuggestions(); _authority = false; var next = _backend.Capture();
        if (!ReferenceEquals(next.Owner,_original.Owner)
            || next.SceneGeneration != _original.SceneGeneration || next.LogicalPath != _original.LogicalPath
            || next.PlaybackPath != _original.PlaybackPath || next.SourceRevision != _original.SourceRevision
            || next.Duration != _original.Duration)
            throw new TheatreEditorStaleException("The original replay source or owner changed. Close this editor.");
        if (!ReferenceEquals(next.Transport,_reviewTransport))
        {
            if (!allowSeekReview || _backend is not ITheatreEditorSeekReviewBackend seekReview)
                throw new TheatreEditorStaleException("The playback transport changed. Use the workspace Refresh Review after seeking settles.");
            if (_tagsDirty) throw new InvalidOperationException("Keep or discard the tag draft before reviewing a replaced seek transport.");
            if (!next.SettledPaused || !next.PublicationVerified)
                throw new InvalidOperationException("Pause and wait for the current replay picture before refreshing the workspace.");
            seekReview.ValidateSeekReview(_context,next);
            _reviewTransport=next.Transport;
        }
        _context = next; _authority = true; if(!_tagsDirty)_tags=string.Join(", ",next.Tags); _status = "Replay editor inputs reviewed.";
    });
    public bool SetEventSearch(string search) => Run(() =>
    {
        Free();
        if(search.Length>128)throw new ArgumentException("Use at most 128 characters to find recorded event types.");
        _eventSearch=search.Trim();_tab=TheatreEditorTab.Events;_selected=0;
        _status=_eventSearch.Length==0?"Showing recorded events with the current player and category filters.":"Finding recorded event types with the current player and category filters.";
    });
    public bool SetTags(string tags) => Run(() =>
    {
        Free();
        if(tags.Length>TheatreReplayTags.MaximumTextLength)throw new ArgumentException("Use at most 1024 characters for replay tags.");
        _tags=tags;_tagsDirty=!String.Equals(_tags,string.Join(", ",_context.Tags),StringComparison.Ordinal);
    });
    public bool SaveTags() => Run(() =>
    {
        Free();Current();
        if(!_context.PublicationVerified||!_context.SettledPaused)throw new InvalidOperationException("Pause and refresh this replay before saving tags.");
        if(_backend is not ITheatreEditorMetadataBackend metadata)throw new InvalidOperationException("Replay tag editing is unavailable for this owner.");
        var tags=TheatreReplayTags.Parse(_tags);var next=metadata.SaveTags(_context,tags);
        if(!ReferenceEquals(next.Owner,_original.Owner)||!ReferenceEquals(next.Transport,_reviewTransport)
            ||next.SourceRevision!=_original.SourceRevision)throw new TheatreEditorStaleException("The original owner changed during the tag edit.");
        _context=next;_authority=next.PublicationVerified;_tags=string.Join(", ",next.Tags);_tagsDirty=false;
        _status=next.PublicationNotice.Length>0?next.PublicationNotice:"Replay tags saved.";
    });
    public bool SetName(string name) => Run(() => { Free(); _name = name.Length <= 80 ? name : name[..80]; });
    public bool NextTab() => Run(() => { Free(); _tab = (TheatreEditorTab)(((int)_tab+1)%5); _selected=0; });
    public bool NextPlayer() => Run(() =>
    { Free(); var slots = _context.Players.Select(p=>p.Slot).Prepend(-1).ToArray(); _player=slots[(Array.IndexOf(slots,_player)+1)%slots.Length]; _selected=0; });
    public bool NextFilter() => Run(() => { Free(); _filter=(TheatreEditorFilter)(((int)_filter+1)%8); _selected=0; });
    public bool Move(int direction) => Run(() =>
    { Free(); if (direction is not (-1 or 1)) throw new ArgumentException("Choose previous or next entry."); var rows=Rows(); if (!rows.IsEmpty) _selected=(_selected+direction+rows.Length)%rows.Length; });
    public bool Seek(bool range) => Run(() => { Free(); Current(); _backend.Seek(_context,Selected(),range); });
    public bool InspectShot(int direction) => Run(() =>
    { Free(); Current(); if (direction is < -1 or > 1) throw new ArgumentException("Choose previous, current or next shot."); _backend.InspectShot(_context,Rows().IsEmpty?null:Selected(),direction,_player); });
    public bool ToggleHud(TheatreEditorHud hud) => Run(() => { Free(); Current(); _backend.ToggleHud(_context,hud); });
    public bool Stage(TheatreEditorEditKind kind) => Run(() =>
    {
        Free(); Current(); if (!Enum.IsDefined(kind)) throw new ArgumentException("Unknown editor change.");
        bool named = kind==TheatreEditorEditKind.RemoveNamedHighlight;
        var before = named?_context.NamedHighlights:_context.Reel;
        var after=before; TheatreEditorEntry? exact=null;
        if (kind is TheatreEditorEditKind.AddSelection or TheatreEditorEditKind.Trim)
            if (!_context.SettledPaused || _context.MarkIn is not uint start || _context.MarkOut is not uint end || start>=end || end>_context.Duration)
                throw new InvalidOperationException("Pause, set valid In/Out marks, and refresh before reviewing this range.");
        if (kind==TheatreEditorEditKind.AddSelection)
            after=after.Add(NewRange(_context.MarkIn!.Value,_context.MarkOut!.Value,Title("Marked selection"),0));
        else if (kind==TheatreEditorEditKind.AddHighlight)
        {
            if (_tab!=TheatreEditorTab.AutomaticHighlights) throw new InvalidOperationException("Select an automatic highlight first.");
            exact=Selected();
            if(before.Any(saved=>exact.Frame<saved.EndFrame&&exact.EndFrame>saved.Frame)) throw new InvalidOperationException("Suggestion overlaps an existing reel segment.");
            after=after.Add(NewRange(exact.Frame,exact.EndFrame,Title(exact.Name),4));
        }
        else if (kind==TheatreEditorEditKind.AddTopHighlights)
        {
            if (_context.Highlights.IsEmpty) throw new InvalidOperationException("This replay has no automatic highlights.");
            after=after.AddRange(VariedSuggestions(before).Select(row=>NewRange(row.Frame,row.EndFrame,row.Name,4)));
        }
        else if (kind==TheatreEditorEditKind.ClearReel)
        { if (before.IsEmpty) throw new InvalidOperationException("The reel is already empty."); after=ImmutableArray<TheatreEditorEntry>.Empty; }
        else
        {
            if (_tab!=(named?TheatreEditorTab.NamedHighlights:TheatreEditorTab.Reel)) throw new InvalidOperationException("Select the matching saved entry first.");
            exact=Selected(); int index=Index(before,exact.Id);
            if (index<0) throw new TheatreEditorStaleException("The selected entry changed. Refresh.");
            switch(kind)
            {
                case TheatreEditorEditKind.MoveUp: case TheatreEditorEditKind.MoveDown:
                    int to=index+(kind==TheatreEditorEditKind.MoveUp?-1:1);
                    if (to<0 || to>=before.Length) throw new InvalidOperationException("That entry is already at the end of the reel.");
                    after=after.RemoveAt(index).Insert(to,exact); break;
                case TheatreEditorEditKind.Trim: after=after.SetItem(index,exact with { Frame=_context.MarkIn!.Value,EndFrame=_context.MarkOut!.Value }); break;
                case TheatreEditorEditKind.CycleCamera: after=after.SetItem(index,exact with { Camera=(exact.Camera+1)%5 }); break;
                case TheatreEditorEditKind.Remove: case TheatreEditorEditKind.RemoveNamedHighlight: after=after.RemoveAt(index); break;
                default: throw new ArgumentException("Unknown editor change.");
            }
        }
        if (after.Length>128) throw new InvalidOperationException("The reel supports at most 128 segments.");
        _pending=new(kind,_context,exact,before,after); _status="Review this exact change before saving.";
    });
    public bool Confirm() => Run(() =>
    {
        Current(); var pending=_pending??throw new InvalidOperationException("No editor change is awaiting confirmation.");
        var next=_backend.Commit(pending);
        if (!ReferenceEquals(next.Owner,_original.Owner) || !ReferenceEquals(next.Transport,_reviewTransport)
            || next.SourceRevision!=_original.SourceRevision) throw new TheatreEditorStaleException("The original owner changed during the edit.");
        _context=next; _pending=null; _authority=next.PublicationVerified;
        _status=next.PublicationNotice.Length>0?next.PublicationNotice:"Reviewed editor change saved.";
    });
    public void Cancel() { Verify(); _pending=null; _error=""; }
    private TheatreEditorEntry NewRange(uint start,uint end,string name,int camera)
        => new(Guid.NewGuid().ToString("N"),start,end,name,"",Camera:camera);
    private string Title(string fallback) => string.IsNullOrWhiteSpace(_name)?fallback:_name.Trim();
    private static int Index(ImmutableArray<TheatreEditorEntry> rows,string id)
    { for(int i=0;i<rows.Length;i++) if(rows[i].Id==id) return i; return -1; }
    private TheatreEditorEntry Selected()
    { var rows=Rows(); if(rows.IsEmpty) throw new InvalidOperationException("No entry is selected."); return rows[Math.Clamp(_selected,0,rows.Length-1)]; }
    private ImmutableArray<TheatreEditorEntry> Rows() => _tab switch
    {
        TheatreEditorTab.Events => _context.Events.Where(row=>(_player<0 || row.EventType<0 || row.Actor==_player || row.Target==_player)
            && Visible(row.EventType,_filter) && (_eventSearch.Length==0||row.EventType>=0&&row.Name.Contains(_eventSearch,StringComparison.OrdinalIgnoreCase))).ToImmutableArray(),
        TheatreEditorTab.AutomaticHighlights => _context.Highlights,
        TheatreEditorTab.NamedHighlights => _context.NamedHighlights,
        TheatreEditorTab.Reel => _context.Reel,
        _ => _context.Analytics
    };
    // Values correspond to the retained ReplayEventType. The backend also preserves the actual enum name in Detail.
    public static bool Visible(int type,TheatreEditorFilter filter) => filter switch
    {
        TheatreEditorFilter.All=>true, TheatreEditorFilter.Combat=>type is 1 or 2 or 3 or 10,
        TheatreEditorFilter.KillsDeaths=>type is 1 or 2, TheatreEditorFilter.Damage=>type==3,
        TheatreEditorFilter.Shots=>type==10, TheatreEditorFilter.SpawnsDeaths=>type is 0 or 1 or 5 or 6,
        TheatreEditorFilter.Objectives=>type is 4 or 7 or 12 or 13 or 14 or 15 or 16 or >=17,
        _=>type<0
    };
    private void Current()
    { var live=_backend.ReadLive(_context); if (!_authority || !live.Current) { _authority=false; throw new TheatreEditorStaleException("The original replay changed. Refresh or close the editor."); } if(live.ExportRendering) throw new InvalidOperationException("Cancel the video export before changing replay editor inputs."); }
    private void Free() { if(_pending!=null) throw new InvalidOperationException("Confirm or cancel the reviewed editor change first."); }
    private bool Run(Action action)
    {
        Verify(); if(_disposed)return false;
        try {action();_error="";return true;}
        catch(Exception error) when(error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {if(error is TheatreEditorStaleException)_authority=false;_error=error.Message;return false;}
    }
    private void Verify() {if(_thread!=Environment.CurrentManagedThreadId)throw new InvalidOperationException("Replay editor belongs to the engine thread.");}
    public void Dispose() {Verify();if(_disposed)return;CancelSuggestions();_disposed=true;_authority=false;_pending=null;_backend.Retire();}
}
#endif
