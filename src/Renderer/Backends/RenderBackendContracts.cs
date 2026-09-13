using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace MphRead
{
    /// <summary>
    /// The backend-facing rendering boundary.  No windowing or graphics API
    /// type crosses this interface; a platform host owns the event loop and a
    /// backend owns the device and its resource lifetime.
    /// </summary>
    public interface IRenderBackend : IDisposable
    {
        RenderBackendInfo Info { get; }
        RenderSurfaceInfo Surface { get; }

        /// <summary>
        /// Acquire a frame for rendering. A false result is normal while the
        /// surface is minimized, occluded, or has no presentable image.
        /// </summary>
        bool TryBeginFrame(out RenderBackendFrame frame);

        /// <summary>Encode the current frame into the owned final target.</summary>
        void Render(RenderBackendFrame frame, RenderFrame snapshot);

        /// <summary>
        /// Submit the encoded frame. A true result means a real GPU submission
        /// succeeded. <see cref="RenderBackendFrame.HasSwapchain"/> determines
        /// whether the host should also acknowledge a visible presentation.
        /// </summary>
        bool TrySubmitFrame(RenderBackendFrame frame);

        /// <summary>Update logical and device-pixel sizes without dropping static resources.</summary>
        void Resize(Vector2i logicalSize, Vector2i framebufferSize);

        /// <summary>Invalidate device-scoped resource caches after device loss/recreation.</summary>
        void InvalidateCaches();
    }

    /// <summary>
    /// Optional backend-neutral capture completion channel. A backend may
    /// complete readbacks after the frame that scheduled them; hosts drain the
    /// channel on the graphics thread and hand owned results to an encoder.
    /// Keeping this separate from <see cref="IRenderBackend"/> preserves the
    /// small fake backends used by fixed-step and renderer tests.
    /// </summary>
    public interface IRenderCaptureSource
    {
        bool TryDequeueCapture(out RenderCaptureResult? result);
        bool TryDequeueCaptureFailure(out RenderCaptureFailure? failure);
    }

    public readonly record struct RenderBackendInfo(
        string Name,
        string Driver,
        string ShaderFormats,
        string SwapchainFormat,
        string PresentMode,
        bool SupportsFinalComposite,
        bool SupportsStaticMeshCache,
        bool SupportsTextureCache);

    public readonly record struct RenderSurfaceInfo(
        Vector2i LogicalSize,
        Vector2i FramebufferSize,
        bool IsMinimized,
        bool HasSwapchain,
        string SwapchainFormat,
        string PresentMode)
    {
        public bool HasDrawablePixels => !IsMinimized
            && HasSwapchain
            && FramebufferSize.X > 0
            && FramebufferSize.Y > 0;
    }

    /// <summary>
    /// Backend-neutral frame token. The native backend may attach its command
    /// buffer privately; callers only observe the gating state.
    /// </summary>
    public sealed class RenderBackendFrame
    {
        internal RenderBackendFrame(bool hasSwapchain, bool minimized, Vector2i framebufferSize)
        {
            HasSwapchain = hasSwapchain;
            IsMinimized = minimized;
            FramebufferSize = framebufferSize;
        }

        public bool HasSwapchain { get; }
        public bool IsMinimized { get; }
        public Vector2i FramebufferSize { get; }
        public bool Encoded { get; internal set; }
        public bool Submitted { get; internal set; }
    }

    /// <summary>
    /// Per-device caches shared by an SDL backend's upload translators. Keys
    /// retain every input that can change display-list compilation; a
    /// GeometryIdentity alone is not sufficient when a model's texture
    /// dimensions or texgen state changes the emitted UVs.
    /// </summary>
    public sealed class DeviceRenderCaches : IDisposable
    {
        private readonly Dictionary<MeshCompileKey, CpuMesh> _meshes = new();
        private readonly Dictionary<TextureIdentity, RenderTexturePixels> _textures = new();
        private readonly Dictionary<PipelineKey, object> _pipelines = new();
        private readonly Dictionary<string, IDisposable> _deviceResources = new(StringComparer.Ordinal);
        private bool _disposed;

        public int MeshCount => _meshes.Count;
        public int TextureCount => _textures.Count;
        public int PipelineCount => _pipelines.Count;
        public int DeviceResourceCount => _deviceResources.Count;

        public CpuMesh GetOrAddStaticMesh(MeshCompileKey key, Func<CpuMesh> factory)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            if (_meshes.TryGetValue(key, out CpuMesh? cached)) return cached;
            CpuMesh mesh = factory() ?? throw new InvalidOperationException("Mesh compiler returned null.");
            _meshes.Add(key, mesh);
            return mesh;
        }

        public RenderTexturePixels GetOrAddTexture(TextureIdentity key, Func<RenderTexturePixels> factory)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            if (_textures.TryGetValue(key, out RenderTexturePixels? cached)) return cached;
            RenderTexturePixels pixels = factory() ?? throw new InvalidOperationException("Texture decoder returned null.");
            _textures.Add(key, pixels);
            return pixels;
        }

        /// <summary>
        /// Shared client-side pixels are immutable for a revision. Dynamic HUD
        /// identities may retain their source identity while changing their
        /// decoded bytes; replace only that record and leave device handles to
        /// the backend's upload layer.
        /// </summary>
        public RenderTexturePixels GetOrUpdateTexture(TextureIdentity key, Func<RenderTexturePixels> factory)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            RenderTexturePixels pixels = factory() ?? throw new InvalidOperationException("Texture decoder returned null.");
            if (_textures.TryGetValue(key, out RenderTexturePixels? cached)
                && cached.Revision == pixels.Revision
                && cached.Width == pixels.Width
                && cached.Height == pixels.Height)
            {
                return cached;
            }
            _textures[key] = pixels;
            return pixels;
        }

        public void SetPipeline(PipelineKey key, object pipeline)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _pipelines[key] = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        }

        public bool TryGetPipeline(PipelineKey key, out object? pipeline)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _pipelines.TryGetValue(key, out pipeline);
        }

        /// <summary>
        /// Cache an actual resource owned by one graphics device. The
        /// implementation is backend-neutral at this boundary; SDL stores
        /// its native buffers, textures, and pipelines in an IDisposable
        /// wrapper and therefore cannot accidentally cross devices.
        /// </summary>
        public T GetOrAddDeviceResource<T>(string key, Func<T> factory) where T : class, IDisposable
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("A resource key is required.", nameof(key));
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            if (_deviceResources.TryGetValue(key, out IDisposable? existing))
            {
                if (existing is T typed) return typed;
                throw new InvalidOperationException($"Device resource key '{key}' is already used by {existing.GetType().Name}.");
            }
            T resource = factory() ?? throw new InvalidOperationException("Device resource factory returned null.");
            _deviceResources.Add(key, resource);
            return resource;
        }

        public bool RemoveDeviceResource(string key)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_deviceResources.Remove(key, out IDisposable? resource)) return false;
            resource.Dispose();
            return true;
        }

        public void Invalidate()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _meshes.Clear();
            _textures.Clear();
            _pipelines.Clear();
            DisposeDeviceResources();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _meshes.Clear();
            _textures.Clear();
            _pipelines.Clear();
            DisposeDeviceResources();
        }

        private void DisposeDeviceResources()
        {
            foreach (IDisposable resource in _deviceResources.Values)
            {
                resource.Dispose();
            }
            _deviceResources.Clear();
        }
    }

    /// <summary>
    /// Complete key for a compiled DS mesh. The instruction list is a source
    /// identity, not a content hash: parsed model lists are immutable for the
    /// life of a model and retaining this reference avoids hashing every draw.
    /// </summary>
    public readonly struct MeshCompileKey : IEquatable<MeshCompileKey>
    {
        public MeshCompileKey(object geometryIdentity, object instructionIdentity, int textureWidth,
            int textureHeight, bool texgen, bool isRoom)
        {
            GeometryIdentity = geometryIdentity ?? throw new ArgumentNullException(nameof(geometryIdentity));
            InstructionIdentity = instructionIdentity ?? throw new ArgumentNullException(nameof(instructionIdentity));
            TextureWidth = textureWidth;
            TextureHeight = textureHeight;
            Texgen = texgen;
            IsRoom = isRoom;
        }

        public object GeometryIdentity { get; }
        public object InstructionIdentity { get; }
        public int TextureWidth { get; }
        public int TextureHeight { get; }
        public bool Texgen { get; }
        public bool IsRoom { get; }

        public bool Equals(MeshCompileKey other)
            => ReferenceEquals(GeometryIdentity, other.GeometryIdentity)
                && ReferenceEquals(InstructionIdentity, other.InstructionIdentity)
                && TextureWidth == other.TextureWidth
                && TextureHeight == other.TextureHeight
                && Texgen == other.Texgen
                && IsRoom == other.IsRoom;

        public override bool Equals(object? obj) => obj is MeshCompileKey other && Equals(other);
        public override int GetHashCode()
            => HashCode.Combine(
                System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(GeometryIdentity),
                System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(InstructionIdentity),
                TextureWidth, TextureHeight, Texgen, IsRoom);
        public static bool operator ==(MeshCompileKey left, MeshCompileKey right) => left.Equals(right);
        public static bool operator !=(MeshCompileKey left, MeshCompileKey right) => !left.Equals(right);
    }

    /// <summary>Backend-neutral decoded model pixels, before GPU upload.</summary>
    public sealed class RenderTexturePixels
    {
        /// <summary>
        /// Largest dimension accepted by the neutral texture contract.  This
        /// admits desktop replacement textures while preventing accidental
        /// allocation from malformed content.
        /// </summary>
        public const int MaximumDimension = 16384;

        /// <summary>Maximum tightly packed RGBA8 payload for one record.</summary>
        public const long MaximumRgba8Bytes = 256L * 1024 * 1024;

        public static int ValidateRgba8ByteCount(int width, int height)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            if (width > MaximumDimension)
            {
                throw new ArgumentOutOfRangeException(nameof(width),
                    $"Texture dimensions may not exceed {MaximumDimension}.");
            }
            if (height > MaximumDimension)
            {
                throw new ArgumentOutOfRangeException(nameof(height),
                    $"Texture dimensions may not exceed {MaximumDimension}.");
            }

            long byteCount = checked((long)width * height * 4);
            if (byteCount > MaximumRgba8Bytes)
            {
                throw new ArgumentOutOfRangeException(nameof(width),
                    $"RGBA8 texture payload may not exceed {MaximumRgba8Bytes} bytes.");
            }
            return checked((int)byteCount);
        }

        public RenderTexturePixels(TextureIdentity identity, int width, int height, ReadOnlyMemory<byte> rgba8,
            long revision = 0, bool onlyOpaque = false, Vector3? alphaWeightedFlatColor = null)
        {
            int expectedBytes = ValidateRgba8ByteCount(width, height);
            if (rgba8.Length != expectedBytes)
            {
                throw new ArgumentException("RGBA8 data must contain exactly width*height*4 bytes.", nameof(rgba8));
            }
            Identity = identity;
            Width = width;
            Height = height;
            Rgba8 = rgba8;
            Revision = revision;
            OnlyOpaque = onlyOpaque;
            AlphaWeightedFlatColor = alphaWeightedFlatColor ?? Vector3.One;
        }

        public TextureIdentity Identity { get; }
        public int Width { get; }
        public int Height { get; }
        public ReadOnlyMemory<byte> Rgba8 { get; }
        public long Revision { get; }
        public bool OnlyOpaque { get; }
        public Vector3 AlphaWeightedFlatColor { get; }
    }
}
