#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.IO;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
namespace MphRead.Mods.Launcher.RmlUi.Pages.Theatre;
internal sealed record StudioWaveFileRequest(long Sequence,StudioAudioFileTarget Target,RmlUiDocumentToken Document,
    TheatreEditorContext Source,long SourceEpoch,long FieldRevision,string OriginalValue);
public sealed partial class TheatrePlaybackPagePresenter
{
    private StudioWaveFileRequest? _wavePending,_waveActive;
    private TheatreEditorContext? _waveSource;
    private long _waveSequence,_waveSourceEpoch,_waveMusicRevision,_waveCueRevision;
    private string? _waveMusicObserved,_waveCueObserved;
    private bool WaveFilesForeground=>WorkspaceMode&&IsOpen&&!_workspaceClosing&&_workspaceTab=="audio"
        &&_studioToolsModal==_document&&_pages.Top==_document&&_host.CurrentInputDocument==_document
        &&_editorBackend is ITheatreStudioAudioSettingsBackend&&_editorBackend is ITheatreStudioToolsBackend;
    private static string WaveField(StudioAudioFileTarget target)=>target==StudioAudioFileTarget.MusicWave?"replay_studio_music_file":"replay_studio_cue_wave";
    private void ObserveWaveFields()
    {
        if(!WaveFilesForeground||_editor==null)return;
        var source=_editor.Snapshot().Context;
        if(_waveSource==null||!ReferenceEquals(_waveSource.Owner,source.Owner)||_waveSource.SceneGeneration!=source.SceneGeneration
            ||_waveSource.LogicalPath!=source.LogicalPath||_waveSource.SourceRevision!=source.SourceRevision)
        {_waveSource=source;_waveSourceEpoch++;}
        foreach(var target in new[]{StudioAudioFileTarget.MusicWave,StudioAudioFileTarget.CustomCueWave})
        {
            string value=_host.ReadField(_document,WaveField(target));
            if(target==StudioAudioFileTarget.MusicWave)
            {if(value!=_waveMusicObserved){_waveMusicObserved=value;_waveMusicRevision++;}}
            else if(value!=_waveCueObserved){_waveCueObserved=value;_waveCueRevision++;}
        }
    }
    private bool HandleWorkspaceWaveChooser(string id)
    {
        if(id is not ("studio_music_choose" or "studio_cue_choose"))return false;
        if(!WaveFilesForeground||_wavePending!=null||_waveActive!=null)return true;
        ObserveWaveFields();var source=_editor!.Snapshot().Context;
        ((ITheatreStudioToolsBackend)_editorBackend!).ValidateStudioTool(source);
        var target=id=="studio_music_choose"?StudioAudioFileTarget.MusicWave:StudioAudioFileTarget.CustomCueWave;
        _wavePending=new(++_waveSequence,target,_document,source,_waveSourceEpoch,
            target==StudioAudioFileTarget.MusicWave?_waveMusicRevision:_waveCueRevision,_host.ReadField(_document,WaveField(target)));
        return true;
    }
    internal bool TryTakeStudioWaveRequest(out StudioWaveFileRequest? request)
    {
        request=null;
        if(_wavePending is not {} pending)return false;
        _wavePending=null;
        if(!StudioWaveRequestCurrent(pending))return false;
        _waveActive=request=pending;return true;
    }
    internal bool StudioWaveRequestCurrent(StudioWaveFileRequest request)
    {
        if(!WaveFilesForeground||request.Document!=_document)return false;
        ObserveWaveFields();
        return request.SourceEpoch==_waveSourceEpoch&&_editorBackend!.ReadLive(request.Source).Current
            &&request.FieldRevision==(request.Target==StudioAudioFileTarget.MusicWave?_waveMusicRevision:_waveCueRevision)
            &&_host.ReadField(_document,WaveField(request.Target))==request.OriginalValue;
    }
    internal bool CompleteStudioWaveRequest(StudioWaveFileRequest request,string? selected,string? error)
    {
        if(_waveActive?.Sequence!=request.Sequence)return false;
        _waveActive=null;
        if(!StudioWaveRequestCurrent(request))return false;
        try
        {
            ((ITheatreStudioToolsBackend)_editorBackend!).ValidateStudioTool(request.Source);
            if(error!=null){_workspaceProblem=error;return true;}
            if(selected==null){_workspaceProblem="WAV selection canceled. Audio draft unchanged.";return true;}
            if(!Path.IsPathFullyQualified(selected)||selected.Length>4096||!selected.EndsWith(".wav",StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Choose an absolute WAV path.");
            _host.SetField(_document,WaveField(request.Target),selected);ObserveWaveFields();
            _workspaceProblem="WAV selected in the audio draft. Save audio settings or apply the cue when ready.";
            return true;
        }
        catch(Exception problem)when(problem is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {_workspaceProblem=problem.Message;return true;}
    }
    private void TickWorkspaceWaveFields()
    {
        if(!WaveFilesForeground)return;ObserveWaveFields();
        bool busy=_wavePending!=null||_waveActive!=null;
        _host.SetBool(_document,"disabled:studio_music_choose",busy);
        _host.SetBool(_document,"disabled:studio_cue_choose",busy);
    }
    internal void CancelStudioWaveRequests(){_wavePending=_waveActive=null;}
}
#endif
