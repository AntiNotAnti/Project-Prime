using System;
using System.Diagnostics;
using System.Linq;

namespace MphRead.Mods.Network;

internal enum CombatProfileSection : byte { BeginShot, EndShot, HistoricalGeometry, EmitPending, NativeCatchup, Supports }
internal readonly record struct CombatProfileRow(string Section,long Calls,long AllocatedBytes,
    double TotalMilliseconds,int WindowSamples,double? P50,double? P95,double? P99,double? P999,long[] WindowTicks);

/// <summary>Opt-in, fixed-memory owner-thread profiling. Nested scopes overlap; totals must not be summed.</summary>
internal static class NetCombatProfile
{
    internal const int Capacity=2048;
    private sealed class Cell
    {
        internal readonly long[] Ticks=new long[Capacity];
        internal long Count, Total, Allocations;
    }
    private static readonly Cell[] Cells=Enum.GetValues<CombatProfileSection>().Select(_=>new Cell()).ToArray();
    private static uint _epoch;
    internal static bool Enabled { get; set; }
    internal readonly struct Scope : IDisposable
    {
        private readonly long _started,_allocated;
        private readonly CombatProfileSection _section;
        private readonly uint _generation;
        private readonly bool _enabled;
        internal Scope(CombatProfileSection section)
        {
            _section=section;_enabled=Enabled;_generation=_epoch;
            _allocated=_enabled?GC.GetAllocatedBytesForCurrentThread():0;
            _started=_enabled?Stopwatch.GetTimestamp():0;
        }
        public void Dispose()
        {
            if(!_enabled || _generation!=_epoch) return;
            long elapsed=Stopwatch.GetTimestamp()-_started;
            var cell=Cells[(int)_section];
            cell.Allocations+=GC.GetAllocatedBytesForCurrentThread()-_allocated;
            cell.Ticks[cell.Count++%Capacity]=elapsed;cell.Total+=elapsed;
        }
    }
    internal static Scope Measure(CombatProfileSection section)=>new(section);
    internal static CombatProfileRow[] Capture()
    {
        var result=new CombatProfileRow[Cells.Length];
        for(int i=0;i<Cells.Length;i++)
        {
            var c=Cells[i];int count=(int)Math.Min(c.Count,Capacity);
            long[] samples=c.Ticks.AsSpan(0,count).ToArray();Array.Sort(samples);
            double? Q(double p)=>count==0?null:samples[Math.Min(count-1,(int)Math.Ceiling(p*count)-1)]*1000.0/Stopwatch.Frequency;
            result[i]=new(((CombatProfileSection)i).ToString(),c.Count,c.Allocations,c.Total*1000.0/Stopwatch.Frequency,
                count,Q(.5),Q(.95),Q(.99),Q(.999),samples);
        }
        return result;
    }
    internal static void Reset()
    {
        _epoch++;
        foreach(var cell in Cells){Array.Clear(cell.Ticks);cell.Count=cell.Total=cell.Allocations=0;}
    }
}
