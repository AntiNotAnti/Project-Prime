#if !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;
using Silk.NET.Core.Native;
using Silk.NET.WebGPU;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;
using WgpuColor = Silk.NET.WebGPU.Color;
using WgpuSampler = Silk.NET.WebGPU.Sampler;
using WgpuTexture = Silk.NET.WebGPU.Texture;
using WgpuTextureFormat = Silk.NET.WebGPU.TextureFormat;

namespace MphRead.Mods.Render
{
    /// <summary>
    /// First live slice of the desktop WebGPU compatibility renderer.
    ///
    /// It owns the selected DX12/Vulkan/Metal surface and implements enough of
    /// the historical GL state machine to draw the launcher/Avalonia surface
    /// through the same Begin/Vertex/TexImage calls the OpenGL path uses.
    /// World/display-list state is recorded too; its full material pipeline is
    /// layered onto this executor in the next pass.
    /// </summary>
    internal sealed unsafe class ModernGraphicsCompat : IDisposable
    {
        private sealed class NativeTexture
        {
            internal WgpuTexture* Texture;
            internal TextureView* View;
            internal WgpuSampler* Sampler;
        }

        private sealed class GeometryList
        {
            internal float[] Vertices = Array.Empty<float>();
            internal int[] Triangles = Array.Empty<int>();
            internal int[] Lines = Array.Empty<int>();
        }

        private readonly record struct PipelineKey(
            PrimitiveTopology Topology,
            bool Blend,
            BlendingFactor Source,
            BlendingFactor Destination,
            ColorWriteMask WriteMask);

        private sealed class PipelineRecord
        {
            internal RenderPipeline* Pipeline;
            internal BindGroupLayout* Layout;
        }

        private const string UiShader = @"
struct VertexInput {
    @location(0) position: vec3<f32>,
    @location(1) color: vec4<f32>,
    @location(3) texcoord: vec3<f32>,
};
struct VertexOutput {
    @builtin(position) position: vec4<f32>,
    @location(0) color: vec4<f32>,
    @location(1) texcoord: vec2<f32>,
};
@group(0) @binding(0) var picture: texture_2d<f32>;
@group(0) @binding(1) var picture_sampler: sampler;

@vertex
fn vs_main(input: VertexInput) -> VertexOutput {
    var output: VertexOutput;
    output.position = vec4<f32>(input.position, 1.0);
    output.color = input.color;
    output.texcoord = input.texcoord.xy;
    return output;
}
@fragment
fn fs_main(input: VertexOutput) -> @location(0) vec4<f32> {
    return textureSample(picture, picture_sampler, input.texcoord) * input.color;
}
";

        private static ModernGraphicsCompat? _current;
        private static BufferMapAsyncStatus _mapStatus = BufferMapAsyncStatus.Unknown;

        private readonly ModernGraphicsDevice _device;
        private readonly WebGPU _api;
        private readonly Queue* _queue;
        private readonly ModernGraphicsCompatState _programs = new();
        private readonly ModernGraphicsResourceState _resources = new();
        private readonly LegacyGeometryBatch _batch = new();
        private readonly Dictionary<int, GeometryList> _lists = new();
        private readonly Dictionary<int, NativeTexture> _nativeTextures = new();
        private readonly Dictionary<PipelineKey, PipelineRecord> _pipelines = new();
        private readonly HashSet<EnableCap> _enabled = new();

        private ShaderModule* _uiShader;
        private WgpuTexture* _whiteTexture;
        private TextureView* _whiteView;
        private WgpuSampler* _whiteSampler;

        private SurfaceTexture _surfaceTexture;
        private TextureView* _surfaceView;
        private bool _surfaceAcquired;
        private WgpuTextureFormat _surfaceFormat;
        private CompositeAlphaMode _alphaMode;
        private uint _width;
        private uint _height;

        private Vector4 _currentColor = Vector4.One;
        private Vector3 _currentNormal = Vector3.UnitZ;
        private Vector3 _currentTexcoord;
        private bool _colorSet;
        private bool _recording;
        private int _recordingList;
        private int _listHighWater = 1;

        private Vector4 _clearColor = new(0, 0, 0, 0);
        private BlendingFactor _blendSource = BlendingFactor.One;
        private BlendingFactor _blendDestination = BlendingFactor.Zero;
        private bool _maskRed = true;
        private bool _maskGreen = true;
        private bool _maskBlue = true;
        private bool _maskAlpha = true;
        private int _viewportX;
        private int _viewportY;
        private int _viewportWidth = 1;
        private int _viewportHeight = 1;
        private int _scissorX;
        private int _scissorY;
        private int _scissorWidth = 1;
        private int _scissorHeight = 1;
        private bool _disposed;

        private ModernGraphicsCompat(NativeWindow window, GraphicsBackend backend)
        {
            _device = ModernGraphicsDevice.CreateForWindow(window, backend);
            _api = _device.Api;
            _queue = _api.DeviceGetQueue(_device.Device);
            QuerySurfaceFormat();
            CreateUiShader();
            CreateWhiteTexture();
            ResizeCore(window.FramebufferSize.X, window.FramebufferSize.Y);
            Mods.DebugLog.Line("render",
                $"modern compatibility renderer active: {GraphicsBackendPolicy.DisplayName(backend)} "
                + $"on {_device.AdapterName}");
        }

        internal static bool Active => _current != null;

        internal static void Initialize(NativeWindow window, GraphicsBackend backend)
        {
            _current?.Dispose();
            _current = new ModernGraphicsCompat(window, backend);
        }

        internal static void Shutdown()
        {
            ModernGraphicsCompat? current = _current;
            _current = null;
            current?.Dispose();
        }

        private static ModernGraphicsCompat Current =>
            _current ?? throw new InvalidOperationException("Modern graphics compatibility renderer is not active.");

        internal static void Resize(int width, int height)
        {
            if (_current == null) return;
            Current.ResizeCore(width, height);
        }

        internal static void Present()
        {
            Current.PresentCore();
        }

        internal static void Begin(PrimitiveType mode)
        {
            ModernGraphicsCompat self = Current;
            if (!self._recording)
            {
                self._batch.Clear();
                self._colorSet = false;
            }
            self._batch.Begin(mode);
        }

