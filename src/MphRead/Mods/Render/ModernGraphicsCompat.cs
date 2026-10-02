#if !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
#if !ANDROID
using OpenTK.Windowing.Desktop;
#endif
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
    /// Shared desktop and Android WebGPU compatibility renderer.
    ///
    /// It owns the selected DX12/Vulkan/Metal surface and implements enough of
    /// the historical GL state machine to draw the launcher/Avalonia surface
    /// through the same Begin/Vertex/TexImage calls the OpenGL path uses.
    /// World/display-list state is recorded too; its full material pipeline is
    /// layered onto this executor in the next pass.
    /// </summary>
    internal sealed unsafe partial class ModernGraphicsCompat : IDisposable
    {
        internal static bool SurfaceUsesSrgb => Current._surfaceFormat == WgpuTextureFormat.Bgra8UnormSrgb
            || Current._surfaceFormat == WgpuTextureFormat.Rgba8UnormSrgb;

        private sealed class NativeTexture
        {
            internal WgpuTexture* Texture;
            internal TextureView* View;
            internal TextureView* SampleView;
            internal WgpuSampler* Sampler;
            internal WgpuTextureFormat Format;
            internal int Width;
            internal int Height;
            internal int MipCount;
        }

        private sealed class GeometryList
        {
            internal float[] Vertices = Array.Empty<float>();
            internal int[] Triangles = Array.Empty<int>();
            internal int[] Lines = Array.Empty<int>();
            internal Vector3? EndNormal;
            internal Vector4? EndColor;
        }

        private readonly record struct PipelineKey(
            PrimitiveTopology Topology,
            WgpuTextureFormat ColorFormat,
            bool Blend,
            BlendingFactor Source,
            BlendingFactor Destination,
            ColorWriteMask WriteMask, BlendEquationMode Equation);

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
@group(0) @binding(2) var<uniform> viewport: vec4<f32>;

@vertex
fn vs_main(input: VertexInput) -> VertexOutput {
    var output: VertexOutput;
    output.position = vec4<f32>(input.position.xy * viewport.xy + viewport.zw,
        input.position.z * 0.5 + 0.5, 1.0);
    output.color = input.color;
    output.texcoord = input.texcoord.xy;
    return output;
}
@fragment
fn fs_main(input: VertexOutput) -> @location(0) vec4<f32> {
    return textureSample(picture, picture_sampler, input.texcoord) * input.color;
}
@fragment
fn fs_ui_srgb(input: VertexOutput) -> @location(0) vec4<f32> {
    let c = textureSample(picture, picture_sampler, input.texcoord) * input.color;
    let straight = c.rgb / max(c.a, 0.00001);
    let linear = select(pow((straight + vec3<f32>(0.055)) / 1.055, vec3<f32>(2.4)),
        straight / 12.92, straight <= vec3<f32>(0.04045));
    return vec4<f32>(linear * c.a, c.a);
}
";

        private WgpuBuffer* _uiViewportBuffer;
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
        private bool _normalSet;
        private Vector3 _listInitialNormal, _listInitialTexcoord;
        private Vector4 _listInitialColor;
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
        private bool _vsync = true;
        private PresentMode _presentMode = PresentMode.Fifo;
        private readonly HashSet<PresentMode> _presentModes = new();

        internal static void SetVSync(bool enabled)
        {
            var self = Current;
            if (self._vsync == enabled) return;
            self._vsync = enabled;
            self.ReleaseSurfaceTexture();
            self.SelectPresentMode();
            self.ConfigureSurface();
        }

        /// <summary>
        /// True when presentation itself owns the display cadence. Immediate
        /// and Mailbox can be software-paced; FIFO-style fallback cannot.
        /// </summary>
        internal static bool PresentationBlocks =>
            Current._presentMode is not (PresentMode.Immediate or PresentMode.Mailbox);

        internal static string ActivePresentMode => Current._presentMode.ToString();

        private void SelectPresentMode()
        {
            _presentMode = !_vsync && _presentModes.Contains(PresentMode.Immediate) ? PresentMode.Immediate
                : !_vsync && _presentModes.Contains(PresentMode.Mailbox) ? PresentMode.Mailbox : PresentMode.Fifo;
        }

        private void ConfigureSurface()
        {
            if (_width == 0 || _height == 0 || _device.Surface == null) return;
            Mods.DebugLog.Checkpoint("render",
                $"configuring {_device.Backend} surface: {_width}x{_height} "
                + $"format={_surfaceFormat} alpha={_alphaMode} present={_presentMode}");
            _api.SurfaceConfigure(_device.Surface, new SurfaceConfiguration
            {
                Device = _device.Device, Format = _surfaceFormat,
                Usage = TextureUsage.RenderAttachment | TextureUsage.CopySrc,
                AlphaMode = _alphaMode, Width = _width, Height = _height, PresentMode = _presentMode
            });
        }

        private bool _disposed;

        private ModernGraphicsCompat(ModernGraphicsDevice device, int width, int height,
            ModernGraphicsCompat? previous = null)
        {
            _device = device;
            if (previous != null)
            {
                _programs = previous._programs;
                _resources = previous._resources;
                _batch = previous._batch;
                _lists = previous._lists;
                _enabled = previous._enabled;
                _resources.InvalidateNativeResources();
            }
            _api = _device.Api;
            _queue = _api.DeviceGetQueue(_device.Device);
            try
            {
                Mods.DebugLog.Checkpoint("render", "modern startup: querying surface capabilities");
                QuerySurfaceFormat();
                Mods.DebugLog.Checkpoint("render", "modern startup: creating UI shader");
                CreateUiShader();
                Mods.DebugLog.Checkpoint("render", "modern startup: creating core shaders");
                CreateCoreShaders();
                Mods.DebugLog.Checkpoint("render", "modern startup: creating default texture");
                CreateWhiteTexture();
                Mods.DebugLog.Checkpoint("render", $"modern startup: configuring surface {width}x{height}");
                ResizeCore(width, height);
                if (previous != null)
                {
                    _wireframe = previous._wireframe;
                    _currentColor = previous._currentColor;
                    _currentNormal = previous._currentNormal;
                    _currentTexcoord = previous._currentTexcoord;
                    _colorSet = previous._colorSet;
                    _recording = previous._recording;
                    _recordingList = previous._recordingList;
                    _listHighWater = previous._listHighWater;
                    _clearColor = previous._clearColor;
                    _blendSource = previous._blendSource;
                    _blendDestination = previous._blendDestination;
                    _maskRed = previous._maskRed;
                    _maskGreen = previous._maskGreen;
                    _maskBlue = previous._maskBlue;
                    _maskAlpha = previous._maskAlpha;
                    _viewportX = previous._viewportX;
                    _viewportY = previous._viewportY;
                    _viewportWidth = previous._viewportWidth;
                    _viewportHeight = previous._viewportHeight;
                    _scissorX = previous._scissorX;
                    _scissorY = previous._scissorY;
                    _scissorWidth = previous._scissorWidth;
                    _scissorHeight = previous._scissorHeight;
                    _vsync = previous._vsync;
                    _depthWrite = previous._depthWrite;
                    _depthFunction = previous._depthFunction;
                    _cullFace = previous._cullFace;
                    _alphaFunction = previous._alphaFunction;
                    _alphaReference = previous._alphaReference;
                    _blendEquation = previous._blendEquation;
                    _clearStencil = previous._clearStencil;
                    _stencilFunction = previous._stencilFunction;
                    _stencilReference = previous._stencilReference;
                    _stencilReadMask = previous._stencilReadMask;
                    _stencilWriteMask = previous._stencilWriteMask;
                    _stencilFail = previous._stencilFail;
                    _stencilDepthFail = previous._stencilDepthFail;
                    _stencilPass = previous._stencilPass;
                    _polygonOffsetFactor = previous._polygonOffsetFactor;
                    _polygonOffsetUnits = previous._polygonOffsetUnits;
                    SelectPresentMode();
                    ConfigureSurface();
                }
                _device.ThrowIfFailed();
                LogCapabilities();
                Mods.DebugLog.Checkpoint("render",
                    $"modern compatibility renderer active: {GraphicsBackendPolicy.DisplayName(_device.Backend)} "
                    + $"on {_device.AdapterName}");
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal static bool Active => _current != null;

#if !ANDROID
        internal static void Initialize(NativeWindow window, GraphicsBackend backend)
        {
            Shutdown();
            _deviceRecoveryAttempts = 0;
            _deviceRecoveryFailure = null;
            DeviceGeneration++;
            _current = new ModernGraphicsCompat(ModernGraphicsDevice.CreateForWindow(window, backend),
                window.FramebufferSize.X, window.FramebufferSize.Y);
        }
#else
        internal static void AttachAndroidWindow(nint window, int width, int height)
        {
            if (_current == null)
            {
                _deviceRecoveryAttempts = 0;
                _deviceRecoveryFailure = null;
                DeviceGeneration++;
                _current = new ModernGraphicsCompat(ModernGraphicsDevice.CreateForAndroidWindow(window), width, height);
            }
            else
            {
                var self = Current;
                self.ReleaseSurfaceTexture();
                self._device.SetAndroidWindow(window);
                self.QuerySurfaceFormat();
                self._width = self._height = 0;
                self.ResizeCore(width, height);
            }
        }

        internal static void DetachAndroidWindow()
        {
            // Teardown must not try to reconstruct a lost device, especially
            // after reconstruction itself failed.
            var current = _current;
            if (current == null || current._disposed) return;
            current.ReleaseSurfaceTexture();
            current._device.SetAndroidWindow(0);
            current._width = current._height = 0;
        }
#endif

        internal static void Shutdown()
        {
            ModernGraphicsCompat? current = _current;
            _current = null;
            current?.Dispose();
        }

        private static int _deviceGeneration;
        internal static (GraphicsBackend Backend, string Adapter, string Driver) DeviceIdentity
            => (Current._device.Backend, Current._device.AdapterName, Current._device.DriverDescription);

        internal static int DeviceGeneration
        {
            get
            {
                // Scene history must observe reconstruction before the first draw.
                if (_current != null && _deviceRecoveryFailure == null && _current._device.IsLost)
                    _ = Current;
                return _deviceGeneration;
            }
            private set => _deviceGeneration = value;
        }
        private static int _deviceRecoveryAttempts;
        private static Exception? _deviceRecoveryFailure;
        internal static Exception? RecoveryFailure => _deviceRecoveryFailure;
        private static ModernGraphicsCompat Current
        {
            get
            {
                if (_deviceRecoveryFailure != null)
                    throw new InvalidOperationException("WebGPU device reconstruction failed; restart with OpenGL.", _deviceRecoveryFailure);
                var current = _current ?? throw new InvalidOperationException("Modern graphics compatibility renderer is not active.");
                if (current._device.IsLost && current._device.Surface != null)
                {
                    if (_deviceRecoveryAttempts++ != 0)
                    {
                        _deviceRecoveryFailure = new InvalidOperationException("WebGPU device was lost again after reconstruction.");
                        throw _deviceRecoveryFailure;
                    }
                    Console.Error.WriteLine("[render] Reconstructing the lost WebGPU device and renderer resources.");
                    try
                    {
                        _current = new ModernGraphicsCompat(current._device.CreateReplacement(),
                            (int)current._width, (int)current._height, current);
                        DeviceGeneration++;
                    }
                    catch (Exception ex)
                    {
                        _deviceRecoveryFailure = ex;
                        throw;
                    }
                    finally { current.Dispose(); }
                    current = _current;
                }
                return current;
            }
        }

        internal static void Resize(int width, int height)
        {
            if (_current == null) return;
            Current.ResizeCore(width, height);
        }

        internal static void DestroyDeviceForCheck()
        {
            var self = Current;
            self._device.DestroyForCheck();
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
                self.DrawTransientBatch();
                self._batch.Clear();
            }
        }

        internal static void Vertex3(float x, float y, float z)
        {
            ModernGraphicsCompat self = Current;
            self._batch.AddVertex(new Vector3(x, y, z), self._currentColor,
                self._currentNormal, self._currentTexcoord, self._colorSet, !self._recording || self._normalSet);
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
            var self = Current;
            self._currentNormal = new Vector3(x, y, z);
            self._normalSet = true;
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
            self._listInitialColor = self._currentColor;
            self._listInitialNormal = self._currentNormal;
            self._listInitialTexcoord = self._currentTexcoord;
            self._colorSet = self._normalSet = false;
        }

        internal static void EndList()
        {
            ModernGraphicsCompat self = Current;
            self._recording = false;
            self.ReleaseListGeometry(self._recordingList);
            self._lists[self._recordingList] = new GeometryList
            {
                Vertices = self._batch.Vertices.ToArray(),
                Triangles = self._batch.TriIndices.ToArray(),
                Lines = self._batch.LineIndices.ToArray(),
                EndNormal = self._normalSet ? self._currentNormal : null,
                EndColor = self._colorSet ? self._currentColor : null
            };
            self._currentNormal = self._listInitialNormal;
            self._currentColor = self._listInitialColor;
            self._currentTexcoord = self._listInitialTexcoord;
            self._batch.Clear();
        }

        internal static void CallList(int list)
        {
            ModernGraphicsCompat self = Current;
            if (self._lists.TryGetValue(list, out GeometryList? geometry))
            {
                self._drawingList = true;
                try { self.DrawBatch(geometry.Vertices, geometry.Triangles, geometry.Lines); }
                finally { self._drawingList = false; }
                if (geometry.EndNormal is Vector3 normal) self._currentNormal = normal;
                if (geometry.EndColor is Vector4 color) self._currentColor = color;
            }
        }

        internal static void DeleteLists(int list, int range)
        {
            ModernGraphicsCompat self = Current;
            for (int i = 0; i < range; i++)
            {
                self.ReleaseListGeometry(list + i);
                self._lists.Remove(list + i);
            }
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

        internal static void GenerateMipmap(GenerateMipmapTarget target)
        {
            var self = Current;
            self._resources.GenerateMipmap(target);
            self.EnsureTexture(self._resources.BoundTexture(self._resources.ActiveTextureUnit));
        }

        internal static void TexImage2D(TextureTarget target, PixelInternalFormat internalFormat,
            int width, int height, PixelFormat format, PixelType type, IntPtr pixels) =>
            Current._resources.TexImage2D(target, internalFormat, width, height, format, type, pixels);

        internal static void TexImage2D<T>(TextureTarget target, PixelInternalFormat internalFormat,
            int width, int height, PixelFormat format, PixelType type, T[] pixels) where T : struct =>
            Current._resources.TexImage2D(target, internalFormat, width, height, format, type, pixels);

        internal static void TexSubImage2D(TextureTarget target, int x, int y, int width, int height,
            PixelFormat format, PixelType type, IntPtr pixels)
        {
            var self = Current;
            int id = self._resources.BoundTexture(self._resources.ActiveTextureUnit);
            var native = self.EnsureTexture(id);
            self._resources.TexSubImage2D(target, x, y, width, height, format, type, pixels);
            self.UploadSubImage(id, native, x, y, width, height);
        }

        internal static void TexSubImage2D<T>(TextureTarget target, int x, int y, int width, int height,
            PixelFormat format, PixelType type, T[] pixels) where T : struct
        {
            var self = Current;
            int id = self._resources.BoundTexture(self._resources.ActiveTextureUnit);
            var native = self.EnsureTexture(id);
            self._resources.TexSubImage2D(target, x, y, width, height, format, type, pixels);
            self.UploadSubImage(id, native, x, y, width, height);
        }

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
            self._scissorWidth = Math.Max(0, width);
            self._scissorHeight = Math.Max(0, height);
        }

        private void ApplyScissor(RenderPassEncoder* pass, int width, int height)
        {
            if (!_enabled.Contains(EnableCap.ScissorTest)) return;
            // Clip both endpoints, not the origin followed by the old extent.
            // Zero-area and wholly offscreen rectangles must remain empty.
            long left = Math.Clamp((long)_scissorX, 0, width);
            long right = Math.Clamp((long)_scissorX + _scissorWidth, 0, width);
            long bottom = Math.Clamp((long)_scissorY, 0, height);
            long top = Math.Clamp((long)_scissorY + _scissorHeight, 0, height);
            _api.RenderPassEncoderSetScissorRect(pass, (uint)left, (uint)(height - top),
                (uint)(right - left), (uint)(top - bottom));
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
        internal static void DeleteRenderbuffer(int renderbuffer)
        {
            ModernGraphicsCompat self = Current;
            self._resources.DeleteRenderbuffer(renderbuffer);
            if (self._nativeRenderbuffers.Remove(renderbuffer, out NativeRenderbuffer? native))
                self.ReleaseNativeRenderbuffer(native);
        }
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
                StringName.Extensions => "GL_EXT_texture_filter_anisotropic",
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
                (GetPName)0x84FF => 16,
                GetPName.CurrentProgram => self._programs.CurrentProgram,
                GetPName.DrawFramebufferBinding => self._resources.DrawFramebuffer,
                GetPName.ReadFramebufferBinding => self._resources.ReadFramebuffer,
                GetPName.RenderbufferBinding => self._resources.BoundRenderbuffer,
                GetPName.ActiveTexture => (int)TextureUnit.Texture0 + self._resources.ActiveTextureUnit,
                GetPName.TextureBinding2D => self._resources.BoundTexture(self._resources.ActiveTextureUnit),
                GetPName.PackAlignment => 4,
                GetPName.UnpackAlignment => 4,
                GetPName.MaxTextureSize => 8192,
                GetPName.MaxRenderbufferSize => 8192,
                GetPName.DepthWritemask => self._depthWrite ? 1 : 0,
                GetPName.BlendSrcRgb => (int)self._blendSource,
                GetPName.BlendDstRgb => (int)self._blendDestination,
                GetPName.CullFaceMode => (int)self._cullFace,
                GetPName.DepthFunc => (int)self._depthFunction,
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
        internal static void AlphaFunc(AlphaFunction function, float reference) { Current._alphaFunction = function; Current._alphaReference = reference; }
        internal static void PolygonMode(TriangleFace face, OpenTK.Graphics.OpenGL.PolygonMode mode)
        {
            if (face != TriangleFace.FrontAndBack || mode is not (OpenTK.Graphics.OpenGL.PolygonMode.Fill or OpenTK.Graphics.OpenGL.PolygonMode.Line))
                throw new NotSupportedException("Modern polygon mode supports Fill/Line for both faces.");
            Current._wireframe = mode == OpenTK.Graphics.OpenGL.PolygonMode.Line;
        }
        // Portable WebGPU line primitives have a fixed one-pixel width.
        // Retain the GL facade call; wireframe topology is implemented above.
        internal static void LineWidth(float width) { }
        internal static void ClearStencil(int value) { Current._clearStencil = value; }
        internal static void DepthMask(bool enabled) { Current._depthWrite = enabled; }
        internal static void DepthFunc(DepthFunction function) { Current._depthFunction = function; }
        internal static void CullFace(TriangleFace face) { Current._cullFace = face; }
        internal static void BlendEquation(BlendEquationMode mode) { Current._blendEquation = mode; }
        internal static void StencilFunc(StencilFunction function, int reference, int mask) { Current._stencilFunction = function; Current._stencilReference = reference; Current._stencilReadMask = mask; }
        internal static void StencilOp(OpenTK.Graphics.OpenGL.StencilOp fail,
            OpenTK.Graphics.OpenGL.StencilOp zfail, OpenTK.Graphics.OpenGL.StencilOp zpass)
        { Current._stencilFail = fail; Current._stencilDepthFail = zfail; Current._stencilPass = zpass; }
        internal static void StencilMask(int mask) { Current._stencilWriteMask = mask; }
        internal static void PolygonOffset(float factor, float units) { Current._polygonOffsetFactor = factor; Current._polygonOffsetUnits = units; }
        internal static void MatrixMode(MatrixMode mode) { }
        internal static void PushMatrix() { }
        internal static void PopMatrix() { }
        internal static void LoadIdentity() { }
        internal static void PushAttrib(AttribMask mask) => Current.SaveAttributes(mask);
        internal static void PopAttrib() => Current.RestoreAttributes();
        internal static void DebugMessageCallback(DebugProc callback, IntPtr userParam) { }

        internal static void Finish()
        {
            var current = Current;
            current.FlushCommands();
            current.ResetFrameBuffers();
            current._device.Native.DevicePoll(current._device.Device, true, null);
        }

        internal static void ReadPixels<T>(int x, int y, int width, int height,
            PixelFormat format, PixelType type, T[] pixels) where T : struct
        {
            Current.ReadPixelsCore(x, y, width, height, format, type, pixels);
        }

        internal static void CopyTexSubImage2D(TextureTarget target, int level, int xoffset, int yoffset,
            int x, int y, int width, int height)
        {
            Current.CopyTexSubImage2DCore(target, level, xoffset, yoffset, x, y, width, height);
        }

        internal static void BlitFramebuffer(int sourceX0, int sourceY0, int sourceX1, int sourceY1,
            int destinationX0, int destinationY0, int destinationX1, int destinationY1,
            ClearBufferMask mask, BlitFramebufferFilter filter)
        {
            Current.BlitFramebufferCore(sourceX0, sourceY0, sourceX1, sourceY1,
                destinationX0, destinationY0, destinationX1, destinationY1, mask, filter);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            DiscardCommands();
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
            ReleaseSurfaceDepth();
            if (_clearShader != null) _api.ShaderModuleRelease(_clearShader);
            DisposeGeometry();
            DisposeGeneratedShaders();
            DisposeCoreShaders();
            DisposeUniformBuffers();
            if (_uiShader != null) _api.ShaderModuleRelease(_uiShader);
            if (_queue != null) _api.QueueRelease(_queue);
            _device.Dispose();
        }

        private void LogCapabilities()
        {
            SupportedLimits limits = default;
            _api.DeviceGetLimits(_device.Device, &limits);
            bool timestamp = _api.AdapterHasFeature(_device.Adapter, FeatureName.TimestampQuery);
            string capabilities = $"backend={_device.Backend} adapter=\"{_device.AdapterName}\" "
                + $"driver=\"{_device.DriverDescription}\" wgpu=0x{_device.NativeVersion:x8} "
                + $"surface={_surfaceFormat} maxTexture2D={limits.Limits.MaxTextureDimension2D} "
                + $"anisotropy=16 presentModes={string.Join(',', _presentModes)} "
                + $"internalHdr=RGBA16Float outputHdr=false depth=Depth24Plus,Depth24PlusStencil8 "
                + $"adapterTimestampQuery={timestamp} gpuTimingEnabled=false";
            Console.WriteLine("[render] " + capabilities);
            Mods.DebugLog.Line("render", capabilities);
        }

        private void QuerySurfaceFormat()
        {
            SurfaceCapabilities capabilities = default;
            _api.SurfaceGetCapabilities(_device.Surface, _device.Adapter, ref capabilities);
            try
            {
                if (capabilities.FormatCount == 0)
                    throw new InvalidOperationException("WebGPU surface reports no presentation formats.");
                _presentModes.Clear();
                for (nuint i = 0; i < capabilities.PresentModeCount; i++) _presentModes.Add(capabilities.PresentModes[i]);
                SelectPresentMode();
                _surfaceFormat = capabilities.Formats[0];
                // Shared GL shaders already produce display-encoded colors. Match
                // the legacy non-sRGB framebuffer instead of encoding them twice.
                for (nuint i = 0; i < capabilities.FormatCount; i++)
                    if (capabilities.Formats[i] is WgpuTextureFormat.Bgra8Unorm or WgpuTextureFormat.Rgba8Unorm)
                    { _surfaceFormat = capabilities.Formats[i]; break; }
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
            if (width <= 0 || height <= 0)
            {
                ReleaseSurfaceTexture();
                _width = _height = 0;
                return;
            }
            uint newWidth = (uint)width;
            uint newHeight = (uint)height;
            if (_width == newWidth && _height == newHeight) return;
            ReleaseSurfaceTexture();
            ReleaseSurfaceDepth();
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
            ConfigureSurface();
        }

        private bool AcquireSurfaceTexture()
        {
            _device.ThrowIfFailed();
            if (_surfaceAcquired) return true;
            if (_width == 0 || _height == 0) return false;

            for (int attempt = 0; attempt < 2; attempt++)
            {
                SurfaceTexture acquired = default;
                long acquireStart = PerformanceStart();
                _api.SurfaceGetCurrentTexture(_device.Surface, &acquired);
                if (acquireStart != 0)
                {
                    double acquireMs = System.Diagnostics.Stopwatch.GetElapsedTime(acquireStart).TotalMilliseconds;
                    _surfaceAcquisitions++; _surfaceAcquireMs += acquireMs;
                    _longestSurfaceAcquireMs = Math.Max(_longestSurfaceAcquireMs, acquireMs);
                }
                _surfaceTexture = acquired;
                if (acquired.Status == SurfaceGetCurrentTextureStatus.Success && acquired.Texture != null)
                {
                    _surfaceView = _api.TextureCreateView(acquired.Texture, null);
                    _surfaceAcquired = _surfaceView != null;
                    if (!_surfaceAcquired) ReleaseSurfaceTexture();
                    return _surfaceAcquired;
                }
                ReleaseSurfaceTexture();
                if (acquired.Status == SurfaceGetCurrentTextureStatus.Timeout) return false;
                if (acquired.Status is SurfaceGetCurrentTextureStatus.Outdated or SurfaceGetCurrentTextureStatus.Lost)
                {
                    if (attempt != 0) break;
                    if (acquired.Status == SurfaceGetCurrentTextureStatus.Lost) _device.RecreateSurface();
                    QuerySurfaceFormat();
                    ConfigureSurface();
                    continue;
                }
                throw new InvalidOperationException($"WebGPU presentation failed: {acquired.Status}.");
            }
            throw new InvalidOperationException("WebGPU surface could not be recovered after reconfiguration.");
        }

        private void PresentCore()
        {
            _device.ThrowIfFailed();
            FlushCommands();
            ResetFrameBuffers();
            if (!_surfaceAcquired) return;
            _api.SurfacePresent(_device.Surface);
            ReleaseSurfaceTexture();
#if !ANDROID
            // Startup is only considered healthy once a modern frame reaches
            // the presentation surface. This also covers failures that occur
            // after device creation but before the first visible frame.
            GraphicsBackendPolicy.CompleteStartupAttempt(_device.Backend);
#endif
        }

        private void ReleaseSurfaceTexture()
        {
            if (!_disposed) FlushCommands();
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
            _nativeTextures.TryGetValue(id, out NativeTexture? existing);
            WgpuTextureFormat format = NativeTextureFormat(record);
            byte[] data = record.Dirty ? ConvertPixels(record, ref format) : Array.Empty<byte>();
            int width = Math.Max(1, record.Width);
            int height = Math.Max(1, record.Height);
            bool depth = format == WgpuTextureFormat.Depth24Plus || format == WgpuTextureFormat.Depth24PlusStencil8;
            int mipCount = record.HasMipmaps && !depth
                ? 1 + (int)Math.Floor(Math.Log2(Math.Max(width, height))) : 1;
            if (existing != null && existing.Width == width && existing.Height == height
                && existing.Format == format && existing.MipCount == mipCount)
            {
                if (record.Dirty && data.Length > 0) UploadTexture(existing, data);
                if (record.SamplerDirty) UpdateSampler(existing, record);
                if (mipCount > 1 && (record.Dirty || record.MipmapsDirty)) GenerateNativeMipmaps(existing);
                record.Dirty = record.MipmapsDirty = false;
                return existing;
            }
            // Expanding a render target's mip chain must preserve its GPU base
            // level; the CPU image may be absent or older than the rendered image.
            bool preserveBase = existing != null && !record.Dirty
                && existing.Width == width && existing.Height == height && existing.Format == format;
            var descriptor = new TextureDescriptor
            {
                Size = new Extent3D((uint)width, (uint)height, 1),
                Format = format,
                Usage = TextureUsage.CopyDst | TextureUsage.TextureBinding
                    | TextureUsage.RenderAttachment | TextureUsage.CopySrc,
                MipLevelCount = (uint)mipCount,
                SampleCount = 1,
                Dimension = TextureDimension.Dimension2D
            };
            var native = new NativeTexture
            {
                Texture = _api.DeviceCreateTexture(_device.Device, descriptor),
                Format = format,
                Width = width,
                Height = height,
                MipCount = mipCount
            };
            if (native.Texture == null)
                throw new InvalidOperationException($"Could not allocate modern texture {id}.");

            native.View = CreateMipView(native, 0);
            if (format == WgpuTextureFormat.Depth24PlusStencil8)
            {
                var depthViewDescriptor = new TextureViewDescriptor
                {
                    // A depth-only view of Depth24PlusStencil8 resolves to the
                    // aspect-specific Depth24Plus view format in WebGPU.
                    Format = WgpuTextureFormat.Depth24Plus,
                    Dimension = TextureViewDimension.Dimension2D,
                    Aspect = TextureAspect.DepthOnly,
                    BaseMipLevel = 0,
                    MipLevelCount = 1,
                    BaseArrayLayer = 0,
                    ArrayLayerCount = 1
                };
                native.SampleView = _api.TextureCreateView(native.Texture, depthViewDescriptor);
            }
            else if (format == WgpuTextureFormat.Depth24Plus)
            {
                native.SampleView = native.View;
            }
            else
            {
                native.SampleView = mipCount > 1 ? _api.TextureCreateView(native.Texture, null) : native.View;
            }
            UpdateSampler(native, record);
            if (preserveBase)
            {
                var encoder = BeginCommands();
                var source = new ImageCopyTexture { Texture = existing!.Texture, Aspect = TextureAspect.All };
                var destination = new ImageCopyTexture { Texture = native.Texture, Aspect = TextureAspect.All };
                var extent = new Extent3D((uint)width, (uint)height, 1);
                _api.CommandEncoderCopyTextureToTexture(encoder, &source, &destination, &extent);
                EndCommands();

            }
            else if (data.Length > 0) UploadTexture(native, data);
            if (mipCount > 1) GenerateNativeMipmaps(native);
            if (depth && record.MipmapsDirty)
                Mods.DebugLog.Line("render", $"Texture {id}: mipmaps unavailable for {format}; using base level.");
            if (existing != null) ReleaseNativeTexture(existing);
            record.NativeMipCount = mipCount;
            record.Dirty = record.MipmapsDirty = false;
            _nativeTextures[id] = native;
            return native;
        }

        private void UpdateSampler(NativeTexture native, ModernGraphicsResourceState.TextureRecord record)
        {
            if (native.Sampler != null) _api.SamplerRelease(native.Sampler);
            bool linearMip = record.MinFilter == (int)TextureMinFilter.NearestMipmapLinear
                || record.MinFilter == (int)TextureMinFilter.LinearMipmapLinear;
            bool useMips = record.MinFilter != (int)TextureMinFilter.Nearest
                && record.MinFilter != (int)TextureMinFilter.Linear;
            // WebGPU requires all three filters to be linear for anisotropy.
            ushort anisotropy = (ushort)(ToFilter(record.MinFilter) == FilterMode.Linear
                && ToFilter(record.MagFilter) == FilterMode.Linear && linearMip ? record.Anisotropy : 1);
            native.Sampler = _api.DeviceCreateSampler(_device.Device, new SamplerDescriptor
            {
                MinFilter = ToFilter(record.MinFilter),
                MagFilter = ToFilter(record.MagFilter),
                MipmapFilter = linearMip ? MipmapFilterMode.Linear : MipmapFilterMode.Nearest,
                LodMinClamp = 0,
                LodMaxClamp = useMips ? native.MipCount - 1 : 0,
                AddressModeU = ToAddress(record.WrapS),
                AddressModeV = ToAddress(record.WrapT),
                AddressModeW = AddressMode.ClampToEdge,
                MaxAnisotropy = anisotropy
            });
            record.SamplerDirty = false;
        }

        private void UploadSubImage(int id, NativeTexture native, int x, int y, int width, int height)
        {
            if (width == 0 || height == 0) return;
            var record = _resources.Texture(id);
            // CPU storage is a recovery image, not an authoritative copy of a
            // rendered texture. Upload only the patch; preserve all other GPU pixels.
            int pixelBytes = ModernGraphicsResourceState.BytesPerPixel(record.Format, record.Type);
            int stride = checked(width * pixelBytes);
            byte[] patch;
            if (!record.FramebufferOrigin && x == 0 && y == 0
                && width == record.Width && height == record.Height)
            {
                // Video/UI producers replace the whole image. Reuse their CPU
                // storage instead of allocating another full-frame staging copy.
                patch = record.Pixels!;
            }
            else
            {
                patch = new byte[checked(stride * height)];
                for (int row = 0; row < height; row++)
                    System.Buffer.BlockCopy(record.Pixels!, ((y + row) * record.Width + x) * pixelBytes,
                        patch, (record.FramebufferOrigin ? height - 1 - row : row) * stride, stride);
            }
            var patchRecord = new ModernGraphicsResourceState.TextureRecord
            {
                Width = width, Height = height, Pixels = patch,
                Format = record.Format, Type = record.Type, InternalFormat = record.InternalFormat
            };
            var format = native.Format;
            byte[] data = ConvertPixels(patchRecord, ref format);
            if (format != native.Format)
                throw new NotSupportedException("Texture sub-image changed the native format.");
            UploadTexture(native, data, x, record.FramebufferOrigin ? record.Height - y - height : y,
                width, height);
            record.Dirty = false;
            record.MipmapsDirty = record.HasMipmaps;
        }

        private void UploadTexture(NativeTexture native, byte[] data, int x = 0, int y = 0,
            int width = 0, int height = 0)
        {
            long uploadStart = PerformanceStart();
            // Texture updates must follow earlier draws sampling the same image.
            FlushCommands();
            if (width == 0) width = native.Width;
            if (height == 0) height = native.Height;
            if (data.Length > 0)
            {
                var destination = new ImageCopyTexture
                {
                    Texture = native.Texture,
                    Origin = new Origin3D((uint)x, (uint)y, 0),
                    Aspect = TextureAspect.All,
                    MipLevel = 0
                };
                uint bytesPerRow = checked((uint)(width * (native.Format == WgpuTextureFormat.Rgba16float ? 8 : 4)));
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
            if (uploadStart != 0)
            {
                _textureUploadBytes += checked((long)width * height *
                    (native.Format == WgpuTextureFormat.Rgba16float ? 8 : 4));
                _textureUploadMs += System.Diagnostics.Stopwatch.GetElapsedTime(uploadStart).TotalMilliseconds;
            }
        }

        private TextureView* CreateMipView(NativeTexture native, int level)
        {
            return _api.TextureCreateView(native.Texture, new TextureViewDescriptor
            {
                Format = native.Format, Dimension = TextureViewDimension.Dimension2D,
                Aspect = TextureAspect.All, BaseMipLevel = (uint)level, MipLevelCount = 1,
                BaseArrayLayer = 0, ArrayLayerCount = 1
            });
        }

        private void GenerateNativeMipmaps(NativeTexture native)
        {
            if (native.MipCount <= 1) return;

            // One staging surface per source texture is enough for the entire
            // chain. The old path allocated and destroyed a native texture for
            // every mip level, which made 4K/8K material startup dominated by
            // driver allocation and synchronization rather than downsampling.
            int scratchWidth = Math.Max(1, native.Width >> 1);
            int scratchHeight = Math.Max(1, native.Height >> 1);
            WgpuTexture* scratch = null;
            TextureView* scratchView = null;
            try
            {
                var scratchDescriptor = new TextureDescriptor
                {
                    Size = new Extent3D((uint)scratchWidth, (uint)scratchHeight, 1),
                    Format = native.Format,
                    Usage = TextureUsage.RenderAttachment | TextureUsage.TextureBinding
                        | TextureUsage.CopySrc,
                    MipLevelCount = 1,
                    SampleCount = 1,
                    Dimension = TextureDimension.Dimension2D
                };
                scratch = _api.DeviceCreateTexture(_device.Device, scratchDescriptor);
                if (scratch == null)
                    throw new InvalidOperationException("Could not allocate staged mip target.");
                scratchView = _api.TextureCreateView(scratch, null);
                if (scratchView == null)
                    throw new InvalidOperationException("Could not create staged mip view.");

                for (int level = 1; level < native.MipCount; level++)
                {
                    TextureView* source = CreateMipView(native, level - 1);
                    try
                    {
                        int sw = Math.Max(1, native.Width >> (level - 1));
                        int sh = Math.Max(1, native.Height >> (level - 1));
                        int dw = Math.Max(1, native.Width >> level);
                        int dh = Math.Max(1, native.Height >> level);

                        // Use the reusable surface's upper-left dw x dh region.
                        // BlitTargets receives the logical destination extent,
                        // so the viewport and sampling remain identical to the
                        // previous one-texture-per-level implementation.
                        BlitTargets(new CoreTarget(native.Texture, source, native.Format, null, sw, sh),
                            new CoreTarget(scratch, scratchView, native.Format, null, dw, dh),
                            0, 0, sw, sh, 0, 0, dw, dh, BlitFramebufferFilter.Linear);

                        var from = new ImageCopyTexture
                        {
                            Texture = scratch, MipLevel = 0, Aspect = TextureAspect.All
                        };
                        var to = new ImageCopyTexture
                        {
                            Texture = native.Texture, MipLevel = (uint)level, Aspect = TextureAspect.All
                        };
                        var extent = new Extent3D((uint)dw, (uint)dh, 1);
                        _api.CommandEncoderCopyTextureToTexture(
                            BeginCommands(), &from, &to, &extent);
                        EndCommands();
                    }
                    finally
                    {
                        _api.TextureViewRelease(source);
                    }
                }
            }
            finally
            {
                if (scratchView != null) _api.TextureViewRelease(scratchView);
                if (scratch != null) _api.TextureRelease(scratch);
            }
        }

        private static WgpuTextureFormat NativeTextureFormat(
            ModernGraphicsResourceState.TextureRecord record)
        {
            return record.InternalFormat switch
            {
                PixelInternalFormat.Depth24Stencil8 => WgpuTextureFormat.Depth24PlusStencil8,
                PixelInternalFormat.DepthComponent => WgpuTextureFormat.Depth24Plus,
                PixelInternalFormat.DepthComponent24 => WgpuTextureFormat.Depth24Plus,
                PixelInternalFormat.Rgba16f => WgpuTextureFormat.Rgba16float,
                PixelInternalFormat.Rgba8 => WgpuTextureFormat.Rgba8Unorm,
                _ => record.Format == PixelFormat.Bgra
                    ? WgpuTextureFormat.Bgra8Unorm
                    : WgpuTextureFormat.Rgba8Unorm
            };
        }

                private static byte[] ConvertPixels(ModernGraphicsResourceState.TextureRecord record,
            ref WgpuTextureFormat format)
        {
            if (record.Width <= 0 || record.Height <= 0 || record.Pixels == null)
                return Array.Empty<byte>();
            if (format == WgpuTextureFormat.Rgba16float && record.Format == PixelFormat.Rgba)
            {
                int count = checked(record.Width * record.Height * 4);
                if (record.Type == PixelType.HalfFloat) return record.Pixels;
                var halves = new byte[checked(count * 2)];
                for (int i = 0; i < count; i++)
                {
                    float value = record.Type == PixelType.Float ? BitConverter.ToSingle(record.Pixels, i * 4)
                        : record.Type == PixelType.UnsignedByte ? record.Pixels[i] / 255f
                        : throw new NotSupportedException($"HDR upload type {record.Type} is unsupported.");
                    BitConverter.TryWriteBytes(halves.AsSpan(i * 2, 2), BitConverter.HalfToUInt16Bits((System.Half)value));
                }
                return halves;
            }
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

        private void DrawTransientBatch()
        {
            DrawBatch(CollectionsMarshal.AsSpan(_batch.Vertices),
                CollectionsMarshal.AsSpan(_batch.TriIndices),
                CollectionsMarshal.AsSpan(_batch.LineIndices),
                persistentVertices: null, persistentTriangles: null, persistentLines: null);
        }

        private void DrawBatch(float[] vertices, int[] triangles, int[] lines) =>
            DrawBatch(vertices, triangles, lines, vertices, triangles, lines);

        private void DrawBatch(ReadOnlySpan<float> vertices, ReadOnlySpan<int> triangles,
            ReadOnlySpan<int> lines, float[]? persistentVertices, int[]? persistentTriangles,
            int[]? persistentLines)
        {
            if (vertices.Length == 0) return;
            ModernProgramKind kind = CurrentProgramKind();
            bool core = _resources.DrawFramebuffer != 0
                || UsesGeneratedShader(kind)
                || kind == ModernProgramKind.World
                || kind == ModernProgramKind.Rtt
                || kind == ModernProgramKind.Shift
                || kind == ModernProgramKind.Cel
                || kind == ModernProgramKind.PlayerOutline
                || kind == ModernProgramKind.ToneMap;
            if (triangles.Length > 0)
            {
                ReadOnlySpan<int> indices = triangles;
                int[]? persistentIndices = persistentTriangles;
                if (_wireframe)
                {
                    // Display lists retain their cached edge array. Dynamic
                    // wireframe is diagnostic-only; it may materialize here,
                    // while ordinary gameplay remains allocation-free.
                    int[] source = persistentTriangles ?? triangles.ToArray();
                    int[] edges = WireframeIndices(source);
                    indices = edges;
                    persistentIndices = persistentTriangles != null ? edges : null;
                }
                var topology = _wireframe ? PrimitiveTopology.LineList : PrimitiveTopology.TriangleList;
                if (core) DrawCoreIndexed(vertices, indices, topology, kind,
                    persistentVertices, persistentIndices);
                else DrawIndexed(vertices, indices, topology,
                    persistentVertices, persistentIndices);
            }
            if (lines.Length > 0)
            {
                if (core) DrawCoreIndexed(vertices, lines, PrimitiveTopology.LineList, kind,
                    persistentVertices, persistentLines);
                else DrawIndexed(vertices, lines, PrimitiveTopology.LineList,
                    persistentVertices, persistentLines);
            }
        }

        private void DrawIndexed(ReadOnlySpan<float> vertices, ReadOnlySpan<int> indices,
            PrimitiveTopology topology, float[]? persistentVertices = null,
            int[]? persistentIndices = null)
        {
            if (!AcquireSurfaceTexture()) return;
            PipelineRecord pipeline = Pipeline(topology);

            ulong vertexBytes = (ulong)(vertices.Length * sizeof(float));
            ulong indexBytes = (ulong)(indices.Length * sizeof(int));
            NativeGeometry geometryBuffers = persistentVertices != null && persistentIndices != null
                ? PrepareGeometry(persistentVertices, persistentIndices)
                : PrepareGeometry(vertices, indices);
            WgpuBuffer* vertex = geometryBuffers.Vertex;
            WgpuBuffer* index = geometryBuffers.Index;

            TextureView* textureView = _whiteView;
            WgpuSampler* sampler = _whiteSampler;
            int bound = _resources.BoundTexture(0);
            if (_enabled.Contains(EnableCap.Texture2D) && bound != 0)
            {
                NativeTexture texture = EnsureTexture(bound);
                textureView = texture.SampleView;
                sampler = texture.Sampler;
            }

            _uiViewportBuffer = RentUniformBuffer(16);
            var viewport = ViewportTransform((int)_width, (int)_height);
            WriteProfiledBuffer(_uiViewportBuffer, 0, &viewport, 16);
            var entries = stackalloc BindGroupEntry[3];
            entries[0] = new BindGroupEntry { Binding = 0, TextureView = textureView };
            entries[1] = new BindGroupEntry { Binding = 1, Sampler = sampler };
            entries[2] = new BindGroupEntry { Binding = 2, Buffer = _uiViewportBuffer, Size = 16 };
            BindGroup* bindGroup = CreateTrackedBindGroup( new BindGroupDescriptor
            {
                Layout = pipeline.Layout,
                Entries = entries,
                EntryCount = 3
            });

            CommandEncoder* encoder = BeginCommands();
            var attachment = new RenderPassColorAttachment
            {
                DepthSlice = uint.MaxValue, // WGPU_DEPTH_SLICE_UNDEFINED: this is a 2D view.
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
            _api.RenderPassEncoderSetViewport(pass, 0, 0, _width, _height, 0, 1);
            ApplyScissor(pass, (int)_width, (int)_height);
            _api.RenderPassEncoderDrawIndexed(pass, (uint)indices.Length, 1, 0, 0, 0);
            _api.RenderPassEncoderEnd(pass);
            EndCommands();

            _api.RenderPassEncoderRelease(pass);

            ReleaseTrackedBindGroup(bindGroup);
        }

        private PipelineRecord Pipeline(PrimitiveTopology topology)
        {
            var key = new PipelineKey(topology, _surfaceFormat, _enabled.Contains(EnableCap.Blend),
                _blendSource, _blendDestination, CurrentWriteMask(), _blendEquation);
            if (_pipelines.TryGetValue(key, out PipelineRecord? existing)) return existing;
            long pipelineStart = PerformanceStart();

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
                        Operation = ToBlendOperation(key.Equation)
                    },
                    Alpha = new BlendComponent
                    {
                        SrcFactor = ToBlend(key.Source),
                        DstFactor = ToBlend(key.Destination),
                        Operation = ToBlendOperation(key.Equation)
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
            nint fragmentEntry = SilkMarshal.StringToPtr(
                _surfaceFormat is WgpuTextureFormat.Bgra8UnormSrgb or WgpuTextureFormat.Rgba8UnormSrgb
                ? "fs_ui_srgb" : "fs_main");
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
                RecordPipelineCreation(pipelineStart);
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
            if (_enabled.Contains(EnableCap.ScissorTest) || CurrentWriteMask() != ColorWriteMask.All
                || !_depthWrite || _stencilWriteMask != -1)
                ClearPartial(mask);
            else ClearOffscreenCore(mask);
        }

        private void ReadPixelsCore<T>(int x, int y, int width, int height,
            PixelFormat format, PixelType type, T[] pixels) where T : struct
        {
            if (type != PixelType.UnsignedByte || (format != PixelFormat.Rgb && format != PixelFormat.Rgba))
                throw new NotSupportedException("Modern launcher readback currently supports RGB/RGBA unsigned-byte.");
            if (_resources.ReadFramebuffer != 0)
            {
                ReadOffscreenPixelsCore(x, y, width, height, format, type, pixels);
                return;
            }
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
            CommandEncoder* encoder = BeginCommands();
            var source = new ImageCopyTexture
            {
                Texture = _surfaceTexture.Texture,
                MipLevel = 0,
                Origin = new Origin3D((uint)Math.Max(0, x),
                    (uint)Math.Max(0, (int)_height - y - height), 0),
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
            EndCommands();

            bool mappedSuccessfully = false;
            try
            {
                FlushCommands();
                ResetFrameBuffers();
                _mapStatus = BufferMapAsyncStatus.Unknown;
                _api.BufferMapAsync(readback, MapMode.Read, 0, (nuint)total,
                    new PfnBufferMapCallback((status, _) => _mapStatus = status), null);
                _device.Native.DevicePoll(_device.Device, true, null);
                if (_mapStatus != BufferMapAsyncStatus.Success)
                    throw new InvalidOperationException($"Modern readback map failed: {_mapStatus}.");

                mappedSuccessfully = true;
                byte* mapped = (byte*)_api.BufferGetConstMappedRange(readback, 0, (nuint)total);
                int components = format == PixelFormat.Rgb ? 3 : 4;
                byte[] output = new byte[checked(width * height * components)];
                bool bgra = _surfaceFormat == WgpuTextureFormat.Bgra8Unorm
                    || _surfaceFormat == WgpuTextureFormat.Bgra8UnormSrgb;
                for (int row = 0; row < height; row++)
                {
                    byte* src = mapped + (height - 1 - row) * paddedRow;
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

                if (typeof(T) != typeof(byte))
                    throw new NotSupportedException("Modern launcher readback currently targets byte arrays.");
                Array.Copy(output, (byte[])(object)pixels, Math.Min(output.Length, pixels.Length));
            }
            finally
            {
                if (mappedSuccessfully) _api.BufferUnmap(readback);

                _api.BufferRelease(readback);
            }
        }

        private void ReleaseNativeTexture(NativeTexture texture)
        {
            if (texture.Sampler != null) _api.SamplerRelease(texture.Sampler);
            if (texture.SampleView != null && texture.SampleView != texture.View)
                _api.TextureViewRelease(texture.SampleView);
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
