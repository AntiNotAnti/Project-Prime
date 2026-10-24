using System.Security.Cryptography;
using System.Text.Json;
namespace ProjectPrime.PayloadEscort;
// This is an isolated bounded DTO, NOT a patch to the game's protocol or Hello handshake.
public sealed record EscortSnapshot(int Schema,Guid MatchId,long AuthorityEpoch,Guid MapId,string Fingerprint,
    int RoundId,long Tick,Team Attacker,RoundPhase Phase,long Progress,long BestProgress,int Checkpoints,
    int RemainingTicks,bool Overtime,int FuseTicks,int HoldTicks,int EscortCount,bool Contested);
public static class EscortReplication {
    public const int Schema=1;
    public const int MaximumBytes=4096;
    public static string Fingerprint(EscortRules rules,PayloadRoute route){
        var data=new {schema=Schema,tickRate=EscortRules.TickRate,distanceScale=EscortRules.DistanceScale,rules,mapId=route.MapId,points=route.Points.Select(p=>new[]{p.X,p.Y,p.Z}),checkpoints=route.Checkpoints};
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(data))).ToLowerInvariant();
    }
    public static EscortSnapshot Capture(EscortRound round,long epoch,PayloadRoute route,EscortRules rules)=>
        new(Schema,round.MatchId,epoch,route.MapId,Fingerprint(rules,route),round.RoundId,round.Tick,
            round.Attacker,round.Phase,round.Progress,round.BestProgress,round.CapturedCheckpoints,
            round.RemainingTicks,round.Overtime,round.FuseRemaining,round.HoldRemaining,round.Escorts,round.Contested);
    public static byte[] Encode(EscortSnapshot state){var bytes=JsonSerializer.SerializeToUtf8Bytes(state);
        if(bytes.Length>MaximumBytes)throw new ArgumentException("Snapshot too large.");return bytes;}
}
/// <summary>Bound to an authenticated match identity. Does not authenticate a socket by itself.</summary>
public sealed class SnapshotReceiver {
    private readonly long[] _checkpoints;
    private readonly Guid _match,_map;private readonly long _epoch,_length;private readonly string _fingerprint;
    public EscortSnapshot? Current { get; private set; }
    public SnapshotReceiver(Guid match,long epoch,PayloadRoute route,EscortRules rules){
        _checkpoints=route.Checkpoints.Select(c=>c.Distance).ToArray();_match=match;_epoch=epoch;_map=route.MapId;_length=route.Length;_fingerprint=EscortReplication.Fingerprint(rules,route);
    }
    public bool TryApply(ReadOnlySpan<byte> bytes){
        if(bytes.Length==0||bytes.Length>EscortReplication.MaximumBytes)return false;
        EscortSnapshot? s;
        try{s=JsonSerializer.Deserialize<EscortSnapshot>(bytes);}catch(JsonException){return false;}
        if(s is null||s.Schema!=EscortReplication.Schema||s.MatchId!=_match||s.MapId!=_map||s.AuthorityEpoch!=_epoch
            ||s.Fingerprint!=_fingerprint||s.RoundId<0||s.Tick<0||!Enum.IsDefined(s.Attacker)||!Enum.IsDefined(s.Phase)
            ||s.Progress<0||s.BestProgress<s.Progress||s.BestProgress>_length||s.Checkpoints is <0 or >2
            ||s.RemainingTicks<0||s.FuseTicks<0||s.HoldTicks<0||s.EscortCount is <0 or >4
            ||s.RemainingTicks>24*60*60*60||s.FuseTicks>3600||s.HoldTicks>36000)return false;
        long floor=s.Checkpoints==0?0:_checkpoints[s.Checkpoints-1];
        if(s.Progress<floor || (s.Checkpoints<2 && s.Progress>=_checkpoints[s.Checkpoints])
            || (s.Overtime && s.RemainingTicks>0)
            || (s.Phase==RoundPhase.Preparation && (s.Progress!=0||s.BestProgress!=0||s.Overtime)))return false;
        if(Current is {} previous && s.RoundId==previous.RoundId && (s.BestProgress<previous.BestProgress
            ||s.Checkpoints<previous.Checkpoints||s.Attacker!=previous.Attacker||(int)s.Phase<(int)previous.Phase))return false;
        if(Current is {} old && (s.RoundId<old.RoundId||(s.RoundId==old.RoundId&&s.Tick<=old.Tick)))return false;
        Current=s;return true;
    }
}