        internal static void End()
        {
            ModernGraphicsCompat self = Current;
            self._batch.End();
            if (!self._recording)
            {
                self.DrawBatch(self._batch.Vertices.ToArray(),
                    self._batch.TriIndices.ToArray(), self._batch.LineIndices.ToArray());
                self._batch.Clear();
            }
        }

        internal static void Vertex3(float x, float y, float z)
        {
            ModernGraphicsCompat self = Current;
            self._batch.AddVertex(new Vector3(x, y, z), self._currentColor,
                self._currentNormal, self._currentTexcoord, self._colorSet);
        }

        internal static void Vertex3(Vector3 value) => Vertex3(value.X, value.Y, value.Z);

        internal static void Vertex2(float x, float y) => Vertex3(x, y, 0);

        internal static void Color3(float r, float g, float b)
        {
            ModernGraphicsCompat self = Current;
            self._currentColor = new Vector4(r, g, b, 1);
            self._colorSet = true;
        }

        internal static void Color3(Vector3 value) => Color3(value.X, value.Y, value.Z);

        internal static void Color4(float r, float g, float b, float a)
        {
            ModernGraphicsCompat self = Current;
            self._currentColor = new Vector4(r, g, b, a);
            self._colorSet = true;
        }

        internal static void Normal3(float x, float y, float z)
        {
            Current._currentNormal = new Vector3(x, y, z);
        }

        internal static void TexCoord2(float s, float t)
        {
            Current._currentTexcoord = new Vector3(s, t, 0);
        }

        internal static void TexCoord3(float s, float t, float r)
        {
            Current._currentTexcoord = new Vector3(s, t, r);
        }

        internal static void TexCoord3(Vector3 value)
        {
            Current._currentTexcoord = value;
        }

        internal static void MultiTexCoord2(TextureUnit unit, float s, float t)
        {
            // The launcher noise program uses unit 1 only for decoration. The
            // first live modern slice preserves the photo and UI texture on
            // unit 0; the second coordinate set joins the full backdrop pass.
            if (unit == TextureUnit.Texture0) TexCoord2(s, t);
        }

        internal static int GenLists(int range)
        {
            ModernGraphicsCompat self = Current;
            int id = self._listHighWater;
            self._listHighWater += range;
            return id;
        }

        internal static void NewList(int list, ListMode mode)
        {
            ModernGraphicsCompat self = Current;
            self._batch.Clear();
            self._recording = true;
            self._recordingList = list;
            self._colorSet = false;
        }

        internal static void EndList()
        {
            ModernGraphicsCompat self = Current;
            self._recording = false;
            self._lists[self._recordingList] = new GeometryList
            {
                Vertices = self._batch.Vertices.ToArray(),
                Triangles = self._batch.TriIndices.ToArray(),
                Lines = self._batch.LineIndices.ToArray()
            };
            self._batch.Clear();
        }

        internal static void CallList(int list)
        {
            ModernGraphicsCompat self = Current;
            if (self._lists.TryGetValue(list, out GeometryList? geometry))
            {
                self.DrawBatch(geometry.Vertices, geometry.Triangles, geometry.Lines);
            }
        }

        internal static void DeleteLists(int list, int range)
        {
            ModernGraphicsCompat self = Current;
            for (int i = 0; i < range; i++) self._lists.Remove(list + i);
        }

        internal static int GenTexture() => Current._resources.GenTexture();

        internal static void DeleteTexture(int texture)
        {
            ModernGraphicsCompat self = Current;
            self._resources.DeleteTexture(texture);
            if (self._nativeTextures.Remove(texture, out NativeTexture? native))
            {
                self.ReleaseNativeTexture(native);
            }
        }

        internal static bool IsTexture(int texture) => Current._resources.IsTexture(texture);

        internal static void ActiveTexture(TextureUnit unit) => Current._resources.ActiveTexture(unit);

        internal static void BindTexture(TextureTarget target, int texture) =>
            Current._resources.BindTexture(target, texture);

        internal static void TexParameter(TextureTarget target, TextureParameterName name, int value) =>
            Current._resources.TexParameter(target, name, value);

        internal static void GenerateMipmap(GenerateMipmapTarget target) =>
            Current._resources.GenerateMipmap(target);

        internal static void TexImage2D(TextureTarget target, PixelInternalFormat internalFormat,
            int width, int height, PixelFormat format, PixelType type, IntPtr pixels) =>
            Current._resources.TexImage2D(target, internalFormat, width, height, format, type, pixels);

        internal static void TexImage2D<T>(TextureTarget target, PixelInternalFormat internalFormat,
            int width, int height, PixelFormat format, PixelType type, T[] pixels) where T : struct =>
            Current._resources.TexImage2D(target, internalFormat, width, height, format, type, pixels);

        internal static void TexSubImage2D(TextureTarget target, int x, int y, int width, int height,
            PixelFormat format, PixelType type, IntPtr pixels) =>
            Current._resources.TexSubImage2D(target, x, y, width, height, format, type, pixels);

        internal static void TexSubImage2D<T>(TextureTarget target, int x, int y, int width, int height,
            PixelFormat format, PixelType type, T[] pixels) where T : struct =>
            Current._resources.TexSubImage2D(target, x, y, width, height, format, type, pixels);

        internal static void GetTexLevelParameter(TextureTarget target, int level,
            GetTextureParameter name, out int value) =>
            Current._resources.GetTexLevelParameter(target, level, name, out value);

