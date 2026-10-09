using System;

namespace MphRead.Mods.Network;

internal enum EarlyClaimMode : byte { Off, Shadow }
[Flags]
internal enum EarlyClaimBlockers : byte
{
    None=0, MissingNativeProof=1, UnsupportedCategory=2, UnfencedNativeComponent=4, OpenAttackOrder=8
}
internal readonly record struct ClaimEarlySettlementDecision(EarlyClaimBlockers Blockers, int HypotheticalHealth)
{
    internal bool Eligible => Blockers==EarlyClaimBlockers.None;
    internal static ClaimEarlySettlementDecision Evaluate(bool admittedAndReserved, bool narrowDirect,
        bool exactNativeSuppression, bool sourceOrderClosed, int health, uint damage)
    {
        var reasons=EarlyClaimBlockers.None;
        if(!admittedAndReserved) reasons|=EarlyClaimBlockers.MissingNativeProof;
        if(!narrowDirect) reasons|=EarlyClaimBlockers.UnsupportedCategory;
        if(!exactNativeSuppression) reasons|=EarlyClaimBlockers.UnfencedNativeComponent;
        if(!sourceOrderClosed) reasons|=EarlyClaimBlockers.OpenAttackOrder;
        return new(reasons,(int)Math.Max(0,(long)health-damage));
    }
}
internal readonly record struct ClaimShadowSample(ShotKey Shot, byte Victim, ushort VictimGeneration,
    ushort VictimLife, ushort ClaimId, uint Frame, uint Waited, ClaimEarlySettlementDecision Decision);

/// <summary>Observes proof already reserved by the existing arbitration. Never reserves or applies damage.</summary>
internal static class NetClaimEarlySettlement
{
    internal static EarlyClaimMode Mode { get; set; }=EarlyClaimMode.Shadow;
    private static readonly ClaimShadowSample[] Samples=new ClaimShadowSample[512];
    private static int _cursor, _count;
    internal static long Observed { get; private set; }
    internal static long Proven { get; private set; }
    internal static long Eligible { get; private set; }
    internal static bool Configure(string value)
    {
        if(value=="off"){Mode=EarlyClaimMode.Off;return true;}
        if(value=="shadow"){Mode=EarlyClaimMode.Shadow;return true;}
        // An enabled flag must not lie about a missing safety proof or weaken the fallback.
        return false;
    }
    internal static void Observe(in HitClaimPacket claim,int shooter,bool reserved,uint waited,int health)
    {
        if(Mode==EarlyClaimMode.Off) return;
        bool direct=claim.Beam==(byte)BeamType.Imperialist && (claim.Flags&HitClaimPacket.FlagDirect)!=0
            && (claim.Flags&(HitClaimPacket.FlagContinuousTick|HitClaimPacket.FlagHalfturret))==0;
        var decision=ClaimEarlySettlementDecision.Evaluate(reserved,direct,
            exactNativeSuppression:false,sourceOrderClosed:false,health,claim.Damage);
        // Current NetRescueIndex is shot/victim scoped. Native witness serials do not
        // flow into that suppression key, and admission has no closed source frontier.
        Samples[_cursor]=new(new(claim.AuthorityEpoch,claim.MatchId,shooter,claim.ShooterGeneration,
            claim.ShooterLifeId,claim.ShotId),claim.VictimSlot,claim.VictimGeneration,claim.VictimLifeId,
            claim.ClaimId,NetSession.NetFrame,waited,decision);
        _cursor=(_cursor+1)%Samples.Length;_count=Math.Min(_count+1,Samples.Length);
        Observed++; if(reserved) Proven++; if(decision.Eligible) Eligible++;
    }
    internal static ClaimShadowSample[] Snapshot()
    {
        var result=new ClaimShadowSample[_count];
        for(int i=0;i<_count;i++)result[i]=Samples[(_cursor-_count+Samples.Length+i)%Samples.Length];
        return result;
    }
    internal static void Reset(){Array.Clear(Samples);_cursor=_count=0;Observed=Proven=Eligible=0;}
}
