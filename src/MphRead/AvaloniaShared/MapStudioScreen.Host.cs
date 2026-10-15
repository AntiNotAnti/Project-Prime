#if MPHREAD_AVALONIA && !ANDROID
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;
namespace MphRead.Mods.Launcher.Gui;
internal sealed partial class MapStudioScreen
{
    internal MapDocument? StudioDocument => _document;
    internal event Action? DocumentChanged;
    internal Func<MapProject,CancellationToken,Task>? NativePlaytest;
    internal ICommunityCredentialSource? NativeCommunityCredentials;
    internal Func<string,MapContentIdentity,CancellationToken,Task<MapDefinition>>? NativeInstall;
    internal Func<string,MapContentIdentity,string,CancellationToken,Task>? NativeHost;
    internal Func<MapProject,CancellationToken,Task<MapAuditResult>>? NativeAudit;
    internal Func<string,bool>? NativeCanOpenSource;
    internal Action<string>? NativeRequireSaveTarget;
    internal async Task StudioDrainAsync(CancellationToken cancellation=default)
    {
        _work?.Cancel();
        if(_jobFinished is {} job)await job.Task.WaitAsync(cancellation);
        _autosave.Dispose();
        await _autosave.Completion.WaitAsync(cancellation);
        // Work holds its own cancellation/generation guard. A disposed screen
        // increments its generation before releasing controls and documents.
        cancellation.ThrowIfCancellationRequested();
    }
    internal async Task StudioSaveAsync(string path,CancellationToken cancellation)
    {
        if(_document==null)throw new InvalidOperationException("Open a map first.");
        NativeRequireSaveTarget?.Invoke(path);
        _autosave.Dispose();await _autosave.Completion.WaitAsync(cancellation);
        cancellation.ThrowIfCancellationRequested();
        try { _document.Save(path);_document.DiscardRecovery(CustomRooms.UserMapDirectory);_path.Text=_document.FilePath;Changed(); }
        finally { _autosave=new(); }
    }
    internal async Task StudioRecoveryAsync(CancellationToken cancellation)
    {
        _autosave.Dispose();await _autosave.Completion.WaitAsync(cancellation);
        cancellation.ThrowIfCancellationRequested();
        try
        {
            if(_document?.IsDirty==true)
            {
                using var save=new MapAutosaveService();save.Queue(_document.CaptureAutosave(CustomRooms.UserMapDirectory));
                await save.Completion.WaitAsync(cancellation);
                if(save.Result?.Error is {} error)throw new System.IO.IOException(error);
            }
        }
        finally { _autosave=new(); }
    }
    internal Task StudioValidateAsync()=>Validate();
    internal Task StudioBuildAsync(bool package)=>Build(package);
    internal Task StudioPlaytestAsync()=>Play();
    internal void StudioFrameAll()=>_viewport?.FrameAll();
    internal void StudioToggleWireframe(){if(_viewport!=null){_viewport.Wireframe=!_viewport.Wireframe;_viewport.InvalidateVisual();}}
    internal void StudioShowPanel(string name)=>ShowInspectorPage(name,false);
    internal void StudioFilesDropped(IReadOnlyList<string> files)=>OnFilesDropped(files);
}
#endif
