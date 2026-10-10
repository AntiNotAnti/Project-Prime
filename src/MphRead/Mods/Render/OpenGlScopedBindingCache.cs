#if !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;

namespace MphRead.Mods.Render;

/// <summary>
/// Strictly world-graph-scoped exact binding/raster state reuse. An unknown
/// initial GL texture unit/binding is *never* inferred from the previous frame.
/// Unknown/native display lists, attribute stacks and context transitions
/// invalidate all assumptions. No glGet state queries in the hot path.
/// </summary>
internal sealed class OpenGlScopedBindingCache
{
    internal static bool Enabled { get; } =
        (Environment.GetEnvironmentVariable("PROJECT_PRIME_GL_BIND_CACHE") == "1"
         || Array.Exists(Environment.GetCommandLineArgs(), a =>
             a.Equals("-glbindcache", StringComparison.OrdinalIgnoreCase)))
        && Environment.GetEnvironmentVariable("PROJECT_PRIME_GL_FORCE_BINDINGS") != "1"
        && !Array.Exists(Environment.GetCommandLineArgs(), a =>
            a.Equals("-gllegacybindings", StringComparison.OrdinalIgnoreCase));

    private readonly Dictionary<TextureUnit, int> _boundTextures = new();
    private TextureUnit? _unit;
    private TriangleFace? _cullFace;
    private OpenTK.Graphics.OpenGL.PolygonMode? _polygonMode;
    private float? _lineWidth;
    internal bool Active { get; private set; }

    internal void Begin()
    {
        Invalidate();
        Active = true;
    }

    internal void End()
    {
        Active = false;
        Invalidate();
    }

    internal void Invalidate()
    {
        _unit = null;
        _boundTextures.Clear();
        _cullFace = null;
        _polygonMode = null;
        _lineWidth = null;
    }

    internal void ResetContext() => End();

    internal bool ShouldActivateTexture(TextureUnit unit)
    {
        if (!Active) return true;
        if (_unit == unit) return false;
        _unit = unit;
        return true;
    }

    internal bool ShouldBindTexture(TextureTarget target, int texture)
    {
        // Only 2D texture binds with a known explicitly-selected unit are
        // cacheable. First calls and all unknown targets reach the driver.
        if (!Active || !_unit.HasValue || target != TextureTarget.Texture2D)
            return true;
        if (_boundTextures.TryGetValue(_unit.Value, out int current)
            && current == texture)
            return false;
        _boundTextures[_unit.Value] = texture;
        return true;
    }

    internal bool ShouldCullFace(TriangleFace face)
    {
        if (!Active) return true;
        if (_cullFace == face) return false;
        _cullFace = face;
        return true;
    }

    internal bool ShouldPolygonMode(TriangleFace face,
        OpenTK.Graphics.OpenGL.PolygonMode mode)
    {
        if (!Active || face != TriangleFace.FrontAndBack)
        {
            _polygonMode = null;
            return true;
        }
        if (_polygonMode == mode) return false;
        _polygonMode = mode;
        return true;
    }

    internal bool ShouldLineWidth(float width)
    {
        if (!Active) return true;
        if (_lineWidth == width) return false;
        _lineWidth = width;
        return true;
    }

    internal void DeleteTexture(int id)
    {
        if (!Active) return;
        // Binding zero or ID reuse after deletion can invalidate the prior
        // recorded binding. Err on the side of fresh driver calls.
        Invalidate();
    }
}
#endif