        internal static int CreateShader(ShaderType type) => Current._programs.CreateShader(type);
        internal static void ShaderSource(int shader, string source) => Current._programs.ShaderSource(shader, source);
        internal static void CompileShader(int shader) => Current._programs.CompileShader(shader);
        internal static void GetShader(int shader, ShaderParameter name, out int value) =>
            Current._programs.GetShader(shader, name, out value);
        internal static string GetShaderInfoLog(int shader) => Current._programs.GetShaderInfoLog(shader);
        internal static void DeleteShader(int shader) => Current._programs.DeleteShader(shader);
        internal static int CreateProgram() => Current._programs.CreateProgram();
        internal static void DeleteProgram(int program) => Current._programs.DeleteProgram(program);
        internal static void AttachShader(int program, int shader) => Current._programs.AttachShader(program, shader);
        internal static void DetachShader(int program, int shader) => Current._programs.DetachShader(program, shader);
        internal static void LinkProgram(int program) => Current._programs.LinkProgram(program);
        internal static void GetProgram(int program, GetProgramParameterName name, out int value) =>
            Current._programs.GetProgram(program, name, out value);
        internal static string GetProgramInfoLog(int program) => Current._programs.GetProgramInfoLog(program);
        internal static void UseProgram(int program) => Current._programs.UseProgram(program);
        internal static int GetUniformLocation(int program, string name) =>
            Current._programs.GetUniformLocation(program, name);
        internal static void GetUniform(int program, int location, out int value) =>
            Current._programs.GetUniform(program, location, out value);
        internal static void Uniform1(int location, int value) => Current._programs.Uniform1(location, value);
        internal static void Uniform1(int location, float value) => Current._programs.Uniform1(location, value);
        internal static void Uniform1(int location, int count, float[] value) =>
            Current._programs.Uniform1(location, count, value);
        internal static void Uniform2(int location, float x, float y) => Current._programs.Uniform2(location, x, y);
        internal static void Uniform3(int location, Vector3 value) => Current._programs.Uniform3(location, value);
        internal static void Uniform3(int location, int count, float[] value) =>
            Current._programs.Uniform3(location, count, value);
        internal static void Uniform4(int location, Vector4 value) => Current._programs.Uniform4(location, value);
        internal static void Uniform4(int location, ref Vector4 value) => Current._programs.Uniform4(location, value);
        internal static void Uniform4(int location, float x, float y, float z, float w) =>
            Current._programs.Uniform4(location, x, y, z, w);
        internal static void Uniform4(int location, int x, int y, int z, int w) =>
            Current._programs.Uniform4(location, x, y, z, w);
        internal static void UniformMatrix4(int location, bool transpose, ref Matrix4 value) =>
            Current._programs.UniformMatrix4(location, transpose, value);
        internal static void UniformMatrix4(int location, int count, bool transpose, float[] value) =>
            Current._programs.UniformMatrix4(location, count, transpose, value);

        internal static void Enable(EnableCap cap) => Current._enabled.Add(cap);
        internal static void Disable(EnableCap cap) => Current._enabled.Remove(cap);
        internal static bool IsEnabled(EnableCap cap) => Current._enabled.Contains(cap);

        internal static void BlendFunc(BlendingFactor source, BlendingFactor destination)
        {
            Current._blendSource = source;
            Current._blendDestination = destination;
        }

        internal static void ColorMask(bool red, bool green, bool blue, bool alpha)
        {
            Current._maskRed = red;
            Current._maskGreen = green;
            Current._maskBlue = blue;
            Current._maskAlpha = alpha;
        }

        internal static void ClearColor(float red, float green, float blue, float alpha) =>
            Current._clearColor = new Vector4(red, green, blue, alpha);

        internal static void ClearColor(OpenTK.Mathematics.Color4 color) =>
            ClearColor(color.R, color.G, color.B, color.A);

        internal static void Clear(ClearBufferMask mask) => Current.ClearCore(mask);

        internal static void Viewport(int x, int y, int width, int height)
        {
            ModernGraphicsCompat self = Current;
            self._viewportX = x;
            self._viewportY = y;
            self._viewportWidth = Math.Max(1, width);
            self._viewportHeight = Math.Max(1, height);
        }

        internal static void Scissor(int x, int y, int width, int height)
        {
            ModernGraphicsCompat self = Current;
            self._scissorX = x;
            self._scissorY = y;
            self._scissorWidth = Math.Max(1, width);
            self._scissorHeight = Math.Max(1, height);
        }

        internal static int GenFramebuffer() => Current._resources.GenFramebuffer();
        internal static void DeleteFramebuffer(int framebuffer) => Current._resources.DeleteFramebuffer(framebuffer);
        internal static bool IsFramebuffer(int framebuffer) => Current._resources.IsFramebuffer(framebuffer);
        internal static void BindFramebuffer(FramebufferTarget target, int framebuffer) =>
            Current._resources.BindFramebuffer(target, framebuffer);
        internal static void FramebufferTexture2D(FramebufferTarget target, FramebufferAttachment attachment,
            TextureTarget textureTarget, int texture, int level)
        {
            if (textureTarget != TextureTarget.Texture2D || level != 0)
                throw new NotSupportedException("Modern compatibility FBOs currently use base-level 2D textures.");
            Current._resources.FramebufferTexture2D(target, attachment, texture);
        }
        internal static FramebufferErrorCode CheckFramebufferStatus(FramebufferTarget target) =>
            Current._resources.CheckFramebufferStatus(target);

        internal static int GenRenderbuffer() => Current._resources.GenRenderbuffer();
        internal static void DeleteRenderbuffer(int renderbuffer) => Current._resources.DeleteRenderbuffer(renderbuffer);
        internal static void BindRenderbuffer(RenderbufferTarget target, int renderbuffer) =>
            Current._resources.BindRenderbuffer(target, renderbuffer);
        internal static void RenderbufferStorage(RenderbufferTarget target, RenderbufferStorage format,
            int width, int height) => Current._resources.RenderbufferStorage(target, format, width, height);
        internal static void FramebufferRenderbuffer(FramebufferTarget target, FramebufferAttachment attachment,
            RenderbufferTarget renderbufferTarget, int renderbuffer) =>
            Current._resources.FramebufferRenderbuffer(target, attachment, renderbufferTarget, renderbuffer);
        internal static void GetFramebufferAttachmentParameter(FramebufferTarget target,
            FramebufferAttachment attachment, FramebufferParameterName name, out int value) =>
            Current._resources.GetFramebufferAttachmentParameter(target, attachment, name, out value);

        internal static ErrorCode GetError() => ErrorCode.NoError;

