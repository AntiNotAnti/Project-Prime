using System;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

internal enum PredictedKillState : byte { None, Pending, Confirmed, Rejected, Expired }
internal sealed class PredictedKillTicket
{
    internal ShotKey Shot { get; private set; }
    internal byte Victim { get; private set; }
    internal ushort Generation { get; private set; }
    internal ushort Life { get; private set; }
    internal ushort Claim { get; private set; }
    internal uint Started { get; private set; }
    internal PredictedKillState State { get; private set; }
    private uint _deadline, _transition;
    private float _releasePose;
    internal bool Start(in ShotKey shot,byte victim,ushort generation,ushort life,ushort claim,uint now,int ping)
    {
        if(State is PredictedKillState.Pending or PredictedKillState.Confirmed || shot.ShotId==0 || shot.AuthorityEpoch==0
            || shot.MatchId==0 || shot.Generation==0 || shot.LifeId==0 || shot.ShooterSlot is <0 or >=8
            || victim>=8 || generation==0 || life==0 || claim==0) return false;
        Shot=shot;Victim=victim;Generation=generation;Life=life;Claim=claim;Started=now;
        // RTT plus 300ms margin, clamped to 400–1000ms. Claims can outlive this visual.
        _deadline=now+(uint)Math.Clamp((int)(Math.Max(0,ping)*.06f)+18,24,60);
        State=PredictedKillState.Pending;return true;
    }
    internal bool Verdict(in ShotKey shot,byte victim,ushort generation,ushort life,ushort claim,bool lethal,uint now)
    {
        if(State!=PredictedKillState.Pending || shot!=Shot || victim!=Victim || generation!=Generation || life!=Life || claim!=Claim) return false;
        Settle(lethal?PredictedKillState.Confirmed:PredictedKillState.Rejected,now);return true;
    }
    internal void Settle(PredictedKillState state,uint now)
    {
        if(State!=PredictedKillState.Pending) return;
        _releasePose=Math.Clamp((now-Started)/10f,0,1);_transition=now;State=state;
    }
    internal float Pose(uint now)
    {
        if(State==PredictedKillState.Pending && unchecked((int)(now-_deadline))>=0) Settle(PredictedKillState.Expired,_deadline);
        if(State==PredictedKillState.None) return 0;
        if(State==PredictedKillState.Pending) return Math.Clamp((now-Started+1)/10f,0,1);
        if(State==PredictedKillState.Confirmed)
        {
            if(now-_transition<18) return 1;
            State=PredictedKillState.Expired;_releasePose=1;_transition+=18;
        }
        float release=1-Math.Clamp((now-_transition)/6f,0,1);
        if(release==0) Clear();return _releasePose*release;
    }
    internal void Clear(){State=PredictedKillState.None;Shot=default;Claim=0;}
}

internal static class NetPredictedKillPresentation
{
    internal static bool Enabled { get; set; }
    internal static long Started, Confirmed, Rejected, Expired;
    private static readonly PredictedKillTicket[] Tickets=Create();
    private static PredictedKillTicket[] Create(){var t=new PredictedKillTicket[8];for(int i=0;i<t.Length;i++)t[i]=new();return t;}
    internal static void Start(in ShotKey shot,byte victim,ushort claim)
    {
        int local=NetSession.LocalSlot;
        if(!Enabled || NetSession.Role!=NetRole.Client || local<0 || shot.ShooterSlot!=local
            || NetSession.SlotSpectating[local] || victim>=8 || victim==local
            || !NetSession.MatchesStream(shot.MatchId,shot.AuthorityEpoch)
            || !NetPlayerLifecycle.Matches(local,shot.Generation,shot.LifeId)) return;
        if (Tickets[victim].Start(shot,victim,NetPlayerLifecycle.Generation(victim),NetPlayerLifecycle.Get(victim),
            claim,NetSession.NetFrame,NetSession.SlotPing[local])) Started++;
    }
    internal static void Ack(in CombatAckEntry ack)
    {
        if(!Enabled || ack.VictimSlot>=8 || NetSession.LocalSlot<0) return;
        var shot=ShotKey.For(NetSession.LocalSlot,ack.ShotId);
        bool lethal=ack.Accepted && (ack.Flags&(CombatAckFlags.Lethal|CombatAckFlags.OutcomePresent))==(CombatAckFlags.Lethal|CombatAckFlags.OutcomePresent)
            && ack.HealthAfter==0;
        if(Tickets[ack.VictimSlot].Verdict(shot,ack.VictimSlot,ack.VictimGeneration,ack.VictimLife,ack.ClaimId,lethal,NetSession.NetFrame))
        { if(lethal) Confirmed++; else Rejected++; }

    }
    internal static void Fact(in ReplayShotFact fact)
    {
        if(!Enabled || fact.VictimSlot>=8) return;
        var ticket=Tickets[fact.VictimSlot];
        if(ticket.Generation!=fact.VictimGeneration || ticket.Life!=fact.VictimLifeId
            || ticket.Shot.MatchId!=fact.MatchId || ticket.Shot.AuthorityEpoch!=fact.AuthorityEpoch) return;
        var shot=CombatImpactIdentity.From(fact).Shot;
        if(shot==ticket.Shot)
        {
            if(ticket.Verdict(shot,fact.VictimSlot,fact.VictimGeneration,fact.VictimLifeId,ticket.Claim,fact.Lethal,NetSession.NetFrame))
            { if(fact.Lethal) Confirmed++; else Rejected++; }
        }
        else if(fact.Lethal && ticket.State==PredictedKillState.Pending)
        { ticket.Settle(PredictedKillState.Rejected,NetSession.NetFrame);Rejected++; }
    }
    internal static Matrix4 DrawPose(int victim)
    {
        if(!Enabled || (uint)victim>=8 || NetSession.Role!=NetRole.Client || NetSession.LocalSlot<0
            || Mods.SpectatorMode.IsSpectating || NetSession.SlotSpectating[NetSession.LocalSlot]) return Matrix4.Identity;
        var ticket=Tickets[victim];
        if(!NetSession.MatchesStream(ticket.Shot.MatchId,ticket.Shot.AuthorityEpoch)
            || !NetPlayerLifecycle.Matches(ticket.Shot.ShooterSlot,ticket.Shot.Generation,ticket.Shot.LifeId)
            || !NetPlayerLifecycle.Matches(victim,ticket.Generation,ticket.Life)
            || NetSession.RemoteStateValid[victim] && NetSession.RemoteStates[victim].Health==0)
        {ticket.Clear();return Matrix4.Identity;}
        // Rotate only the local render matrix around the feet. All animations, collision and health remain owned by simulation.
        bool pending=ticket.State==PredictedKillState.Pending;
        float pose=ticket.Pose(NetSession.NetFrame);
        if(pending && ticket.State is PredictedKillState.Expired or PredictedKillState.None) Expired++;
        return Matrix4.CreateRotationX(-MathF.PI*.45f*pose);
    }
    internal static void Reset(){foreach(var ticket in Tickets)ticket.Clear();Started=Confirmed=Rejected=Expired=0;}
}
