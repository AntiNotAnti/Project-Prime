#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.MapGen;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;
using OpenTK.Graphics.OpenGL;

namespace MphRead.Mods.Launcher.RmlUi.Render;
internal static partial class RmlUiOpenGlSmoke
{
    private sealed partial class CurrentReplayEditor
    {
        private sealed record WorldComparison(TheatreEditorContext Context,string Path,string Facts,PassiveReplayPlayer Player,
            CancellationToken Cancellation,TaskCompletionSource<TheatreStudioToolResult> Completion);
        private WorldComparison? _worldComparison;
        private long _lastWorldPoll;
        private sealed class ComparisonGraphicsScope:IDisposable
        {
            private readonly int _draw=GL.GetInteger(GetPName.DrawFramebufferBinding),_read=GL.GetInteger(GetPName.ReadFramebufferBinding),_program=GL.GetInteger(GetPName.CurrentProgram);
            internal ComparisonGraphicsScope()=>GL.PushAttrib(AttribMask.AllAttribBits);
            public void Dispose(){GL.PopAttrib();GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer,_draw);GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer,_read);GL.UseProgram(_program);}
        }
        private Task<TheatreStudioToolResult> BeginWorldComparison(TheatreEditorContext context,string input,CancellationToken cancellation)
        {
            Validate(context,true);StopComparisonPreview();RetireWorldComparison();cancellation.ThrowIfCancellationRequested();
            string path=Path.GetFullPath(input);RequireRegular(path);string facts=FileFacts(path);
            using var metadata=DemoReader.Open(path,out var result,metadataOnly:true);
            if(result!=ReplayOpenResult.Success||metadata?.Metadata is not{} other||DemoPlayback.Metadata is not{} original)
                throw new InvalidDataException("Comparison requires complete replay metadata.");
            if(!StringComparer.OrdinalIgnoreCase.Equals(other.RoomKey,original.RoomKey)||other.MapHash!=original.MapHash
                ||other.CustomMapIdentity.HasValue!=original.CustomMapIdentity.HasValue
                ||other.CustomMapIdentity is{} custom&&(!custom.Matches(original.CustomMapIdentity!.Value)||!CustomRooms.Installed.HasExact(custom))
                ||ReplayMapIdentity.Validate(other)!=ReplayOpenResult.Success)
                throw new InvalidDataException("Compare recordings with the exact same already-installed map identity. No package is installed by this comparison.");
            var foreground=DemoPlayback.ReviewedStudioWorld();
            using var graphics=new ComparisonGraphicsScope();
            var player=new PassiveReplayPlayer(path,foreground.Scene.Size,new ReplayPlayerOptions(true,false,false));
            try
            {
                if(context.Frame>player.Transport.DurationFrames)throw new InvalidDataException("The comparison replay ends before this reviewed frame.");
                player.Seek(context.Frame,resume:false);
                var completion=new TaskCompletionSource<TheatreStudioToolResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                _worldComparison=new(context,path,facts,player,cancellation,completion);_lastWorldPoll=0;return completion.Task;
            }
            catch{player.Dispose();throw;}
        }
        private void PollWorldComparison()
        {
            if(_worldComparison is not{} job)return;
            if(_lastWorldPoll!=0&&Stopwatch.GetElapsedTime(_lastWorldPoll).TotalMilliseconds<8)return;
            _lastWorldPoll=Stopwatch.GetTimestamp();
            try
            {
                job.Cancellation.ThrowIfCancellationRequested();Validate(job.Context,true);
                if(job.Facts!=FileFacts(job.Path))throw new IOException("The comparison recording changed while seeking.");
                using var graphics=new ComparisonGraphicsScope();job.Player.Update(120,4);
                if(!job.Player.Ready)return;
                if(job.Player.Transport.CurrentFrame!=job.Context.Frame)throw new InvalidDataException("The comparison did not settle at the exact reviewed frame.");
                var foreground=DemoPlayback.ReviewedStudioWorld();var compared=job.Player.Current;
                using var picture=compared.Scene.PrepareNativeReplayComparisonPicture(foreground.Scene);
                using var a=ReplayWorldCheckpoint.Capture(foreground);using var b=ReplayWorldCheckpoint.Capture(compared);
                var differences=a.CanonicalGraphDifferences(b);
                var report=new{Version=2,Build=ReplayStateHash.BuildId,Frame=job.Context.Frame,
                    GameplayEqual=ReplayStateHash.Compute(foreground.Scene,job.Context.Frame)==ReplayStateHash.Compute(compared.Scene,job.Context.Frame),
                    PresentationEqual=foreground.Scene.ReplayPresentationHash(job.Context.Frame)==compared.Scene.ReplayPresentationHash(job.Context.Frame),
                    FullGraphEqual=differences.Length==0,
                    ComparisonKind="normalized-strict-canonical-graph",
                    RawFullGraphEqual=a.GraphFingerprint()==b.GraphFingerprint(),
                    Normalization="Rooted reference/alias correspondence; matching private HUD/view preparation; PlayerEntity draw caches _fpRenderPose/_fpRenderPoseValid/_modelTransform; asset-derived Node.Transform/Animation, matrix-stack values, material RenderMode only",
                    Differences=differences, RawGraphDifferences=a.GraphDifferences(b), DifferenceLimit=64};
                Validate(job.Context,true);if(job.Facts!=FileFacts(job.Path))throw new IOException("The comparison recording changed during capture.");
                string directory=Path.Combine(DemoLibrary.Directory,"exports","world_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
                File.WriteAllBytes(Path.Combine(directory,"foreground.ppwc"),a.Bytes.ToArray());
                File.WriteAllBytes(Path.Combine(directory,"compared.ppwc"),b.Bytes.ToArray());
                string output=Path.Combine(directory,"world-comparison.json");File.WriteAllText(output,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
                _worldComparison=null;job.Player.Dispose();job.Completion.TrySetResult(new(JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}),output));
            }
            catch(Exception error)when(error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or OperationCanceledException)
            {
                _worldComparison=null;using var graphics=new ComparisonGraphicsScope();job.Player.Dispose();
                if(error is OperationCanceledException)job.Completion.TrySetCanceled(job.Cancellation);else job.Completion.TrySetException(error);
            }
        }
        private void RetireWorldComparison()
        {
            var job=_worldComparison;_worldComparison=null;if(job==null)return;
            using var graphics=new ComparisonGraphicsScope();job.Player.Dispose();job.Completion.TrySetCanceled();
        }
    }
}
#endif
