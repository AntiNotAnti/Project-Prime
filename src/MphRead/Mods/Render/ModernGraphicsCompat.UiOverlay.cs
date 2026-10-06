#if !MPHREAD_SERVER
using System;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace MphRead.Mods.Render;

internal sealed unsafe partial class ModernGraphicsCompat
{
    // Recovery preserves the logical resource registry; a new renderer creates
    // another one. Read ownership/teardown health without invoking Current.
    internal static object? UiResourceOwner => _current?._resources;
    internal static bool UiNativeResourcesAvailable => _current != null
        && !_current._disposed && _deviceRecoveryFailure == null && !_current._device.IsLost;

    private static readonly EnableCap[] UiOverlayCaps =
    {
        EnableCap.DepthTest, EnableCap.CullFace, EnableCap.AlphaTest,
        EnableCap.StencilTest, EnableCap.ScissorTest, EnableCap.Blend, EnableCap.Texture2D
    };

    internal static UiOverlayStateScope CaptureUiOverlayState() => new(Current);

    // Exact state modified by FullscreenUiOverlay. This keeps animated panels
    // from allocating the general attrib stack's closure/hashset/32-unit array.
    internal readonly struct UiOverlayStateScope : IDisposable
    {
        private readonly ModernGraphicsCompat _owner;
        private readonly uint _caps, _colorMask;
        private readonly int _program, _framebuffer, _activeTexture, _texture0, _texture1;
        private readonly int _x, _y, _width, _height;
        private readonly Vector4 _color;
        private readonly Vector3 _texcoord;
        private readonly bool _colorSet, _depthWrite, _wireframe;
        private readonly BlendingFactor _source, _destination;
        private readonly BlendEquationMode _equation;

        internal UiOverlayStateScope(ModernGraphicsCompat owner)
        {
            _owner = owner;
            _caps = 0;
            for (int i = 0; i < UiOverlayCaps.Length; i++)
                if (owner._enabled.Contains(UiOverlayCaps[i])) _caps |= 1u << i;
            _colorMask = (owner._maskRed ? 1u : 0) | (owner._maskGreen ? 2u : 0)
                | (owner._maskBlue ? 4u : 0) | (owner._maskAlpha ? 8u : 0);
            _program = owner._programs.CurrentProgram;
            _framebuffer = owner._resources.DrawFramebuffer;
            _activeTexture = owner._resources.ActiveTextureUnit;
            _texture0 = owner._resources.BoundTexture(0);
            _texture1 = owner._resources.BoundTexture(1);
            _x = owner._viewportX; _y = owner._viewportY;
            _width = owner._viewportWidth; _height = owner._viewportHeight;
            _color = owner._currentColor; _texcoord = owner._currentTexcoord;
            _colorSet = owner._colorSet; _depthWrite = owner._depthWrite; _wireframe = owner._wireframe;
            _source = owner._blendSource; _destination = owner._blendDestination;
            _equation = owner._blendEquation;
        }

        public void Dispose()
        {
            var current = _current;
            // Recovery keeps this registry. Terminal teardown or a fresh
            // renderer must never receive a former owner's state or IDs.
            if (_owner == null || current == null || current._disposed
                || !ReferenceEquals(_owner._resources, current._resources)) return;
            for (int i = 0; i < UiOverlayCaps.Length; i++)
            {
                if ((_caps & (1u << i)) != 0) current._enabled.Add(UiOverlayCaps[i]);
                else current._enabled.Remove(UiOverlayCaps[i]);
            }
            current._maskRed = (_colorMask & 1) != 0; current._maskGreen = (_colorMask & 2) != 0;
            current._maskBlue = (_colorMask & 4) != 0; current._maskAlpha = (_colorMask & 8) != 0;
            current._viewportX = _x; current._viewportY = _y;
            current._viewportWidth = _width; current._viewportHeight = _height;
            current._currentColor = _color; current._currentTexcoord = _texcoord;
            current._colorSet = _colorSet; current._depthWrite = _depthWrite; current._wireframe = _wireframe;
            current._blendSource = _source; current._blendDestination = _destination; current._blendEquation = _equation;
            current._programs.UseProgram(_program);
            current._resources.BindFramebuffer(FramebufferTarget.DrawFramebuffer, _framebuffer);
            current._resources.ActiveTexture(TextureUnit.Texture0);
            current._resources.BindTexture(TextureTarget.Texture2D, current._resources.IsTexture(_texture0) ? _texture0 : 0);
            current._resources.ActiveTexture(TextureUnit.Texture1);
            current._resources.BindTexture(TextureTarget.Texture2D, current._resources.IsTexture(_texture1) ? _texture1 : 0);
            current._resources.ActiveTexture(TextureUnit.Texture0 + _activeTexture);
        }
    }
}
#endif
