using System;
using MphRead.Mods.Network;
namespace MphRead.NetTest;
internal static class CombatProfileTests
{
    internal static int Run()
    {
        try
        {
            NetCombatProfile.Reset();NetCombatProfile.Enabled=false;
            using(var warm=NetCombatProfile.Measure(CombatProfileSection.Supports)){}
            long bytes=GC.GetAllocatedBytesForCurrentThread();
            for(int i=0;i<10000;i++){using var scope=NetCombatProfile.Measure(CombatProfileSection.Supports);}
            if(GC.GetAllocatedBytesForCurrentThread()!=bytes || NetCombatProfile.Capture()[5].Calls!=0)
                throw new Exception("disabled profile must allocate/retain nothing");
            NetCombatProfile.Enabled=true;
            for(int i=0;i<5000;i++){using var scope=NetCombatProfile.Measure(CombatProfileSection.Supports);}
            var result=NetCombatProfile.Capture()[5];
            if(result.Calls!=5000 || result.WindowSamples!=2048 || result.AllocatedBytes!=0 || result.P99<result.P50)
                throw new Exception("profile bounds/count/quantiles");
            using(var scope=NetCombatProfile.Measure(CombatProfileSection.Supports)){NetCombatProfile.Reset();}
            if(NetCombatProfile.Capture()[5].Calls!=0)throw new Exception("old scope crosses reset");
            Console.WriteLine("PASS: 3 profiling contracts (10,000 disabled / 5,000 measured scopes)");return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
        finally{NetCombatProfile.Enabled=false;NetCombatProfile.Reset();}
    }
}
