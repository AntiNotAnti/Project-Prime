#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.IO;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Theatre;

public sealed partial class TheatrePlaybackPagePresenter
{
    private readonly ITheatreRangeBackend? _rangeBackend;
    private TheatreRangeController? _ranges;
    private RmlUiDocumentToken _rangeModal;
    private string _rangeProblem = "";
    private bool HandleRangeIntent(in RmlUiIntent intent)
    {
        if (!IsOpen || _rangeBackend == null || !Enum.IsDefined((TheatrePlaybackAction)intent.Argument)) return false;
        var action = (TheatrePlaybackAction)intent.Argument;
        bool toolbar = _rangeModal == default && intent.Document == _document
            && action is TheatrePlaybackAction.Ranges or TheatrePlaybackAction.MarkIn or TheatrePlaybackAction.MarkOut;
        bool panel = _rangeModal != default && intent.Document == _rangeModal
            && action is TheatrePlaybackAction.MarkIn or TheatrePlaybackAction.MarkOut
                or TheatrePlaybackAction.RangeSaveHighlight or TheatrePlaybackAction.RangeCreateVirtual
                or TheatrePlaybackAction.RangeRefresh or TheatrePlaybackAction.RangeCancel
                or TheatrePlaybackAction.RangeSaveMaterialized or TheatrePlaybackAction.RangeCancelMaterialized;
        if ((!toolbar && !panel) || !_pages.Accept(intent)) return false;
        try
        {
            _rangeProblem = "";
            _ranges ??= new(_rangeBackend);
            switch (action)
            {
                case TheatrePlaybackAction.Ranges:
                    _ranges.Refresh(); Viewport.Release();
                    _rangeModal = _pages.OpenModal(new("replay-ranges", "pages/theatre/ranges.rml", "replay_range_name"));
                    _host.SetField(_rangeModal, "replay_range_name", ""); break;
                case TheatrePlaybackAction.MarkIn:
                    if (!_ranges.MarkIn()) _rangeProblem = _ranges.Snapshot().Error;
                    break;
                case TheatrePlaybackAction.MarkOut:
                    if (!_ranges.MarkOut()) _rangeProblem = _ranges.Snapshot().Error;
                    break;
                case TheatrePlaybackAction.RangeRefresh: _ranges.Refresh(); break;
                case TheatrePlaybackAction.RangeSaveHighlight:
                    if (_ranges.SaveHighlight(_host.ReadField(_rangeModal, "replay_range_name")))
                        _host.SetField(_rangeModal, "replay_range_name", ""); break;
                case TheatrePlaybackAction.RangeCreateVirtual:
                    if (_ranges.CreateVirtualClip(_host.ReadField(_rangeModal, "replay_range_name")))
                        _host.SetField(_rangeModal, "replay_range_name", ""); break;
                case TheatrePlaybackAction.RangeSaveMaterialized:
                    _ranges.SaveMaterializedClip(_host.ReadField(_rangeModal, "replay_range_name")); break;
                case TheatrePlaybackAction.RangeCancelMaterialized: _ranges.CancelMaterializedClip(); break;
                case TheatrePlaybackAction.RangeCancel: CloseRanges(); break;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { _rangeProblem = error.Message; }
        PresentRanges(); return true;
    }
    private void PresentRanges()
    {
        if (_rangeBackend == null) return;
        if (_rangeModal != default && !_host.IsAlive(_rangeModal))
            _rangeModal = default;
        var state = _ranges?.Snapshot();
        string start = state?.MarkIn is uint first ? Time(first) + $" ({first})" : "NOT SET";
        string end = state?.MarkOut is uint last ? Time(last) + $" ({last})" : "NOT SET";
        string summary = "IN " + start + " // OUT " + end;
        var live = state?.LiveSelection;
        _host.SetText(_document, "replay_range_summary", live is { } actual
            ? "IN " + (actual.MarkIn is uint liveStart ? Time(liveStart) : "NOT SET")
                + " // OUT " + (actual.MarkOut is uint liveEnd ? Time(liveEnd) : "NOT SET")
            : "Open Range Actions to review the replay marks.");
        if (_rangeModal == default || state == null) return;
        if (_ranges != null && _ranges.TryTakeMaterializedClipSaved(out string savedName)
            && _host.ReadField(_rangeModal, "replay_range_name").Trim() == savedName)
            _host.SetField(_rangeModal, "replay_range_name", "");
        _host.SetText(_rangeModal, "replay_range_replay", state.ReplayName);
        _host.SetText(_rangeModal, "replay_range_selection", "REVIEWED " + summary + " // REPLAY " + Time(state.Duration));
        _host.SetText(_rangeModal, "replay_range_status", state.Status);
        _host.SetText(_rangeModal, "replay_range_error", _rangeProblem.Length > 0 ? _rangeProblem : state.Error);
        foreach (string id in new[] { "mark_in", "mark_out", "highlight", "virtual", "materialized" })
            _host.SetBool(_rangeModal, "disabled:replay_range_" + id, !state.Current || state.SavingClip
                || (id is "highlight" or "virtual" or "materialized") && !state.ValidRange
                || id == "materialized" && !state.MaterializedClipsAvailable);
        _host.SetBool(_rangeModal, "disabled:replay_range_cancel_save", !state.SavingClip);
        _host.SetBool(_rangeModal, "disabled:replay_range_refresh", state.SavingClip);
        _host.SetBool(_rangeModal, "disabled:replay_range_name", !state.Current || state.SavingClip);
        // Refresh is the explicit way to review changed marks or sidecars; it never rebinds another owner.
    }
    private bool BackRanges()
    {if(WorkspaceMode&&!_workspaceClosing&&true)return false;
        if (_rangeModal == default) return false;
        CloseRanges(); return true;
    }
    private bool CloseRanges(bool retire = false)
    {
        if(WorkspaceMode&&!_workspaceClosing){SelectWorkspaceTab("camera");return true;}
        if (_rangeModal != default)
        {
            if (_rangeModal!=_document && _host.IsAlive(_rangeModal) && !_pages.CloseModal()) return false;
            _rangeModal = default;
            _ranges?.CancelMaterializedClip();
        }
        if (retire) { _ranges?.Dispose(); _ranges = null; }
        _rangeProblem = ""; return true;
    }
}
#endif
