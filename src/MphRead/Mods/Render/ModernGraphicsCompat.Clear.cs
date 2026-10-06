#if !MPHREAD_SERVER
using System;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using Silk.NET.WebGPU;
using WgpuTextureFormat = Silk.NET.WebGPU.TextureFormat;
namespace MphRead.Mods.Render;
internal sealed unsafe partial class ModernGraphicsCompat
{
    private NativeRenderbuffer? _surfaceDepth;
    private ShaderModule* _clearShader;
    private TextureView* EnsureSurfaceDepth()
    {
        if (_surfaceDepth != null) return _surfaceDepth.View;
        var texture = _api.DeviceCreateTexture(_device.Device, new TextureDescriptor
        {
            Size = new Extent3D(_width, _height, 1), Format = WgpuTextureFormat.Depth24PlusStencil8,
            Usage = TextureUsage.RenderAttachment, MipLevelCount = 1, SampleCount = 1,
            Dimension = TextureDimension.Dimension2D
        });
        _surfaceDepth = new NativeRenderbuffer
        {
            Texture = texture, View = _api.TextureCreateView(texture, null),
            Width = (int)_width, Height = (int)_height
        };
        return _surfaceDepth.View;
    }
    private void ReleaseSurfaceDepth()
    {
        if (_surfaceDepth == null) return;
        ReleaseNativeRenderbuffer(_surfaceDepth);
        _surfaceDepth = null;
    }
    private void ClearPartial(ClearBufferMask mask)
    {
        if (_enabled.Contains(EnableCap.ScissorTest) && (_scissorWidth <= 0 || _scissorHeight <= 0)) return;
        int program = _programs.CurrentProgram;
        SaveAttributes(AttribMask.AllAttribBits);
        try
        {
            bool scissor = _enabled.Contains(EnableCap.ScissorTest);
            _enabled.Clear();
            if (scissor) _enabled.Add(EnableCap.ScissorTest);
            _programs.UseProgram(0);
            _depthWrite &= (mask & ClearBufferMask.DepthBufferBit) != 0;
            if (_depthWrite) _enabled.Add(EnableCap.DepthTest);
            _depthFunction = DepthFunction.Always;
            if ((mask & ClearBufferMask.StencilBufferBit) != 0)
            {
                _enabled.Add(EnableCap.StencilTest);
                _stencilFunction = StencilFunction.Always;
                _stencilReference = _clearStencil;
                _stencilPass = OpenTK.Graphics.OpenGL.StencilOp.Replace;
            }
            if ((mask & ClearBufferMask.ColorBufferBit) == 0)
                _maskRed = _maskGreen = _maskBlue = _maskAlpha = false;
            _currentColor = _clearColor;
            var geometry = new LegacyGeometryBatch();
            geometry.Begin(PrimitiveType.Triangles);
            foreach (var p in new[] { new Vector3(-1, -1, 1), new Vector3(3, -1, 1), new Vector3(-1, 3, 1) })
                geometry.AddVertex(p, _clearColor, Vector3.UnitZ, Vector3.Zero, true);
            geometry.End();
            DrawCoreIndexed(geometry.Vertices.ToArray(), geometry.TriIndices.ToArray(),
                PrimitiveTopology.TriangleList, ModernProgramKind.Clear);
        }
        finally
        {
            _programs.UseProgram(program);
            RestoreAttributes();
        }
    }
}
#endif
