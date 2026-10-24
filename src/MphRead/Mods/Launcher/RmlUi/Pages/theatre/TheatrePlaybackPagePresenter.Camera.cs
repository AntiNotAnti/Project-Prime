#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Globalization;
using System.IO;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Theatre;

public sealed partial class TheatrePlaybackPagePresenter
{
    private readonly ITheatreCameraBackend? _cameraBackend;
    private TheatreCameraController? _cameraEditor;
    private RmlUiDocumentToken _cameraModal, _cameraConfirmation;
    private string _cameraProblem = "";
    private bool HandleCameraIntent(in RmlUiIntent intent)
    {
        if (!IsOpen || _cameraBackend == null || !Enum.IsDefined((TheatrePlaybackAction)intent.Argument)) return false;
        var action = (TheatrePlaybackAction)intent.Argument;
        bool opening = action == TheatrePlaybackAction.CameraEditor && _cameraModal == default && intent.Document == _document;
        bool confirming = _cameraConfirmation != default && intent.Document == _cameraConfirmation
            && action is TheatrePlaybackAction.CameraConfirm or TheatrePlaybackAction.CameraCancelConfirm;
        bool editing = _cameraConfirmation == default && _cameraModal != default && intent.Document == _cameraModal
            && intent.Argument >= (int)TheatrePlaybackAction.CameraRefresh && intent.Argument <= (int)TheatrePlaybackAction.CameraTrack
            && action is not (TheatrePlaybackAction.CameraConfirm or TheatrePlaybackAction.CameraCancelConfirm);
        if ((!opening && !confirming && !editing) || !_pages.Accept(intent)) return false;
        try
        {
            _cameraProblem = "";
            if (opening)
            {
                Viewport.Release(); _cameraEditor?.Dispose();
                _cameraEditor = new(_cameraBackend);
                _cameraModal = _pages.OpenModal(new("replay-camera", "pages/theatre/camera.rml", "replay_camera_refresh"));
            }
            else if (_cameraEditor != null) switch (action)
            {
                case TheatrePlaybackAction.CameraRefresh: _cameraEditor.Refresh(); break;
                case TheatrePlaybackAction.CameraFree: _cameraEditor.FreeCamera(); break;
                case TheatrePlaybackAction.CameraFovLess: _cameraEditor.Lens(-5, 0); break;
                case TheatrePlaybackAction.CameraFovMore: _cameraEditor.Lens(5, 0); break;
                case TheatrePlaybackAction.CameraRollLess: _cameraEditor.Lens(0, -5); break;
                case TheatrePlaybackAction.CameraRollMore: _cameraEditor.Lens(0, 5); break;
                case TheatrePlaybackAction.CameraPrevious: _cameraEditor.Move(-1); break;
                case TheatrePlaybackAction.CameraNext: _cameraEditor.Move(1); break;
                case TheatrePlaybackAction.CameraPreview: _cameraEditor.Preview(); break;
                case TheatrePlaybackAction.CameraTrack: _cameraEditor.ToggleTrack(); break;
                case TheatrePlaybackAction.CameraAdd: if (_cameraEditor.Add()) OpenCameraConfirmation(); break;
                case TheatrePlaybackAction.CameraUpdate: if (_cameraEditor.RequestUpdate()) OpenCameraConfirmation(); break;
                case TheatrePlaybackAction.CameraRemove: if (_cameraEditor.RequestRemove()) OpenCameraConfirmation(); break;
                case TheatrePlaybackAction.CameraConfirm:
                    if (_cameraEditor.Confirm() && _pages.CloseModal()) _cameraConfirmation = default;
                    break;
                case TheatrePlaybackAction.CameraCancelConfirm: BackCamera(); break;
                case TheatrePlaybackAction.CameraClose: CloseCamera(); break;
            }
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { _cameraProblem = error.Message; }
        PresentCamera(); return true;
    }
    private void OpenCameraConfirmation()
    {
        if (_cameraEditor?.Snapshot().Pending == null) return;
        _cameraConfirmation = _pages.OpenModal(new("replay-camera-confirm", "pages/theatre/camera-confirm.rml", "replay_camera_confirm_cancel"));
    }
    private static string CameraPose(TheatreCameraPose pose)
        => string.Create(CultureInfo.InvariantCulture, $"Position {pose.X:0.##}, {pose.Y:0.##}, {pose.Z:0.##} // FOV {pose.Fov:0.#}° // roll {pose.Roll:0.#}°");
    private void PresentCamera()
    {
        if (_cameraConfirmation != default && !_host.IsAlive(_cameraConfirmation))
        { _cameraConfirmation = default; _cameraEditor?.Cancel(); }
        if (_cameraModal != default && !_host.IsAlive(_cameraModal))
        { _cameraModal = default; _cameraEditor?.Dispose(); _cameraEditor = null; }
        if (_cameraModal == default || _cameraEditor == null) return;
        var state = _cameraEditor.Snapshot();
        _host.SetText(_cameraModal, "replay_camera_replay", Path.GetFileName(state.Context.LogicalPath));
        _host.SetText(_cameraModal, "replay_camera_review", $"REVIEWED FRAME {state.Context.Frame} // " + CameraPose(state.Context.Pose));
        _host.SetText(_cameraModal, "replay_camera_count", state.Context.Keys.Length == 0 ? "NO CAMERA KEYFRAMES"
            : $"KEY {state.SelectedIndex + 1} / {state.Context.Keys.Length} (MAXIMUM 1024)");
        _host.SetText(_cameraModal, "replay_camera_selected", state.Selected is { } key
            ? $"FRAME {key.Frame} // " + Time(key.Frame) + " // " + CameraPose(key.Pose) : "Add the reviewed camera pose at the playhead.");
        _host.SetText(_cameraModal, "replay_camera_status", state.Status);
        _host.SetText(_cameraModal, "replay_camera_error", _cameraProblem.Length > 0 ? _cameraProblem : state.Error.Length > 0 ? state.Error
            : !state.Current ? "The camera review is stale. Pause playback, cancel any confirmation and refresh." : "");
        foreach (string id in new[] { "free", "fov_less", "fov_more", "roll_less", "roll_more", "add", "track" })
            _host.SetBool(_cameraModal, "disabled:replay_camera_" + id, !state.Current);
        foreach (string id in new[] { "previous", "next", "preview", "update", "remove" })
            _host.SetBool(_cameraModal, "disabled:replay_camera_" + id, !state.Current || state.Selected == null);
        _host.SetBool(_cameraModal, "disabled:replay_camera_tools_open", !state.Current || state.Pending != null || _cameraBackend is not ITheatreCameraToolsBackend);
        _host.SetText(_cameraModal, "replay_camera_track", state.Context.TrackPlaying ? "CAMERA TRACK: ON" : "CAMERA TRACK: OFF");
        if (_cameraConfirmation == default) return;
        _host.SetText(_cameraConfirmation, "replay_camera_confirm_title", state.Pending?.Change == TheatreCameraChange.Remove ? "REMOVE THIS CAMERA KEYFRAME?" : "REPLACE THIS CAMERA KEYFRAME?");
        _host.SetText(_cameraConfirmation, "replay_camera_confirm_replay", Path.GetFileName(state.Context.LogicalPath));
        string details = "No camera edit is pending.";
        if (state.Pending is { } exact)
        {
            details = $"FRAME {exact.Key.Frame} // " + Time(exact.Key.Frame) + "\n";
            foreach (var existing in exact.Context.Keys)
                if (existing.Frame == exact.Key.Frame) { details += "CURRENT " + CameraPose(existing.Pose) + "\n"; break; }
            if (exact.Change != TheatreCameraChange.Remove) details += "REPLACEMENT " + CameraPose(exact.Key.Pose);
        }
        _host.SetText(_cameraConfirmation, "replay_camera_confirm_exact", details);
        _host.SetText(_cameraConfirmation, "replay_camera_confirm_error", _cameraProblem.Length > 0 ? _cameraProblem : state.Error);
        _host.SetBool(_cameraConfirmation, "disabled:replay_camera_confirm_apply", !state.Current || state.Pending == null);
    }
    private bool BackCamera()
    {if(WorkspaceMode&&!_workspaceClosing&&_cameraConfirmation==default)return false;
        if (_cameraConfirmation != default)
        {
            if (!_pages.CloseModal()) return true;
            _cameraConfirmation = default; _cameraEditor?.Cancel(); PresentCamera(); return true;
        }
        if (_cameraModal == default) return false;
        CloseCamera(); return true;
    }
    private bool CloseCamera()
    {
        if(WorkspaceMode&&!_workspaceClosing){SelectWorkspaceTab("camera");return true;}
        if (_cameraConfirmation != default)
        {
            if (_host.IsAlive(_cameraConfirmation) && !_pages.CloseModal()) return false;
            _cameraConfirmation = default;
        }
        if (_cameraModal != default)
        {
            if (_cameraModal!=_document && _host.IsAlive(_cameraModal) && !_pages.CloseModal()) return false;
            _cameraModal = default;
        }
        _cameraEditor?.Dispose(); _cameraEditor = null; _cameraProblem = ""; return true;
    }
}
#endif
