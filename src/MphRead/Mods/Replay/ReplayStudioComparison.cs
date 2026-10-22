#if !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using MphRead.Mods.Network;

namespace MphRead.Mods.Replay;

internal sealed record ReplayComparisonPose(int Slot,float X,float Y,float Z,int Health,int Weapon,
    ushort Generation,ushort Life);
internal sealed record ReplayPacketEvidence(uint Frame,string DecoderHash,ImmutableArray<ReplayComparisonPose> Players)
{
    public uint? RestoredCheckpointFrame { get; init; }
    public int FramesScanned { get; init; }
    public string? IndexFallbackReason { get; init; }
}
internal sealed record ReplayPoseDifference(int Slot,double Distance,int HealthA,int HealthB,int WeaponA,int WeaponB,
    bool SameLife);
internal sealed record ReplayPacketComparison(uint Frame,bool DecoderEqual,ImmutableArray<ReplayPoseDifference> Players,
    ImmutableArray<int> OnlyA,ImmutableArray<int> OnlyB);

internal static class ReplayStudioComparison
{
    // Detached packet-only compare. The optional durable reader is never a
    // Scene, does not create GL resources and does not touch foreground playback.
    internal static ReplayPacketEvidence Read(string path,uint frame,CancellationToken cancellation,bool preferIndexed=true)
    {
        var host=new PassiveReplaySessionHost();
        using var reader=new ReplayPlaybackSession(host);
        if(!reader.JoinDetached(path,cancellation))
            throw new InvalidDataException(reader.LastError??"Cannot open comparison recording.");
        if(frame>reader.LastFrame)
            throw new InvalidDataException("The comparison recording ends before the reviewed frame.");

        // Durable entries currently contain scene-world checkpoints. Their
        // decoder includes scene projection/advance effects and producer-stream
        // diagnostics, so it is not an exact packet-only baseline for this file.
        // Until an explicit packet-decoder index contract exists, preserve the
        // canonical complete packet history rather than publishing a false hash.
        string? fallback=preferIndexed && reader.DurableCheckpoints.Count>0
            ? "Saved-world indexes do not declare a packet-only decoder baseline; using complete packet history."
            : null;

        int scanned=0;
        while(!reader.AtEnd && (reader.IsWarming||reader.CurrentFrame<frame || !reader.HasSimulatedFrame))
        {
            cancellation.ThrowIfCancellationRequested();
            reader.PumpFrame();
            // This detached consumer never constructs a BombWorld. Match the
            // spatial-analysis contract after validating each frame's packets.
            host.State.DiscardDetachedDrawPackets();
            scanned++;
            if(reader.LastResult!=ReplayOpenResult.Success)
                throw new IOException(reader.LastError??"Replay comparison packet decoding failed.");
            if(!reader.IsWarming&&reader.CurrentFrame>=frame)break;
        }
        if(reader.IsWarming||reader.CurrentFrame!=frame)
            throw new InvalidDataException("The comparison recording has no completed state at the reviewed frame.");

        var players=ImmutableArray.CreateBuilder<ReplayComparisonPose>();
        for(int slot=0;slot<8;slot++)
            if(host.State.TryGetPlayer(slot,out var player))
                players.Add(new(slot,player.Position.X,player.Position.Y,player.Position.Z,
                    player.Health,player.CurrentWeapon,player.SlotGeneration,player.LifeId));
        string hash=Convert.ToHexString(SHA256.HashData(host.State.CaptureCheckpoint().Bytes));
        return new(frame,hash,players.ToImmutable()){RestoredCheckpointFrame=null,FramesScanned=scanned,IndexFallbackReason=fallback};
    }

    internal static ReplayPacketComparison Compare(ReplayPacketEvidence a,ReplayPacketEvidence b)
    {
        if(a.Frame!=b.Frame)throw new ArgumentException("Compare exactly the same reviewed frame.");
        var rows=a.Players.Join(b.Players,p=>p.Slot,p=>p.Slot,(x,y)=>new ReplayPoseDifference(x.Slot,
            Math.Sqrt(Math.Pow(x.X-y.X,2)+Math.Pow(x.Y-y.Y,2)+Math.Pow(x.Z-y.Z,2)),
            x.Health,y.Health,x.Weapon,y.Weapon,x.Generation!=0&&x.Life!=0
                &&x.Generation==y.Generation&&x.Life==y.Life))
            .ToImmutableArray();
        return new(a.Frame,a.DecoderHash==b.DecoderHash,rows,
            a.Players.Select(p=>p.Slot).Except(b.Players.Select(p=>p.Slot)).ToImmutableArray(),
            b.Players.Select(p=>p.Slot).Except(a.Players.Select(p=>p.Slot)).ToImmutableArray());
    }
}
#endif
