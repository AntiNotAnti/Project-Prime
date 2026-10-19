#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.IO;
using MphRead.Mods.Launcher.Core;
namespace MphRead.Mods.Launcher.RmlUi.Pages.Theatre;
public sealed partial class TheatrePlaybackPagePresenter
{
    private TheatreEditorContext? _workspaceComparisonReview;
    private string _workspaceComparisonProblem="";
    internal void ReportWorkspaceComparisonFailure(Exception error)
    {
        try { (_editorBackend as ITheatreWorkspaceComparisonPreviewBackend)?.StopComparisonPreview(); }
        catch (Exception stop) when (stop is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
        { _workspaceComparisonProblem = "A/B preview stopped after a render error: " + stop.GetBaseException().Message; }
        _workspaceComparisonProblem = "A/B preview stopped after a render error. " + error.GetBaseException().Message;
        if (IsOpen) Present();
    }
    private bool HandleWorkspaceComparisonControl(string id)
    {
        if(id is not("studio_comparison_preview" or "studio_comparison_stop"))return false;
        if(_editorBackend is not ITheatreWorkspaceComparisonPreviewBackend backend)return true;
        try
        {
            if(id=="studio_comparison_stop")backend.StopComparisonPreview();
            else
            {
                var context=_workspaceComparisonReview??throw new InvalidOperationException("Pause and refresh the replay before comparing pictures.");
                if(FourViews)throw new InvalidOperationException("Turn off Four views before opening A / B preview.");
                backend.BeginComparisonPreview(context,_host.ReadField(_document,"studio_comparison_path"));
            }
            _workspaceComparisonProblem="";
        }
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {_workspaceComparisonProblem=error.Message;}
        return true;
    }
    private void PresentWorkspaceComparisonControls()
    {
        if(_editorBackend is not ITheatreWorkspaceComparisonPreviewBackend backend||_editor==null)return;
        var review=_editor.Snapshot();_workspaceComparisonReview=review.Live.Current&&review.Context.SettledPaused?review.Context:null;
        var state=backend.ReadComparisonPreview(review.Context);
        if(FourViews&&state.Active){backend.StopComparisonPreview();state=backend.ReadComparisonPreview(review.Context);}
        _host.SetText(_document,"studio_comparison_status",String.IsNullOrEmpty(_workspaceComparisonProblem)?state.Status:_workspaceComparisonProblem);
        _host.SetBool(_document,"disabled:studio_comparison_preview",_workspaceComparisonReview==null||FourViews||state.Active);
        _host.SetBool(_document,"disabled:studio_comparison_stop",!state.Active);
    }
    internal object? CurrentComparisonPreviewScene()
        =>_editor!=null&&_editorBackend is ITheatreWorkspaceComparisonPreviewBackend backend
            ?backend.CurrentComparisonPreviewScene(_editor.Snapshot().Context):null;
}
#endif