        internal static string GetString(StringName name)
        {
            ModernGraphicsCompat self = Current;
            return name switch
            {
                StringName.Vendor => "wgpu-native",
                StringName.Renderer => self._device.AdapterName,
                StringName.Version => $"WebGPU 1.0 / {GraphicsBackendPolicy.DisplayName(self._device.Backend)}",
                StringName.ShadingLanguageVersion => "WGSL",
                _ => ""
            };
        }

        internal static int GetInteger(GetPName name)
        {
            ModernGraphicsCompat self = Current;
            return name switch
            {
                GetPName.CurrentProgram => self._programs.CurrentProgram,
                GetPName.DrawFramebufferBinding => self._resources.DrawFramebuffer,
                GetPName.ReadFramebufferBinding => self._resources.ReadFramebuffer,
                GetPName.FramebufferBinding => self._resources.DrawFramebuffer,
                GetPName.RenderbufferBinding => self._resources.BoundRenderbuffer,
                GetPName.ActiveTexture => (int)TextureUnit.Texture0 + self._resources.ActiveTextureUnit,
                GetPName.TextureBinding2D => self._resources.BoundTexture(self._resources.ActiveTextureUnit),
                GetPName.PackAlignment => 4,
                GetPName.UnpackAlignment => 4,
                GetPName.MaxTextureSize => 8192,
                _ => 0
            };
        }

        internal static void GetInteger(GetPName name, out int value) => value = GetInteger(name);

        internal static void GetInteger(GetPName name, int[] values)
        {
            if (values.Length == 0) return;
            if (name == GetPName.Viewport && values.Length >= 4)
            {
                ModernGraphicsCompat self = Current;
                values[0] = self._viewportX;
                values[1] = self._viewportY;
                values[2] = self._viewportWidth;
                values[3] = self._viewportHeight;
                return;
            }
            values[0] = GetInteger(name);
        }

        internal static void PixelStore(PixelStoreParameter name, int value) { }
        internal static void ReadBuffer(ReadBufferMode mode) { }
        internal static void DrawBuffer(DrawBufferMode mode) { }
        internal static void TexEnv(TextureEnvTarget target, TextureEnvParameter name, int value) { }
        internal static void AlphaFunc(AlphaFunction function, float reference) { }
        internal static void PolygonMode(TriangleFace face, OpenTK.Graphics.OpenGL.PolygonMode mode) { }
        internal static void LineWidth(float width) { }
        internal static void ClearStencil(int value) { }
        internal static void DepthMask(bool enabled) { }
        internal static void DepthFunc(DepthFunction function) { }
        internal static void CullFace(TriangleFace face) { }
        internal static void BlendEquation(BlendEquationMode mode) { }
        internal static void StencilFunc(StencilFunction function, int reference, int mask) { }
        internal static void StencilOp(OpenTK.Graphics.OpenGL.StencilOp fail,
            OpenTK.Graphics.OpenGL.StencilOp zfail, OpenTK.Graphics.OpenGL.StencilOp zpass) { }
        internal static void StencilMask(int mask) { }
        internal static void PolygonOffset(float factor, float units) { }
        internal static void MatrixMode(MatrixMode mode) { }
        internal static void PushMatrix() { }
        internal static void PopMatrix() { }
        internal static void LoadIdentity() { }
        internal static void PushAttrib(AttribMask mask) { }
        internal static void PopAttrib() { }
        internal static void DebugMessageCallback(DebugProc callback, IntPtr userParam) { }

        internal static void Finish()
        {
            Current._device.Native.DevicePoll(Current._device.Device, true, null);
        }

        internal static void ReadPixels<T>(int x, int y, int width, int height,
            PixelFormat format, PixelType type, T[] pixels) where T : struct
        {
            Current.ReadPixelsCore(x, y, width, height, format, type, pixels);
        }

        internal static void CopyTexSubImage2D(TextureTarget target, int level, int xoffset, int yoffset,
            int x, int y, int width, int height)
        {
            throw new NotSupportedException("Modern scene copy-to-texture lands with the world/FBO pass.");
        }

        internal static void BlitFramebuffer(int sourceX0, int sourceY0, int sourceX1, int sourceY1,
            int destinationX0, int destinationY0, int destinationX1, int destinationY1,
            ClearBufferMask mask, BlitFramebufferFilter filter)
        {
            throw new NotSupportedException("Modern framebuffer blits land with replay/export FBO support.");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ReleaseSurfaceTexture();
            foreach (NativeTexture texture in _nativeTextures.Values) ReleaseNativeTexture(texture);
            _nativeTextures.Clear();
            foreach (PipelineRecord pipeline in _pipelines.Values)
            {
                if (pipeline.Layout != null) _api.BindGroupLayoutRelease(pipeline.Layout);
                if (pipeline.Pipeline != null) _api.RenderPipelineRelease(pipeline.Pipeline);
            }
            _pipelines.Clear();
            if (_whiteSampler != null) _api.SamplerRelease(_whiteSampler);
            if (_whiteView != null) _api.TextureViewRelease(_whiteView);
            if (_whiteTexture != null) _api.TextureRelease(_whiteTexture);
            if (_uiShader != null) _api.ShaderModuleRelease(_uiShader);
            if (_queue != null) _api.QueueRelease(_queue);
            _device.Dispose();
        }

        private void QuerySurfaceFormat()
        {
            SurfaceCapabilities capabilities = default;
            _api.SurfaceGetCapabilities(_device.Surface, _device.Adapter, ref capabilities);
            try
            {
                if (capabilities.FormatCount == 0)
                    throw new InvalidOperationException("WebGPU surface reports no presentation formats.");
                _surfaceFormat = capabilities.Formats[0];
                _alphaMode = capabilities.AlphaModeCount > 0
                    ? capabilities.AlphaModes[0] : CompositeAlphaMode.Auto;
            }
            finally
            {
                _api.SurfaceCapabilitiesFreeMembers(capabilities);
            }
        }

