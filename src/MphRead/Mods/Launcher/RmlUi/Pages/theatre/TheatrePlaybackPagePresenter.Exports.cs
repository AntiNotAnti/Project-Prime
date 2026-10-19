#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Theatre;

public sealed partial class TheatrePlaybackPagePresenter
{
    private readonly ITheatreExportBackend? _exportBackend;
    private TheatreExportController? _exports;
    private RmlUiDocumentToken _exportModal, _exportConfirmation;
    private string _exportProblem = "";
    private bool HandleExportIntent(in RmlUiIntent intent)
    {
        if (!IsOpen || _exportBackend == null || !Enum.IsDefined((TheatrePlaybackAction)intent.Argument)) return false;
        var action = (TheatrePlaybackAction)intent.Argument;
        bool opening = action == TheatrePlaybackAction.ExportOpen && _exportModal == default && intent.Document == _document;
        bool confirming = _exportConfirmation != default && intent.Document == _exportConfirmation
            && action is TheatrePlaybackAction.ExportConfirm or TheatrePlaybackAction.ExportCancelConfirm;
        bool editing = _exportConfirmation == default && _exportModal != default && intent.Document == _exportModal
            && intent.Argument >= (int)TheatrePlaybackAction.ExportRefresh && intent.Argument <= (int)TheatrePlaybackAction.ExportOpenOutput
            && action is not (TheatrePlaybackAction.ExportConfirm or TheatrePlaybackAction.ExportCancelConfirm);
        if ((!opening && !confirming && !editing) || !_pages.Accept(intent)) return false;
        try
        {
            _exportProblem = "";
            if (opening)
            {
                Viewport.Release(); _exports?.Dispose(); _exports = new(_exportBackend);
                _exportModal = _pages.OpenModal(new("replay-export", "pages/theatre/export.rml", "replay_export_refresh"));
            }
            else if (_exports != null) switch (action)
            {
                case TheatrePlaybackAction.ExportRefresh: _exports.Refresh(); break;
                case TheatrePlaybackAction.ExportPreset: _exports.Change("preset"); break;
                case TheatrePlaybackAction.ExportResolution: _exports.Change("resolution"); break;
                case TheatrePlaybackAction.ExportFps: _exports.Change("fps"); break;
                case TheatrePlaybackAction.ExportGameHud: _exports.Change("game-hud"); break;
                case TheatrePlaybackAction.ExportReplayOverlay: _exports.Change("replay-overlay"); break;
                case TheatrePlaybackAction.ExportEncoder: _exports.Change("encoder"); break;
                case TheatrePlaybackAction.ExportSource: _exports.Change("source"); break;
                case TheatrePlaybackAction.ExportCamera: _exports.Change("camera"); break;
                case TheatrePlaybackAction.ExportQueue: if(_exports.SetEncoderExecutable(_host.ReadField(_exportModal,"replay_export_executable")))_exports.Enqueue(); break;
                case TheatrePlaybackAction.ExportPrevious: _exports.Move(-1); break;
                case TheatrePlaybackAction.ExportNext: _exports.Move(1); break;
                case TheatrePlaybackAction.ExportCancelActive:
                    if (_exports.Request(TheatreExportConfirmationKind.CancelActive)) OpenExportConfirmation(); break;
                case TheatrePlaybackAction.ExportRetry:
                    if (_exports.Request(TheatreExportConfirmationKind.Retry)) OpenExportConfirmation(); break;
                case TheatrePlaybackAction.ExportClearPending:
                    if (_exports.Request(TheatreExportConfirmationKind.ClearPending)) OpenExportConfirmation(); break;
                case TheatrePlaybackAction.ExportConfirm:
                    if (_exports.Confirm() && _pages.CloseModal()) _exportConfirmation = default; break;
                case TheatrePlaybackAction.ExportCancelConfirm: BackExports(); break;
                case TheatrePlaybackAction.ExportOpenOutput: _exports.OpenOutput(); break;
                case TheatrePlaybackAction.ExportClose: CloseExports(); break;
            }
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { _exportProblem = error.Message; }
        PresentExports(); return true;
    }
    private void OpenExportConfirmation()
    {
        _exportConfirmation = _pages.OpenModal(new("replay-export-confirm", "pages/theatre/export-confirm.rml", "replay_export_confirm_cancel"));
    }
    private RmlUiDocumentToken _exportExecutableDocument;
    private void PresentExports()
    {
        if (_exportConfirmation != default && !_host.IsAlive(_exportConfirmation))
        { _exportConfirmation = default; _exports?.CancelConfirmation(); }
        if (_exportModal != default && !_host.IsAlive(_exportModal))
        { _exportModal = default; _exports?.Dispose(); _exports = null; }
        if (_exportModal == default || _exports == null) return;
        var state = _exports.Snapshot(); var options = state.Options; var queue = state.Queue;
        if(_exportExecutableDocument!=_exportModal)
        {_host.SetField(_exportModal,"replay_export_executable",options.EncoderExecutable);_exportExecutableDocument=_exportModal;}
        _host.SetText(_exportModal,"replay_export_audio_summary",state.Context.AudioSummary);

        _host.SetText(_exportModal, "replay_export_replay", Path.GetFileName(state.Context.LogicalPath));
        _host.SetText(_exportModal, "replay_export_preset", "PRESET: " + state.Presets[options.Preset].Name);
        _host.SetText(_exportModal, "replay_export_resolution", "RESOLUTION: " + new[] { "720p", "1080p", "1440p", "4K" }[options.Resolution]);
        _host.SetText(_exportModal, "replay_export_fps", $"FPS: {options.Fps}");
        _host.SetText(_exportModal, "replay_export_game_hud", "GAME HUD: " + (options.GameHud ? "ON" : "OFF"));
        _host.SetText(_exportModal, "replay_export_replay_overlay", "REPLAY OVERLAY: " + (options.ReplayOverlay ? "ON" : "OFF"));
        _host.SetText(_exportModal, "replay_export_encoder", "VIDEO CODEC: " + options.Encoder);
        _host.SetText(_exportModal, "replay_export_source", "SOURCE: " + (options.Source == TheatreExportSource.Selection ? "REVIEWED MARKS"
            : options.Source == TheatreExportSource.FullReplay ? "FULL REPLAY" : "SAVED REEL"));
        _host.SetText(_exportModal, "replay_export_camera", "CAMERA: " + (options.Camera == TheatreExportCamera.SavedCameraTrack ? "SAVED TRACK" : options.Camera.ToString().ToUpperInvariant()));
        _host.SetText(_exportModal, "replay_export_range", "REVIEWED IN " + (state.Context.MarkIn is uint start ? start.ToString(CultureInfo.InvariantCulture) : "NOT SET")
            + " // OUT " + (state.Context.MarkOut is uint end ? end.ToString(CultureInfo.InvariantCulture) : "NOT SET")
            + $" // {state.Context.Duration} TOTAL FRAMES // {state.Context.ReelSegments} REEL SEGMENTS");
        _host.SetText(_exportModal, "replay_export_progress", queue.State.ToUpperInvariant() + " // " + queue.Progress.ToString("P0", CultureInfo.InvariantCulture) + " // " + queue.Status);
        _host.SetText(_exportModal, "replay_export_active", queue.Active is { } active ? "ACTIVE " + DescribeExport(active) : "NO ACTIVE EXPORT");
        _host.SetText(_exportModal, "replay_export_pending", queue.Pending.IsEmpty ? "NO PENDING EXPORTS"
            : $"PENDING {state.SelectedIndex + 1} / {queue.Pending.Length} // " + DescribeExport(queue.Pending[state.SelectedIndex]));
        _host.SetText(_exportModal, "replay_export_failures", string.Join("\n", queue.Failures));
        _host.SetText(_exportModal, "replay_export_output", queue.OutputPath.Length == 0 ? "No completed video output yet." : queue.OutputPath);
        _host.SetText(_exportModal, "replay_export_status", state.Status);
        _host.SetText(_exportModal, "replay_export_error", _exportProblem.Length > 0 ? _exportProblem : state.Error.Length > 0 ? state.Error
            : !state.Current ? "Export inputs changed. Refresh before queueing another video." : "");
        _host.SetBool(_exportModal, "disabled:replay_export_queue", !state.Current || queue.Pending.Length >= 8);
        _host.SetBool(_exportModal, "disabled:replay_export_cancel_active", queue.Active == null);
        _host.SetBool(_exportModal, "disabled:replay_export_retry", queue.Retry == null || queue.Pending.Length >= 8);
        _host.SetBool(_exportModal, "disabled:replay_export_clear_pending", queue.Pending.IsEmpty);
        _host.SetBool(_exportModal, "disabled:replay_export_previous", queue.Pending.IsEmpty);
        _host.SetBool(_exportModal, "disabled:replay_export_next", queue.Pending.IsEmpty);
        _host.SetBool(_exportModal, "disabled:replay_export_open_output", queue.OutputPath.Length == 0);
        if (_exportConfirmation == default) return;
        var pending = state.Confirmation;
        string title = pending?.Kind == TheatreExportConfirmationKind.CancelActive ? "CANCEL THIS EXPORT?"
            : pending?.Kind == TheatreExportConfirmationKind.Retry ? "RETRY THIS ORIGINAL EXPORT?" : "CLEAR THESE PENDING EXPORTS?";
        _host.SetText(_exportConfirmation, "replay_export_confirm_title", title);
        string exact = pending?.Kind switch
        {
            TheatreExportConfirmationKind.CancelActive => pending.Queue.Active == null ? "No active export." : DescribeExport(pending.Queue.Active),
            TheatreExportConfirmationKind.Retry => pending.Queue.Retry == null ? "No retry target." : DescribeExport(pending.Queue.Retry),
            TheatreExportConfirmationKind.ClearPending => string.Join("\n\n", pending.Queue.Pending.Select(DescribeExport)), _ => "No operation is pending."
        };
        _host.SetText(_exportConfirmation, "replay_export_confirm_exact", exact);
        _host.SetText(_exportConfirmation, "replay_export_confirm_note", pending?.Kind == TheatreExportConfirmationKind.Retry
            ? "Retry uses the original reviewed inputs and output folder. Existing files in that export folder may be replaced."
            : pending?.Kind == TheatreExportConfirmationKind.CancelActive ? "Cancellation keeps generated files and leaves pending exports queued."
            : "This removes the reviewed pending jobs from the queue. The active export and generated files are preserved.");
        _host.SetText(_exportConfirmation, "replay_export_confirm_error", _exportProblem.Length > 0 ? _exportProblem : state.Error);
        _host.SetBool(_exportConfirmation, "disabled:replay_export_confirm_apply", pending == null || pending.Queue.Revision != queue.Revision);
    }
    private static string DescribeExport(TheatreExportJob job) => job.ReplayName + " // " + job.Id.ToString("N")[..8]
        + "\n" + job.Description + "\n" + job.OutputPath;
    private bool BackExports()
    {if(WorkspaceMode&&!_workspaceClosing&&_exportConfirmation==default)return false;
        if (_exportConfirmation != default)
        {
            if (!_pages.CloseModal()) return true;
            _exportConfirmation = default; _exports?.CancelConfirmation(); PresentExports(); return true;
        }
        if (_exportModal == default) return false;
        CloseExports(); return true;
    }
    private bool CloseExports()
    {
        if(WorkspaceMode&&!_workspaceClosing){SelectWorkspaceTab("camera");return true;}
        if (_exportConfirmation != default)
        {
            if (_host.IsAlive(_exportConfirmation) && !_pages.CloseModal()) return false;
            _exportConfirmation = default;
        }
        if (_exportModal != default)
        {
            if (_exportModal!=_document && _host.IsAlive(_exportModal) && !_pages.CloseModal()) return false;
            _exportModal = default;
        }
        _exports?.Dispose(); _exports = null; _exportProblem = ""; return true;
    }
}
#endif
