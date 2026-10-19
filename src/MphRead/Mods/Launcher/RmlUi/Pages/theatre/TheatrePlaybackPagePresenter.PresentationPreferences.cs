#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
namespace MphRead.Mods.Launcher.RmlUi.Pages.Theatre;
public sealed partial class TheatrePlaybackPagePresenter
{
    private bool _studioSaveViewRequest;
    public void RequestStudioSaveView()
    {
        if(WorkspaceMode&&!_workspaceClosing&&_pages.Top==_document)_studioSaveViewRequest=true;
    }
    public bool TryTakeStudioSaveViewRequest()
    {bool request=_studioSaveViewRequest;_studioSaveViewRequest=false;return request;}
    internal void ReportStudioPresentationPreferences(string status)
    {
        if(!WorkspaceMode||_workspaceClosing)return;
        _workspaceProblem=status;
        _host.SetText(_document,"studio_notice",status);
    }
}
#endif
