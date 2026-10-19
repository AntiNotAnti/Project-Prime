#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Globalization;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Theatre;
public sealed partial class TheatrePlaybackPagePresenter
{
    private RmlUiDocumentToken _spatialModal;
    private TheatreEditorContext? _spatialContext;
    private TheatreSpatialAnalysis? _spatialAnalysis;
    private TheatreSpatialGrid? _spatialGrid;
    private (TheatreSpatialAnalysis? Analysis,TheatreSpatialKind Kind,int Player,bool Marked)? _spatialProjectionKey;
    private Task<TheatreSpatialAnalysis>? _spatialTask;
    private CancellationTokenSource? _spatialCancellation;
    private TheatreSpatialKind _spatialKind;
    private int _spatialPlayer=-1;
    private bool _spatialMarked,_spatialWorld;
    private ImmutableArray<TheatreSpatialSample> _spatialFiltered=ImmutableArray<TheatreSpatialSample>.Empty;
    internal ImmutableArray<TheatreSpatialSample> SpatialWorldMarkers()
    {
        if(!_spatialWorld||_spatialAnalysis==null||_spatialContext==null||_editorBackend is not ITheatreSpatialBackend backend)return ImmutableArray<TheatreSpatialSample>.Empty;
        try{backend.ValidateSpatial(_spatialContext);return TheatreSpatialProjection.WorldMarkers(_spatialFiltered);}
        catch(Exception error)when(error is IOException or InvalidOperationException or UnauthorizedAccessException or ArgumentException){return ImmutableArray<TheatreSpatialSample>.Empty;}
    }
    private string _spatialStatus="Choose Analyze to read recorded positions. Playback remains available after closing this panel.",_spatialError="";
    private bool HandleSpatialIntent(in RmlUiIntent intent)
    {
        if(!IsOpen||_editorBackend is not ITheatreSpatialBackend backend||_editor==null
            ||!Enum.IsDefined((TheatrePlaybackAction)intent.Argument))return false;
        var action=(TheatrePlaybackAction)intent.Argument;
        bool opening=action==TheatrePlaybackAction.SpatialOpen&&_spatialModal==default&&intent.Document==_editorModal;
        bool editing=_spatialModal!=default&&intent.Document==_spatialModal&&_pages.Top==_spatialModal
            &&(action>=TheatrePlaybackAction.SpatialAnalyze&&action<=TheatrePlaybackAction.SpatialClose||action==TheatrePlaybackAction.SpatialWorld);
        if((!opening&&!editing)||!_pages.Accept(intent))return false;
        try
        {
            _spatialError="";
            if(opening)
            {
                _spatialContext=_editor.Snapshot().Context;backend.ValidateSpatial(_spatialContext);
                _spatialAnalysis=null;_spatialKind=TheatreSpatialKind.Movement;_spatialPlayer=-1;_spatialMarked=false;_spatialWorld=false;
                _spatialStatus="Choose Analyze to read recorded positions. This does not seek or step the active replay.";
                _spatialModal=_pages.OpenModal(new("replay-spatial","pages/theatre/spatial.rml","replay_spatial_analyze"));
            }
            else if(action==TheatrePlaybackAction.SpatialClose)CloseSpatial();
            else if(action==TheatrePlaybackAction.SpatialCancel){CancelSpatialWork();_spatialStatus="Analysis cancelled.";}
            else if(_spatialContext is{} context)
            {
                backend.ValidateSpatial(context);
                switch(action)
                {
                    case TheatrePlaybackAction.SpatialAnalyze:
                        CancelSpatialWork();_spatialAnalysis=null;
                        _spatialCancellation=new();_spatialTask=backend.AnalyzeSpatial(context,_spatialCancellation.Token);
                        _spatialStatus="Reading recorded positions on a detached worker...";break;
                    case TheatrePlaybackAction.SpatialWorld:_spatialWorld=!_spatialWorld;break;
                    case TheatrePlaybackAction.SpatialKind:_spatialKind=(TheatreSpatialKind)(((int)_spatialKind+1)%Enum.GetValues<TheatreSpatialKind>().Length);break;
                    case TheatrePlaybackAction.SpatialPlayer:
                        var slots=context.Players.Select(p=>p.Slot).Prepend(-1).ToArray();_spatialPlayer=slots[(Array.IndexOf(slots,_spatialPlayer)+1)%slots.Length];break;
                    case TheatrePlaybackAction.SpatialRange:
                        if(context.MarkIn is not uint start||context.MarkOut is not uint end||start>=end||end>context.Duration)
                            throw new InvalidOperationException("Set valid In/Out marks, close this panel and refresh the editor before filtering to a range.");
                        _spatialMarked=!_spatialMarked;break;
                }
            }
        }
        catch(Exception error)when(error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or OperationCanceledException)
        {CancelSpatialWork();_spatialAnalysis=null;_spatialError=error.Message;}
        PresentSpatial();return true;
    }
    private void PresentSpatial()
    {
        if(_spatialModal==default)return;
        if(!_host.IsAlive(_spatialModal)){CancelSpatialWork();_spatialWorld=false;_spatialFiltered=ImmutableArray<TheatreSpatialSample>.Empty;_spatialModal=default;_spatialContext=null;_spatialAnalysis=null;return;}
        if(_spatialContext is not{} context||_editorBackend is not ITheatreSpatialBackend backend)return;
        if(_editor==null||!_editor.Snapshot().Live.Current)
        {CancelSpatialWork();_spatialAnalysis=null;_spatialError="The replay owner changed. Close this panel.";}
        if(_spatialTask is{IsCompleted:true} completed)
        {
            _spatialTask=null;
            try
            {
                var result=completed.GetAwaiter().GetResult();backend.ValidateSpatial(context);
                if(result.Duration!=context.Duration)throw new InvalidDataException("Replay duration changed during analysis.");
                _spatialAnalysis=result;_spatialStatus=$"{result.Samples.Length:N0} recorded samples. Position bounds remain fixed across filters.";
            }
            catch(Exception error)when(error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or OperationCanceledException)
            {_spatialAnalysis=null;_spatialError=error is OperationCanceledException?"Analysis cancelled.":error.Message;}
            _spatialCancellation?.Dispose();_spatialCancellation=null;
        }
        _host.SetText(_spatialModal,"replay_spatial_source",Path.GetFileName(context.LogicalPath));
        _host.SetText(_spatialModal,"replay_spatial_kind","KIND: "+_spatialKind.ToString().ToUpperInvariant());
        _host.SetText(_spatialModal,"replay_spatial_player","PLAYER: "+(_spatialPlayer<0?"ALL":context.Players.FirstOrDefault(p=>p.Slot==_spatialPlayer)?.Name??_spatialPlayer.ToString(CultureInfo.InvariantCulture)));
        _host.SetText(_spatialModal,"replay_spatial_range",_spatialMarked?$"REVIEWED FRAMES {context.MarkIn}–{context.MarkOut}":"FULL REPLAY");
        _host.SetText(_spatialModal,"replay_spatial_status",_spatialStatus);
        _host.SetText(_spatialModal,"replay_spatial_error",_spatialError);
        _host.SetBool(_spatialModal,"disabled:replay_spatial_analyze",_spatialTask!=null||_spatialError.Length>0);
        _host.SetBool(_spatialModal,"disabled:replay_spatial_cancel",_spatialTask==null);
        _host.SetText(_spatialModal,"replay_spatial_world",_spatialWorld?"WORLD MARKERS: ON":"WORLD MARKERS: OFF");
        foreach(string id in new[]{"kind","player","range","world"})_host.SetBool(_spatialModal,"disabled:replay_spatial_"+id,_spatialAnalysis==null);
        var projectionKey=(_spatialAnalysis,_spatialKind,_spatialPlayer,_spatialMarked);
        bool projectionChanged=_spatialProjectionKey!=projectionKey;
        if(projectionChanged)
        {
            _spatialProjectionKey=projectionKey;
            _spatialFiltered=_spatialAnalysis==null?ImmutableArray<TheatreSpatialSample>.Empty:TheatreSpatialProjection.Filter(_spatialAnalysis,_spatialKind,_spatialPlayer,_spatialMarked?context.MarkIn!.Value:0,_spatialMarked?context.MarkOut!.Value:context.Duration);
            _spatialGrid=_spatialAnalysis==null?null:TheatreSpatialProjection.Project(_spatialAnalysis,_spatialKind,_spatialPlayer,
                _spatialMarked?context.MarkIn!.Value:0,_spatialMarked?context.MarkOut!.Value:context.Duration);
        }
        var grid=_spatialGrid;
        _host.SetText(_spatialModal,"replay_spatial_bounds",grid==null?"No analysis published.":$"{grid.Samples:N0} MATCHING SAMPLES // WEIGHT {grid.Weight:0.##} // X {grid.MinX:0.##}…{grid.MaxX:0.##} // Z {grid.MinZ:0.##}…{grid.MaxZ:0.##}");
        if(projectionChanged)
        {
        // Four dots per recorded vector keep native layout bounded while preserving its exact direction.
        var rays=_spatialFiltered.Where(p=>p.Direction is{} d&&float.IsFinite(d.X)&&float.IsFinite(d.Z)).Take(300).ToArray();
        float span=grid==null?1:Math.Max(1,Math.Max(grid.MaxX-grid.MinX,grid.MaxZ-grid.MinZ));
        for(int i=0;i<1200;i++)
        {
            string id="replay_spatial_ray_"+i;int ray=i/4;bool visible=grid!=null&&ray<rays.Length;
            if(visible)
            {
                var sample=rays[ray];var direction=sample.Direction!;float along=(i%4)/3f*10;
                float x=(sample.X+direction.X*along-grid!.MinX)/span*254;
                float y=(sample.Z+direction.Z*along-grid.MinZ)/span*254;
                visible=x>=0&&x<=254&&y>=0&&y<=254;
                if(visible)_host.SetText(_spatialModal,"rect:"+id,Rect(x,y,2,2));
            }
            _host.SetBool(_spatialModal,"visible:"+id,visible);
        }
        }
        for(int i=0;i<TheatreSpatialProjection.Size*TheatreSpatialProjection.Size;i++)
            for(int level=0;level<=4;level++)_host.SetBool(_spatialModal,$"class:replay_heat_{i}:heat_{level}",(grid?.Levels[i]??0)==level);
    }
    private void CancelSpatialWork()
    {
        _spatialCancellation?.Cancel();_spatialCancellation?.Dispose();_spatialCancellation=null;
        if(_spatialTask is{} retired)_=retired.ContinueWith(t=>{_ = t.Exception;},CancellationToken.None,TaskContinuationOptions.OnlyOnFaulted,TaskScheduler.Default);
        _spatialTask=null;
    }
    private bool BackSpatial(){if(WorkspaceMode&&!_workspaceClosing&&true)return false;if(_spatialModal==default)return false;CloseSpatial();return true;}
    private bool CloseSpatial()
    {
        if(WorkspaceMode&&!_workspaceClosing){SelectWorkspaceTab("camera");return true;}
        if(_spatialModal!=default&&_spatialModal!=_document&&_host.IsAlive(_spatialModal)&&!_pages.CloseModal())return false;
        CancelSpatialWork();_spatialWorld=false;_spatialFiltered=ImmutableArray<TheatreSpatialSample>.Empty;_spatialModal=default;_spatialContext=null;_spatialAnalysis=null;_spatialError="";return true;
    }
}
#endif
