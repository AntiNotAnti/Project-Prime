using System;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

internal static class ImpactPresentationTests
{
    internal static int Run()
    {
        int checks=0;
        void Check(bool value,string label){checks++;if(!value)throw new Exception(label);}
        try
        {
            var impact=LiveImpactTests.Fixture;
            Vector3 position=new(10,20,30), velocity=new(1,0,0);
            Check(ImpactPresentationRules.Point(impact,position)==position+impact.Presentation.BodyOffset,"known collision offset follows moving victim");
            Check(ImpactPresentationRules.Point(impact with {Presentation=impact.Presentation with {HasBodyOffset=false}},position)==impact.Fact.ImpactPoint,"unknown offset uses world point, never invents head location");
            Check(ImpactPresentationRules.CanCorrelate(position,velocity,position+Vector3.UnitX),"forward cue correlation");
            Check(!ImpactPresentationRules.CanCorrelate(position,velocity,position-Vector3.UnitX),"no backward trail");
            Check(!ImpactPresentationRules.CanCorrelate(position,velocity,position+Vector3.UnitY),"no sharp curve");
            Check(!ImpactPresentationRules.CanCorrelate(position,velocity,position+Vector3.UnitX*5),"bounded correction distance");
            Check(!ImpactPresentationRules.CanCorrelate(position,velocity,new(float.NaN,0,0)),"nonfinite rejected");
            Check(position==new Vector3(10,20,30) && velocity==Vector3.UnitX,"visual rules preserve simulation values");
            Check(ImpactPresentationRules.Component(0,0,false)==1 && ImpactPresentationRules.Component(0,1,false)==2,"pellet ordinals");
            Check(ImpactPresentationRules.Component(1,0,true)==0 && ImpactPresentationRules.Component(2,0,true)==0,"unproven child lineage never guesses a match");
            Check(ImpactPresentationRules.Component(uint.MaxValue,0,true)==0 && ImpactPresentationRules.Component(0,0,true)==0,"unrepresentable children fail to unknown");
            Check(ImpactPresentationRules.Component(0,15,false)==0,"ordinal overflow never aliases");
            Check(ImpactPresentationRules.SameBlast(impact,impact with{Fact=impact.Fact with{VictimSlot=2},Presentation=impact.Presentation with{Kind=CombatImpactKind.Splash}}),"multi-victim single blast");
            Check(!ImpactPresentationRules.SameBlast(impact,impact with{Presentation=impact.Presentation with{Component=2}}),"distinct pellets not coalesced");
            Check(!ImpactPresentationRules.SameBlast(impact,impact with{Fact=impact.Fact with{ShotId=10}}),"distinct shots not coalesced");
            Check(!ImpactPresentationRules.SameBlast(impact,impact with{Fact=impact.Fact with{ResolveTick=impact.Fact.ResolveTick+3}}),"continuous ticks do not disappear indefinitely");
            var unknown=impact with{Presentation=default};
            Check(!ImpactPresentationRules.SameBlast(unknown,unknown),"unknown components never guessed equal");
            // Authoritative state is only read by the visual receive lane.
            HealthShotTests.Session(); NetCombatFactPublisher.LiveEnabled=true;
            var fact=impact.Fact with {MatchId=51,AuthorityEpoch=4,ShooterGeneration=9,ShooterLifeId=2,VictimGeneration=10,VictimLifeId=7,ResolveTick=1};
            byte[] bytes=new byte[LiveCombatImpactPacket.Size];
            LiveCombatImpactPacket.Write(impact with{Fact=fact},bytes);
            var before=NetSession.RemoteStates[1];
            Check(NetLiveImpactInbox.Accept(bytes),"exact live occupant accepted");
            Check(!NetLiveImpactInbox.Accept(bytes),"duplicate accepted once");
            Check(NetSession.RemoteStates[1].Health==before.Health && NetSession.RemoteStates[1].DamageEventId==before.DamageEventId
                && NetSession.RemoteStates[1].Position==before.Position,"ingress cannot author health/damage/movement");
            HealthShotTests.Snapshot(2,HealthShotTests.State(8));
            Check(!NetLiveImpactInbox.Accept(bytes),"respawn fences stale impacts");
            NetLiveImpactInbox.Reset();Check(NetLiveImpactInbox.Count==0,"seek/session reset clears pending and seen");
            Console.WriteLine($"PASS: {checks} impact presentation assertions");return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
        finally{NetSession.Stop();NetCombatFactPublisher.LiveEnabled=false;}
    }
}
