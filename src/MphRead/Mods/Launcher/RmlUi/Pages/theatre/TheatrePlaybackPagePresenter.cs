#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Globalization;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Theatre;

/// <summary>Engine-owned playback transport and viewport input, presented by a native document.</summary>
public sealed partial class TheatrePlaybackPagePresenter : IDisposable
{
    private static readonly RmlUiPageSpec Page = new("theatre-playback", "pages/theatre/playback.rml", "replay_pause");
    private readonly RmlUiHost _host;
    private readonly RmlUiPageManager _pages;
    private readonly TheatrePlaybackController _controller;
    private RmlUiDocumentToken _document;
    private bool _disposed;
    public TheatrePlaybackPagePresenter(RmlUiHost host, RmlUiPageManager pages, TheatrePlaybackController controller,
        ITheatreBookmarkBackend? bookmarks = null, ITheatreRangeBackend? ranges = null, ITheatreCameraBackend? camera = null,
        ITheatreExportBackend? exports = null, ITheatreEditorBackend? editor = null)
        => (_host, _pages, _controller, _bookmarkBackend, _rangeBackend, _cameraBackend, _exportBackend, _editorBackend) = (host, pages, controller, bookmarks, ranges, camera, exports, editor);
    public bool FourViews { get; private set; }
    private string _fourViewProblem = "";
    internal void ReportFourViewFailure(Exception error)
    {
        if (!FourViews || !IsOpen) return;
        FourViews = false;
        Viewport.Release();
        _fourViewProblem = "Four-camera preview disabled after a render error. Your replay is still open. "
            + error.GetBaseException().Message;
        Present();
    }
    public TheatreViewportController Viewport { get; } = new();
    public RmlUiDocumentToken Document => _document;
    public bool IsOpen => !_disposed && _document != default && _host.IsAlive(_document) && _pages.Page == _document;
    public void Open() { ObjectDisposedException.ThrowIf(_disposed, this); _document = _pages.OpenPage(_editorBackend==null?Page:new("studio-workspace","pages/theatre/studio-workspace.rml","replay_pause")); Present(); }
    public bool Present()
    {
        if (!IsOpen) return false;
        TheatrePlaybackSnapshot state = _controller.Snapshot();
        if(WorkspaceMode)EnsureWorkspaceOwners();
        _host.SetBool(_document,"visible:replay_four_views",_editorBackend!=null);
        _host.SetBool(_document,"visible:replay_view_labels",FourViews&&_editorBackend!=null
            &&!MphRead.Mods.Network.ReplayController.IsSeeking);
        _host.SetText(_document,"replay_four_views",FourViews?"SINGLE VIEW":"FOUR VIEWS");
        _host.SetBool(_document,"disabled:replay_four_views",!state.Active||state.ExportRendering
            ||!FourViews&&MphRead.Mods.Network.ReplayController.IsSeeking);
        _host.SetText(_document, "replay_pause", state.PlayAction ? "PLAY" : "PAUSE");
        _host.SetText(_document, "replay_time", Time(state.Frame) + " / " + Time(state.Duration));
        _host.SetText(_document, "replay_state", state.State.ToUpperInvariant() + " // MOVEMENT: " + state.Movement.ToUpperInvariant());
        _host.SetText(_document, "replay_rate", state.Rate.ToString("0.##", CultureInfo.InvariantCulture) + "× SPEED");
        _host.SetText(_document, "replay_camera", state.Camera.ToUpperInvariant() + " CAMERA");
        if(!WorkspaceMode)_host.SetText(_document, "replay_studio", _editorBackend == null ? "OPEN IN PRIME STUDIO" : "REPLAY EDITOR");
        string error = _fourViewProblem.Length > 0 ? _fourViewProblem : _editorProblem.Length > 0 ? _editorProblem : _exportProblem.Length > 0 ? _exportProblem : _cameraProblem.Length > 0 ? _cameraProblem : _rangeProblem.Length > 0 ? _rangeProblem : _bookmarkProblem.Length > 0 ? _bookmarkProblem : state.Error;
        _host.SetText(_document, "replay_error", error); _host.SetBool(_document, "replay_error", error.Length > 0);
        _host.SetBool(_document, "replay_inactive", !state.Active);
        // The workspace exposes these owners through persistent inspector tabs;
        // viewer-only launch buttons are not bindings in its document.
        if(!WorkspaceMode)
        {
            _host.SetBool(_document, "visible:replay_bookmarks", _bookmarkBackend != null);
            _host.SetBool(_document, "visible:replay_camera_editor", _cameraBackend != null);
            _host.SetBool(_document, "visible:replay_export_open", _exportBackend != null);
            _host.SetBool(_document, "visible:replay_range_controls", _rangeBackend != null);
            _host.SetBool(_document, "disabled:replay_bookmarks", !state.Active || state.ExportRendering || _bookmarkBackend == null);
            _host.SetBool(_document, "disabled:replay_camera_editor", !state.Active || state.ExportRendering || _cameraBackend == null);
            _host.SetBool(_document, "disabled:replay_export_open", !state.Active || _exportBackend == null);
        }
        _host.SetBool(_document, "visible:replay_range_summary", _rangeBackend != null);
        foreach (string id in new[] { "mark_in", "mark_out", "ranges" })
            if(!WorkspaceMode||id!="ranges")_host.SetBool(_document, "disabled:replay_" + id, !state.Active || state.ExportRendering || _rangeBackend == null);
        foreach (string id in new[] { "pause", "jump_back", "jump_forward", "restart", "step", "rate", "camera", "previous_player", "next_player", "position", "studio", "fullscreen" })
            if(!WorkspaceMode||id!="studio")_host.SetBool(_document, "disabled:replay_" + id, !state.Active || state.ExportRendering && id != "fullscreen");
        _host.SetBool(_document, "disabled:replay_back", state.ExportRendering);
        if (_host.FocusedElement() != "replay_position")
            _host.SetField(_document, "replay_position", state.Duration == 0 ? "0" : ((long)state.Frame * 1000 / state.Duration).ToString(CultureInfo.InvariantCulture));
        PresentBookmarks();
        PresentRanges();
        PresentCamera();
        PresentCameraTools();
        PresentTimeline();
        PresentExports();
        PresentEditor();
        PresentSpatial();
        PresentStudioTools();
        PresentWorkspace();
        return true;
    }
    public bool HandleIntent(in RmlUiIntent intent)
    {
        if(HandleWorkspaceIntent(intent))return true;
        if(IsOpen&&intent.Kind==RmlUiIntentKind.ReplayAction&&intent.Argument==(int)TheatrePlaybackAction.FourViews)
        {
            if(_editorBackend==null||intent.Document!=_document||!_pages.Accept(intent)
                ||_controller.Snapshot().ExportRendering
                ||!FourViews&&MphRead.Mods.Network.ReplayController.IsSeeking)return false;
            Viewport.Release();_fourViewProblem="";FourViews=!FourViews;Present();return true;
        }
        if (intent.Kind == RmlUiIntentKind.ReplayAction && (intent.Argument == (int)TheatrePlaybackAction.CameraToolsTransform || intent.Argument == (int)TheatrePlaybackAction.CameraToolsHandles || intent.Argument == (int)TheatrePlaybackAction.CameraToolsCurveKey))
            return HandleCameraToolsIntent(intent);
        if (intent.Kind == RmlUiIntentKind.ReplayAction && (intent.Argument >= (int)TheatrePlaybackAction.StudioToolsOpen && intent.Argument <= (int)TheatrePlaybackAction.StudioToolsClose || intent.Argument is (int)TheatrePlaybackAction.StudioToolsPreview or (int)TheatrePlaybackAction.StudioToolsStopPreview or (int)TheatrePlaybackAction.StudioAudioEnabled or (int)TheatrePlaybackAction.StudioAudioSave || intent.Argument >= (int)TheatrePlaybackAction.StudioCueEvent && intent.Argument <= (int)TheatrePlaybackAction.StudioCueRemove))
            return HandleStudioToolsIntent(intent);
        if (intent.Kind == RmlUiIntentKind.ReplayAction && (intent.Argument >= (int)TheatrePlaybackAction.SpatialOpen && intent.Argument <= (int)TheatrePlaybackAction.SpatialClose || intent.Argument == (int)TheatrePlaybackAction.SpatialWorld))
            return HandleSpatialIntent(intent);
        if (intent.Kind == RmlUiIntentKind.ReplayAction && (intent.Argument >= (int)TheatrePlaybackAction.TimelineOpen && intent.Argument <= (int)TheatrePlaybackAction.TimelineActivate))
            return HandleTimelineIntent(intent);
        if (intent.Kind == RmlUiIntentKind.ReplayAction && intent.Argument >= (int)TheatrePlaybackAction.CameraToolsOpen)
            return HandleCameraToolsIntent(intent);
        if (intent.Kind == RmlUiIntentKind.ReplayAction && (intent.Argument >= (int)TheatrePlaybackAction.EditorOpen
            || intent.Argument == (int)TheatrePlaybackAction.Studio && _editorBackend != null))
            return HandleEditorIntent(intent);
        if (intent.Kind == RmlUiIntentKind.ReplayAction && intent.Argument >= (int)TheatrePlaybackAction.ExportOpen)
            return HandleExportIntent(intent);
        if (intent.Kind == RmlUiIntentKind.ReplayAction && intent.Argument >= (int)TheatrePlaybackAction.CameraEditor)
            return HandleCameraIntent(intent);
        if (intent.Kind == RmlUiIntentKind.ReplayAction && intent.Argument >= (int)TheatrePlaybackAction.Ranges)
            return HandleRangeIntent(intent);
        if (intent.Kind == RmlUiIntentKind.ReplayAction && intent.Argument >= (int)TheatrePlaybackAction.Bookmarks)
            return HandleBookmarkIntent(intent);
        if (!IsOpen || intent.Kind != RmlUiIntentKind.ReplayAction || intent.Document != _document
            || !Enum.IsDefined((TheatrePlaybackAction)intent.Argument) || !_pages.Accept(intent)) return false;
        int position = 0;
        if ((TheatrePlaybackAction)intent.Argument == TheatrePlaybackAction.Seek)
        {
            // RmlUi range controls expose their value in floating-point form,
            // including step-one values such as "125.000000".
            if (!Double.TryParse(_host.ReadField(_document, "replay_position"), NumberStyles.Float,
                CultureInfo.InvariantCulture, out double value) || !Double.IsFinite(value)) return false;
            position = (int)Math.Round(Math.Clamp(value, 0, 1000), MidpointRounding.AwayFromZero);
        }
        _controller.Dispatch((TheatrePlaybackAction)intent.Argument, position);
        Present(); return true;
    }
    public bool TryTakeEngineCommand(out TheatrePlaybackAction command) => _controller.TryTakeEngineCommand(out command);
    public bool Close()
    {
        if(_cameraTools?.Snapshot().Dirty==true)
        {_cameraToolsProblem="Save or discard the unsaved camera document before leaving Studio.";PresentCameraTools();return false;}
        _workspaceClosing=true;
        try
        {
        CloseWorkspaceProjectFiles();
        Viewport.Release();
        if (!CloseStudioTools()) return false;
        if (!CloseSpatial()) return false;
        if (!CloseTimeline()) return false;
        if (!CloseCameraTools()) return false;
        if (!CloseEditor()) return false;
        if (!CloseExports()) return false;
        if (!CloseCamera()) return false;
        if (!CloseRanges(retire: true)) return false;
        if (!CloseBookmarks()) return false;
        if (IsOpen && !_pages.ClosePage()) return false;
        _document = default;return true;
        }
        finally{_workspaceClosing=false;}
    }
    public void Dispose() { if (_disposed) return; if (!Close()) throw new InvalidOperationException("The replay controls could not be closed."); _disposed = true; }
    private static string Time(uint frames) => $"{frames / 3600}:{frames / 60 % 60:00}";
}
#endif