        private void ResizeCore(int width, int height)
        {
            if (width <= 0 || height <= 0) return;
            uint newWidth = (uint)width;
            uint newHeight = (uint)height;
            if (_width == newWidth && _height == newHeight) return;
            ReleaseSurfaceTexture();
            _width = newWidth;
            _height = newHeight;
            _viewportX = 0;
            _viewportY = 0;
            _viewportWidth = width;
            _viewportHeight = height;
            _scissorX = 0;
            _scissorY = 0;
            _scissorWidth = width;
            _scissorHeight = height;
            var config = new SurfaceConfiguration
            {
                Device = _device.Device,
                Format = _surfaceFormat,
                Usage = TextureUsage.RenderAttachment | TextureUsage.CopySrc,
                AlphaMode = _alphaMode,
                Width = _width,
                Height = _height,
                PresentMode = PresentMode.Fifo
            };
            _api.SurfaceConfigure(_device.Surface, config);
        }

        private bool AcquireSurfaceTexture()
        {
            if (_surfaceAcquired) return true;
            if (_width == 0 || _height == 0) return false;

            _surfaceTexture = default;
            _api.SurfaceGetCurrentTexture(_device.Surface, &_surfaceTexture);
            if (_surfaceTexture.Status != SurfaceGetCurrentTextureStatus.Success
                || _surfaceTexture.Texture == null)
            {
                if (_surfaceTexture.Texture != null) _api.TextureRelease(_surfaceTexture.Texture);
                _surfaceTexture = default;
                var config = new SurfaceConfiguration
                {
                    Device = _device.Device,
                    Format = _surfaceFormat,
                    Usage = TextureUsage.RenderAttachment | TextureUsage.CopySrc,
                    AlphaMode = _alphaMode,
                    Width = _width,
                    Height = _height,
                    PresentMode = PresentMode.Fifo
                };
                _api.SurfaceConfigure(_device.Surface, config);
                _api.SurfaceGetCurrentTexture(_device.Surface, &_surfaceTexture);
            }
            if (_surfaceTexture.Status != SurfaceGetCurrentTextureStatus.Success
                || _surfaceTexture.Texture == null)
            {
                return false;
            }

            _surfaceView = _api.TextureCreateView(_surfaceTexture.Texture, null);
            _surfaceAcquired = _surfaceView != null;
            return _surfaceAcquired;
        }

        private void PresentCore()
        {
            if (!_surfaceAcquired) return;
            _api.SurfacePresent(_device.Surface);
            ReleaseSurfaceTexture();
        }

        private void ReleaseSurfaceTexture()
        {
            if (_surfaceView != null)
            {
                _api.TextureViewRelease(_surfaceView);
                _surfaceView = null;
            }
            if (_surfaceTexture.Texture != null)
            {
                _api.TextureRelease(_surfaceTexture.Texture);
                _surfaceTexture = default;
            }
            _surfaceAcquired = false;
        }

        private void CreateUiShader()
        {
            nint code = SilkMarshal.StringToPtr(UiShader);
            try
            {
                var wgsl = new ShaderModuleWGSLDescriptor
                {
                    Code = (byte*)code,
                    Chain = new ChainedStruct { SType = SType.ShaderModuleWgslDescriptor }
                };
                var descriptor = new ShaderModuleDescriptor
                {
                    NextInChain = (ChainedStruct*)&wgsl
                };
                _uiShader = _api.DeviceCreateShaderModule(_device.Device, descriptor);
                if (_uiShader == null)
                    throw new InvalidOperationException("Could not create modern UI shader.");
            }
            finally
            {
                SilkMarshal.Free(code);
            }
        }

        private void CreateWhiteTexture()
        {
            var descriptor = new TextureDescriptor
            {
                Size = new Extent3D(1, 1, 1),
                Format = WgpuTextureFormat.Rgba8Unorm,
                Usage = TextureUsage.CopyDst | TextureUsage.TextureBinding,
                MipLevelCount = 1,
                SampleCount = 1,
                Dimension = TextureDimension.Dimension2D
            };
            _whiteTexture = _api.DeviceCreateTexture(_device.Device, descriptor);
            _whiteView = _api.TextureCreateView(_whiteTexture, null);
            _whiteSampler = _api.DeviceCreateSampler(_device.Device, new SamplerDescriptor
            {
                MinFilter = FilterMode.Nearest,
                MagFilter = FilterMode.Nearest,
                MipmapFilter = MipmapFilterMode.Nearest,
                AddressModeU = AddressMode.ClampToEdge,
                AddressModeV = AddressMode.ClampToEdge,
                AddressModeW = AddressMode.ClampToEdge,
                MaxAnisotropy = 1
            });
            uint white = 0xFFFFFFFFu;
            var destination = new ImageCopyTexture
            {
                Texture = _whiteTexture,
                Aspect = TextureAspect.All,
                MipLevel = 0
            };
            var layout = new TextureDataLayout { BytesPerRow = 4, RowsPerImage = 1 };
            var extent = new Extent3D(1, 1, 1);
            _api.QueueWriteTexture(_queue, destination, &white, (nuint)sizeof(uint), layout, extent);
        }

        private NativeTexture EnsureTexture(int id)
        {
            if (id == 0) throw new InvalidOperationException("Texture zero is the default object.");
            ModernGraphicsResourceState.TextureRecord record = _resources.Texture(id);
            if (_nativeTextures.TryGetValue(id, out NativeTexture? existing) && !record.Dirty)
                return existing;

            if (existing != null)
            {
                ReleaseNativeTexture(existing);
                _nativeTextures.Remove(id);
            }

            WgpuTextureFormat format = WgpuTextureFormat.Rgba8Unorm;
            byte[] data = ConvertPixels(record, ref format);
            int width = Math.Max(1, record.Width);
            int height = Math.Max(1, record.Height);
            var descriptor = new TextureDescriptor
            {
                Size = new Extent3D((uint)width, (uint)height, 1),
                Format = format,
                Usage = TextureUsage.CopyDst | TextureUsage.TextureBinding
                    | TextureUsage.RenderAttachment | TextureUsage.CopySrc,
                MipLevelCount = 1,
                SampleCount = 1,
                Dimension = TextureDimension.Dimension2D
            };
            var native = new NativeTexture
            {
                Texture = _api.DeviceCreateTexture(_device.Device, descriptor)
            };
            if (native.Texture == null)
                throw new InvalidOperationException($"Could not allocate modern texture {id}.");

            native.View = _api.TextureCreateView(native.Texture, null);
            native.Sampler = _api.DeviceCreateSampler(_device.Device, new SamplerDescriptor
            {
                MinFilter = ToFilter(record.MinFilter),
                MagFilter = ToFilter(record.MagFilter),
                MipmapFilter = MipmapFilterMode.Nearest,
                AddressModeU = ToAddress(record.WrapS),
                AddressModeV = ToAddress(record.WrapT),
                AddressModeW = AddressMode.ClampToEdge,
                MaxAnisotropy = 1
            });

            if (data.Length > 0)
            {
                var destination = new ImageCopyTexture
                {
                    Texture = native.Texture,
                    Aspect = TextureAspect.All,
                    MipLevel = 0
                };
                uint bytesPerRow = checked((uint)(width * 4));
                var layout = new TextureDataLayout
                {
                    BytesPerRow = bytesPerRow,
                    RowsPerImage = (uint)height
                };
                var extent = new Extent3D((uint)width, (uint)height, 1);
                fixed (byte* ptr = data)
                {
                    _api.QueueWriteTexture(_queue, destination, ptr, (nuint)data.Length, layout, extent);
                }
            }

            record.Dirty = false;
            _nativeTextures[id] = native;
            return native;
        }

