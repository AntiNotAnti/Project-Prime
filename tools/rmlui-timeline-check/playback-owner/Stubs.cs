using MphRead.Mods.Network;
using OpenTK.Mathematics;
namespace MphRead { internal sealed class Scene { internal Poses ReplayPoses=new(); } internal sealed class Poses {internal bool IndexedFirePresentation=true;} }
namespace MphRead.Mods.Replay { }
namespace MphRead.Mods.Network
{
    internal enum ReplayState { Inactive,Playing,Paused,Seeking,Ended,Error }
    internal enum ReplayOpenResult { Success,Empty }
    internal enum ReplayEventType { Kill }
    internal readonly record struct ReplayEvent(uint Frame,ReplayEventType Type);
    internal readonly record struct ReplayCheckpointIndex(long Offset,uint Frame);
    internal sealed class ReplayTimelineClip {internal uint StartRecordingFrame=0;}
    internal sealed class ReplayPlaybackSession
    {
        internal ReplayTransport Transport;internal uint CurrentFrame=50,LastFrame=600;
        internal bool IsActive=true,IsWarming=false,HasSimulatedFrame=true;internal bool AtEnd=>CurrentFrame>=LastFrame;
        internal string CurrentPath="SYNTHETIC";internal ReplayOpenResult LastResult=ReplayOpenResult.Success;
        internal ReplayEvent[] Events=Array.Empty<ReplayEvent>();internal ReplayCheckpointIndex[] DurableCheckpoints=Array.Empty<ReplayCheckpointIndex>();
        internal ReplayPlaybackSession(){Transport=new(this);Transport.Begin();Transport.Pause();}
        internal uint CheckpointVisibleFrame(ReplayCheckpointIndex index)=>index.Frame;
        internal ReplayWorldCheckpoint LoadCheckpoint(ReplayCheckpointIndex index,bool requireIndexedFirePresentation)=>throw new InvalidOperationException();
        internal void WarnVerification(string warning){}
    }
    internal sealed class PassiveReplayScene:IDisposable
    {
        internal ReplayPlaybackSession Session=new();internal Scene Scene=new();internal bool Disposed;
        private static int _opens;
        internal PassiveReplayScene(string path,Vector2i size){if(++_opens>1)Session.CurrentFrame=0;}
        internal PassiveReplayScene(ReplayTimelineClip clip,Vector2i size):this("SYNTHETIC",size){}
        internal bool Step(){Session.CurrentFrame++;Session.Transport.AfterFrame();return true;}
        public void Dispose()=>Disposed=true;
    }
    internal sealed class ReplayWorldCheckpoint:IDisposable
    {
        internal byte[] Bytes=Array.Empty<byte>();internal uint Frame=0;
        internal static ReplayWorldCheckpoint Capture(PassiveReplayScene scene)=>throw new InvalidOperationException();
        internal void Restore(PassiveReplayScene scene,uint? frame)=>throw new InvalidOperationException();public void Dispose(){}
    }
    internal static partial class DemoPlayback
    {
        private static PassiveReplayPlayer? _player;private static readonly object _prepared=new();
        internal static void SetPlayer(PassiveReplayPlayer player)=>_player=player;
    }
    internal static class DemoLibrary {internal const string Directory="NEVER-USED";}
    internal static class ReplayArchive {internal static ReplayOpenResult Extract(string source,uint start,uint end,string path,System.Threading.CancellationToken cancellation)=>throw new InvalidOperationException("No archive writes in this source fixture.");}
    internal static class ReplayStorageJobs {internal static Task<T> Run<T>(Func<T> action,System.Threading.CancellationToken cancellation)=>throw new InvalidOperationException("No storage jobs in this source fixture.");}
}
