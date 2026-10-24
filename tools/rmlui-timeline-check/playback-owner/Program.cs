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
Check(ReferenceEquals(firstScene.OwnerSource,player.Current.OwnerSource)&&player.Current.OwnerSource!=null,"owner presentation verification task belongs to the player across scene replacements");
Check(!tools.Refresh(),"new replica cannot be reviewed before a successful native picture exists for it");memory.Publish();
Check(tools.Refresh()&&ReferenceEquals(tools.Snapshot().Context.Camera.Transport,player.Transport),"explicit private controller review adopts settled new transport only under same durable player/source");
Check(tools.Destination("100")&&tools.Stage(TheatreCameraToolsChange.Move),"exact current-owner camera group proposal is staged before external seek");
player.Seek(0);player.Update();memory.Publish();Check(!tools.Confirm()&&memory.Writes==0,"old pending group context cannot confirm against successor transport even for same durable player");tools.Cancel();
Check(tools.Refresh(),"explicit refresh permits editing the same player's new successful paused picture");
using var restarted=new PassiveReplayPlayer("SYNTHETIC",new Vector2i(640,360),new(false,false,false));DemoPlayback.SetPlayer(restarted);memory.Player=restarted;memory.Publish();
Check(!ReferenceEquals(owner,DemoPlayback.PlaybackOwner)&&!tools.Refresh(),"same-path restarted private player has a new owner and cannot rebind original tools panel");
Check(memory.Writes==0,"actual seek/rebuild lifecycle proof creates no saved camera or replay outputs");
using(var budgeted=new PassiveReplayPlayer("SYNTHETIC",new Vector2i(640,360),new(true,false,false)))
{
    budgeted.Transport.Seek(575,false);
    Check(budgeted.Update(maximumMilliseconds:0)==1&&budgeted.Transport.IsSeeking,"zero budget allows one indivisible seek step and yields with target pending");
    budgeted.Transport.Seek(500,false);budgeted.Transport.Seek(525,false);
    Check(budgeted.Update(maximumMilliseconds:0)==1&&budgeted.CoalescedSeeks==1,"latest transport target coalesces superseded requests without resetting to abandoned targets");
    budgeted.Transport.Seek(31,false);
    int loops=0;while(!budgeted.Ready){Check(budgeted.Update(maximumMilliseconds:0)<=1,"each interactive seek update yields after its first fixed step");if(++loops>1000)throw new InvalidOperationException("seek never settled");}
    Check(budgeted.Current.Session.CurrentFrame==31&&budgeted.Transport.IsPaused,"backward replacement settles at exact latest target");
    Check(budgeted.SeekTimings.Snapshot().TotalSamples==1&&budgeted.UpdateTimings.Snapshot().TotalSamples>0,"externally requested seeks and updates retain bounded timing evidence");
    budgeted.Transport.SetPlaybackRate(4);budgeted.Transport.Play();
    Check(budgeted.Update(maximumMilliseconds:0)==4,"ordinary 4x playback preserves due simulation steps under interactive seek budget");
    bool refused=false;try{budgeted.Update(maximumMilliseconds:double.NaN);}catch(ArgumentOutOfRangeException){refused=true;}
    Check(refused,"invalid budget is refused");
}
var timings=new ReplayTimingWindow();for(int i=1;i<=3000;i++)timings.Add(i);
var summary=timings.Snapshot();
Check(summary.TotalSamples==3000&&summary.WindowSamples==2048&&summary.MaximumMilliseconds==3000&&summary.P50Milliseconds==1976,"percentiles use the bounded most recent window");
using(var entered=new ManualResetEventSlim())using(var release=new ManualResetEventSlim())
using(var prefetch=new PassiveReplayPlayer("SYNTHETIC",new Vector2i(640,360),new(true,false,true)))
{
    int workerThread=0;
    ReplayPlaybackSession.BackgroundRead=_=>{workerThread=Environment.CurrentManagedThreadId;entered.Set();release.Wait();return new byte[1];};
    prefetch.Current.Session.DurableCheckpoints=[new(1,400)];
    try
    {
        prefetch.Seek(575);Check(prefetch.Update(maximumMilliseconds:0)==0,"cold checkpoint read returns to UI without restoring or simulating");
        Check(entered.Wait(5000)&&workerThread!=Environment.CurrentManagedThreadId,"immutable checkpoint bytes are read on a worker");
        prefetch.Seek(20);while(!prefetch.Ready)prefetch.Update(maximumMilliseconds:0);
        Check(prefetch.Current.Session.CurrentFrame==20,"superseding seek settles without awaiting obsolete checkpoint worker");
        release.Set();
        Check(prefetch.Update(maximumMilliseconds:0)==0&&prefetch.Current.Session.CurrentFrame==20,"obsolete checkpoint completion cannot publish a world or advance paused state");
    }
    finally{release.Set();ReplayPlaybackSession.BackgroundRead=null;}
}
using(var fallback=new PassiveReplayPlayer("SYNTHETIC",new Vector2i(640,360),new(true,false,true)))
{
    var reads=new System.Collections.Concurrent.ConcurrentBag<uint>();
    ReplayPlaybackSession.SynchronousReads=0;
    ReplayPlaybackSession.BackgroundRead=index=>{reads.Add(index.Frame);if(index.Frame==400)throw new IOException("bad newest checkpoint");return new byte[1];};
    fallback.Current.Session.DurableCheckpoints=[new(1,200),new(2,400)];
    try
    {
        fallback.Seek(575);var timeout=System.Diagnostics.Stopwatch.StartNew();
        while(fallback.RejectedDurableCheckpoints==0){fallback.Update(maximumMilliseconds:0);Thread.Yield();if(timeout.ElapsedMilliseconds>5000)throw new Exception("worker did not reject corrupt newest checkpoint");}
        Check(ReplayPlaybackSession.SynchronousReads==0,"corrupt latest checkpoint yields before selecting an earlier disk fallback");
        while(!fallback.Ready){fallback.Update(maximumMilliseconds:0);Thread.Yield();if(timeout.ElapsedMilliseconds>10000)throw new Exception("fallback did not settle");}
        Check(reads.Contains(200u)&&reads.Contains(400u)&&ReplayPlaybackSession.SynchronousReads==0,"every durable fallback is read on worker before owner-thread validation");
        Check(fallback.Current.Session.CurrentFrame==575,"corrupt optional checkpoints reconstruct exact target from baseline");
    }
    finally{ReplayPlaybackSession.BackgroundRead=null;}
}
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