        private static byte[] ConvertPixels(ModernGraphicsResourceState.TextureRecord record,
            ref WgpuTextureFormat format)
        {
            if (record.Width <= 0 || record.Height <= 0 || record.Pixels == null)
                return Array.Empty<byte>();
            if (record.Type != PixelType.UnsignedByte)
                throw new NotSupportedException($"Modern launcher texture type {record.Type} is not supported yet.");

            byte[] source = record.Pixels;
            int pixels = checked(record.Width * record.Height);
            if (record.Format == PixelFormat.Rgba)
            {
                format = WgpuTextureFormat.Rgba8Unorm;
                return source;
            }
            if (record.Format == PixelFormat.Bgra)
            {
                format = WgpuTextureFormat.Bgra8Unorm;
                return source;
            }
            if (record.Format == PixelFormat.Rgb || record.Format == PixelFormat.Bgr)
            {
                format = WgpuTextureFormat.Rgba8Unorm;
                var result = new byte[checked(pixels * 4)];
                bool bgr = record.Format == PixelFormat.Bgr;
                for (int i = 0; i < pixels; i++)
                {
                    int s = i * 3;
                    int d = i * 4;
                    result[d + 0] = source[s + (bgr ? 2 : 0)];
                    result[d + 1] = source[s + 1];
                    result[d + 2] = source[s + (bgr ? 0 : 2)];
                    result[d + 3] = 255;
                }
                return result;
            }
            throw new NotSupportedException($"Modern launcher texture format {record.Format} is not supported yet.");
        }

        private void DrawBatch(float[] vertices, int[] triangles, int[] lines)
        {
            if (_resources.DrawFramebuffer != 0)
                throw new NotSupportedException("Modern offscreen framebuffer drawing lands with the world pass.");
            if (vertices.Length == 0) return;
            if (triangles.Length > 0)
                DrawIndexed(vertices, triangles, PrimitiveTopology.TriangleList);
            if (lines.Length > 0)
                DrawIndexed(vertices, lines, PrimitiveTopology.LineList);
        }

        private void DrawIndexed(float[] vertices, int[] indices, PrimitiveTopology topology)
        {
            if (!AcquireSurfaceTexture()) return;
            PipelineRecord pipeline = Pipeline(topology);

            ulong vertexBytes = (ulong)(vertices.Length * sizeof(float));
            ulong indexBytes = (ulong)(indices.Length * sizeof(int));
            WgpuBuffer* vertex = _api.DeviceCreateBuffer(_device.Device, new BufferDescriptor
            {
                Size = vertexBytes,
                Usage = BufferUsage.Vertex | BufferUsage.CopyDst
            });
            WgpuBuffer* index = _api.DeviceCreateBuffer(_device.Device, new BufferDescriptor
            {
                Size = indexBytes,
                Usage = BufferUsage.Index | BufferUsage.CopyDst
            });
            fixed (float* vertexPtr = vertices)
                _api.QueueWriteBuffer(_queue, vertex, 0, vertexPtr, (nuint)vertexBytes);
            fixed (int* indexPtr = indices)
                _api.QueueWriteBuffer(_queue, index, 0, indexPtr, (nuint)indexBytes);

            TextureView* textureView = _whiteView;
            WgpuSampler* sampler = _whiteSampler;
            int bound = _resources.BoundTexture(0);
            if (_enabled.Contains(EnableCap.Texture2D) && bound != 0)
            {
                NativeTexture texture = EnsureTexture(bound);
                textureView = texture.View;
                sampler = texture.Sampler;
            }

            var entries = stackalloc BindGroupEntry[2];
            entries[0] = new BindGroupEntry { Binding = 0, TextureView = textureView };
            entries[1] = new BindGroupEntry { Binding = 1, Sampler = sampler };
            BindGroup* bindGroup = _api.DeviceCreateBindGroup(_device.Device, new BindGroupDescriptor
            {
                Layout = pipeline.Layout,
                Entries = entries,
                EntryCount = 2
            });

            CommandEncoder* encoder = _api.DeviceCreateCommandEncoder(_device.Device, new CommandEncoderDescriptor());
            var attachment = new RenderPassColorAttachment
            {
                View = _surfaceView,
                ResolveTarget = null,
                LoadOp = LoadOp.Load,
                StoreOp = StoreOp.Store
            };
            var passDescriptor = new RenderPassDescriptor
            {
                ColorAttachments = &attachment,
                ColorAttachmentCount = 1
            };
            RenderPassEncoder* pass = _api.CommandEncoderBeginRenderPass(encoder, passDescriptor);
            _api.RenderPassEncoderSetPipeline(pass, pipeline.Pipeline);
            _api.RenderPassEncoderSetBindGroup(pass, 0, bindGroup, 0, null);
            _api.RenderPassEncoderSetVertexBuffer(pass, 0, vertex, 0, vertexBytes);
            _api.RenderPassEncoderSetIndexBuffer(pass, index, IndexFormat.Uint32, 0, indexBytes);
            _api.RenderPassEncoderSetViewport(pass, _viewportX, _viewportY,
                (float)_viewportWidth, (float)_viewportHeight, 0, 1);
            if (_enabled.Contains(EnableCap.ScissorTest))
            {
                _api.RenderPassEncoderSetScissorRect(pass, (uint)Math.Max(0, _scissorX),
                    (uint)Math.Max(0, _scissorY), (uint)_scissorWidth, (uint)_scissorHeight);
            }
            _api.RenderPassEncoderDrawIndexed(pass, (uint)indices.Length, 1, 0, 0, 0);
            _api.RenderPassEncoderEnd(pass);
            CommandBuffer* commands = _api.CommandEncoderFinish(encoder, new CommandBufferDescriptor());
            _api.QueueSubmit(_queue, 1, &commands);

            _api.CommandBufferRelease(commands);
            _api.RenderPassEncoderRelease(pass);
            _api.CommandEncoderRelease(encoder);
            _api.BindGroupRelease(bindGroup);
            _api.BufferRelease(index);
            _api.BufferRelease(vertex);
        }

