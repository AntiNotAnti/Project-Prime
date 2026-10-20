using System;
using MphRead;
using MphRead.Mods.Cosmetics;
using MphRead.Mods.Render;

// Pure presentation admission only: no actor, rig loading, death/HP or physics.
internal static class CorpsePresentationBudgetChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var rng=Rng.Current;var before=(rng.Rng1,rng.Rng2);
        foreach(var row in new[]{(CosmeticEffectQuality.Off,0,0f),
            (CosmeticEffectQuality.Low,2,24f),(CosmeticEffectQuality.Medium,4,48f),(CosmeticEffectQuality.High,8,72f)})
        {
            var (quality,capacity,distance)=row;float limit=distance*distance;
            check(CorpsePresentationBudget.Capacity(quality)==capacity&&CorpsePresentationBudget.Distance(quality)==distance,
                "corpse presentation quality has explicit capacity/distance "+quality);
            check(CorpsePresentationBudget.Within(quality,0)==(capacity>0)&&CorpsePresentationBudget.Within(quality,-0f)==(capacity>0),
                "zero-distance admission is enabled only for non-Off quality "+quality);
            check(CorpsePresentationBudget.Within(quality,limit)==(capacity>0),"squared-distance boundary is inclusive "+quality);
            if(capacity>0)
            {
                check(CorpsePresentationBudget.Within(quality,MathF.BitDecrement(limit)),"adjacent representable distance inside limit admits "+quality);
                check(!CorpsePresentationBudget.Within(quality,MathF.BitIncrement(limit)),"adjacent representable distance outside limit refuses "+quality);
                check(!CorpsePresentationBudget.Within(quality,float.MaxValue),"finite distance overflow-scale input refuses "+quality);
            }
            foreach(float invalid in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity,-1f,-float.Epsilon})
                check(!CorpsePresentationBudget.Within(quality,invalid),"nonfinite/negative squared distance always refuses "+quality);
        }
        foreach(var unknown in new[]{(CosmeticEffectQuality)(-1),(CosmeticEffectQuality)4,(CosmeticEffectQuality)int.MaxValue})
            check(CorpsePresentationBudget.Capacity(unknown)==0&&CorpsePresentationBudget.Distance(unknown)==0
                &&!CorpsePresentationBudget.Within(unknown,0)&&!CorpsePresentationBudget.Within(unknown,1),
                "unknown presentation quality is disabled without fallback escalation");
        for(int n=0;n<2000;n++)
        {var quality=(CosmeticEffectQuality)(n%5);CorpsePresentationBudget.Capacity(quality);CorpsePresentationBudget.Distance(quality);CorpsePresentationBudget.Within(quality,0);}
        long allocated=GC.GetAllocatedBytesForCurrentThread();int admitted=0,capacitySum=0;float distanceSum=0;
        for(int n=0;n<100_000;n++)
        {
            var quality=(CosmeticEffectQuality)(n%5);capacitySum+=CorpsePresentationBudget.Capacity(quality);
            distanceSum+=CorpsePresentationBudget.Distance(quality);if(CorpsePresentationBudget.Within(quality,0))admitted++;
        }
        allocated=GC.GetAllocatedBytesForCurrentThread()-allocated;
        check(allocated==0&&admitted==60_000&&capacitySum==280_000&&distanceSum==2_880_000,
            "100k warmed budget queries allocate0B with nonvacuous enabled/disabled results");
        check(ReferenceEquals(Rng.Current,rng)&&(rng.Rng1,rng.Rng2)==before,"presentation budget queries leave both gameplay RNG streams unchanged");
    }
}
