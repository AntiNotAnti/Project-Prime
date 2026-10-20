using System;
using MphRead.Mods.Render.Hud;

namespace MphRead.Mods.CustomWeapons;

// Acquired-only presentation entries. Native six-slot persistence is unchanged;
// copies of its permutation are frozen for one hold, followed by M7 and a return.
internal sealed class CustomWeaponWheel
{
    private readonly CustomCycleTarget[] _entries=new CustomCycleTarget[8];
    internal int Count { get; }
    internal int Selection { get; private set; }=-1;
    private int _drag;
    private float _travel;
    internal CustomCycleTarget this[int index]=>index>=0&&index<Count?_entries[index]:throw new ArgumentOutOfRangeException(nameof(index));
    internal CustomWeaponWheel(ReadOnlySpan<BeamType> nativeSix,ReadOnlySpan<int> order,BeamType fallback,bool selected)
    {
        if(nativeSix.Length!=6||order.Length!=6)throw new ArgumentException("Six native wheel entries required.");
        if((int)fallback<0||(int)fallback>8)throw new ArgumentException("Native return weapon required.");
        int seen=0,seenBeams=0;
        for(int i=0;i<6;i++)
        {
            int native=order[i];if((uint)native>=6||(seen&(1<<native))!=0)throw new ArgumentException("Invalid native wheel permutation.");
            var beam=nativeSix[native];
            if(beam is not (BeamType.VoltDriver or BeamType.Battlehammer or BeamType.Imperialist
                or BeamType.Judicator or BeamType.Magmaul or BeamType.ShockCoil)||(seenBeams&(1<<(int)beam))!=0)
                throw new ArgumentException("Distinct native affinity entries required.");
            seenBeams|=1<<(int)beam;seen|=1<<native;_entries[i]=new(false,beam);
        }
        _entries[6]=new(true,BeamType.None);Count=7;
        bool contains=false;for(int i=0;i<6;i++)contains|=_entries[i].Native==fallback;
        if(!contains){_entries[7]=new(false,fallback);Count=8;}
        _drag=selected?6:-1;
        if(!selected)for(int i=0;i<Count;i++)if(!_entries[i].Custom&&_entries[i].Native==fallback)_drag=i;
    }
    internal int Point(int index,ushort enabled)
    {Selection=index>=0&&index<Count&&(enabled&(1<<index))!=0?index:-1;return Selection;}
    internal int Drag(float delta,float step,ushort enabled)
    {
        if(!float.IsFinite(delta))return Point(-1,enabled);
        if(!float.IsFinite(step)||step<=0)step=64;
        // Input magnitudes are bounded before iterating; extreme accumulated
        // pointer values cannot turn a single frame into an unbounded loop.
        step=Math.Clamp(step,.001f,1_000_000f);
        delta=Math.Clamp(delta,-step*Count,step*Count);
        _travel=Math.Clamp(_travel+delta,-step*Count,step*Count);
        while(_travel>=step){_travel-=step;Move(1,enabled);}
        while(_travel<=-step){_travel+=step;Move(-1,enabled);}
        return Point(_drag,enabled);
    }
    private void Move(int direction,ushort enabled)
    {
        int start=_drag<0?(direction>0?0:Count-1):_drag+direction;
        for(int i=start;i>=0&&i<Count;i+=direction)
            if((enabled&(1<<i))!=0){_drag=i;return;}
    }
    internal static int Direction(float x,float y,float threshold,int count)
    {
        if(count is not (7 or 8)||!float.IsFinite(x)||!float.IsFinite(y)||!float.IsFinite(threshold)
            ||threshold<0||x*x+y*y<threshold*threshold||x==0&&y==0)return -1;
        // Normalize in double precision before wrapping. Float addition of Tau
        // at exact left/right axes can otherwise round a sector boundary down.
        double sector=Math.Atan2(x,y)/Math.Tau*count;if(sector<0)sector+=count;
        return Math.Clamp((int)sector,0,count-1);
    }
    internal static HudSpriteBounds Bounds(int index,int count,in HudSpriteBounds zone)
    {
        if(count is not (7 or 8)||(uint)index>=count||zone.Width<=0||zone.Height<=0
            ||!float.IsFinite(zone.Left)||!float.IsFinite(zone.Top)||!float.IsFinite(zone.Width)||!float.IsFinite(zone.Height))return default;
        float extent=Math.Min(zone.Width,zone.Height),angle=(index+.5f)*MathF.Tau/count;
        float x=zone.Left+zone.Width/2+MathF.Sin(angle)*extent*.36f;
        float y=zone.Top+zone.Height/2-MathF.Cos(angle)*extent*.36f;
        return new(x-extent*.135f,y-extent*.04f,extent*.27f,extent*.08f);
    }
}
