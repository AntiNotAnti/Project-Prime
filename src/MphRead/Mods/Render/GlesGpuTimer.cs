#if ANDROID
using System;
using ES = OpenTK.Graphics.ES30;

namespace MphRead.Mods.Render;

/// <summary>
/// Optional EXT_disjoint_timer_query access. The caller owns query lifetime,
/// scopes and asynchronous polling, and must discard samples when Disjoint is true.
/// </summary>
internal sealed class GlesGpuTimer
{
    private const ES.QueryTarget TimeElapsed = (ES.QueryTarget)0x88BF;
    private const ES.GetQueryObjectParam ResultAvailable = (ES.GetQueryObjectParam)0x8867;
    private const ES.GetQueryObjectParam Result = (ES.GetQueryObjectParam)0x8866;
    internal static bool Supported => OpenGlContextPolicy.Current.DisjointTimerQueries;

    internal int Create()
    {
        if (!Supported) return 0;
        ES.GL.Ext.GenQueries(1, out int id);
        return id;
    }
    internal void Begin(int id) => ES.GL.Ext.BeginQuery(TimeElapsed, id);
    internal void End() => ES.GL.Ext.EndQuery(TimeElapsed);
    internal bool Available(int id)
    {
        ES.GL.Ext.GetQueryObject(id, ResultAvailable, out int available);
        return available != 0;
    }
    internal long Nanoseconds(int id)
    {
        // Use the unsigned overload: capability probing verifies ui64vEXT.
        // Never fetch an unavailable result, which would block the rendering thread.
        if (!Available(id)) return -1;
        ES.GL.Ext.GetQueryObject((uint)id, Result, out ulong value);
        return value <= long.MaxValue ? (long)value : -1;
    }
    internal bool Disjoint()
    {
        ES.GL.GetInteger((ES.GetPName)0x8FBB, out int disjoint);
        return disjoint != 0;
    }
    internal void Delete(int id)
    {
        if (id != 0) ES.GL.Ext.DeleteQueries(1, ref id);
    }
}
#endif
