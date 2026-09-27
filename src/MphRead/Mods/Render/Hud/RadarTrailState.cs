using System;
using System.Numerics;
namespace MphRead.Mods.Render.Hud;

/// <summary>Fixed presentation history; never advanced by rendering.</summary>
public struct RadarTrailState
{
    private Vector3 _current, _a, _b, _c, _d;
    private ulong _tick, _identity;
    private bool _valid;
    public int Count { get; private set; }
    public void Clear() { this=default; }
    public void Sample(Vector3 position, ulong tick, ulong identity, bool eligible)
    {
        if(!eligible) { Clear(); return; }
        if(!_valid || identity!=_identity || tick<_tick || tick>_tick+2 || Vector3.DistanceSquared(position,_current)>16)
        { Clear(); _valid=true; _current=position; _tick=tick; _identity=identity; return; }
        if(tick==_tick) return;
        _d=_c; _c=_b; _b=_a; _a=_current; _current=position; _tick=tick;
        Count=Math.Min(4,Count+1);
    }
    public Vector3 Get(int index) => index switch { 0=>_a,1=>_b,2=>_c,3=>_d,_=>throw new ArgumentOutOfRangeException(nameof(index)) };
}
