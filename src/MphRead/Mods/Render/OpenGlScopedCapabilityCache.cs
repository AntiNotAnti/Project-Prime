#if !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;

namespace MphRead.Mods.Render;

/// <summary>
/// Tracks known capability values ONLY while the retained world graph runs.
/// This never queries the driver, skips no call with unknown initial state and
/// is reset at each graph boundary. Display lists can affect GL state, so the
/// tracker trusts only lists observed compiling without state-changing calls.
/// </summary>
internal sealed class OpenGlScopedCapabilityCache
{
    private readonly Dictionary<EnableCap, bool> _known = new();
    private readonly HashSet<int> _geometryOnlyLists = new();
    private int _compilingList;
    private bool _compilingListHasState;
    internal bool Active { get; private set; }
    internal bool IsGeometryOnlyList(int list) => _geometryOnlyLists.Contains(list);

    internal void Begin()
    {
        _known.Clear();
        Active = true;
    }

    internal void End()
    {
        Active = false;
        _known.Clear();
    }

    internal void ResetContext()
    {
        End();
        _geometryOnlyLists.Clear();
        _compilingList = 0;
        _compilingListHasState = false;
    }

    internal bool ShouldSubmit(EnableCap cap, bool enabled)
    {
        if (_compilingList != 0)
            _compilingListHasState = true;

        if (!Active || _compilingList != 0 || !Cacheable(cap))
            return true;

        if (_known.TryGetValue(cap, out bool existing) && existing == enabled)
            return false;

        _known[cap] = enabled;
        return true;
    }

    internal void Invalidate()
    {
        _known.Clear();
        if (_compilingList != 0)
            _compilingListHasState = true;
    }

    internal void BeginList(int list, ListMode mode)
    {
        Invalidate();
        _geometryOnlyLists.Remove(list);
        _compilingList = list;
        _compilingListHasState = mode != ListMode.Compile;
    }

    internal void EndList()
    {
        if (_compilingList != 0 && !_compilingListHasState)
            _geometryOnlyLists.Add(_compilingList);
        _compilingList = 0;
        _compilingListHasState = false;
        Invalidate();
    }

    internal void DeleteLists(int first, int count)
    {
        for (int i = 0; i < count; i++)
            _geometryOnlyLists.Remove(first + i);
        Invalidate();
    }

    internal void CallList(int list)
    {
        // OpenGL compiles arbitrary state into display lists. If a list was
        // not created through the facade, or compiled a capability change,
        // no previously inferred state is trustworthy after it executes.
        if (Active && !_geometryOnlyLists.Contains(list))
            Invalidate();
        if (_compilingList != 0)
            _compilingListHasState = true;
    }

    private static bool Cacheable(EnableCap cap) => cap is
        EnableCap.AlphaTest or EnableCap.Blend or EnableCap.CullFace
        or EnableCap.DepthTest or EnableCap.StencilTest
        or EnableCap.ScissorTest or EnableCap.PolygonOffsetFill;
}
#endif
