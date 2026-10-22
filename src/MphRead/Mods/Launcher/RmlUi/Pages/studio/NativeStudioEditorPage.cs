#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.Threading;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Studio;

/// <summary>Native chrome and bounded input over the existing retained MapViewport renderer.</summary>
internal sealed partial class NativeStudioEditorPage : IDisposable
{
    private readonly RmlUiHost _host;
    private readonly RmlUiPageManager _pages;
    private readonly INativeStudioEditorSession _session;
    private readonly INativeStudioEditorViewport? _viewport;
    private readonly NativeStudioEditorController _controller;
    private readonly NativeStudioEditorDetails _details;
    private CancellationTokenRegistration _retirement;
    private RmlUiDocumentToken _page;
    private NativeStudioEditTicket _frameTicket;
    private long _presented = -1;
    private long _detailsPresented = -1, _bindingRevision;
    private NativeStudioEditTicket _shownTicket;
    private Guid _shownDelete;
    private string _pendingFocus = "";
    private Guid _selected;
    private bool _disposed, _closed, _publishRequested, _drawFailed;
    private int _x, _y, _w, _h, _capture = -1;
    private int _frameWidth, _frameHeight, _clientWidth, _clientHeight;
    private double _scaleX = 1, _scaleY = 1, _lastX, _lastY;
    public RmlUiDocumentToken Document => _page;
    public bool Closed => _closed;
    public bool LegacyRequested { get; private set; }
    internal const string PublishButtonId = "native_editor_publish";
    internal const string ViewportId = "native_editor_viewport";
    private bool Foreground => !_disposed && !_closed && _page != default && _host.IsAlive(_page)
        && _pages.Top == _page && _host.CurrentInputDocument == _page;
    public NativeStudioEditorPage(RmlUiHost host, RmlUiPageManager pages, INativeStudioEditorSession session)
    {
        _host = host; _pages = pages; _session = session; _viewport = session as INativeStudioEditorViewport;
        _controller = new(session);
        _details = new(session);
    }
    public void Open()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _page = _pages.OpenPage(new("studio-editor", "pages/studio/native-editor.rml", "native_editor_back"));
        _retirement = _pages.Lifetime(_page).Register(() => { ReleaseInput(); _disposed = _closed = true; _publishRequested = false; _resourceControls?.CancelDelete(); _session.Dispose(); });
        Refresh();
    }
    public bool TryTakePublishRequest()
    { bool requested = _publishRequested; _publishRequested = false; return requested && Foreground; }
    public void ShowStatus(string message) { if (!_disposed) { _controller.ShowStatus(message); Refresh(); } }
    public bool Back()
    {
        if (!Foreground) return false;
        if(_modelDragging){ReleaseInput();_controller.ShowStatus("MODELING GESTURE CANCELED // EDITOR REMAINS OPEN");Refresh();return false;}
        ReleaseInput(); _publishRequested = false; _closed = _controller.Close(); Refresh(); return _closed;
    }
    public bool Handle(in RmlUiIntent intent)
    {
        bool publish = intent.Kind == RmlUiIntentKind.CommunityDetailAction && intent.Argument == 5;
        if (!Foreground || intent.Document != _page || (!publish && intent.Kind != RmlUiIntentKind.StudioAction)
            || !_pages.Accept(intent)) return false;
        if ((_session as INativeStudioEditorBuild)?.CaptureBuild().Busy == true && (publish || intent.Argument is < 200 or > 223) && intent.Argument != 32)
        { _controller.ShowStatus("BUILD WORK BUSY // CANCEL BUILD OR WAIT FOR COMPLETION"); Refresh(); return true; }
        ReleaseInput();
        if (publish) { _details.CancelDelete(); _resourceControls?.CancelDelete(); _publishRequested = true; return true; }
        _publishRequested = false;
        if(intent.Argument is >=200 and <=223){_details.CancelDelete();_resourceControls?.CancelDelete();bool consumed=HandleBuildIntent(intent.Argument);Refresh();return consumed;}
        if(intent.Argument is >=190 and <200){_details.CancelDelete();_resourceControls?.CancelDelete();bool consumed=HandleAssetIoIntent(intent.Argument);Refresh();return consumed;}
        if(intent.Argument is >=139 and <190){_details.CancelDelete();_resourceControls?.CancelDelete();bool consumed=HandleModelingIntent(intent.Argument);Refresh();return consumed;}
        if(intent.Argument is >=85 and <139){_details.CancelDelete();bool consumed=HandleResourceIntent(intent.Argument);Refresh();return consumed;}
        _resourceControls?.CancelDelete();
        SavePropertyDrafts();
        if(intent.Argument is not(70 or 71 or 72))_details.CancelDelete();
        switch (intent.Argument)
        {
            case 0: if (_controller.Close()) { LegacyRequested = true; _closed = true; } break;
            case 32: Back(); break;
            case 33: _controller.Apply(NativeStudioEditorOperation.Undo); break;
            case 34: _controller.Apply(NativeStudioEditorOperation.Redo); break;
            case 35: _controller.Apply(NativeStudioEditorOperation.Save, path: Field("path", 32767)); break;
            case 36: _controller.Apply(NativeStudioEditorOperation.Move, Field("move_x"), Field("move_y"), Field("move_z")); break;
            case 37: _controller.Apply(NativeStudioEditorOperation.Rotate, Field("angle")); break;
            case 38: _controller.Apply(NativeStudioEditorOperation.Scale, Field("factor")); break;
            case 39: _controller.Apply(NativeStudioEditorOperation.FrameAll); break;
            case 40: _controller.Apply(NativeStudioEditorOperation.FrameSelection); break;
            case 41: _controller.Apply(NativeStudioEditorOperation.Perspective); break;
            case 42: _controller.Apply(NativeStudioEditorOperation.Top); break;
            case 43: _controller.Apply(NativeStudioEditorOperation.Front); break;
            case 44: _controller.Apply(NativeStudioEditorOperation.Side); break;
            case 45: _controller.Page(-1); break;
            case 46: _controller.Page(1); break;
            case 69: if(_details.Duplicate(_shownTicket)){_controller.AcceptSelection();}_controller.ShowStatus(_details.Status);break;
            case 70: _details.RequestDelete(_shownTicket);_controller.ShowStatus(_details.Status);break;
            case 71: if(_details.ConfirmDelete())_controller.AcceptSelection();_controller.ShowStatus(_details.Status);break;
            case 72: _details.CancelDelete();break;
            case 73: _details.Page(-1);break;
            case 74: _details.Page(1);break;
            case 75: if(_details.Apply())_controller.AcceptSelection();_controller.ShowStatus(_details.Status);break;
            case 76: _details.Reset();break;
            default:
                if (intent.Argument is >= 47 and < 55) { if(_controller.Select(intent.Argument - 47))_details.Reset(); }
                else if(intent.Argument is >=60 and <69){if(_details.Create(_shownTicket,(NativeStudioCreateKind)(intent.Argument-60)))_controller.AcceptSelection();_controller.ShowStatus(_details.Status);}
                else if(intent.Argument is >=77 and <85)_details.Cycle(intent.Argument-77);
                else return false;
                break;
        }
        if(intent.Argument is >=33 and <=38)_details.Reset();
        Refresh(); return true;
    }
    private void SavePropertyDrafts()
    {var rows=_details.Rows;for(int i=0;i<rows.Count;i++)_details.SetDraft(i,_host.ReadField(_page,"native_property_value"+i,Math.Min(4096,rows[i].Field.MaxLength+1)));}
    private string Field(string name, int maximum = 64) => _host.ReadField(_page, "native_editor_" + name, maximum);
    public void Refresh()
    {
        if (!Foreground) { ReleaseInput(); return; }
        if (_capture >= 0 && _host.FocusedElement() != ViewportId) ReleaseInput();
        RefreshBuild();
        RefreshAssetIo();
        RefreshResources();
        RefreshModeling();
        var state = _controller.Current();
        if (!state.Owner.Alive) { _closed = true; ReleaseInput(); return; }
        Guid pendingDelete=_details.Delete?.Confirmation??Guid.Empty;
        if(pendingDelete!=_shownDelete){_pendingFocus=pendingDelete!=Guid.Empty?"native_editor_confirm_delete":"native_editor_delete";_shownDelete=pendingDelete;}
        if(_pendingFocus.Length>0&&_host.FocusDocument(_page,_pendingFocus))_pendingFocus="";
        if (state.Revision == _presented && _details.Revision==_detailsPresented && (ResourceControls==null||ResourceControls.Revision==_resourcePresented) && ModelControls.Revision==_modelPresented&&(_assetIoView?.Revision??0)==_assetIoPresented&&(_buildView?.Revision??0)==_buildPresented) return;
        var data = state.Owner;
        _shownTicket=data.Ticket;
        var bindings = new Dictionary<string, RmlUiBindingValue>
        {
            ["native_editor_title"] = RmlUiBindingValue.FromText(data.Name + (data.Dirty ? " // UNSAVED" : " // SAVED")),
            ["native_editor_selection"] = RmlUiBindingValue.FromText(data.Selection),
            ["native_editor_status"] = RmlUiBindingValue.FromText(state.Status),
            ["native_editor_page_label"] = RmlUiBindingValue.FromText($"OBJECTS {state.Page + 1} / {state.PageCount}"),
            ["disabled:native_editor_previous"] = RmlUiBindingValue.FromBoolean(state.Page == 0),
            ["disabled:native_editor_next"] = RmlUiBindingValue.FromBoolean(state.Page + 1 >= state.PageCount),
            ["disabled:native_editor_undo"] = RmlUiBindingValue.FromBoolean(!data.CanUndo),
            ["disabled:native_editor_redo"] = RmlUiBindingValue.FromBoolean(!data.CanRedo)
        };
        bindings["visible:native_editor_delete_panel"]=RmlUiBindingValue.FromBoolean(_details.Delete!=null);
        bindings["native_editor_delete_label"]=RmlUiBindingValue.FromText(_details.Delete?.Label??"");
        bindings["native_property_page"]=RmlUiBindingValue.FromText($"PROPERTIES {_details.PageIndex+1} / {_details.PageCount}");
        bindings["disabled:native_property_previous"]=RmlUiBindingValue.FromBoolean(_details.PageIndex==0);
        bindings["disabled:native_property_next"]=RmlUiBindingValue.FromBoolean(_details.PageIndex+1>=_details.PageCount);
        var properties=_details.Rows;
        for(int i=0;i<8;i++)
        {
            var row=i<properties.Count?properties[i]:null;bool cycle=row?.Field.Kind is NativeStudioPropertyKind.Boolean or NativeStudioPropertyKind.Choice;
            bindings["visible:native_property_row"+i]=RmlUiBindingValue.FromBoolean(row!=null);
            bindings["native_property_label"+i]=RmlUiBindingValue.FromText(row?.Field.Label??"");
            bindings["visible:native_property_value"+i]=RmlUiBindingValue.FromBoolean(row!=null&&!cycle);
            bindings["visible:native_property_cycle"+i]=RmlUiBindingValue.FromBoolean(row!=null&&cycle);
            bindings["native_property_cycle"+i]=RmlUiBindingValue.FromText(row?.Value??"");
            if(row!=null&&!cycle&&_details.Revision!=_detailsPresented)_host.SetField(_page,"native_property_value"+i,row.Value);
        }
        for (int i = 0; i < NativeStudioEditorController.PageSize; i++)
        {
            var row = i < state.Rows.Count ? state.Rows[i] : null;
            bindings["native_editor_row" + i] = RmlUiBindingValue.FromText(row == null ? "—" : (row.Locked ? "LOCKED // " : "") + row.Label);
            bindings["disabled:native_editor_row" + i] = RmlUiBindingValue.FromBoolean(row == null);
            bindings["class:native_editor_row" + i + ":selected"] = RmlUiBindingValue.FromBoolean(row?.Id == data.Ticket.Selection);
        }
        foreach (string name in new[] { "move", "rotate", "scale" })
            bindings["disabled:native_editor_" + name] = RmlUiBindingValue.FromBoolean(data.Ticket.Selection == Guid.Empty);
        if (_presented < 0) _host.SetField(_page, "native_editor_path", data.Path);
        if (_selected != data.Ticket.Selection)
        {
            foreach (string field in new[] { "move_x", "move_y", "move_z", "angle" }) _host.SetField(_page, "native_editor_" + field, "0");
            _host.SetField(_page, "native_editor_factor", "1"); _selected = data.Ticket.Selection;
        }
        BindResources(bindings);
        BindAssetIo(bindings);
        BindModeling(bindings);
        BindBuild(bindings);
        _pages.Present(_page, ++_bindingRevision, bindings); _presented = state.Revision;_detailsPresented=_details.Revision;
    }
    /// <summary>Call AFTER host.Update/layout and BEFORE native chrome draw, on the existing RenderWindow GL owner.</summary>
    public bool DrawViewport(int width, int height, int clientWidth = 0, int clientHeight = 0)
    {
        if (_capture >= 0 && _host.FocusedElement() != ViewportId) ReleaseInput();
        if (!Foreground || _viewport == null || width <= 0 || height <= 0
            || !_host.TryGetElementBounds(_page, ViewportId, out float x, out float y, out float w, out float h)
            || !_host.TryGetElementBounds(_page, "native_editor_frame", out float px, out float py, out float pw, out float ph)
            || x < px || y < py || x + w > px + pw + .5f || y + h > py + ph + .5f)
        { ClearRectangle(); return false; }
        int left = (int)Math.Ceiling(x), top = (int)Math.Ceiling(y), right = (int)Math.Floor(x + w), bottom = (int)Math.Floor(y + h);
        if (left < 0 || top < 0 || right > width || bottom > height || right <= left || bottom <= top)
        { ClearRectangle(); return false; }
        int logicalWidth = clientWidth > 0 ? clientWidth : width, logicalHeight = clientHeight > 0 ? clientHeight : height;
        double scaleX = width / (double)Math.Max(1, logicalWidth), scaleY = height / (double)Math.Max(1, logicalHeight);
        if (_x != left || _y != top || _w != right - left || _h != bottom - top || _scaleX != scaleX || _scaleY != scaleY
            || _frameWidth != width || _frameHeight != height || _clientWidth != logicalWidth || _clientHeight != logicalHeight) ReleaseInput();
        _x = left; _y = top; _w = right - left; _h = bottom - top;
        _scaleX = scaleX; _scaleY = scaleY;
        _frameTicket = _session.Capture().Ticket;
        if (!_viewport.DrawViewport(_x, _y, _w, _h, width, height))
        {
            if (!_drawFailed) _controller.ShowStatus("VIEWPORT RENDERER UNAVAILABLE // EXISTING DOCUMENT AND UNDO HISTORY RETAINED");
            _drawFailed = true; ClearRectangle(); return false;
        }
        if (_drawFailed) _controller.ShowStatus("VIEWPORT RESTORED // EXISTING DOCUMENT RETAINED");
        _drawFailed = false;
        _frameWidth = width; _frameHeight = height; _clientWidth = logicalWidth; _clientHeight = logicalHeight;
        ModelDrawComplete();
        return true;
    }
    /// <summary>Input-only resize fence; fresh valid drawing must publish a new coordinate basis.</summary>
    public bool ValidateInputBasis(int framebufferWidth, int framebufferHeight, int clientWidth, int clientHeight)
    {
        if (Foreground && _w > 0 && _h > 0 && framebufferWidth > 0 && framebufferHeight > 0 && clientWidth > 0 && clientHeight > 0
            && _frameWidth == framebufferWidth && _frameHeight == framebufferHeight && _clientWidth == clientWidth && _clientHeight == clientHeight)
            return true;
        ClearRectangle(); return false;
    }
    public bool PointerButton(int code, double x, double y, bool down)
    {
        if (!down)
        { bool consumed = _capture == code; if (consumed) { if(code==0&&Foreground&&_host.FocusedElement()==ViewportId&&Inside(x,y))ModelPointerUp(x,y); ReleaseInput(); Refresh(); } return consumed; }
        if (!Foreground || _viewport == null || code is < 0 or > 2 || !Inside(x, y)) { ReleaseInput(); return false; }
        if (!_host.FocusDocument(_page, ViewportId)) return false;
        _lastX = x * _scaleX; _lastY = y * _scaleY; _capture = code;
        if (code == 0)
        { if(ModelPointerDown(x,y)){Refresh();return true;} var result = _viewport.Pick(_frameTicket, _lastX - _x, _lastY - _y); if (result.Success) {_controller.AcceptSelection();_details.Reset();} Refresh(); }
        return true;
    }
    public bool PointerMoved(double x, double y)
    {
        if (_capture < 0) return false;
        if (!Foreground || _host.FocusedElement() != ViewportId || !Inside(x, y)) { ReleaseInput(); return true; }
        if(_modelDragging){if(ModelViewport is not {} model||ModelBasis is not {} basis||!model.MoveModelingPointer(basis,x,y))ReleaseInput();return true;}
        double px = x * _scaleX, py = y * _scaleY;
        _viewport?.Camera(px - _lastX, py - _lastY, _capture == 1, _capture == 2); _lastX = px; _lastY = py; return true;
    }
    public bool Wheel(double x, double y, double delta)
    {
        if (!Foreground || _viewport == null || !Inside(x, y) || !double.IsFinite(delta)) return false;
        ReleaseInput(); _viewport.Zoom(delta); return true;
    }
    private bool Inside(double x, double y) => _w > 0 && _h > 0 && double.IsFinite(x) && double.IsFinite(y)
        && x * _scaleX >= _x && y * _scaleY >= _y && x * _scaleX < _x + _w && y * _scaleY < _y + _h;
    private void ClearRectangle() { _w = _h = 0; ReleaseInput(); }
    public void ReleaseInput() { _capture = -1; _modelDragging=false; _viewport?.CancelInput(); }
    public void Dispose()
    {
        _retirement.Dispose(); if (_disposed) return;
        ReleaseInput(); _disposed = _closed = true; _publishRequested = false; _resourceControls?.CancelDelete(); _session.Dispose();
        if (_page != default && _pages.Page == _page) _pages.ClosePage();
    }
}
#endif
