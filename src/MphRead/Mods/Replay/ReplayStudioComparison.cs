#if !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using MphRead.Mods.Network;

namespace MphRead.Mods.Replay;
internal sealed record ReplayComparisonPose(int Slot,float X,float Y,float Z,int Health,int Weapon);
internal sealed record ReplayPacketEvidence(uint Frame,string DecoderHash,ImmutableArray<ReplayComparisonPose> Players);
internal sealed record ReplayPoseDifference(int Slot,double Distance,int HealthA,int HealthB,int WeaponA,int WeaponB);
internal sealed record ReplayPacketComparison(uint Frame,bool DecoderEqual,ImmutableArray<ReplayPoseDifference> Players,ImmutableArray<int> OnlyA,ImmutableArray<int> OnlyB);
internal static class ReplayStudioComparison
{
    internal static ReplayPacketEvidence Read(string path,uint frame,CancellationToken cancellation)
    {
        var host=new PassiveReplaySessionHost();using var reader=new ReplayPlaybackSession(host);
        if(!reader.JoinDetached(path,cancellation))throw new InvalidDataException(reader.LastError??"Cannot open comparison recording.");
        if(frame>reader.LastFrame)throw new InvalidDataException("The comparison recording ends before the reviewed frame.");
        while(!reader.AtEnd)
        {
            cancellation.ThrowIfCancellationRequested();reader.PumpFrame();
            if(reader.LastResult!=ReplayOpenResult.Success)throw new InvalidDataException(reader.LastError);
            if(!reader.IsWarming&&reader.CurrentFrame>=frame)break;
        }
        if(reader.IsWarming||reader.CurrentFrame!=frame)throw new InvalidDataException("The comparison recording has no completed state at the reviewed frame.");
        var players=ImmutableArray.CreateBuilder<ReplayComparisonPose>();
        for(int slot=0;slot<8;slot++)if(host.State.TryGetPlayer(slot,out var player))
            players.Add(new(slot,player.Position.X,player.Position.Y,player.Position.Z,player.Health,player.CurrentWeapon));
        string hash=Convert.ToHexString(SHA256.HashData(host.State.CaptureCheckpoint().Bytes));
        return new(frame,hash,players.ToImmutable());
    }
    internal static ReplayPacketComparison Compare(ReplayPacketEvidence a,ReplayPacketEvidence b)
    {
        if(a.Frame!=b.Frame)throw new ArgumentException("Compare exactly the same reviewed frame.");
        var rows=a.Players.Join(b.Players,p=>p.Slot,p=>p.Slot,(x,y)=>new ReplayPoseDifference(x.Slot,
            Math.Sqrt(Math.Pow(x.X-y.X,2)+Math.Pow(x.Y-y.Y,2)+Math.Pow(x.Z-y.Z,2)),x.Health,y.Health,x.Weapon,y.Weapon)).ToImmutableArray();
        return new(a.Frame,a.DecoderHash==b.DecoderHash,rows,a.Players.Select(p=>p.Slot).Except(b.Players.Select(p=>p.Slot)).ToImmutableArray(),b.Players.Select(p=>p.Slot).Except(a.Players.Select(p=>p.Slot)).ToImmutableArray());
    }
}
#endif
