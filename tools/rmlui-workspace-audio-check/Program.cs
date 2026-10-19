// Production SelectWorkspaceTab is source-linked by prepare-source.py, not duplicated here.
int checks=0;
void Check(bool value,string label){if(!value)throw new InvalidOperationException(label);checks++;Console.WriteLine("PASS "+label);}
var backend=new AudioBackend();var host=new Host();var page=new WorkspaceAudioHarness(host,backend);
page.Tab("audio");
Check(page.Problem.Length>0&&backend.Validations==1&&page.CueOpens==0,"first stale Audio context fails before cue initialization");
Check(page.AudioModal==0&&host.Fields.Count==0,"failed Audio initialization does not publish an initialized modal or reset draft fields");
backend.Current=true;page.Tab("audio");
Check(backend.Validations==2&&page.CueOpens==1&&page.AudioModal==1,"valid current owner can retry the same Audio tab and initialize exactly once");
var cueFailure=new WorkspaceAudioHarness(new Host(),backend){FailCue=true};cueFailure.Tab("audio");
Check(cueFailure.AudioModal==0&&cueFailure.Problem.Length>0,"failed cue/settings initialization also leaves Audio entry retryable");
cueFailure.FailCue=false;cueFailure.Tab("audio");
Check(cueFailure.AudioModal==1&&cueFailure.CueOpens==2,"cue initialization retries once after its original read failure clears");
host.SetField(1,"replay_studio_game","0.375");host.SetField(1,"replay_studio_music","0.125");
page.Tab("camera");page.Tab("audio");
Check(page.CueOpens==1&&backend.Validations==4&&host.Fields["replay_studio_game"]=="0.375"&&host.Fields["replay_studio_music"]=="0.125",
    "normal tab return retains reviewed cue state and unsaved bus drafts instead of reopening or resetting");
Check(page.Presents==4&&page.Releases==4,"every tab transition presents through the actual selector and releases stale input");
Console.WriteLine($"PASS {checks} actual source-linked Audio initialization retry/draft checks");

internal interface ITheatreStudioToolsBackend {void ValidateStudioTool(object context);}
internal interface ITheatreSpatialBackend {void ValidateSpatial(object context);}
internal sealed class AudioBackend:ITheatreStudioToolsBackend
{internal bool Current;internal int Validations;public void ValidateStudioTool(object context){Validations++;if(!Current)throw new InvalidOperationException("Synthetic stale owner");}}
internal sealed class Host
{internal readonly Dictionary<string,string> Fields=new();internal void SetField(int document,string id,string value)=>Fields[id]=value;}
internal sealed class Editor{internal EditorSnapshot Snapshot()=>new(new object());}
internal sealed record EditorSnapshot(object Context);
internal sealed class Viewport{internal int Releases;internal void Release()=>Releases++;}
internal sealed partial class WorkspaceAudioHarness
{
    private readonly Host _host;private readonly object _editorBackend;private readonly Editor _editor=new();
    private readonly Viewport Viewport=new();private readonly int _document=1;private bool WorkspaceMode=>true;
    private string _workspaceProblem="",_workspaceTab="camera";private int _studioToolsModal,_spatialModal;
    private object? _studioToolsContext,_spatialContext;
    internal int CueOpens,Presents;internal bool FailCue;internal string Problem=>_workspaceProblem;internal int AudioModal=>_studioToolsModal;
    internal int Releases=>Viewport.Releases;
    internal WorkspaceAudioHarness(Host host,object backend){_host=host;_editorBackend=backend;}
    internal void Tab(string tab)=>SelectWorkspaceTab(tab);
    private void ReleaseTimelineInput(){}private void ReleaseCameraCurveInput(){}private void EnsureWorkspaceOwners(){}
    private void OpenAudioCues(ITheatreStudioToolsBackend tools,object context){CueOpens++;if(FailCue)throw new IOException("Synthetic cue read failure");}
    private void PresentWorkspace()=>Presents++;
}
