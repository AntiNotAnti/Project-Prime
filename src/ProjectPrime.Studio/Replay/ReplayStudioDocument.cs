using System.Security.Cryptography;
using Avalonia.Threading;
using ProjectPrime.Studio.Settings;
using ProjectPrime.Studio.Shell;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;
using MphRead.Mods.StudioReplay;
using OpenTK.Mathematics;
namespace ProjectPrime.Studio.Replay;

public sealed class ReplayStudioDocument : IStudioDocument, IStudioDocumentNotifications
{
    public StudioDocumentId Id { get; }
    public StudioDocumentKind Kind { get; }
    public string Title => Path is null ? "Replay Studio" : System.IO.Path.GetFileName(Path);
    public string? Path { get; }
    public bool Dirty { get; private set; }
    public bool CanSave => Path is not null;
    public StudioDocumentState State { get; private set; } = StudioDocumentState.Open;
    public event Action? Changed;
    public ReplayViewportHost Host { get; }
    public ReplayStudioSession? Session { get; private set; }
    internal ReplayCameraTrack Camera { get; } = new();
    private StudioReplaySnapshot? _snapshot;
    private readonly ReplayCameraTrackIdentity? _identity;
    internal Func<string,Func<CancellationToken,Task>,CancellationToken,Task>? JobRunner { get; }
    public uint CurrentFrame { get; private set; }
    public uint DurationFrames { get; private set; }
    internal uint? ClipIn { get; private set; }
    internal uint? ClipOut { get; private set; }
    internal ReplayCameraMode CameraMode { get; set; } = ReplayCameraMode.FirstPerson;
    internal int Slot { get; set; }
    internal bool PlayTrack { get; set; } = true;
    internal bool GameHud { get; set; } = true;
    internal bool PreviewAudio { get; set; } = true;
    internal bool ExportAudio { get; set; } = true;
    internal bool ReplayOverlay { get; set; }
    internal bool Director { get; set; }
    internal bool Multiview { get; set; }
    internal ReplayCameraMode ComparisonMode { get; set; } = ReplayCameraMode.Chase;
    internal int ComparisonSlot { get; set; } = 1;
    internal string Diagnostic { get; private set; } = "Waiting for replay graphics.";
    internal IReadOnlyList<ReplayEvent> Events { get; private set; } = Array.Empty<ReplayEvent>();
    internal string[] ProtectedInputs
    {
        get
        {
            var inputs=new List<string>();if(Path is {} path){inputs.Add(path);if(path.EndsWith(ReplayVirtualClips.Extension,StringComparison.OrdinalIgnoreCase)&&ReplayVirtualClips.TryLoad(path,out var clip)&&clip is not null)inputs.Add(clip.SourceReplay);}
            if(_snapshot is {} snapshot)inputs.Add(snapshot.PlaybackPath);return inputs.ToArray();
        }
    }
    internal string SourceHash => _snapshot?.SourceHash ?? "";
    public ReplayStudioDocument(StudioDocumentKind kind, string? path, StudioPaths paths, StudioDocumentId? id = null, Func<string,Func<CancellationToken,Task>,CancellationToken,Task>? jobRunner=null)
    {
        Id=id ?? StudioDocumentId.New(); Kind=kind; Path=path; JobRunner=jobRunner;Host=new(this);
        if(path is not null)
        {
            _identity=ReplayCameraTrackIdentity.Create(path);
            if(!Camera.Load(path))throw new InvalidDataException(Camera.LastError);
        }
    }
    public static async Task<ReplayStudioDocument> OpenAsync(StudioDocumentKind kind,string path,StudioPaths paths,CancellationToken token,StudioDocumentId? id=null,IReadOnlyList<string>? packageDirectories=null, Func<string,Func<CancellationToken,Task>,CancellationToken,Task>? jobRunner=null)
    {
        token.ThrowIfCancellationRequested();path=System.IO.Path.GetFullPath(path);
        if(!File.Exists(path))throw new FileNotFoundException("Replay not found.",path);
        var snapshot=await StudioReplaySnapshot.CreateAsync(path,paths.BuildCacheDirectory,token);
        try
        {
            var document=new ReplayStudioDocument(kind,path,paths,id,jobRunner);
            if(!snapshot.SourceHash.Equals(Convert.ToHexString(document._identity!.Value.SourceContentHash),StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Replay changed while opening. Open it again.");
            document._snapshot=snapshot;return document;
        }
        catch { snapshot.Dispose();throw; }
    }
    internal void Initialize(int width,int height)
    {
        if(Path is null || Session is not null)return;
        if(_snapshot is null)throw new InvalidOperationException("Replay snapshot has not been prepared.");
        var player=new PassiveReplayPlayer(_snapshot.PlaybackPath,new Vector2i(width,height));
        try
        {
            if(player.Current.Session.Metadata is {} metadata && ReplayMapIdentity.Validate(metadata)!=ReplayOpenResult.Success)
                throw new InvalidDataException("The exact historical replay map is unavailable. Install its recorded package hash before editing.");
            player.Transport.Pause();Session=new(player);Events=player.Current.Session.Events.ToArray();
        }
        catch { player.Dispose();throw; }
        Notify();
    }
    internal void Refresh()
    {
        if(Session is not {} session)return;
        var transport=session.Player.Transport;
        CurrentFrame=transport.CurrentFrame;DurationFrames=transport.DurationFrames;ClipIn=transport.ClipIn;ClipOut=transport.ClipOut;
        Diagnostic=$"{transport.CurrentSeconds:F2}s / {transport.DurationSeconds:F2}s · {transport.State} · seek {session.Player.SeekMilliseconds:F1}ms · checkpoints {session.Player.CheckpointBytes/1048576d:F1} MiB";
        if(session.Player.Current.Session.LastWarning is {} warning)Diagnostic+=" · "+warning;
    }
    internal void Release()
    {
        Session?.Dispose();Session=null;
        if(State==StudioDocumentState.Closed){_snapshot?.Dispose();_snapshot=null;}
    }
    public void TogglePause()=>Host.Enqueue(player=>player.Transport.TogglePause());
    public void MarkIn()=>Host.Enqueue(player=>player.Transport.MarkIn());
    public void MarkOut()=>Host.Enqueue(player=>player.Transport.MarkOut());
    public void Seek(uint frame)=>Host.Enqueue(player=>player.Seek(frame));
    public void SetCameraKey(float x,float y,float z,float yawDegrees,float pitchDegrees,float fovDegrees)
    {
        var rotation=Quaternion.FromEulerAngles(MathHelper.DegreesToRadians(pitchDegrees),MathHelper.DegreesToRadians(yawDegrees),0);
        lock(Camera)
            if(!Camera.Put(new ReplayCameraKeyframe(CurrentFrame,new Vector3(x,y,z),rotation,MathHelper.DegreesToRadians(fovDegrees))))throw new InvalidOperationException(Camera.LastError);
        Edited();
    }
    public void AddCameraKey()=>Host.Enqueue(player=>
    {
        lock(Camera)
        {
            player.Current.Scene.StudioReplayPrepare(PlayTrack?Camera:new ReplayCameraTrack(),player.Transport.CurrentFrame,1,CameraMode,Slot);
            player.Current.Scene.StudioReplayApplyPresentationCamera();
            if(!Camera.Put(player.Current.Scene.StudioReplayCapture(player.Transport.CurrentFrame)))throw new InvalidOperationException(Camera.LastError);
        }
        Edited();
    });
    internal ReplayCameraKeyframe[] CameraKeys(){lock(Camera)return Camera.Keys.ToArray();}
    internal void EditCameraKeys(IEnumerable<uint> frames,int delta,bool delete,ReplayCameraInterpolation? interpolation=null,ReplayCameraEase? ease=null)
    {
        lock(Camera)
        {
            var selected=frames.ToHashSet(); var before=Camera.Keys.ToArray();
            var replacement=before.Where(k=>!selected.Contains(k.Frame)).ToList();
            if(!delete)
                foreach(var key in before.Where(k=>selected.Contains(k.Frame)))
                {
                    long target=(long)key.Frame+delta;
                    if(target<0||target>DurationFrames||replacement.Any(k=>k.Frame==target))throw new InvalidOperationException("Camera keys would overlap or leave the replay.");
                    replacement.Add(key with{Frame=(uint)target,Interpolation=interpolation??key.Interpolation,Ease=ease??key.Ease});
                }
            Camera.Clear();foreach(var key in replacement.OrderBy(k=>k.Frame))Camera.Put(key);
        }
        Edited();
    }
    private void Edited(){Dirty=true;Notify();}
    private void Notify()=>Dispatcher.UIThread.Post(()=>Changed?.Invoke());
    public Task SaveAsync(string? targetPath,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();if(Path is null||_identity is null)throw new InvalidOperationException("Open a replay first.");
        var current=ReplayCameraTrackIdentity.Create(Path);var opened=_identity.Value;
        if(!CryptographicOperations.FixedTimeEquals(current.SourceContentHash,opened.SourceContentHash)||current.StartFrame!=opened.StartFrame||current.EndFrame!=opened.EndFrame)
            throw new IOException("The source replay or clip range changed. Camera sidecar was not overwritten.");
        lock(Camera)if(!Camera.Save(Path))throw new IOException(Camera.LastError);
        Dirty=false;Notify();return Task.CompletedTask;
    }
    public Task RecoverAsync(CancellationToken token){token.ThrowIfCancellationRequested();return Task.CompletedTask;}
    public Task CloseAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();State=StudioDocumentState.Closed;Host.Dispose();
        if(Session is null){_snapshot?.Dispose();_snapshot=null;}
        return Task.CompletedTask;
    }
    public ValueTask DisposeAsync(){CloseAsync(CancellationToken.None);return ValueTask.CompletedTask;}
    public async Task ExportSelectionAsync(string destination,CancellationToken token=default)
    {
        if(_snapshot is null)throw new InvalidOperationException("Open a replay first.");
        using var lease=_snapshot.Acquire();
        string[] inputs=ProtectedInputs;
        destination=System.IO.Path.GetFullPath(destination);
        StudioReplayOutputGuard.ValidateClip(destination,inputs);
        uint start=ClipIn??0,end=ClipOut??DurationFrames;string source=_snapshot.PlaybackPath;
        if(start>=end)throw new InvalidOperationException("Mark a non-empty replay range.");
        // Extract into a private sibling, then validate again at publication.
        string temporary=destination+"."+Guid.NewGuid().ToString("N")+".ppdemo";
        try
        {
            await Task.Run(()=>{var result=ReplayArchive.Extract(source,start,end,temporary,token);if(result!=ReplayOpenResult.Success)throw new IOException("Replay export failed: "+result);},token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            StudioReplayOutputGuard.ValidateClip(destination,inputs);
            File.Move(temporary,destination,true);
        }
        finally { if(File.Exists(temporary))File.Delete(temporary); }
    }
    internal async Task ExportVideoAsync(string destination,int width,int height,int fps,string preset,CancellationToken token,bool reel=false)
    {
        if(_snapshot is null)throw new InvalidOperationException("Open a replay first.");
        using var lease=_snapshot.Acquire();
        StudioReplayOutputGuard.ValidateVideo(System.IO.Path.GetFullPath(destination),ProtectedInputs);
        uint start=ClipIn??0,end=ClipOut??DurationFrames;
        ReplayVideoSegment[]? segments=null;
        if(reel)
        {
            segments=Path is null?[]:ReplayReels.Segments(Path).Select(s=>new ReplayVideoSegment(s.StartFrame,s.EndFrame,s.Name,s.Camera)).ToArray();
            if(segments.Length==0)throw new InvalidOperationException("Add at least one range to the reel.");
            if(segments.Length>32||segments.Any(s=>s.StartFrame>=s.EndFrame||s.EndFrame>DurationFrames))throw new InvalidOperationException("The reel must contain at most 32 ranges within this recording.");
            start=segments[0].StartFrame;end=segments[0].EndFrame;
        }
        if(start>=end)throw new InvalidOperationException("Mark a non-empty replay range.");
        await ReplayExportWorker.ExportAsync(_snapshot.PlaybackPath,_snapshot.PlaybackHash,destination,start,end,width,height,fps,GameHud,CameraMode,Slot,preset,PlayTrack?CameraKeys():[],ExportAudio,Multiview,ComparisonMode,ComparisonSlot,ReplayOverlay,Director,segments,ProtectedInputs,token);
    }
    internal void Bookmark(string name)
    {if(Path is null)return;ReplayAnnotations.AddBookmark(Path,CurrentFrame,name,DurationFrames);Notify();}
    internal void NamedClip(string name,bool reel)
    {
        if(Path is null)return;uint start=ClipIn??0,end=ClipOut??DurationFrames;
        if(start>=end)throw new InvalidOperationException("Mark a non-empty range.");
        if(reel)ReplayReels.Add(Path,start,end,name);else ReplayAnnotations.AddHighlight(Path,start,end,name);Notify();
    }
}
public sealed class ReplayStudioSession : IDisposable
{
    internal ReplayDirectorState Director { get; } = new();
    internal MphRead.Mods.StudioReplay.StudioReplayOverlay? Overlay { get; set; }
    internal ReplayPlaybackSession? OverlaySession { get; set; }
    internal PassiveReplayPlayer Player {get;}
    internal ReplayStudioSession(PassiveReplayPlayer player)=>Player=player;
    public void Dispose()=>Player.Dispose();
}
