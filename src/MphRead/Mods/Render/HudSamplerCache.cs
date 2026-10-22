using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;

namespace MphRead.Mods.Render;

/// <summary>Exact sampler state observed within one HUD pass; never infers initial bindings.</summary>
internal sealed class HudSamplerCache
{
    internal static readonly bool Enabled = OpenGlOptimizationPolicy.Enabled("HUD_SAMPLERS");
    private readonly Dictionary<TextureUnit, int> _bindings = new();
    private readonly Dictionary<(int Texture, TextureParameterName Parameter), int> _parameters = new();
    private TextureUnit? _unit;
    private bool _compilingList;
    internal bool Active { get; private set; }
    internal void Begin() { Invalidate(); _compilingList = false; Active = true; }
    internal void End() { Active = false; _compilingList = false; Invalidate(); }
    internal void BeginList() { Invalidate(); _compilingList = true; }
    internal void EndList() { Invalidate(); _compilingList = false; }
    internal void Invalidate() { _unit = null; _bindings.Clear(); _parameters.Clear(); }
    internal void Activate(TextureUnit unit) { if (Active) _unit = unit; }
    internal void Bind(TextureTarget target, int texture)
    {
        if (!Active) return;
        if (target != TextureTarget.Texture2D || !_unit.HasValue) { Invalidate(); return; }
        _bindings[_unit.Value] = texture;
    }
    internal bool ShouldSet(TextureTarget target, TextureParameterName parameter, int value)
    {
        // Cache only the four HUD sampler scalars, on explicitly bound nonzero textures.
        if (!Active || _compilingList || target != TextureTarget.Texture2D || !_unit.HasValue
            || !_bindings.TryGetValue(_unit.Value, out int texture) || texture <= 0
            || parameter is not (TextureParameterName.TextureMinFilter or TextureParameterName.TextureMagFilter
                or TextureParameterName.TextureWrapS or TextureParameterName.TextureWrapT)) return true;
        var key = (texture, parameter);
        if (_parameters.TryGetValue(key, out int known) && known == value) return false;
        _parameters[key] = value;
        return true;
    }
}
