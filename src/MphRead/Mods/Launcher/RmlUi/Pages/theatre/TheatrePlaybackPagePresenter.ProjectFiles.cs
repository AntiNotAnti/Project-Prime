#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher.Core;
namespace MphRead.Mods.Launcher.RmlUi.Pages.Theatre;
public sealed partial class TheatrePlaybackPagePresenter
{
    private Task<string?>? _projectPicker;
    private Task<ITheatreProjectSave>? _projectSave;
    private CancellationTokenSource? _projectCancellation;
    private TheatreEditorContext? _projectContext;
    private bool _projectPickingOpen;
    private string? _projectOpenRequest;
    public bool TryTakeStudioOpenRequest(out string path)
    {
        path="";
        if(_projectOpenRequest==null)return false;
        if(!ProjectFilesForeground||_projectContext==null||_editorBackend?.ReadLive(_projectContext).Current!=true)
        {_projectOpenRequest=null;return false;}
        path=_projectOpenRequest;_projectOpenRequest=null;return true;
    }
    private bool ProjectFilesForeground=>WorkspaceMode&&IsOpen&&!_workspaceClosing&&_pages.Top==_document&&_host.CurrentInputDocument==_document;
    private bool HandleWorkspaceProjectControl(string id)
    {
        if(id is not ("studio_open" or "studio_save_as"))return false;
        if(!ProjectFilesForeground||_projectPicker!=null||_projectSave!=null||_projectOpenRequest!=null)return true;
        if(_editor?.Snapshot().Pending!=null)throw new InvalidOperationException("Apply or cancel the reviewed edit before opening or saving a project.");
        _projectContext=_editor?.Snapshot().Context??throw new InvalidOperationException("Open and review a recording first.");
        if(_editorBackend?.ReadLive(_projectContext).Current!=true)throw new InvalidOperationException("The replay owner changed. Reopen the workspace.");
        _projectPickingOpen=id=="studio_open";
        _projectCancellation=new();
        _projectPicker=_projectPickingOpen?NativeReplayProjectPicker.Open(_projectCancellation.Token):NativeReplayProjectPicker.SaveAs(_projectCancellation.Token);
        _workspaceProblem=_projectPickingOpen?"Choose a .ppdemo recording or .ppclip project. The current recording remains open until the selection is accepted.":"Choose a new .ppclip filename. Save As preserves the recording and rebases reviewed camera, markers and reel.";
        return true;
    }
    private void TickWorkspaceProjectFiles()
    {
        if(!ProjectFilesForeground)return;
        _host.SetBool(_document,"disabled:studio_open",_projectPicker!=null||_projectSave!=null||_projectOpenRequest!=null);
        _host.SetBool(_document,"disabled:studio_save_as",_projectPicker!=null||_projectSave!=null||_projectOpenRequest!=null||_editorBackend is not ITheatreProjectFilesBackend);
        try
        {
            if(_projectPicker is {IsCompleted:true} picker)
            {
                _projectPicker=null;string? path=picker.GetAwaiter().GetResult();
                if(String.IsNullOrWhiteSpace(path)){_workspaceProblem="Project dialog canceled. Recording unchanged.";ClearProjectCancellation();return;}
                if(_projectContext==null||_editorBackend?.ReadLive(_projectContext).Current!=true)throw new InvalidOperationException("The original replay owner changed while the file dialog was open.");
                if(!Path.IsPathFullyQualified(path)||path.Length>4096||path.Contains('\0'))throw new ArgumentException("Choose an absolute project file path.");
                path=Path.GetFullPath(path);
                if(_projectPickingOpen)
                {
                    if(!path.EndsWith(".ppclip",StringComparison.OrdinalIgnoreCase)&&!path.EndsWith(".ppdemo",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Choose a .ppdemo recording or .ppclip project.");
                    _projectOpenRequest=path;_workspaceProblem="Project selected. Validating its source before opening.";ClearProjectCancellation();
                }
                else
                {
                    var backend=_editorBackend as ITheatreProjectFilesBackend??throw new InvalidOperationException("This replay owner cannot save projects.");
                    _projectSave=backend.PrepareProjectSaveAs(_projectContext,path,_projectCancellation!.Token);
                    _workspaceProblem="Preparing reviewed clip project and sidecars...";
                }
            }
            if(_projectSave is {IsCompleted:true} save)
            {
                _projectSave=null;using var prepared=save.GetAwaiter().GetResult();
                string saved=((ITheatreProjectFilesBackend)_editorBackend!).PublishProjectSaveAs(_projectContext!,prepared);
                // Adopt through the same checked library/source-lease path as Open.
                _projectOpenRequest=saved;_workspaceProblem="Saved project: "+saved;ClearProjectCancellation();
            }
        }
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or OperationCanceledException)
        {_workspaceProblem=error is OperationCanceledException?"Project operation canceled. Recording unchanged.":error.Message;_projectPicker=null;_projectSave=null;ClearProjectCancellation();}
        _host.SetText(_document,"studio_notice",_workspaceProblem);
    }
    private void ClearProjectCancellation(){_projectCancellation?.Dispose();_projectCancellation=null;}
    private void CloseWorkspaceProjectFiles()
    {
        _projectCancellation?.Cancel();ClearProjectCancellation();_projectPicker=null;_projectOpenRequest=null;_projectContext=null;
        if(_projectSave is {} pending)
        {
            _projectSave=null;
            _=pending.ContinueWith(completed=>{if(completed.IsCompletedSuccessfully)completed.Result.Dispose();else _=completed.Exception;},TaskScheduler.Default);
        }
    }
}
#endif
