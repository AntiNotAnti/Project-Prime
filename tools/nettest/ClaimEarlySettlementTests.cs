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
            Check(NetClaimEarlySettlement.Configure("enabled") && !NetClaimEarlySettlement.Configure("anything"),"explicit enabled mode and malformed mode refused");
            Check(!NetClaimEarlySettlement.HorizonClosed(145,100,45) && !NetClaimEarlySettlement.HorizonClosed(146,100,45)
                && NetClaimEarlySettlement.HorizonClosed(147,100,45),"fractional admission boundary closes conservatively");
            Check(!NetClaimEarlySettlement.HorizonClosed(10,uint.MaxValue,45),"wrapped/unknown ordering fails closed");
            NetClaimEarlySettlement.Seal(100); NetClaimEarlySettlement.Seal(90);
            Check(!NetClaimEarlySettlement.AdmissionOpen(100) && NetClaimEarlySettlement.AdmissionOpen(101),"closed frontier cannot move backwards");
            NetClaimEarlySettlement.Reset();
            var claim=new HitClaimPacket{MatchId=1,AuthorityEpoch=2,ShotId=3,ShooterGeneration=4,ShooterLifeId=5,
                VictimSlot=1,VictimGeneration=6,VictimLifeId=7,ClaimId=8,Beam=(byte)BeamType.Imperialist,Flags=HitClaimPacket.FlagDirect,Damage=128};
            for(int i=0;i<1000;i++)NetClaimEarlySettlement.Observe(claim,0,true,0,99);
            Check(NetClaimEarlySettlement.Proven==1000 && NetClaimEarlySettlement.Eligible==0,"native proof alone never licenses early application");
            Check(NetClaimEarlySettlement.Snapshot().Length==512,"bounded shadow history");
            Check(NetClaimEarlySettlement.Snapshot()[0].Decision.Blockers==(EarlyClaimBlockers.UnfencedNativeComponent|EarlyClaimBlockers.OpenAttackOrder),"current production blockers explicit");
            var index = new NetRescueIndex();
            var key = new ShotKey(1,2,0,3,4,5);
            ulong direct=NetHitClaims.ComponentKey(10,true,false), splash=NetHitClaims.ComponentKey(10,false,false),
                child=NetHitClaims.ComponentKey(11,true,false), turret=NetHitClaims.ComponentKey(10,true,true);
            Check(index.Insert(0,1,key,6,7,100,direct),"reserve exact direct component");
            for(int i=0;i<10;i++) Check(index.Consume(0,1,key,6,7,101,direct),"repeated native callbacks remain suppressed");
            Check(!index.Consume(0,1,key,6,7,101,splash) && !index.Consume(0,1,key,6,7,101,child)
                && !index.Consume(0,1,key,6,7,101,turret),"direct cannot spend splash, child or turret");
            Check(!index.Consume(0,1,key with { ShotId=6 },6,7,101,direct),"full shot identity required");
            Check(!index.Consume(0,1,key,6,7,821,direct),"paid markers expire after native retention");
            for(uint i=1;i<=512;i++) Check(index.Insert(0,1,key,6,7,900,i),"bounded component capacity");
            Check(!index.CanInsert(0,1,key,6,7,900,513),"full partition fails closed before damage");
            Check(!index.Consume(0,1,key,6,8,900,1),"new victim life discards old payment");
            index.Validate();
            NetClaimEarlySettlement.Reset();Check(NetClaimEarlySettlement.Snapshot().Length==0,"lifecycle reset");
            Console.WriteLine($"PASS: {checks} early settlement safety assertions");return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
        finally{NetClaimEarlySettlement.Reset();NetClaimEarlySettlement.Mode=EarlyClaimMode.Shadow;}
    }
}
