using System;
using MphRead.Mods.Network;
namespace MphRead.NetTest;
internal static class ClaimEarlySettlementTests
{
    internal static int Run()
    {
        int checks=0;
        void Check(bool value,string label){checks++;if(!value)throw new Exception(label);}
        try
        {
            for(int mask=0;mask<16;mask++)
            {
                var d=ClaimEarlySettlementDecision.Evaluate((mask&1)!=0,(mask&2)!=0,(mask&4)!=0,(mask&8)!=0,99,128);
                Check(d.Eligible==(mask==15),"every independent proof fence required");
                Check(d.HypotheticalHealth==0,"lethal hypothesis only");
            }
            Check(ClaimEarlySettlementDecision.Evaluate(true,true,true,true,99,20).HypotheticalHealth==79,"body hypothesis");
            Check(NetClaimEarlySettlement.Configure("off") && NetClaimEarlySettlement.Configure("shadow"),"safe rollout modes");
            Check(!NetClaimEarlySettlement.Configure("enabled") && !NetClaimEarlySettlement.Configure("anything"),"unsafe enabled mode fails explicitly");
            var claim=new HitClaimPacket{MatchId=1,AuthorityEpoch=2,ShotId=3,ShooterGeneration=4,ShooterLifeId=5,
                VictimSlot=1,VictimGeneration=6,VictimLifeId=7,ClaimId=8,Beam=(byte)BeamType.Imperialist,Flags=HitClaimPacket.FlagDirect,Damage=128};
            for(int i=0;i<1000;i++)NetClaimEarlySettlement.Observe(claim,0,true,0,99);
            Check(NetClaimEarlySettlement.Proven==1000 && NetClaimEarlySettlement.Eligible==0,"native proof alone never licenses early application");
            Check(NetClaimEarlySettlement.Snapshot().Length==512,"bounded shadow history");
            Check(NetClaimEarlySettlement.Snapshot()[0].Decision.Blockers==(EarlyClaimBlockers.UnfencedNativeComponent|EarlyClaimBlockers.OpenAttackOrder),"current production blockers explicit");
            NetClaimEarlySettlement.Reset();Check(NetClaimEarlySettlement.Snapshot().Length==0,"lifecycle reset");
            Console.WriteLine($"PASS: {checks} early settlement safety assertions");return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
        finally{NetClaimEarlySettlement.Reset();NetClaimEarlySettlement.Mode=EarlyClaimMode.Shadow;}
    }
}
