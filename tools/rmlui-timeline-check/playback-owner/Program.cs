using System.Collections.Immutable;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
int checks=0;void Check(bool value,string text){if(!value)throw new InvalidOperationException(text);checks++;}
using var player=new PassiveReplayPlayer("SYNTHETIC",new Vector2i(640,360),new ReplayPlayerOptions(true,false,false));
DemoPlayback.SetPlayer(player);object owner=DemoPlayback.PlaybackOwner;var firstScene=player.Current;var firstTransport=player.Transport;
var memory=new CameraOwner(player);memory.Publish();using var tools=new TheatreCameraToolsController(memory);
Check(tools.Preview()&&player.Transport.IsSeeking&&!tools.Snapshot().Current,"actual retained transport accepts preview seek and retires old camera review");
player.Update();Check(!ReferenceEquals(firstTransport,player.Transport)&&!ReferenceEquals(firstScene,player.Current)&&firstScene.Disposed,"actual PassiveReplayPlayer.Rebuild replaces scene/session/transport and disposes previous world");
Check(ReferenceEquals(owner,DemoPlayback.PlaybackOwner)&&player.Ready&&player.Transport.IsPaused,"actual DemoPlayback CWT owner survives own settled seek-driven replacement");
Check(!tools.Refresh(),"new replica cannot be reviewed before a successful native picture exists for it");memory.Publish();
Check(tools.Refresh()&&ReferenceEquals(tools.Snapshot().Context.Camera.Transport,player.Transport),"explicit private controller review adopts settled new transport only under same durable player/source");
Check(tools.Destination("100")&&tools.Stage(TheatreCameraToolsChange.Move),"exact current-owner camera group proposal is staged before external seek");
player.Seek(0);player.Update();memory.Publish();Check(!tools.Confirm()&&memory.Writes==0,"old pending group context cannot confirm against successor transport even for same durable player");tools.Cancel();
Check(tools.Refresh(),"explicit refresh permits editing the same player's new successful paused picture");
using var restarted=new PassiveReplayPlayer("SYNTHETIC",new Vector2i(640,360),new(false,false,false));DemoPlayback.SetPlayer(restarted);memory.Player=restarted;memory.Publish();
Check(!ReferenceEquals(owner,DemoPlayback.PlaybackOwner)&&!tools.Refresh(),"same-path restarted private player has a new owner and cannot rebind original tools panel");
Check(memory.Writes==0,"actual seek/rebuild lifecycle proof creates no saved camera or replay outputs");
Console.WriteLine($"PASS {checks} private actual PassiveReplayPlayer/ReplayTransport/DemoPlayback-owner successor tests; synthetic scene boundary, no world or GL");

sealed class CameraOwner:ITheatreCameraToolsBackend
{
    internal PassiveReplayPlayer Player;private object? _presented;private long _revision;internal int Writes;
    private static readonly TheatreCameraPose Pose=new(1,2,3,0,0,0,1,80,0,-1,0,0);
    internal CameraOwner(PassiveReplayPlayer player)=>Player=player;
    internal void Publish(){_presented=Player.Current.Scene;_revision++;}
    public TheatreCameraToolsContext CaptureTools()
    {
        if(!ReferenceEquals(_presented,Player.Current.Scene)||!Player.Ready)throw new InvalidOperationException("No successful picture for this replica.");
        return new(new(1,Player.Transport,Player.Current.Scene,Player.Transport.SeekGeneration,"SYNTHETIC","SYNTHETIC","RECORDING","CAMERA",Player.Transport.CurrentFrame,600,true,Pose,
            ImmutableArray.Create(new TheatreCameraKey(10,Pose),new TheatreCameraKey(20,Pose)),false){PresentationRevision=_revision},DemoPlayback.PlaybackOwner,"SOURCE","ANNOTATION",
            ImmutableArray<TheatreCameraMarker>.Empty,ImmutableArray<int>.Empty,new(false,true,false),new object());
    }
    public bool ToolsCurrent(TheatreCameraToolsContext context)=>ReferenceEquals(context.Owner,DemoPlayback.PlaybackOwner)&&ReferenceEquals(context.Camera.Transport,Player.Transport)
        &&ReferenceEquals(context.Camera.PresentationScene,Player.Current.Scene)&&Player.Ready&&context.Camera.Frame==Player.Transport.CurrentFrame&&context.Camera.SeekGeneration==Player.Transport.SeekGeneration;
    public void ValidateReviewedTools(TheatreCameraToolsContext context){if(!ToolsCurrent(context))throw new TheatreCameraStaleException("Original owner/replica/seek changed.");}
    public TheatreCameraToolsResult CommitTools(TheatreCameraToolsEdit edit){ValidateReviewedTools(edit.Context);Writes++;throw new InvalidOperationException("No real camera persistence permitted.");}
    public void PreviewTools(TheatreCameraToolsContext context,TheatreCameraKey key){ValidateReviewedTools(context);Player.Seek(key.Frame);}
    public void ChangeMotion(TheatreCameraToolsContext context,TheatreCameraMotionChange change)=>throw new InvalidOperationException();
}
