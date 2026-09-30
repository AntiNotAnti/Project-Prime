#if !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
namespace MphRead.Mods.Render;
internal sealed unsafe partial class ModernGraphicsCompat
{
    private readonly Stack<Action> _attributeStack = new();
    private void SaveAttributes(AttribMask mask)
    {
        if (mask != AttribMask.AllAttribBits) throw new NotSupportedException("Only AllAttribBits is used by the compatibility renderer.");
        bool saved_wireframe = _wireframe;
        var saved_currentColor = _currentColor;
        var saved_currentNormal = _currentNormal;
        var saved_currentTexcoord = _currentTexcoord;
        var saved_colorSet = _colorSet;
        var saved_clearColor = _clearColor;
        var saved_blendSource = _blendSource;
        var saved_blendDestination = _blendDestination;
        var saved_maskRed = _maskRed;
        var saved_maskGreen = _maskGreen;
        var saved_maskBlue = _maskBlue;
        var saved_maskAlpha = _maskAlpha;
        var saved_viewportX = _viewportX;
        var saved_viewportY = _viewportY;
        var saved_viewportWidth = _viewportWidth;
        var saved_viewportHeight = _viewportHeight;
        var saved_scissorX = _scissorX;
        var saved_scissorY = _scissorY;
        var saved_scissorWidth = _scissorWidth;
        var saved_scissorHeight = _scissorHeight;
        var saved_depthWrite = _depthWrite;
        var saved_depthFunction = _depthFunction;
        var saved_cullFace = _cullFace;
        var saved_alphaFunction = _alphaFunction;
        var saved_alphaReference = _alphaReference;
        var saved_blendEquation = _blendEquation;
        var saved_clearStencil = _clearStencil;
        var saved_stencilFunction = _stencilFunction;
        var saved_stencilReference = _stencilReference;
        var saved_stencilReadMask = _stencilReadMask;
        var saved_stencilWriteMask = _stencilWriteMask;
        var saved_stencilFail = _stencilFail;
        var saved_stencilDepthFail = _stencilDepthFail;
        var saved_stencilPass = _stencilPass;
        var saved_polygonOffsetFactor = _polygonOffsetFactor;
        var saved_polygonOffsetUnits = _polygonOffsetUnits;
        var enabled = new HashSet<EnableCap>(_enabled);
        int active = _resources.ActiveTextureUnit;
        var textures = new int[32];
        for (int i = 0; i < textures.Length; i++) textures[i] = _resources.BoundTexture(i);
        _attributeStack.Push(() =>
        {
            _wireframe = saved_wireframe;
            _currentColor = saved_currentColor;
            _currentNormal = saved_currentNormal;
            _currentTexcoord = saved_currentTexcoord;
            _colorSet = saved_colorSet;
            _clearColor = saved_clearColor;
            _blendSource = saved_blendSource;
            _blendDestination = saved_blendDestination;
            _maskRed = saved_maskRed;
            _maskGreen = saved_maskGreen;
            _maskBlue = saved_maskBlue;
            _maskAlpha = saved_maskAlpha;
            _viewportX = saved_viewportX;
            _viewportY = saved_viewportY;
            _viewportWidth = saved_viewportWidth;
            _viewportHeight = saved_viewportHeight;
            _scissorX = saved_scissorX;
            _scissorY = saved_scissorY;
            _scissorWidth = saved_scissorWidth;
            _scissorHeight = saved_scissorHeight;
            _depthWrite = saved_depthWrite;
            _depthFunction = saved_depthFunction;
            _cullFace = saved_cullFace;
            _alphaFunction = saved_alphaFunction;
            _alphaReference = saved_alphaReference;
            _blendEquation = saved_blendEquation;
            _clearStencil = saved_clearStencil;
            _stencilFunction = saved_stencilFunction;
            _stencilReference = saved_stencilReference;
            _stencilReadMask = saved_stencilReadMask;
            _stencilWriteMask = saved_stencilWriteMask;
            _stencilFail = saved_stencilFail;
            _stencilDepthFail = saved_stencilDepthFail;
            _stencilPass = saved_stencilPass;
            _polygonOffsetFactor = saved_polygonOffsetFactor;
            _polygonOffsetUnits = saved_polygonOffsetUnits;
            _enabled.Clear();
            _enabled.UnionWith(enabled);
            for (int i = 0; i < textures.Length; i++)
            {
                _resources.ActiveTexture(TextureUnit.Texture0 + i);
                _resources.BindTexture(TextureTarget.Texture2D, _resources.IsTexture(textures[i]) ? textures[i] : 0);
            }
            _resources.ActiveTexture(TextureUnit.Texture0 + active);
        });
    }
    private void RestoreAttributes()
    {
        if (!_attributeStack.TryPop(out Action? restore)) throw new InvalidOperationException("Attribute stack underflow.");
        restore();
    }
}
#endif
