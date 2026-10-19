#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.MapGen;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;
namespace MphRead.Mods.Launcher.RmlUi.Render;
internal static partial class RmlUiOpenGlSmoke
{
    internal static bool RenderNativeReplayComparisonPreview(RenderWindow window)
    {
        var page=_playback;var foreground=DemoPlayback.PresentationScene;
        if(!Shell.StudioWindow||page is not{IsOpen:true,FourViews:false}||!NativeReplayLeaseCurrent(window)
            ||_pages?.Top!=page.Document||foreground==null||!DemoPlayback.Owns(foreground))return false;
        if(page.CurrentComparisonPreviewScene() is not Scene compared)return false;
        var size=window.FramebufferSize;var bounds=NativeReplayViewportBounds(size.X,size.Y);
        if(bounds==null)return false;
        try{return foreground.DrawNativeReplayComparisonViews(compared,bounds.Value,size);}
        catch(Exception error)when(error is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
        {
            Mods.DebugLog.Line("replay","A/B auxiliary preview failed; returning to single view: "+error);
            page.ReportWorkspaceComparisonFailure(error);
            return false;
        }
    }
    private sealed partial class CurrentReplayEditor:ITheatreWorkspaceComparisonPreviewBackend
    {
        private sealed record PreviewRequest(TheatreEditorContext Context,string Path,string Facts,string ForegroundFacts,ReplayMetadata Original);
        private sealed record PreviewPreparation(PreviewRequest Request,CancellationTokenSource Cancellation,Task<PreviewSnapshot> Task);
        private sealed class PreviewSnapshot:IDisposable
        {
            internal readonly PreviewRequest Request;
            internal readonly FileStream Source,Snapshot;
            internal readonly string Directory,Path,ContentHash;
            internal readonly ReplayMetadata Metadata;
            internal readonly ulong GeometryHash;
            internal PreviewSnapshot(PreviewRequest request,FileStream source,FileStream snapshot,string directory,string path,string hash,ReplayMetadata metadata,ulong geometry)
                =>(Request,Source,Snapshot,Directory,Path,ContentHash,Metadata,GeometryHash)=(request,source,snapshot,directory,path,hash,metadata,geometry);
            public void Dispose(){Snapshot.Dispose();Source.Dispose();DeletePreviewSnapshot(Directory);}
        }
        private sealed class ComparisonPreview
        {
            internal readonly PreviewRequest Request;
            internal readonly PreviewSnapshot Snapshot;
            internal readonly PassiveReplayPlayer Player;
            internal readonly CancellationTokenSource Cancellation;
            internal readonly long Started=Stopwatch.GetTimestamp();
            internal Task? Verification;
            internal bool OpenVerified,StorageVerified,Published;
            internal uint TargetFrame;
            internal ComparisonPreview(PreviewRequest request,PreviewSnapshot snapshot,PassiveReplayPlayer player,CancellationTokenSource cancellation)
                =>(Request,Snapshot,Player,Cancellation,TargetFrame)=(request,snapshot,player,cancellation,request.Context.Frame);
        }
        private PreviewPreparation? _comparisonPreparation;
        private ComparisonPreview? _comparisonPreview;
        private long _comparisonPreviewPoll;
        private string _comparisonPreviewStatus="Pause to open a same-map replay. B then follows A's playhead.";
        private static void DeletePreviewSnapshot(string? directory)
        {
            if(directory==null)return;
            try{Directory.Delete(directory,recursive:true);}catch(IOException){}catch(UnauthorizedAccessException){}
        }
        private static string PreviewContentHash(FileStream stream,CancellationToken token)
        {
            long position=stream.Position;
            try
            {
                stream.Position=0;using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                byte[] buffer=new byte[64*1024];int read;
                while((read=stream.Read(buffer))>0){token.ThrowIfCancellationRequested();hash.AppendData(buffer,0,read);}
                token.ThrowIfCancellationRequested();return Convert.ToHexString(hash.GetHashAndReset());
            }
            finally{stream.Position=position;}
        }
        // Storage workers receive only the captured request and private handles;
        // they never read live scene state or construct/step a renderer.
        private static void VerifyPreviewStorage(PreviewRequest request,PreviewSnapshot? snapshot,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if(ReplayNativeFileWitness.Source(request.Context.LogicalPath,request.Context.PlaybackPath)!=request.Context.SourceRevision)
                throw new IOException("The foreground recording changed during comparison preparation.");
            if(FileFacts(request.Path)!=request.Facts||ReplayNativeFileWitness.Facts(request.Context.LogicalPath,request.Context.PlaybackPath)!=request.ForegroundFacts)
                throw new IOException("The reviewed recording changed during comparison preparation.");
            if(snapshot!=null)
            {
                if(PreviewContentHash(snapshot.Source,token)!=snapshot.ContentHash||PreviewContentHash(snapshot.Snapshot,token)!=snapshot.ContentHash)
                    throw new IOException("The second recording changed before its comparison picture.");
                ulong geometry=ReplayMapIdentity.Compute(request.Original.RoomKey);token.ThrowIfCancellationRequested();
                if(geometry==0||geometry!=snapshot.GeometryHash)throw new InvalidDataException("The actual map geometry changed during comparison preparation.");
            }
        }
        private static PreviewSnapshot PrepareComparisonSnapshot(PreviewRequest request,CancellationToken token)
        {
            FileStream? source=null,snapshot=null;string? directory=null;
            try
            {
                VerifyPreviewStorage(request,null,token);RequireRegular(request.Path);
                source=new(request.Path,FileMode.Open,FileAccess.Read,FileShare.Read);string content=PreviewContentHash(source,token);
                directory=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"ProjectPrime-studio-comparison-"+Guid.NewGuid().ToString("N"));
                System.IO.Directory.CreateDirectory(directory);string frozen=System.IO.Path.Combine(directory,"comparison.ppdemo");source.Position=0;
                using(var output=new FileStream(frozen,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                {
                    byte[] buffer=new byte[64*1024];int read;
                    while((read=source.Read(buffer))>0){token.ThrowIfCancellationRequested();output.Write(buffer,0,read);}
                    output.Flush(true);
                }
                snapshot=new(frozen,FileMode.Open,FileAccess.Read,FileShare.Read);
                if(PreviewContentHash(source,token)!=content||PreviewContentHash(snapshot,token)!=content||FileFacts(request.Path)!=request.Facts)
                    throw new IOException("The second recording changed while its immutable preview was copied.");
                using var reader=DemoReader.Open(frozen,out var result,metadataOnly:true);
                if(result!=ReplayOpenResult.Success||reader?.Metadata is not{} metadata)
                    throw new InvalidDataException("Comparison requires complete replay metadata.");
                if(!StringComparer.OrdinalIgnoreCase.Equals(metadata.RoomKey,request.Original.RoomKey)
                    ||metadata.CustomMapIdentity.HasValue!=request.Original.CustomMapIdentity.HasValue
                    ||metadata.CustomMapIdentity is{} custom&&!custom.Matches(request.Original.CustomMapIdentity!.Value))
                    throw new InvalidDataException("Use the exact same map for both comparison pictures.");
                ulong geometry=ReplayMapIdentity.Compute(request.Original.RoomKey);token.ThrowIfCancellationRequested();
                if(!TheatreComparisonGeometryProof.IsExact(geometry,geometry,geometry,request.Original.MapHash,metadata.MapHash))
                    throw new InvalidDataException("Exact resolved map geometry is unavailable or differs between these recordings.");
                var prepared=new PreviewSnapshot(request,source,snapshot,directory,frozen,content,metadata,geometry);
                source=null;snapshot=null;directory=null;return prepared;
            }
            finally{snapshot?.Dispose();source?.Dispose();DeletePreviewSnapshot(directory);}
        }
        public void BeginComparisonPreview(TheatreEditorContext context,string input)
        {
            if(_worldComparison!=null)throw new InvalidOperationException("Finish or cancel the current world comparison first.");
            if(DemoPlayback.Metadata is not{} original)throw new InvalidOperationException("The foreground recording metadata is unavailable.");
            string path=System.IO.Path.GetFullPath(input);RequireRegular(path);
            var request=new PreviewRequest(context,path,FileFacts(path),ReplayNativeFileWitness.Facts(context.LogicalPath,context.PlaybackPath),original);
            ValidateComparisonContext(request);StopComparisonPreview();var cancellation=new CancellationTokenSource();
            _comparisonPreparation=new(request,cancellation,ReplayStorageJobs.Run(()=>PrepareComparisonSnapshot(request,cancellation.Token),cancellation.Token));
            _comparisonPreviewStatus=$"Preparing B at frame {context.Frame}.";
        }
        private static void ObserveSnapshotCompletion(PreviewPreparation preparation)
        {
            preparation.Cancellation.Cancel();
            _=preparation.Task.ContinueWith(completed=>
            {
                try{if(completed.IsCompletedSuccessfully)completed.Result.Dispose();else _=completed.Exception;}
                finally{preparation.Cancellation.Dispose();}
            },TaskScheduler.Default);
        }
        public void StopComparisonPreview()
        {
            var preparation=_comparisonPreparation;_comparisonPreparation=null;
            if(preparation!=null)ObserveSnapshotCompletion(preparation);
            var job=_comparisonPreview;_comparisonPreview=null;
            if(job!=null)
            {
                job.Cancellation.Cancel();
                using var graphics=new ComparisonGraphicsScope();
                try{job.Player.Dispose();}
                finally
                {
                    // A detached verifier may still own the stream position.
                    // Retire it without blocking or disposing underneath its read.
                    if(job.Verification is{} verification)
                        _=verification.ContinueWith(completed=>{try{_=completed.Exception;job.Snapshot.Dispose();}finally{job.Cancellation.Dispose();}},TaskScheduler.Default);
                    else{job.Snapshot.Dispose();job.Cancellation.Dispose();}
                }
            }
            _comparisonPreviewStatus="Comparison preview stopped.";
        }
        private void AdoptComparisonSnapshot(PreviewPreparation preparation,PreviewSnapshot snapshot)
        {
            PassiveReplayPlayer? player=null;
            try
            {
                ValidateComparisonScope(preparation.Request);
                var original=preparation.Request.Original;var metadata=snapshot.Metadata;
                if(metadata.CustomMapIdentity is{} custom&&!CustomRooms.Installed.HasExact(custom))
                    throw new InvalidDataException("Install the exact map before opening a comparison preview.");
                var foreground=DemoPlayback.ReviewedStudioWorld(allowPlaying:true);
                if(!TheatreComparisonGeometryProof.IsExact(snapshot.GeometryHash,foreground.MapHash,snapshot.GeometryHash,original.MapHash,metadata.MapHash))
                    throw new InvalidDataException("The foreground loaded geometry differs from the reviewed map.");
                using var graphics=new ComparisonGraphicsScope();
                player=new(snapshot.Path,foreground.Scene.Size,new ReplayPlayerOptions(true,false,false));
                if(!TheatreComparisonGeometryProof.IsExact(snapshot.GeometryHash,foreground.MapHash,player.Current.MapHash,original.MapHash,metadata.MapHash))
                    throw new InvalidDataException("The private world did not load the reviewed geometry identity.");
                if(preparation.Request.Context.Frame>player.Transport.DurationFrames)throw new InvalidDataException("B ends before this reviewed frame.");
                player.Seek(preparation.Request.Context.Frame,resume:false);
                var job=new ComparisonPreview(preparation.Request,snapshot,player,preparation.Cancellation);
                job.Verification=ReplayStorageJobs.Run(()=>{VerifyPreviewStorage(job.Request,job.Snapshot,job.Cancellation.Token);return true;},job.Cancellation.Token);
                _comparisonPreview=job;player=null;
            }
            catch
            {
                if(player!=null){using var graphics=new ComparisonGraphicsScope();player.Dispose();}
                snapshot.Dispose();preparation.Cancellation.Dispose();throw;
            }
        }
        private void PollComparisonPreview()
        {
            if(_comparisonPreparation==null&&_comparisonPreview==null)return;
            if(_comparisonPreviewPoll!=0&&Stopwatch.GetElapsedTime(_comparisonPreviewPoll).TotalMilliseconds<8
                &&(_comparisonPreview==null||_comparisonPreview.TargetFrame==DemoPlayback.Session.Transport.CurrentFrame))return;
            _comparisonPreviewPoll=Stopwatch.GetTimestamp();
            try
            {
                if(_comparisonPreparation is{} preparation)
                {
                    ValidateComparisonScope(preparation.Request);
                    if(!preparation.Task.IsCompleted)return;
                    if(DemoPlayback.Session.Transport.IsSeeking)return;
                    _comparisonPreparation=null;
                    PreviewSnapshot snapshot;
                    try{snapshot=preparation.Task.GetAwaiter().GetResult();}
                    catch{preparation.Cancellation.Dispose();throw;}
                    AdoptComparisonSnapshot(preparation,snapshot);
                }
                if(_comparisonPreview is not{} job)return;
                ValidateComparisonScope(job.Request);
                if(Stopwatch.GetElapsedTime(job.Started).TotalSeconds>45&&!job.Published)
                    if(!job.StorageVerified)throw new InvalidOperationException("B did not reach the reviewed picture in time. Retry the preview.");
                var foreground=DemoPlayback.Session.Transport;
                if(foreground.IsSeeking)
                {job.Published=false;_comparisonPreviewStatus="A is seeking. B will follow its settled playhead.";return;}
                uint target=foreground.CurrentFrame;
                if(target>job.Player.Transport.DurationFrames)
                {job.Published=false;_comparisonPreviewStatus=$"B ENDED // A FRAME {target} // B END {job.Player.Transport.DurationFrames}";return;}
                if(job.TargetFrame!=target)
                {job.TargetFrame=target;job.Published=false;job.Player.Seek(target,resume:false);}
                if(!job.Player.Ready){using var graphics=new ComparisonGraphicsScope();job.Player.Update(120,4);}
                if(job.Verification is{} verification)
                {
                    if(!verification.IsCompleted)return;
                    verification.GetAwaiter().GetResult();job.Verification=null;
                    if(!job.OpenVerified)job.OpenVerified=true;
                    else job.StorageVerified=true;
                }
                if(job.Player.Ready)
                {
                    if(job.Player.Transport.CurrentFrame!=job.TargetFrame)throw new InvalidDataException("B did not settle at A's playhead.");
                    if(!job.StorageVerified&&job.Verification==null)
                        job.Verification=ReplayStorageJobs.Run(()=>{VerifyPreviewStorage(job.Request,job.Snapshot,job.Cancellation.Token);return true;},job.Cancellation.Token);
                    if(DemoPlayback.ReviewedStudioWorld(allowPlaying:true).MapHash!=job.Snapshot.GeometryHash)
                        throw new InvalidDataException("The foreground loaded geometry changed during comparison.");
                    job.Published=job.StorageVerified;
                    _comparisonPreviewStatus=job.Published?$"A LEFT / B RIGHT // FRAME {job.TargetFrame} // {(foreground.IsPaused||foreground.AtEnd?"PAUSED":"FOLLOWING")}":"Verifying B's recording...";
                }
                else _comparisonPreviewStatus=$"B CATCHING UP // A FRAME {target} // B FRAME {job.Player.Transport.CurrentFrame}";
            }
            catch(Exception error)when(error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or OperationCanceledException)
            {StopComparisonPreview();_comparisonPreviewStatus=error is OperationCanceledException?"Comparison preview canceled.":error.Message;}
        }
        private static void ValidateComparisonContext(PreviewRequest request)
        {
            var context=request.Context;
            ValidateComparisonScope(request);
            if(!OwnerCurrent(context))throw new TheatreEditorStaleException("The reviewed replay transport changed. Pause and refresh before opening B.");
            var transport=(ReplayTransport)context.Transport;
            if(!context.SettledPaused||transport.IsSeeking||!(transport.IsPaused||transport.AtEnd)
                ||transport.CurrentFrame!=context.Frame||transport.SeekGeneration!=context.SeekGeneration
                ||transport.DurationFrames!=context.Duration||transport.ClipIn!=context.MarkIn||transport.ClipOut!=context.MarkOut)
                throw new TheatreEditorStaleException("Playback, seek or marks changed. Start a new comparison preview.");
            if(ReplayReviewedMetadata.Read(context.LogicalPath+ReplayAnnotations.Extension).Revision!=context.AnnotationRevision
                ||ReplayReviewedMetadata.Read(context.LogicalPath+ReplayReels.Extension).Revision!=context.ReelRevision)
                throw new IOException("The reviewed recording or sidecars changed. Start a new comparison preview.");
        }
        private static void ValidateComparisonScope(PreviewRequest request)
        {
            var context=request.Context;
            if(!GameplayLeaseCurrent||!DemoPlayback.IsActive||_gameplayScene!.LifetimeGeneration!=context.SceneGeneration
                ||!ReferenceEquals(DemoPlayback.PlaybackOwner,context.Owner)||DemoPlayback.LogicalPath!=context.LogicalPath
                ||DemoPlayback.PlaybackPath!=context.PlaybackPath||DemoPlayback.Session.Transport.DurationFrames!=context.Duration)
                throw new TheatreEditorStaleException("The foreground replay player or recording changed.");
            if(ReplayNativeFileWitness.Facts(context.LogicalPath,context.PlaybackPath)!=request.ForegroundFacts||FileFacts(request.Path)!=request.Facts)
                throw new IOException("A comparison recording changed. Start a new comparison preview.");
            if(ReplayVideoExporter.Active)throw new InvalidOperationException("Comparison preview stopped for export.");
        }
        public TheatreComparisonPreviewState ReadComparisonPreview(TheatreEditorContext context)
        {
            PollComparisonPreview();
            return new(_comparisonPreparation!=null||_comparisonPreview!=null,_comparisonPreview?.Published==true,
                _comparisonPreview?.TargetFrame??_comparisonPreparation?.Request.Context.Frame??context.Frame,_comparisonPreviewStatus);
        }
        public object? CurrentComparisonPreviewScene(TheatreEditorContext context)
        {
            PollComparisonPreview();
            if(_comparisonPreview is not{Published:true} job||!job.Player.Ready)return null;
            try
            {
                ValidateComparisonScope(job.Request);
                var foreground=DemoPlayback.Session.Transport;
                if(!ReferenceEquals(context.Owner,job.Request.Context.Owner)||context.SourceRevision!=job.Request.Context.SourceRevision
                    ||foreground.IsSeeking||foreground.CurrentFrame!=job.TargetFrame||job.Player.Transport.CurrentFrame!=job.TargetFrame)return null;
                return job.Player.Current.Scene;
            }
            catch(Exception error)when(error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
            {StopComparisonPreview();_comparisonPreviewStatus=error.Message;return null;}
        }
    }
}
#endif