        private PipelineRecord Pipeline(PrimitiveTopology topology)
        {
            var key = new PipelineKey(topology, _enabled.Contains(EnableCap.Blend),
                _blendSource, _blendDestination, CurrentWriteMask());
            if (_pipelines.TryGetValue(key, out PipelineRecord? existing)) return existing;

            var attributes = stackalloc VertexAttribute[3];
            attributes[0] = new VertexAttribute
            {
                Format = VertexFormat.Float32x3,
                Offset = 0,
                ShaderLocation = 0
            };
            attributes[1] = new VertexAttribute
            {
                Format = VertexFormat.Float32x4,
                Offset = 3u * sizeof(float),
                ShaderLocation = 1
            };
            attributes[2] = new VertexAttribute
            {
                Format = VertexFormat.Float32x3,
                Offset = 10u * sizeof(float),
                ShaderLocation = 3
            };
            var vertexLayout = new VertexBufferLayout
            {
                Attributes = attributes,
                AttributeCount = 3,
                StepMode = VertexStepMode.Vertex,
                ArrayStride = (ulong)(LegacyGeometryBatch.FloatsPerVertex * sizeof(float))
            };

            BlendState blend = default;
            BlendState* blendPtr = null;
            if (key.Blend)
            {
                blend = new BlendState
                {
                    Color = new BlendComponent
                    {
                        SrcFactor = ToBlend(key.Source),
                        DstFactor = ToBlend(key.Destination),
                        Operation = BlendOperation.Add
                    },
                    Alpha = new BlendComponent
                    {
                        SrcFactor = ToBlend(key.Source),
                        DstFactor = ToBlend(key.Destination),
                        Operation = BlendOperation.Add
                    }
                };
                blendPtr = &blend;
            }

            var target = new ColorTargetState
            {
                Format = _surfaceFormat,
                Blend = blendPtr,
                WriteMask = key.WriteMask
            };
            nint vertexEntry = SilkMarshal.StringToPtr("vs_main");
            nint fragmentEntry = SilkMarshal.StringToPtr("fs_main");
            try
            {
                var fragment = new FragmentState
                {
                    Module = _uiShader,
                    EntryPoint = (byte*)fragmentEntry,
                    Targets = &target,
                    TargetCount = 1
                };
                var descriptor = new RenderPipelineDescriptor
                {
                    Vertex = new VertexState
                    {
                        Module = _uiShader,
                        EntryPoint = (byte*)vertexEntry,
                        Buffers = &vertexLayout,
                        BufferCount = 1
                    },
                    Primitive = new PrimitiveState
                    {
                        Topology = topology,
                        StripIndexFormat = IndexFormat.Undefined,
                        FrontFace = FrontFace.Ccw,
                        CullMode = CullMode.None
                    },
                    Multisample = new MultisampleState
                    {
                        Count = 1,
                        Mask = ~0u,
                        AlphaToCoverageEnabled = false
                    },
                    Fragment = &fragment
                };
                RenderPipeline* pipeline = _api.DeviceCreateRenderPipeline(_device.Device, descriptor);
                if (pipeline == null) throw new InvalidOperationException("Could not create modern UI pipeline.");
                BindGroupLayout* layout = _api.RenderPipelineGetBindGroupLayout(pipeline, 0);
                var created = new PipelineRecord { Pipeline = pipeline, Layout = layout };
                _pipelines.Add(key, created);
                return created;
            }
            finally
            {
                SilkMarshal.Free(vertexEntry);
                SilkMarshal.Free(fragmentEntry);
            }
        }

        private void ClearCore(ClearBufferMask mask)
        {
            if (_resources.DrawFramebuffer != 0)
                throw new NotSupportedException("Modern offscreen framebuffer clears land with the world pass.");
            if ((mask & ClearBufferMask.ColorBufferBit) == 0) return;
            if (!AcquireSurfaceTexture()) return;

            CommandEncoder* encoder = _api.DeviceCreateCommandEncoder(_device.Device, new CommandEncoderDescriptor());
            var attachment = new RenderPassColorAttachment
            {
                View = _surfaceView,
                ResolveTarget = null,
                LoadOp = LoadOp.Clear,
                StoreOp = StoreOp.Store,
                ClearValue = new WgpuColor
                {
                    R = _clearColor.X,
                    G = _clearColor.Y,
                    B = _clearColor.Z,
                    A = _clearColor.W
                }
            };
            var descriptor = new RenderPassDescriptor
            {
                ColorAttachments = &attachment,
                ColorAttachmentCount = 1
            };
            RenderPassEncoder* pass = _api.CommandEncoderBeginRenderPass(encoder, descriptor);
            _api.RenderPassEncoderEnd(pass);
            CommandBuffer* commands = _api.CommandEncoderFinish(encoder, new CommandBufferDescriptor());
            _api.QueueSubmit(_queue, 1, &commands);
            _api.CommandBufferRelease(commands);
            _api.RenderPassEncoderRelease(pass);
            _api.CommandEncoderRelease(encoder);
        }

