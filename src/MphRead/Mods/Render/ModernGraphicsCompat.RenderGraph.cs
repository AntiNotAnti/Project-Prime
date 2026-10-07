#if !MPHREAD_SERVER
using OpenTK.Graphics.OpenGL;

namespace MphRead.Mods.Render
{
    internal sealed unsafe partial class ModernGraphicsCompat
    {
        internal static void ConfigureRetainedWorldPass(WorldRenderPassKind kind)
        {
            if (_current == null) return;
            Current.ConfigureRetainedWorldPassCore(kind);
        }

        internal static void SetRetainedWorldStencilReference(
            WorldRenderPassKind kind, int polygonId)
        {
            if (_current == null) return;
            ModernGraphicsCompat s = Current;
            s._stencilReference = polygonId;
            s._stencilReadMask = 0xFF;
            s._stencilFunction = kind switch
            {
                WorldRenderPassKind.MarkTranslucent => StencilFunction.Greater,
                WorldRenderPassKind.TranslucentBehind => StencilFunction.Notequal,
                WorldRenderPassKind.TranslucentFront => StencilFunction.Equal,
                _ => StencilFunction.Always
            };
        }

        internal static void FinishRetainedWorldGraph()
        {
            if (_current == null) return;
            ModernGraphicsCompat s = Current;
            s._depthWrite = true;
            s._enabled.Remove(EnableCap.AlphaTest);
            s._enabled.Remove(EnableCap.StencilTest);
            s._enabled.Remove(EnableCap.PolygonOffsetFill);
            s._polygonOffsetFactor = 0;
            s._polygonOffsetUnits = 0;
            s._wireframe = false;
        }

        private void ConfigureRetainedWorldPassCore(WorldRenderPassKind kind)
        {
            switch (kind)
            {
            case WorldRenderPassKind.Opaque:
                _maskRed = _maskGreen = _maskBlue = _maskAlpha = true;
                _enabled.Add(EnableCap.DepthTest);
                _enabled.Add(EnableCap.AlphaTest);
                _alphaFunction = AlphaFunction.Equal;
                _alphaReference = 1f;
                _depthFunction = DepthFunction.Less;
                _depthWrite = true;
                _enabled.Add(EnableCap.StencilTest);
                _stencilWriteMask = 0xFF;
                _stencilFail = OpenTK.Graphics.OpenGL.StencilOp.Zero;
                _stencilDepthFail = OpenTK.Graphics.OpenGL.StencilOp.Zero;
                _stencilPass = OpenTK.Graphics.OpenGL.StencilOp.Zero;
                _stencilFunction = StencilFunction.Always;
                _stencilReference = 0;
                _stencilReadMask = 0xFF;
                _enabled.Remove(EnableCap.PolygonOffsetFill);
                _polygonOffsetFactor = 0;
                _polygonOffsetUnits = 0;
                _enabled.Remove(EnableCap.Blend);
                _blendEquation = BlendEquationMode.FuncAdd;
                _blendSource = BlendingFactor.SrcAlpha;
                _blendDestination = BlendingFactor.OneMinusSrcAlpha;
                break;

            case WorldRenderPassKind.Decal:
                _enabled.Remove(EnableCap.AlphaTest);
                _enabled.Add(EnableCap.PolygonOffsetFill);
                _polygonOffsetFactor = -1;
                _polygonOffsetUnits = -1;
                _depthFunction = DepthFunction.Lequal;
                _enabled.Add(EnableCap.Blend);
                _blendEquation = BlendEquationMode.FuncAdd;
                _blendSource = BlendingFactor.SrcAlpha;
                _blendDestination = BlendingFactor.OneMinusSrcAlpha;
                break;

            case WorldRenderPassKind.MarkTranslucent:
                _enabled.Remove(EnableCap.PolygonOffsetFill);
                _polygonOffsetFactor = 0;
                _polygonOffsetUnits = 0;
                _enabled.Add(EnableCap.AlphaTest);
                _alphaFunction = AlphaFunction.Less;
                _alphaReference = 1f;
                _maskRed = _maskGreen = _maskBlue = _maskAlpha = false;
                _stencilFail = OpenTK.Graphics.OpenGL.StencilOp.Keep;
                _stencilDepthFail = OpenTK.Graphics.OpenGL.StencilOp.Keep;
                _stencilPass = OpenTK.Graphics.OpenGL.StencilOp.Replace;
                _stencilFunction = StencilFunction.Greater;
                _stencilReference = 0;
                _stencilReadMask = 0xFF;
                break;

            case WorldRenderPassKind.RebuildDepth:
                // Preserve the mask/stencil state active at the end of the
                // translucent-mark pass while clearing only depth, exactly as
                // the legacy sequence does.
                ClearCore(ClearBufferMask.DepthBufferBit);
                _stencilFail = OpenTK.Graphics.OpenGL.StencilOp.Keep;
                _stencilDepthFail = OpenTK.Graphics.OpenGL.StencilOp.Keep;
                _stencilPass = OpenTK.Graphics.OpenGL.StencilOp.Keep;
                _stencilFunction = StencilFunction.Always;
                _stencilReference = 0;
                _stencilReadMask = 0xFF;
                _alphaFunction = AlphaFunction.Equal;
                _alphaReference = 1f;
                break;

            case WorldRenderPassKind.TranslucentBehind:
                _alphaFunction = AlphaFunction.Less;
                _alphaReference = 1f;
                _maskRed = _maskGreen = _maskBlue = _maskAlpha = true;
                _depthWrite = false;
                _depthFunction = DepthFunction.Lequal;
                _stencilFail = OpenTK.Graphics.OpenGL.StencilOp.Keep;
                _stencilDepthFail = OpenTK.Graphics.OpenGL.StencilOp.Keep;
                _stencilPass = OpenTK.Graphics.OpenGL.StencilOp.Keep;
                _stencilFunction = StencilFunction.Notequal;
                _stencilReadMask = 0xFF;
                break;

            case WorldRenderPassKind.TranslucentFront:
                _stencilFail = OpenTK.Graphics.OpenGL.StencilOp.Keep;
                _stencilDepthFail = OpenTK.Graphics.OpenGL.StencilOp.Keep;
                _stencilPass = OpenTK.Graphics.OpenGL.StencilOp.Keep;
                _stencilFunction = StencilFunction.Equal;
                _stencilReadMask = 0xFF;
                break;

            case WorldRenderPassKind.TranslucentSingle:
                _enabled.Remove(EnableCap.PolygonOffsetFill);
                _enabled.Add(EnableCap.AlphaTest);
                _alphaFunction = AlphaFunction.Less;
                _alphaReference = 1f;
                _maskRed = _maskGreen = _maskBlue = _maskAlpha = true;
                _enabled.Add(EnableCap.DepthTest);
                _depthWrite = false;
                _depthFunction = DepthFunction.Lequal;
                _enabled.Remove(EnableCap.StencilTest);
                _stencilWriteMask = 0;
                _stencilFunction = StencilFunction.Always;
                _enabled.Add(EnableCap.Blend);
                _blendEquation = BlendEquationMode.FuncAdd;
                _blendSource = BlendingFactor.SrcAlpha;
                _blendDestination = BlendingFactor.OneMinusSrcAlpha;
                break;
            }
        }
    }
}
#endif