        private void ReadPixelsCore<T>(int x, int y, int width, int height,
            PixelFormat format, PixelType type, T[] pixels) where T : struct
        {
            if (type != PixelType.UnsignedByte || (format != PixelFormat.Rgb && format != PixelFormat.Rgba))
                throw new NotSupportedException("Modern launcher readback currently supports RGB/RGBA unsigned-byte.");
            if (_resources.ReadFramebuffer != 0)
                throw new NotSupportedException("Modern offscreen framebuffer readback lands with the world pass.");
            if (!AcquireSurfaceTexture()) return;

            uint copyWidth = (uint)Math.Max(0, width);
            uint copyHeight = (uint)Math.Max(0, height);
            uint rowBytes = copyWidth * 4;
            uint paddedRow = (rowBytes + 255u) & ~255u;
            ulong total = (ulong)paddedRow * copyHeight;
            WgpuBuffer* readback = _api.DeviceCreateBuffer(_device.Device, new BufferDescriptor
            {
                Size = total,
                Usage = BufferUsage.CopyDst | BufferUsage.MapRead
            });
            CommandEncoder* encoder = _api.DeviceCreateCommandEncoder(_device.Device, new CommandEncoderDescriptor());
            var source = new ImageCopyTexture
            {
                Texture = _surfaceTexture.Texture,
                MipLevel = 0,
                Origin = new Origin3D((uint)Math.Max(0, x), (uint)Math.Max(0, y), 0),
                Aspect = TextureAspect.All
            };
            var destination = new ImageCopyBuffer
            {
                Buffer = readback,
                Layout = new TextureDataLayout
                {
                    BytesPerRow = paddedRow,
                    RowsPerImage = copyHeight
                }
            };
            var extent = new Extent3D(copyWidth, copyHeight, 1);
            _api.CommandEncoderCopyTextureToBuffer(encoder, &source, &destination, &extent);
            CommandBuffer* commands = _api.CommandEncoderFinish(encoder, new CommandBufferDescriptor());
            _api.QueueSubmit(_queue, 1, &commands);

            _mapStatus = BufferMapAsyncStatus.Unknown;
            _api.BufferMapAsync(readback, MapMode.Read, 0, (nuint)total,
                new PfnBufferMapCallback((status, _) => _mapStatus = status), null);
            _device.Native.DevicePoll(_device.Device, true, null);
            if (_mapStatus != BufferMapAsyncStatus.Success)
                throw new InvalidOperationException($"Modern readback map failed: {_mapStatus}.");

            byte* mapped = (byte*)_api.BufferGetConstMappedRange(readback, 0, (nuint)total);
            int components = format == PixelFormat.Rgb ? 3 : 4;
            byte[] output = new byte[checked(width * height * components)];
            bool bgra = _surfaceFormat == WgpuTextureFormat.Bgra8Unorm
                || _surfaceFormat == WgpuTextureFormat.Bgra8UnormSrgb;
            for (int row = 0; row < height; row++)
            {
                byte* src = mapped + row * paddedRow;
                int dst = row * width * components;
                for (int col = 0; col < width; col++)
                {
                    byte b0 = src[col * 4 + 0];
                    byte b1 = src[col * 4 + 1];
                    byte b2 = src[col * 4 + 2];
                    byte b3 = src[col * 4 + 3];
                    output[dst++] = bgra ? b2 : b0;
                    output[dst++] = b1;
                    output[dst++] = bgra ? b0 : b2;
                    if (components == 4) output[dst++] = b3;
                }
            }
            _api.BufferUnmap(readback);

            if (typeof(T) != typeof(byte))
                throw new NotSupportedException("Modern launcher readback currently targets byte arrays.");
            Array.Copy(output, (byte[])(object)pixels, Math.Min(output.Length, pixels.Length));

            _api.CommandBufferRelease(commands);
            _api.CommandEncoderRelease(encoder);
            _api.BufferRelease(readback);
        }

        private void ReleaseNativeTexture(NativeTexture texture)
        {
            if (texture.Sampler != null) _api.SamplerRelease(texture.Sampler);
            if (texture.View != null) _api.TextureViewRelease(texture.View);
            if (texture.Texture != null) _api.TextureRelease(texture.Texture);
        }

        private ColorWriteMask CurrentWriteMask()
        {
            ColorWriteMask mask = ColorWriteMask.None;
            if (_maskRed) mask |= ColorWriteMask.Red;
            if (_maskGreen) mask |= ColorWriteMask.Green;
            if (_maskBlue) mask |= ColorWriteMask.Blue;
            if (_maskAlpha) mask |= ColorWriteMask.Alpha;
            return mask;
        }

        private static FilterMode ToFilter(int value)
        {
            return value == (int)TextureMinFilter.Linear
                || value == (int)TextureMagFilter.Linear
                || value == (int)TextureMinFilter.LinearMipmapLinear
                || value == (int)TextureMinFilter.LinearMipmapNearest
                ? FilterMode.Linear : FilterMode.Nearest;
        }

        private static AddressMode ToAddress(int value)
        {
            if (value == (int)TextureWrapMode.ClampToEdge) return AddressMode.ClampToEdge;
            if (value == (int)TextureWrapMode.MirroredRepeat) return AddressMode.MirrorRepeat;
            return AddressMode.Repeat;
        }

        private static BlendFactor ToBlend(BlendingFactor value)
        {
            return value switch
            {
                BlendingFactor.Zero => BlendFactor.Zero,
                BlendingFactor.One => BlendFactor.One,
                BlendingFactor.SrcAlpha => BlendFactor.SrcAlpha,
                BlendingFactor.OneMinusSrcAlpha => BlendFactor.OneMinusSrcAlpha,
                BlendingFactor.DstAlpha => BlendFactor.DstAlpha,
                BlendingFactor.OneMinusDstAlpha => BlendFactor.OneMinusDstAlpha,
                BlendingFactor.SrcColor => BlendFactor.Src,
                BlendingFactor.OneMinusSrcColor => BlendFactor.OneMinusSrc,
                BlendingFactor.DstColor => BlendFactor.Dst,
                BlendingFactor.OneMinusDstColor => BlendFactor.OneMinusDst,
                _ => BlendFactor.One
            };
        }
    }
}
#endif
