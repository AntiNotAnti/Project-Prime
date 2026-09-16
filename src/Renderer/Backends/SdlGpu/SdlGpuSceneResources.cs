using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using MphRead.Mods;
using OpenTK.Mathematics;
using SDL;

namespace MphRead
{
    internal static class SdlGpuMappedMemoryCopy
    {
        public static void Copy(ReadOnlyMemory<byte> source, IntPtr destination)
        {
            if (source.IsEmpty)
            {
                return;
            }
            if (destination == IntPtr.Zero)
            {
                throw new ArgumentNullException(nameof(destination));
            }
            if (MemoryMarshal.TryGetArray(source, out ArraySegment<byte> segment))
            {
                Marshal.Copy(segment.Array!, segment.Offset, destination, segment.Count);
                return;
            }
            byte[] copy = source.ToArray();
            Marshal.Copy(copy, 0, destination, copy.Length);
        }
    }

    internal static class SdlGpuMatrixAbi
    {
        // OpenTK's Matrix4 storage is row-vector oriented. The canonical HLSL
        // uses row_major matrices with mul(matrix, columnVector), while the
        // legacy GL path uploaded the same OpenTK values with transpose=false.
        // Transpose once here so all shader backends observe the legacy math.
        public static Matrix4 Upload(Matrix4 value) => Matrix4.Transpose(value);

        public static void CopyStack(IReadOnlyList<float> source, int matrixCount, Span<float> destination)
        {
            int count = Math.Min(matrixCount, destination.Length / 16);
            for (int matrix = 0; matrix < count; matrix++)
            {
                int offset = matrix * 16;
                for (int row = 0; row < 4; row++)
                for (int column = 0; column < 4; column++)
                    destination[offset + row * 4 + column] = source[offset + column * 4 + row];
            }
        }
    }

    internal static class SdlGpuAlphaPredicate
    {
        public static bool Opaque(float alpha) => alpha == 1f;
        public static bool Transparent(float alpha) => alpha < 1f;
    }

    internal static class SdlGpuCelSurface
    {
        public static VisualStyle Style(RenderFrameOptions options)
            => options.VisualStyle
                ?? (options.CelShading ? VisualStyle.Cel : VisualStyle.Original);

        // Positive values retain the existing cel-band ABI. Negative values
        // identify the other mutually exclusive shader styles without adding
        // another frame constant.
        public static int ShaderStyleCode(RenderFrameOptions options)
            => Style(options) switch
            {
                VisualStyle.Cel => Math.Clamp(options.CelBands, 2, 8),
                VisualStyle.Flat => -1,
                VisualStyle.Pixelated => -2,
                VisualStyle.Retro => -3,
                _ => 0
            };

        // Compatibility name retained for focused renderer tests and callers
        // that only need to know whether cel banding is active.
        public static int BandCount(RenderFrameOptions options)
            => ShaderStyleCode(options) > 0 ? ShaderStyleCode(options) : 0;

        public static bool UsesEnhancedTextures(RenderFrameOptions options)
            => options.EnhancedTextures
                ?? options.Quality.GraphicsPreset == GraphicsPreset.Enhanced;

        public static bool TryGetFlatColor(RenderFrameOptions options,
            IReadOnlyDictionary<TextureIdentity, RenderTexturePixels> textures,
            RenderMaterial material, out Vector3 color)
        {
            TextureIdentity? requested = UsesEnhancedTextures(options)
                    ? material.Enhanced?.Albedo ?? material.Texture
                    : material.Texture;
            VisualStyle style = Style(options);
            if (style is VisualStyle.Cel or VisualStyle.Flat
                && options.ShowTextures && material.Textured
                && requested is TextureIdentity identity
                && textures.TryGetValue(identity, out RenderTexturePixels? pixels))
            {
                color = pixels.AlphaWeightedFlatColor;
                return true;
            }
            color = Vector3.One;
            return false;
        }
    }

    internal static class SdlGpuVisualStyleSampler
    {
        public static SamplerKey From(RenderFrameOptions options,
            RenderMaterial material)
            => SdlGpuCelSurface.Style(options) == VisualStyle.Pixelated
                ? SamplerKey.From(material, filtering: false)
                : SamplerKey.From(material, options.Quality);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct SdlGpuSceneVertexFrameConstants
    {
        internal static int AbiByteSize => sizeof(SdlGpuSceneVertexFrameConstants);

        public Matrix4 View;
        public Matrix4 Projection;
        public Vector4 Options;

        public static SdlGpuSceneVertexFrameConstants Create(Matrix4 view,
            Matrix4 projection, Vector4 options)
            => new()
            {
                View = SdlGpuMatrixAbi.Upload(view),
                Projection = SdlGpuMatrixAbi.Upload(projection),
                Options = options
            };
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct SdlGpuSceneFragmentFrameConstants
    {
        internal const int VisualLightFloatCount = RenderFrame.MaximumVisualLights * 4;
        internal const int VisualLightTileColumns = 16;
        internal const int VisualLightTileRows = 9;
        internal const int VisualLightTileCount
            = VisualLightTileColumns * VisualLightTileRows;
        internal static int AbiByteSize => sizeof(SdlGpuSceneFragmentFrameConstants);

        public Vector4 FogColor;
        public Vector4 Options;
        public Vector4 FogRange;
        public Vector4 CameraWorldPosition;
        public fixed float VisualLightPositionRadius[VisualLightFloatCount];
        public fixed float VisualLightColorIntensity[VisualLightFloatCount];
        public Vector4 VisualLightOptions;
        public fixed uint VisualLightTileMasks[VisualLightTileCount];
        public Matrix4 ShadowViewProjection;
        public Vector4 ShadowOptions;
        public Vector4 EnhancedFogColorDensity;
        public Vector4 EnhancedFogHeightFalloff;

        internal int VisualLightCount => (int)VisualLightOptions.X;

        internal Vector4 GetPositionRadius(int index)
        {
            if ((uint)index >= RenderFrame.MaximumVisualLights)
                throw new ArgumentOutOfRangeException(nameof(index));
            fixed (float* values = VisualLightPositionRadius)
            {
                int offset = index * 4;
                return new Vector4(values[offset], values[offset + 1],
                    values[offset + 2], values[offset + 3]);
            }
        }

        internal Vector4 GetColorIntensity(int index)
        {
            if ((uint)index >= RenderFrame.MaximumVisualLights)
                throw new ArgumentOutOfRangeException(nameof(index));
            fixed (float* values = VisualLightColorIntensity)
            {
                int offset = index * 4;
                return new Vector4(values[offset], values[offset + 1],
                    values[offset + 2], values[offset + 3]);
            }
        }

        internal uint GetTileMask(int column, int row)
        {
            if ((uint)column >= VisualLightTileColumns)
                throw new ArgumentOutOfRangeException(nameof(column));
            if ((uint)row >= VisualLightTileRows)
                throw new ArgumentOutOfRangeException(nameof(row));
            fixed (uint* masks = VisualLightTileMasks)
                return masks[row * VisualLightTileColumns + column];
        }

        public static SdlGpuSceneFragmentFrameConstants Create(Vector4 fogColor,
            Vector4 options, Vector4 fogRange, Vector3 cameraWorldPosition,
            IReadOnlyList<RenderVisualLight>? visualLights, bool enabled,
            bool displayAssetsToLinear = false, Vector2 viewport = default,
            RenderDirectionalShadowState shadow = default,
            RenderEnhancedFogState enhancedFog = default,
            Matrix4 view = default, Matrix4 projection = default)
        {
            SdlGpuSceneFragmentFrameConstants result = default;
            result.FogColor = fogColor;
            result.Options = options;
            result.FogRange = fogRange;
            result.CameraWorldPosition = new Vector4(cameraWorldPosition, 1);
            int count = enabled && visualLights != null
                ? Math.Min(visualLights.Count, RenderFrame.MaximumVisualLights) : 0;
            float* positions = result.VisualLightPositionRadius;
            float* colors = result.VisualLightColorIntensity;
            for (int i = 0; i < count; i++)
            {
                RenderVisualLight light = visualLights![i];
                int offset = i * 4;
                positions[offset] = light.Position.X;
                positions[offset + 1] = light.Position.Y;
                positions[offset + 2] = light.Position.Z;
                positions[offset + 3] = light.Radius;
                colors[offset] = light.Color.X;
                colors[offset + 1] = light.Color.Y;
                colors[offset + 2] = light.Color.Z;
                colors[offset + 3] = light.Intensity;
            }
            result.VisualLightOptions = new Vector4(count,
                displayAssetsToLinear ? 1 : 0,
                Math.Max(1, viewport.X), Math.Max(1, viewport.Y));
            PopulateVisualLightTiles(ref result, visualLights, count, view,
                projection);
            if (shadow.Enabled)
            {
                result.ShadowViewProjection = SdlGpuMatrixAbi.Upload(
                    shadow.ViewProjection);
                result.ShadowOptions = new Vector4(1, shadow.SourceIndex,
                    1f / shadow.MapSize, 0.0015f);
            }
            else result.ShadowViewProjection = SdlGpuMatrixAbi.Upload(Matrix4.Identity);
            result.EnhancedFogColorDensity = new Vector4(enhancedFog.Color,
                enhancedFog.Enabled ? enhancedFog.Density : 0);
            result.EnhancedFogHeightFalloff = new Vector4(enhancedFog.Height,
                enhancedFog.Falloff, enhancedFog.Enabled ? 1 : 0, 0);
            return result;
        }

        private static void PopulateVisualLightTiles(
            ref SdlGpuSceneFragmentFrameConstants result,
            IReadOnlyList<RenderVisualLight>? visualLights, int count,
            Matrix4 view, Matrix4 projection)
        {
            if (count == 0 || visualLights == null) return;
            fixed (uint* masks = result.VisualLightTileMasks)
            {
                for (int lightIndex = 0; lightIndex < count; lightIndex++)
                {
                    RenderVisualLight light = visualLights[lightIndex];
                    Vector4 viewCenter = Vector4.TransformRow(
                        new Vector4(light.Position, 1), view);
                    Vector4 clipCenter = Vector4.TransformRow(viewCenter,
                        projection);
                    float viewDepth = -viewCenter.Z;
                    if (!IsFinite(viewCenter) || !IsFinite(clipCenter)
                        || viewDepth <= light.Radius || clipCenter.W <= 0.0001f)
                    {
                        FillAllTiles(masks, lightIndex);
                        continue;
                    }

                    float inverseW = 1f / clipCenter.W;
                    float centerX = clipCenter.X * inverseW;
                    float centerY = clipCenter.Y * inverseW;
                    float radiusX = MathF.Abs(projection.M11) * light.Radius
                        / viewDepth;
                    float radiusY = MathF.Abs(projection.M22) * light.Radius
                        / viewDepth;
                    float minimumX = (centerX - radiusX) * 0.5f + 0.5f;
                    float maximumX = (centerX + radiusX) * 0.5f + 0.5f;
                    float minimumY = (1f - centerY - radiusY) * 0.5f;
                    float maximumY = (1f - centerY + radiusY) * 0.5f;
                    if (maximumX < 0 || minimumX > 1
                        || maximumY < 0 || minimumY > 1) continue;
                    int firstColumn = Math.Clamp((int)MathF.Floor(minimumX
                        * VisualLightTileColumns), 0,
                        VisualLightTileColumns - 1);
                    int lastColumn = Math.Clamp((int)MathF.Floor(maximumX
                        * VisualLightTileColumns), 0,
                        VisualLightTileColumns - 1);
                    int firstRow = Math.Clamp((int)MathF.Floor(minimumY
                        * VisualLightTileRows), 0, VisualLightTileRows - 1);
                    int lastRow = Math.Clamp((int)MathF.Floor(maximumY
                        * VisualLightTileRows), 0, VisualLightTileRows - 1);
                    uint bit = 1u << lightIndex;
                    for (int row = firstRow; row <= lastRow; row++)
                    for (int column = firstColumn; column <= lastColumn; column++)
                        masks[row * VisualLightTileColumns + column] |= bit;
                }
            }
        }

        private static void FillAllTiles(uint* masks, int lightIndex)
        {
            uint bit = 1u << lightIndex;
            for (int tile = 0; tile < VisualLightTileCount; tile++)
                masks[tile] |= bit;
        }

        private static bool IsFinite(Vector4 value)
            => float.IsFinite(value.X) && float.IsFinite(value.Y)
                && float.IsFinite(value.Z) && float.IsFinite(value.W);
    }

    internal static class SdlGpuEnhancedLightingPolicy
    {
        internal const float DefaultSmoothness = 0.25f;

        public static bool UsesPerPixelLighting(GraphicsPreset preset)
            => preset == GraphicsPreset.Enhanced;

        public static float SmoothnessExponent(float smoothness)
        {
            float clamped = Math.Clamp(float.IsFinite(smoothness) ? smoothness : 0, 0, 1);
            return 4 + 124 * clamped * clamped;
        }

        public static float NormalizedBlinnPhong(float normalDotHalf, float smoothness)
        {
            float exponent = SmoothnessExponent(smoothness);
            float cosine = Math.Clamp(float.IsFinite(normalDotHalf) ? normalDotHalf : 0, 0, 1);
            return MathF.Pow(cosine, exponent) * (exponent + 8) / (8 * MathF.PI);
        }
    }

    internal static class SdlGpuNormalMappingPolicy
    {
        private const float Epsilon = 0.00000001f;

        public static bool IsEnabled(GraphicsPreset preset, bool frameLighting,
            bool showTextures, RenderMaterial material, RenderTopology topology)
            => preset == GraphicsPreset.Enhanced
                && frameLighting
                && showTextures
                && topology == RenderTopology.Triangles
                && material.Lighting
                && material.Textured
                && material.TexgenMode is TexgenMode.None or TexgenMode.Texcoord
                && material.Enhanced?.Normal is TextureIdentity;

        internal static (Vector3 Tangent, Vector3 Bitangent) TransformUvBasis(
            Vector3 tangent, Vector3 bitangent, float a, float b, float c,
            float d)
        {
            float determinant = a * d - b * c;
            if (!float.IsFinite(determinant) || MathF.Abs(determinant) <= Epsilon)
                return (tangent, bitangent);
            float reciprocalDeterminant = 1f / determinant;
            return (SafeNormalize((d * tangent - c * bitangent)
                    * reciprocalDeterminant, tangent),
                SafeNormalize((-b * tangent + a * bitangent)
                    * reciprocalDeterminant, bitangent));
        }

        private static Vector3 SafeNormalize(Vector3 value, Vector3 fallback)
        {
            float lengthSquared = value.LengthSquared;
            return float.IsFinite(lengthSquared) && lengthSquared > Epsilon
                ? value / MathF.Sqrt(lengthSquared) : fallback;
        }
    }

    internal static class SdlGpuSceneVertexAbi
    {
        public const int ByteSize = 72;
        public const int TangentOffset = 56;
        public const int AttributeCount = 7;
    }

    internal static class SdlGpuSceneSamplerAbi
    {
        public const int Albedo = 0;
        public const int Normal = 1;
        public const int Emissive = 2;
        public const int Reflection = 3;
        public const int AmbientOcclusion = 4;
        public const int Shadow = 5;
        public const int SurfaceData = 6;
        public const int Count = 7;
        public const int D3D12PaddedCount = SdlGpuSamplerBindingAbi.D3D12BatchSize;

        // SDL 3.4.16's D3D12 descriptor writer only checks whether a heap is
        // already full before copying a complete binding batch. Seven-entry
        // batches can therefore begin at descriptor 2,044 and overrun its
        // 2,048-entry sampler heap. Padding the D3D12 scene table to eight
        // keeps every batch aligned with that pinned native heap while the
        // shader continues to consume the first seven slots.
        public static int BindingCountForDriver(string driver)
            => SdlGpuSamplerBindingAbi.BindingCountForDriver(driver, Count);
    }

    internal readonly record struct SdlGpuReflectionResourceConfiguration(
        ReflectionProbeKey Key,
        int Dimension,
        int MipLevelCount,
        ulong ContentFingerprint)
    {
        public static SdlGpuReflectionResourceConfiguration From(
            RenderReflectionProbe probe)
        {
            ArgumentNullException.ThrowIfNull(probe);
            return new(probe.Key, probe.Dimension, probe.MipLevelCount,
                probe.ContentFingerprint);
        }
    }

    internal readonly record struct SdlGpuDirectionalShadowConfiguration(
        int MapSize, SDL_GPUTextureFormat DepthFormat);

    internal readonly record struct SdlGpuShadowPipelineKey(
        RenderCullMode CullMode, bool FaceCulling);

    /// <summary>Suppresses repeated work for one failed immutable cube generation.</summary>
    internal sealed class SdlGpuReflectionFailurePolicy
    {
        private SdlGpuReflectionResourceConfiguration? _failed;

        public SdlGpuReflectionResourceConfiguration? Failed => _failed;

        public bool ShouldAttempt(SdlGpuReflectionResourceConfiguration configuration)
            => !_failed.HasValue || _failed.Value != configuration;

        public bool RecordFailure(SdlGpuReflectionResourceConfiguration configuration)
        {
            bool changed = !_failed.HasValue || _failed.Value != configuration;
            _failed = configuration;
            return changed;
        }

        public void RecordSuccess(SdlGpuReflectionResourceConfiguration configuration)
        {
            _ = configuration;
            _failed = null;
        }
    }

    /// <summary>
    /// Device-owned translation of a sealed <see cref="RenderFrame"/>. Static
    /// model meshes, decoded textures, samplers, and immutable pipelines are
    /// cached for the device lifetime. Dynamic primitives use one bounded
    /// resource bucket per frame-in-flight slot and are recycled only after
    /// that slot's fence has completed.
    /// </summary>
    internal unsafe sealed class SdlGpuSceneResources : IDisposable
    {
        private const int MaximumDraws = RenderFrame.DefaultMaximumCapacity * 6;
        private readonly SDL_GPUTextureFormat _depthFormat;
        private readonly bool _depthSampleable;

        private readonly SdlGpuDevice _device;
        private readonly int _sceneSamplerBindingCount;
        private readonly Dictionary<object, GpuMesh> _staticMeshes = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<object, long> _staticMeshLastUsed = new(ReferenceEqualityComparer.Instance);
        private readonly List<object> _expiredStaticMeshes = new();
        private readonly Dictionary<TextureIdentity, GpuTexture> _textures = new();
        private readonly Dictionary<TextureIdentity, long> _textureLastUsed = new();
        private readonly List<TextureIdentity> _expiredTextures = new();
        private readonly Dictionary<SamplerKey, nint> _samplers = new();
        private readonly Dictionary<SamplerKey, SamplerKey> _effectiveSamplerKeys = new();
        private readonly HashSet<(AnisotropyLevel Requested, AnisotropyLevel Effective)> _reportedSamplerNegotiations = new();
        private readonly Dictionary<ScenePipelineKey, nint> _pipelines = new();
        private readonly List<GpuMesh>[] _dynamicSlots;
        private readonly int[] _dynamicSlotUsed;
        private readonly List<GpuTexture>[] _retiredTextureSlots;
        private readonly List<GpuCubeTexture>[] _retiredCubeTextureSlots;
        private readonly List<GpuMesh>[] _retiredMeshSlots;
        private readonly List<IUploadResource>[] _uploadSlots;
        private SDL_GPUShader* _vertexShader;
        private SDL_GPUShader* _fragmentShader;
        private SDL_GPUShader* _depthStencilVertexShader;
        private SDL_GPUShader* _depthStencilFragmentShader;
        private SDL_GPUShader* _surfaceFragmentShader;
        private SDL_GPUShader* _shadowVertexShader;
        private SDL_GPUShader* _shadowFragmentShader;
        private SDL_GPUShader* _distortionVertexShader;
        private SDL_GPUShader* _distortionFragmentShader;
        private SDL_GPUTexture* _sceneColor;
        private SDL_GPUTexture* _sceneMultisampleColor;
        private SDL_GPUTexture* _sceneDepth;
        private SDL_GPUTexture* _bloomColor;
        private SDL_GPUTexture* _bloomMultisampleColor;
        private SDL_GPUTexture* _surfaceColor;
        private SDL_GPUTexture* _surfaceDepth;
        private SDL_GPUTexture* _shadowDepth;
        private SDL_GPUTexture* _distortionColor;
        private SDL_GPUTexture* _distortionMultisampleColor;
        private GpuTexture? _whiteTexture;
        private GpuTexture? _flatNormalTexture;
        private GpuTexture? _blackEmissiveTexture;
        private GpuCubeTexture? _blackReflectionTexture;
        private GpuCubeTexture? _reflectionTexture;
        private readonly SdlGpuReflectionFailurePolicy _reflectionFailure = new();
        private SdlGpuPostResources? _postResources;
        private readonly SdlGpuSsaoResources _ssaoResources;
        private readonly SdlGpuSkyResources _skyResources;
        private readonly SdlGpuConfigurationFailureCache<
            SdlGpuEnhancedSurfaceConfiguration> _surfaceFailure = new();
        private SdlGpuEnhancedSurfacePlan? _surfacePlan;
        private readonly RenderExecutionPlan _executionPlan = new();
        private SdlGpuEnhancedSurfaceConfiguration? _reportedSurfaceFailure;
        private SDL_GPUTexture* _ambientOcclusionForFrame;
        private SDL_GPUTexture* _distortionForFrame;
        private readonly EnhancedDistortionFailureCache _distortionFailure = new();
        private EnhancedDistortionTargetConfiguration? _distortionConfiguration;
        private EnhancedDistortionTargetConfiguration? _reportedDistortionFailure;
        private bool _surfaceAvailableForFrame;
        private bool _shadowAvailableForFrame;
        private int _shadowMapSize;
        private readonly Dictionary<SdlGpuShadowPipelineKey, nint> _shadowPipelines = new();
        private readonly Dictionary<(CullingMode Culling, bool FaceCulling,
            SDL_GPUTextureFormat Format, int Samples), nint> _distortionPipelines = new();
        private readonly SdlGpuConfigurationFailureCache<
            SdlGpuDirectionalShadowConfiguration> _shadowFailure = new();
        private SdlGpuDirectionalShadowConfiguration? _reportedShadowFailure;
        private uint _targetWidth;
        private uint _targetHeight;
        private int _targetSampleCount = 1;
        private uint _bloomTargetWidth;
        private uint _bloomTargetHeight;
        private int _bloomTargetSampleCount = 1;
        private SDL_GPUTextureFormat _bloomTargetFormat;
        private readonly bool _ldrColorSupports2;
        private readonly bool _ldrColorSupports4;
        private readonly bool _hdrColorTargetAndSamplerSupported;
        private readonly bool _hdrColorSupports2;
        private readonly bool _hdrColorSupports4;
        private readonly bool _depthSupports2;
        private readonly bool _depthSupports4;
        private SDL_GPUTextureFormat _sceneColorFormat;
        private bool _sceneUsesHdr;
        private SdlGpuHdrConfiguration? _failedHdrConfiguration;
        private bool _loggedHdrAllocationFallback;
        private readonly HashSet<SdlGpuSampleNegotiation> _reportedSampleNegotiations = new();
        private bool _disposed;
        private long _frameSerial;

        private SdlGpuSceneResources(SdlGpuDevice device)
        {
            _device = device;
            _sceneSamplerBindingCount = SdlGpuSceneSamplerAbi.BindingCountForDriver(
                device.Driver);
            _dynamicSlots = new List<GpuMesh>[device.FrameResources.SlotCount];
            _dynamicSlotUsed = new int[device.FrameResources.SlotCount];
            _retiredTextureSlots = new List<GpuTexture>[device.FrameResources.SlotCount];
            _retiredCubeTextureSlots = new List<GpuCubeTexture>[device.FrameResources.SlotCount];
            _retiredMeshSlots = new List<GpuMesh>[device.FrameResources.SlotCount];
            _uploadSlots = new List<IUploadResource>[device.FrameResources.SlotCount];
            _ssaoResources = new SdlGpuSsaoResources(device);
            _skyResources = new SdlGpuSkyResources(device);
            for (int i = 0; i < _dynamicSlots.Length; i++)
            {
                _dynamicSlots[i] = new List<GpuMesh>();
                _retiredTextureSlots[i] = new List<GpuTexture>();
                _retiredCubeTextureSlots[i] = new List<GpuCubeTexture>();
                _retiredMeshSlots[i] = new List<GpuMesh>();
                _uploadSlots[i] = new List<IUploadResource>();
            }

            ShaderArtifactManifest.ValidateSceneFresh();
            SDL_GPUTextureUsageFlags sampledDepthUsage = SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_DEPTH_STENCIL_TARGET
                | SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER;
            SDL_GPUTextureFormat d24 = SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_D24_UNORM_S8_UINT;
            SDL_GPUTextureFormat d32 = SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_D32_FLOAT_S8_UINT;
            if (SDL3.SDL_GPUTextureSupportsFormat(device.Handle, d24,
                SDL_GPUTextureType.SDL_GPU_TEXTURETYPE_2D, sampledDepthUsage))
            {
                _depthFormat = d24;
                _depthSampleable = true;
            }
            else if (SDL3.SDL_GPUTextureSupportsFormat(device.Handle, d32,
                SDL_GPUTextureType.SDL_GPU_TEXTURETYPE_2D, sampledDepthUsage))
            {
                _depthFormat = d32;
                _depthSampleable = true;
            }
            else if (SDL3.SDL_GPUTextureSupportsFormat(device.Handle, d24,
                SDL_GPUTextureType.SDL_GPU_TEXTURETYPE_2D,
                SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_DEPTH_STENCIL_TARGET))
            {
                _depthFormat = d24;
            }
            else if (SDL3.SDL_GPUTextureSupportsFormat(device.Handle, d32,
                SDL_GPUTextureType.SDL_GPU_TEXTURETYPE_2D,
                SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_DEPTH_STENCIL_TARGET))
            {
                _depthFormat = d32;
            }
            else
            {
                throw new PlatformNotSupportedException("SDL GPU device supports neither D24S8 nor D32S8 scene targets.");
            }
            _ldrColorSupports2 = SDL3.SDL_GPUTextureSupportsSampleCount(device.Handle,
                device.SwapchainFormat, SDL_GPUSampleCount.SDL_GPU_SAMPLECOUNT_2);
            _ldrColorSupports4 = SDL3.SDL_GPUTextureSupportsSampleCount(device.Handle,
                device.SwapchainFormat, SDL_GPUSampleCount.SDL_GPU_SAMPLECOUNT_4);
            SDL_GPUTextureUsageFlags hdrUsage
                = SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COLOR_TARGET
                    | SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER;
            _hdrColorTargetAndSamplerSupported = SDL3.SDL_GPUTextureSupportsFormat(
                device.Handle, SdlGpuHdrPolicy.HdrFormat,
                SDL_GPUTextureType.SDL_GPU_TEXTURETYPE_2D, hdrUsage);
            _hdrColorSupports2 = _hdrColorTargetAndSamplerSupported
                && SDL3.SDL_GPUTextureSupportsSampleCount(device.Handle,
                    SdlGpuHdrPolicy.HdrFormat, SDL_GPUSampleCount.SDL_GPU_SAMPLECOUNT_2);
            _hdrColorSupports4 = _hdrColorTargetAndSamplerSupported
                && SDL3.SDL_GPUTextureSupportsSampleCount(device.Handle,
                    SdlGpuHdrPolicy.HdrFormat, SDL_GPUSampleCount.SDL_GPU_SAMPLECOUNT_4);
            _depthSupports2 = SDL3.SDL_GPUTextureSupportsSampleCount(device.Handle,
                _depthFormat, SDL_GPUSampleCount.SDL_GPU_SAMPLECOUNT_2);
            _depthSupports4 = SDL3.SDL_GPUTextureSupportsSampleCount(device.Handle,
                _depthFormat, SDL_GPUSampleCount.SDL_GPU_SAMPLECOUNT_4);

            (SDL_GPUShaderFormat format, string vertex, string fragment) = SelectArtifacts(device);
            _vertexShader = CreateShader(device.Handle, format, vertex, "main_vs",
                SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_VERTEX, samplers: 0, uniforms: 3);
            try
            {
                _fragmentShader = CreateShader(device.Handle, format, fragment, "main_ps",
                    SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_FRAGMENT,
                    samplers: checked((uint)_sceneSamplerBindingCount), uniforms: 2);
                (SDL_GPUShaderFormat specializedFormat, string specializedSuffix)
                    = SelectShaderFormat();
                string shaderDirectory = ShaderArtifactManifest.Directory;
                _depthStencilVertexShader = CreateShader(device.Handle,
                    specializedFormat, Path.Combine(shaderDirectory,
                        $"depth_stencil.vert.{specializedSuffix}"), "main_vs",
                    SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_VERTEX,
                    samplers: 0, uniforms: 3);
                _depthStencilFragmentShader = CreateShader(device.Handle,
                    specializedFormat, Path.Combine(shaderDirectory,
                        $"depth_stencil.frag.{specializedSuffix}"), "main_ps",
                    SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_FRAGMENT,
                    samplers: checked((uint)SamplerBindingCount(1)), uniforms: 1);
                var whiteIdentity = new TextureIdentity(this, variant: "white-fallback");
                _whiteTexture = GpuTexture.Create(_device, new RenderTexturePixels(
                    whiteIdentity, 1, 1, new byte[] { 255, 255, 255, 255 }, onlyOpaque: true),
                    mipmapped: false);
                var flatNormalIdentity = new TextureIdentity(this, variant: "flat-normal-fallback");
                _flatNormalTexture = GpuTexture.Create(_device, new RenderTexturePixels(
                    flatNormalIdentity, 1, 1, new byte[] { 128, 128, 255, 255 }, onlyOpaque: true),
                    mipmapped: false);
                var blackEmissiveIdentity = new TextureIdentity(this,
                    variant: "black-emissive-fallback");
                _blackEmissiveTexture = GpuTexture.Create(_device,
                    new RenderTexturePixels(blackEmissiveIdentity, 1, 1,
                        new byte[] { 0, 0, 0, 255 }, onlyOpaque: true),
                    mipmapped: false);
                _blackReflectionTexture = GpuCubeTexture.CreateFallback(_device);
                _postResources = SdlGpuPostResources.Create(device);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public static SdlGpuSceneResources Create(SdlGpuDevice device)
            => new(device ?? throw new ArgumentNullException(nameof(device)));

        /// <summary>
        /// The capture-safe scene target is exposed only to the SDL backend's
        /// readback translator. Legacy modes expose their byte scene target;
        /// Enhanced exposes the explicit SDR branch after tone mapping and HUD
        /// scene composition. A float target is never returned for readback.
        /// </summary>
        public SDL_GPUTexture* CaptureSceneColor
            => _postResources != null && _postResources.CaptureSceneColor != null
                ? _postResources.CaptureSceneColor
                : _sceneUsesHdr ? null : _sceneColor;
        private SDL_GPUTexture* SceneRenderColor
            => _sceneMultisampleColor != null ? _sceneMultisampleColor : _sceneColor;
        public uint SceneTargetWidth => _targetWidth;
        public uint SceneTargetHeight => _targetHeight;
        internal SDL_GPUTexture* SurfaceDataTexture
            => _surfaceAvailableForFrame ? _surfaceColor : null;

        public void Encode(SDL_GPUCommandBuffer* commandBuffer, SDL_GPUTexture* finalComposite,
            RenderFrame frame, uint finalWidth, uint finalHeight)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (commandBuffer == null || finalComposite == null) throw new ArgumentNullException(nameof(commandBuffer));
            if (!frame.IsSealed) throw new InvalidOperationException("Scene encoding requires a sealed frame.");
            if (frame.Count > RenderFrame.DefaultMaximumCapacity)
                throw new InvalidOperationException("Scene draw count exceeds the bounded frame contract.");

            uint width = checked((uint)Math.Max(1, frame.SceneTargetSize.X));
            uint height = checked((uint)Math.Max(1, frame.SceneTargetSize.Y));
            bool enhancedOutput = frame.Options.Quality.GraphicsPreset
                == GraphicsPreset.Enhanced;
            bool surfacePrepared = enhancedOutput
                && TryPrepareEnhancedSurface(width, height);
            bool celDepthSampling = SdlGpuSurfaceCelPolicy.RequiresLegacyDepthSampling(
                frame.Options.Quality.GraphicsPreset, frame.CelState.Enabled,
                frame.CelState.Outline, surfacePrepared, _depthSampleable);
            SdlGpuHdrConfiguration hdrConfiguration = new(
                frame.Options.Quality.GraphicsPreset, width, height,
                finalWidth, finalHeight, frame.Options.Quality.MsaaSampleCount,
                celDepthSampling, frame.Options.Quality.Bloom);
            if (_failedHdrConfiguration.HasValue
                && _failedHdrConfiguration.Value != hdrConfiguration)
            {
                _failedHdrConfiguration = null;
            }
            bool attemptHdr = SdlGpuHdrFailurePolicy.ShouldAttemptHdr(
                _failedHdrConfiguration, hdrConfiguration);
            SdlGpuSceneColorPlan colorPlan = SdlGpuHdrPolicy.Resolve(
                frame.Options.Quality.GraphicsPreset, _device.SwapchainFormat,
                _hdrColorTargetAndSamplerSupported && attemptHdr);
            if (frame.Options.Quality.GraphicsPreset == GraphicsPreset.Enhanced
                && _hdrColorTargetAndSamplerSupported && !attemptHdr)
            {
                colorPlan = new SdlGpuSceneColorPlan(_device.SwapchainFormat,
                    UsesHdr: false, SdlGpuHdrFallbackReason.CachedAllocationFailure);
            }
            SdlGpuSampleNegotiation ldrSampleNegotiation = SdlGpuMsaaPolicy.Resolve(
                frame.Options.Quality.MsaaSampleCount, celDepthSampling,
                _ldrColorSupports2, _ldrColorSupports4, _depthSupports2, _depthSupports4);
            SdlGpuSampleNegotiation requestedSampleNegotiation = colorPlan.UsesHdr
                ? SdlGpuMsaaPolicy.Resolve(frame.Options.Quality.MsaaSampleCount,
                    celDepthSampling, _hdrColorSupports2, _hdrColorSupports4,
                    _depthSupports2, _depthSupports4)
                : ldrSampleNegotiation;
            SdlGpuSampleNegotiation sampleNegotiation = EnsureTargets(width, height,
                colorPlan, requestedSampleNegotiation, ldrSampleNegotiation,
                hdrConfiguration);
            ReportSampleNegotiation(sampleNegotiation);
            SdlGpuBloomPlan bloomPlan = SdlGpuBloomPlan.Create(frame, width, height,
                _targetSampleCount);
            try
            {
                PrepareAuxiliaryTargets(width, height, finalWidth, finalHeight,
                    enhancedOutput, bloomPlan);
            }
            catch (Exception ex) when (_sceneUsesHdr)
            {
                SdlGpuSceneColorPlan ldrPlan = new(_device.SwapchainFormat,
                    UsesHdr: false, SdlGpuHdrFallbackReason.UnsupportedRenderTarget);
                sampleNegotiation = EnsureTargets(width, height, ldrPlan,
                    ldrSampleNegotiation, ldrSampleNegotiation,
                    hdrConfiguration);
                ReportSampleNegotiation(sampleNegotiation);
                bloomPlan = SdlGpuBloomPlan.Create(frame, width, height,
                    _targetSampleCount);
                try
                {
                    PrepareAuxiliaryTargets(width, height, finalWidth,
                        finalHeight, enhancedOutput, bloomPlan);
                }
                catch (Exception ldrException)
                {
                    throw new InvalidOperationException(
                        $"SDL HDR auxiliary-target allocation failed ({ex.Message}); "
                        + $"LDR fallback also failed ({ldrException.Message}).",
                        ldrException);
                }
                _failedHdrConfiguration = hdrConfiguration;
                ReportHdrAllocationFallback(ex.Message, sampleNegotiation);
            }
            ResetDynamicSlot();
            _frameSerial++;

            // Uploads must be encoded before the first render pass. Resolve
            // every resource up front so no copy pass can be opened mid-pass.
            var meshes = new Dictionary<DrawSubmission, GpuMesh>(ReferenceEqualityComparer.Instance);
            var hudMeshes = new Dictionary<RenderHudSceneSubmission, GpuMesh>(ReferenceEqualityComparer.Instance);
            foreach (DrawSubmission draw in frame.Submissions)
            {
                if (!TryResolveMesh(frame, draw, commandBuffer, out GpuMesh? mesh))
                {
                    throw new InvalidOperationException(
                        $"Sealed scene frame is missing {draw.Primitive} geometry for submission polygon {draw.PolygonId}.");
                }
                meshes.Add(draw, mesh!);
                ResolveTexture(frame, draw, commandBuffer);
                ResolveNormalTexture(frame, draw, commandBuffer);
                ResolveEmissiveTexture(frame, draw, commandBuffer);
            }
            foreach (RenderHudSceneSubmission hud in frame.HudSceneItems)
            {
                CpuMesh mesh = hud.InlineMesh
                    ?? (hud.GeometryIdentity != null && frame.MeshResources.TryGetValue(hud.GeometryIdentity, out CpuMesh? captured)
                        ? captured : throw new InvalidOperationException($"Sealed scene frame is missing HUD geometry for polygon {hud.PolygonId}."));
                object identity = hud.InlineMesh != null ? hud : hud.GeometryIdentity!;
                hudMeshes.Add(hud, ResolveGpuMesh(identity, mesh, hud.InlineMesh == null, commandBuffer));
                ResolveTexture(frame, hud.Material.Textured, hud.TextureIdentity, hud.PolygonId, commandBuffer);
            }
            foreach (RenderOverlayCommand overlay in frame.OverlayCommands)
            {
                if (overlay.UseTexture) ResolveTexture(frame, true, overlay.Texture, -1, commandBuffer);
                if (overlay.UseMask) ResolveTexture(frame, true, overlay.MaskTexture, -1, commandBuffer);
            }
            if (frame.ColorGrade.Enabled)
            {
                ResolveTexture(frame, textured: true, frame.ColorGrade.LutTexture,
                    polygonId: -1, commandBuffer, mipmapped: false);
            }
            ResolveReflectionTexture(frame, commandBuffer);
            RetireUnusedStaticMeshes();
            RetireUnusedTextures();
            if (_whiteTexture!.EnsureUploaded(commandBuffer)) TrackUpload(_whiteTexture);
            if (_flatNormalTexture!.EnsureUploaded(commandBuffer)) TrackUpload(_flatNormalTexture);
            if (_blackEmissiveTexture!.EnsureUploaded(commandBuffer))
                TrackUpload(_blackEmissiveTexture);
            if (_blackReflectionTexture!.EnsureUploaded(commandBuffer))
                TrackUpload(_blackReflectionTexture);

            _distortionForFrame = TryPrepareEnhancedDistortion(frame, width,
                height) ? _distortionColor : null;

            bool ssaoPrepared = surfacePrepared && _surfacePlan.HasValue
                && _ssaoResources.TryPrepare(commandBuffer, _surfacePlan.Value);

            SdlGpuSceneVertexFrameConstants vertexFrame = SdlGpuSceneVertexFrameConstants.Create(
                frame.ViewMatrix, frame.ProjectionMatrix,
                new Vector4(frame.Options.Lighting ? 1 : 0,
                    frame.Options.ShowColors ? 1 : 0,
                    frame.Options.ShowTextures ? 1 : 0,
                    SdlGpuEnhancedLightingPolicy.UsesPerPixelLighting(
                        frame.Options.Quality.GraphicsPreset) ? 1 : 0));
            _shadowAvailableForFrame = TryPrepareDirectionalShadow(frame);
            float fogMin = frame.FogOffset / (float)0x7FFF;
            float fogMax = (frame.FogOffset + 32 * (0x400 >> frame.FogSlope)) / (float)0x7FFF;
            SdlGpuSceneFragmentFrameConstants fragmentFrame
                = SdlGpuSceneFragmentFrameConstants.Create(
                    frame.FogColor,
                    new Vector4(enhancedOutput
                            ? frame.EnhancedFog.Enabled ? 1 : 0
                            : frame.HasFog && frame.Options.Fog ? 1 : 0,
                        SdlGpuCelSurface.ShaderStyleCode(frame.Options),
                        SdlGpuEnhancedLightingPolicy.UsesPerPixelLighting(
                            frame.Options.Quality.GraphicsPreset) ? 1 : 0,
                        frame.Options.Lighting ? 1 : 0),
                    new Vector4(fogMin, fogMax, 0, 0), frame.CameraWorldPosition,
                    frame.VisualLights, frame.Options.Quality.DynamicVisualLights,
                    viewport: new Vector2(width, height),
                    shadow: _shadowAvailableForFrame
                        ? frame.DirectionalShadow : default,
                    enhancedFog: frame.EnhancedFog,
                    view: frame.ViewMatrix,
                    projection: frame.ProjectionMatrix);

            _surfaceAvailableForFrame = false;
            _ambientOcclusionForFrame = _whiteTexture!.Handle;
            int encoded = 0;
            bool skyEncoded = false;
            SDL_GPUTexture* displayLinear = null;
            SDL_GPUTexture* captureLinear = null;
            SdlGpuPostResources postResources = _postResources
                ?? throw new InvalidOperationException(
                    "SDL post resources were not prepared for the frame.");
            bool sceneReadback = false;
            bool finalReadback = false;
            for (int requestIndex = 0;
                requestIndex < frame.CaptureRequests.Count; requestIndex++)
            {
                if (frame.CaptureRequests[requestIndex].Target
                    == CaptureTargetKind.FinalPresentedFrame)
                    finalReadback = true;
                else
                    sceneReadback = true;
            }
            bool sceneCapture = enhancedOutput
                && postResources.RequiresSceneCapture(
                    frame.Options.Quality.GraphicsPreset, frame.CaptureRequests);
            bool reconstruction = SdlGpuReconstructionPlan.Create(
                frame.Options.Quality.GraphicsPreset, _targetWidth,
                _targetHeight, finalWidth, finalHeight).Enabled;
            RenderGraphLite.Build(_executionPlan, new RenderGraphFeatures(
                DirectionalShadow: _shadowAvailableForFrame,
                SurfaceData: surfacePrepared,
                AmbientOcclusion: ssaoPrepared,
                Sky: SdlGpuSkyPolicy.IsEligible(frame.Sky,
                    frame.Options.Quality.GraphicsPreset),
                Distortion: _distortionForFrame != null,
                Bloom: bloomPlan.Enabled,
                OriginalHud: !enhancedOutput && frame.HudSceneItems.Count > 0,
                EnhancedOutput: enhancedOutput,
                SceneCapture: sceneCapture,
                Visor: enhancedOutput && frame.Visor.Enabled,
                EnhancedHud: enhancedOutput && frame.HudSceneItems.Count > 0,
                SceneReadback: sceneReadback,
                FinalReadback: finalReadback,
                Reconstruction: reconstruction));

            foreach (RenderGraphPass graphPass in _executionPlan.Passes)
            {
                switch (graphPass.Kind)
                {
                    case RenderGraphPassKind.DirectionalShadow:
                        EncodeDirectionalShadow(commandBuffer, frame, meshes);
                        break;
                    case RenderGraphPassKind.SurfaceData:
                        _surfaceAvailableForFrame = TryEncodeEnhancedSurface(
                            commandBuffer, frame, meshes, vertexFrame);
                        break;
                    case RenderGraphPassKind.AmbientOcclusion:
                        if (_surfaceAvailableForFrame
                            && _ssaoResources.TryEncode(commandBuffer, _surfaceColor,
                                LinearClampSamplerHandle, frame.ViewMatrix,
                                frame.ProjectionMatrix))
                        {
                            _ambientOcclusionForFrame
                                = _ssaoResources.OcclusionTexture;
                        }
                        break;
                    case RenderGraphPassKind.Sky:
                        skyEncoded = _skyResources.TryEncode(commandBuffer, frame,
                            SceneRenderColor, _sceneColorFormat,
                            _targetSampleCount, SceneClearColor(frame),
                            SkySamplerHandle,
                            identity => PrepareUnmippedSkyTexture(frame, identity),
                            identity => EncodeUnmippedSkyTextureUpload(frame,
                                identity, commandBuffer),
                            identity => ResolveUnmippedTextureHandle(frame,
                                identity, "enhanced sky"));
                        break;
                    case RenderGraphPassKind.Opaque:
                        EncodePass(commandBuffer, frame, meshes, frame.OpaqueItems,
                            RenderPassKind.Opaque,
                            clearColor: SdlGpuSkyPolicy.OpaqueClearsColor(skyEncoded),
                            clearDepth: true, clearStencil: true,
                            resolveColor: false, ref encoded, vertexFrame,
                            fragmentFrame);
                        break;
                    case RenderGraphPassKind.Decal:
                        EncodePass(commandBuffer, frame, meshes, frame.DecalItems,
                            RenderPassKind.Decal, false, false, false, false,
                            ref encoded, vertexFrame, fragmentFrame);
                        break;
                    case RenderGraphPassKind.TransparentStencil:
                        EncodePass(commandBuffer, frame, meshes,
                            frame.TransparentItems,
                            RenderPassKind.TransparentStencil, false, false,
                            false, false, ref encoded, vertexFrame, fragmentFrame);
                        break;
                    case RenderGraphPassKind.DepthRebuild:
                        EncodePass(commandBuffer, frame, meshes, frame.OpaqueItems,
                            RenderPassKind.DepthRebuild, false, true, false,
                            false, ref encoded, vertexFrame, fragmentFrame);
                        break;
                    case RenderGraphPassKind.TransparentBehind:
                        EncodePass(commandBuffer, frame, meshes,
                            frame.TransparentItems,
                            RenderPassKind.TransparentBehind, false, false,
                            false, false, ref encoded, vertexFrame, fragmentFrame);
                        break;
                    case RenderGraphPassKind.TransparentFront:
                        EncodePass(commandBuffer, frame, meshes,
                            frame.TransparentItems,
                            RenderPassKind.TransparentFront, false, false,
                            false,
                            resolveColor: enhancedOutput
                                || frame.HudSceneItems.Count == 0,
                            ref encoded, vertexFrame, fragmentFrame);
                        break;
                    case RenderGraphPassKind.DistortionVectors:
                        EncodeEnhancedDistortion(commandBuffer, frame, meshes,
                            vertexFrame);
                        break;
                    case RenderGraphPassKind.BloomEmission:
                        EncodeBloom(commandBuffer, frame, meshes, bloomPlan,
                            vertexFrame, fragmentFrame);
                        break;
                    case RenderGraphPassKind.OriginalHud:
                        EncodeHudScene(commandBuffer, frame, hudMeshes,
                            SceneRenderColor, _sceneDepth, _targetWidth,
                            _targetHeight, _sceneColorFormat, _targetSampleCount,
                            _sceneColor, displayAssetsToLinear: false);
                        break;
                    case RenderGraphPassKind.ScenePostProcess:
                        postResources.EncodeScene(commandBuffer, frame,
                            _sceneColor, _sceneDepth,
                            bloomPlan.Enabled ? _bloomColor : null, bloomPlan,
                            finalComposite, finalWidth, finalHeight,
                            _sceneColorFormat, enhancedOutput, this,
                            _distortionForFrame);
                        displayLinear = postResources.DisplayLinearColor;
                        if (enhancedOutput && !reconstruction
                            && displayLinear == null)
                            throw new InvalidOperationException(
                                "SDL display-linear composite is unavailable.");
                        break;
                    case RenderGraphPassKind.Reconstruction:
                        postResources.EncodeReconstruction(commandBuffer, this,
                            frame.Composite.DestinationViewport);
                        displayLinear = postResources.DisplayLinearColor;
                        if (displayLinear == null)
                            throw new InvalidOperationException(
                                "SDL reconstructed display composite is unavailable.");
                        break;
                    case RenderGraphPassKind.SceneCaptureBase:
                        // SceneTarget branches before viewer-local visor work.
                        captureLinear = postResources.EncodeSceneCaptureBase(
                            commandBuffer, frame, _sceneColor, this);
                        break;
                    case RenderGraphPassKind.Visor:
                        displayLinear = postResources.EncodeVisor(commandBuffer,
                            frame, displayLinear, finalWidth, finalHeight,
                            _sceneColorFormat, this);
                        break;
                    case RenderGraphPassKind.EnhancedHud:
                        EncodeHudScene(commandBuffer, frame, hudMeshes,
                            displayLinear, depth: null, finalWidth, finalHeight,
                            _sceneColorFormat, sampleCount: 1, resolveTo: null,
                            displayAssetsToLinear: true);
                        if (captureLinear != null)
                        {
                            EncodeHudScene(commandBuffer, frame, hudMeshes,
                                captureLinear, depth: null, _targetWidth,
                                _targetHeight, _sceneColorFormat, sampleCount: 1,
                                resolveTo: null, displayAssetsToLinear: true);
                        }
                        break;
                    case RenderGraphPassKind.SceneCaptureTransfer:
                        if (captureLinear != null)
                        {
                            postResources.EncodeSceneCaptureTransfer(commandBuffer,
                                frame, captureLinear, this);
                        }
                        break;
                    case RenderGraphPassKind.Overlay:
                        postResources.EncodeOverlays(commandBuffer, frame,
                            enhancedOutput ? displayLinear : finalComposite,
                            finalWidth, finalHeight,
                            enhancedOutput ? _sceneColorFormat
                                : _device.SwapchainFormat,
                            displayLinearComposition: enhancedOutput, this);
                        break;
                    case RenderGraphPassKind.FinalTransfer:
                        postResources.EncodeFinalTransfer(commandBuffer, frame,
                            displayLinear, finalComposite, finalWidth,
                            finalHeight, this);
                        break;
                    case RenderGraphPassKind.SceneReadback:
                    case RenderGraphPassKind.FinalReadback:
                        // SdlGpuBackend owns readback tickets and appends the
                        // actual copy commands immediately after this plan.
                        // These terminal nodes keep the source lifetimes and
                        // both capture tap points explicit in the graph.
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"Unsupported SDL render graph pass {graphPass.Kind}.");
                }
            }
        }

        private void PrepareAuxiliaryTargets(uint width, uint height,
            uint finalWidth, uint finalHeight, bool enhancedOutput,
            SdlGpuBloomPlan bloomPlan)
        {
            if (bloomPlan.Enabled) EnsureBloomTargets(width, height, bloomPlan);
            _postResources!.PrepareSceneTargets(width, height, finalWidth,
                finalHeight, _sceneColorFormat, bloomPlan, enhancedOutput);
        }

        private bool TryPrepareEnhancedDistortion(RenderFrame frame,
            uint width, uint height)
        {
            if (frame.Options.Quality.GraphicsPreset != GraphicsPreset.Enhanced
                || !frame.DistortionSubmissions.HasSources)
            {
                return false;
            }

            EnhancedDistortionCapabilities capabilities = new(
                DistortionCapabilities(
                    SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R16G16_FLOAT),
                DistortionCapabilities(
                    SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R16G16B16A16_FLOAT));
            for (int attempt = 0; attempt < 2; attempt++)
            {
                EnhancedDistortionTargetPlan plan
                    = EnhancedDistortionTargetPlan.Create(width, height,
                        _targetSampleCount, requested: true,
                        frame.DistortionSubmissions.Count, capabilities,
                        _distortionFailure);
                if (!plan.Enabled) return false;
                EnhancedDistortionTargetConfiguration configuration
                    = plan.Configuration!.Value;
                try
                {
                    EnsureDistortionShaders();
                    EnsureDistortionTargets(configuration);
                    foreach (EnhancedDistortionSubmission source
                        in frame.DistortionSubmissions.Items)
                    {
                        _ = GetDistortionPipeline(source.CullingMode,
                            frame.Options.FaceCulling, configuration);
                    }
                    if (!_postResources!.TryPrepareDistortionWarp(
                        _sceneColorFormat)) return false;
                    _distortionFailure.RecordSuccess(configuration);
                    _reportedDistortionFailure = null;
                    return true;
                }
                catch (Exception error) when (error is InvalidOperationException
                    or IOException or PlatformNotSupportedException)
                {
                    _distortionFailure.RecordFailure(configuration);
                    if (_reportedDistortionFailure != configuration)
                    {
                        _reportedDistortionFailure = configuration;
                        Console.Error.WriteLine(
                            $"[render] Enhanced distortion target {configuration.Format} disabled for this resource configuration: {error.Message}");
                    }
                }
            }
            return false;
        }

        private EnhancedDistortionFormatCapabilities DistortionCapabilities(
            SDL_GPUTextureFormat format)
        {
            SDL_GPUTextureUsageFlags usage
                = SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COLOR_TARGET
                    | SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER;
            bool supported = SDL3.SDL_GPUTextureSupportsFormat(_device.Handle,
                format, SDL_GPUTextureType.SDL_GPU_TEXTURETYPE_2D, usage);
            EnhancedDistortionSampleCounts samples = supported
                ? EnhancedDistortionSampleCounts.One
                : EnhancedDistortionSampleCounts.None;
            if (supported && SDL3.SDL_GPUTextureSupportsSampleCount(
                _device.Handle, format,
                SDL_GPUSampleCount.SDL_GPU_SAMPLECOUNT_2))
                samples |= EnhancedDistortionSampleCounts.Two;
            if (supported && SDL3.SDL_GPUTextureSupportsSampleCount(
                _device.Handle, format,
                SDL_GPUSampleCount.SDL_GPU_SAMPLECOUNT_4))
                samples |= EnhancedDistortionSampleCounts.Four;
            return new EnhancedDistortionFormatCapabilities(supported,
                supported, samples);
        }

        private void EnsureDistortionShaders()
        {
            if (_distortionVertexShader != null
                && _distortionFragmentShader != null) return;
            ShaderArtifactManifest.ValidateDistortionFresh();
            (SDL_GPUShaderFormat format, string suffix) = SelectShaderFormat();
            string directory = ShaderArtifactManifest.Directory;
            SDL_GPUShader* vertex = CreateShader(_device.Handle, format,
                Path.Combine(directory, $"distortion.vert.{suffix}"),
                "main_vs", SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_VERTEX,
                samplers: 0, uniforms: 2);
            try
            {
                SDL_GPUShader* fragment = CreateShader(_device.Handle, format,
                    Path.Combine(directory, $"distortion.frag.{suffix}"),
                    "main_ps", SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_FRAGMENT,
                    samplers: 0, uniforms: 1);
                _distortionVertexShader = vertex;
                _distortionFragmentShader = fragment;
            }
            catch
            {
                SDL3.SDL_ReleaseGPUShader(_device.Handle, vertex);
                throw;
            }
        }

        private void EnsureDistortionTargets(
            EnhancedDistortionTargetConfiguration configuration)
        {
            if (_distortionConfiguration == configuration
                && _distortionColor != null
                && (!configuration.RequiresResolve
                    || _distortionMultisampleColor != null)) return;
            SDL_GPUTextureFormat format = configuration.Format switch
            {
                EnhancedDistortionTargetFormat.Rg16Float
                    => SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R16G16_FLOAT,
                EnhancedDistortionTargetFormat.Rgba16Float
                    => SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R16G16B16A16_FLOAT,
                _ => throw new ArgumentOutOfRangeException(nameof(configuration))
            };
            SDL_GPUTexture* resolved = null;
            SDL_GPUTexture* multisample = null;
            try
            {
                resolved = CreateTarget(format,
                    SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COLOR_TARGET
                        | SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER,
                    configuration.Width, configuration.Height, 1);
                if (configuration.RequiresResolve)
                {
                    multisample = CreateTarget(format,
                        SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COLOR_TARGET,
                        configuration.Width, configuration.Height,
                        configuration.RenderSampleCount);
                }
            }
            catch
            {
                if (multisample != null)
                    SDL3.SDL_ReleaseGPUTexture(_device.Handle, multisample);
                if (resolved != null)
                    SDL3.SDL_ReleaseGPUTexture(_device.Handle, resolved);
                throw;
            }
            if (!SDL3.SDL_WaitForGPUIdle(_device.Handle))
            {
                if (multisample != null)
                    SDL3.SDL_ReleaseGPUTexture(_device.Handle, multisample);
                SDL3.SDL_ReleaseGPUTexture(_device.Handle, resolved);
                throw new InvalidOperationException(
                    $"SDL distortion target resize wait failed: {SDL3.SDL_GetError()}");
            }
            if (_distortionMultisampleColor != null)
                SDL3.SDL_ReleaseGPUTexture(_device.Handle,
                    _distortionMultisampleColor);
            if (_distortionColor != null)
                SDL3.SDL_ReleaseGPUTexture(_device.Handle, _distortionColor);
            _distortionColor = resolved;
            _distortionMultisampleColor = multisample;
            _distortionConfiguration = configuration;
        }

        private SDL_GPUGraphicsPipeline* GetDistortionPipeline(
            CullingMode culling, bool faceCulling,
            EnhancedDistortionTargetConfiguration configuration)
        {
            SDL_GPUTextureFormat format = configuration.Format switch
            {
                EnhancedDistortionTargetFormat.Rg16Float
                    => SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R16G16_FLOAT,
                EnhancedDistortionTargetFormat.Rgba16Float
                    => SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R16G16B16A16_FLOAT,
                _ => throw new ArgumentOutOfRangeException(nameof(configuration))
            };
            var key = (culling, faceCulling, format,
                configuration.RenderSampleCount);
            if (_distortionPipelines.TryGetValue(key, out nint cached))
                return (SDL_GPUGraphicsPipeline*)cached;

            SDL_GPUVertexBufferDescription vertexDescription = new()
            {
                slot = 0, pitch = (uint)sizeof(GpuVertex),
                input_rate = SDL_GPUVertexInputRate.SDL_GPU_VERTEXINPUTRATE_VERTEX
            };
            SDL_GPUVertexAttribute* attributes
                = stackalloc SDL_GPUVertexAttribute[SdlGpuSceneVertexAbi.AttributeCount];
            attributes[0] = Attribute(0, SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT3, 0);
            attributes[1] = Attribute(1, SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT4, 12);
            attributes[2] = Attribute(2, SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT3, 28);
            attributes[3] = Attribute(3, SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT2, 40);
            attributes[4] = Attribute(4, SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_UINT, 48);
            attributes[5] = Attribute(5, SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_UINT, 52);
            attributes[6] = Attribute(6, SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT4,
                SdlGpuSceneVertexAbi.TangentOffset);
            SDL_GPUVertexInputState vertexInput = new()
            {
                vertex_buffer_descriptions = &vertexDescription,
                num_vertex_buffers = 1,
                vertex_attributes = attributes,
                num_vertex_attributes = SdlGpuSceneVertexAbi.AttributeCount
            };
            SDL_GPUColorTargetDescription color = new()
            {
                format = format,
                blend_state = new SDL_GPUColorTargetBlendState
                {
                    src_color_blendfactor = SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE,
                    dst_color_blendfactor = SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE,
                    color_blend_op = SDL_GPUBlendOp.SDL_GPU_BLENDOP_ADD,
                    src_alpha_blendfactor = SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE,
                    dst_alpha_blendfactor = SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE,
                    alpha_blend_op = SDL_GPUBlendOp.SDL_GPU_BLENDOP_ADD,
                    color_write_mask = SDL_GPUColorComponentFlags.SDL_GPU_COLORCOMPONENT_R
                        | SDL_GPUColorComponentFlags.SDL_GPU_COLORCOMPONENT_G,
                    enable_blend = true,
                    enable_color_write_mask = true
                }
            };
            SDL_GPUGraphicsPipelineCreateInfo info = new()
            {
                vertex_shader = _distortionVertexShader,
                fragment_shader = _distortionFragmentShader,
                vertex_input_state = vertexInput,
                primitive_type = SDL_GPUPrimitiveType.SDL_GPU_PRIMITIVETYPE_TRIANGLELIST,
                rasterizer_state = new SDL_GPURasterizerState
                {
                    fill_mode = SDL_GPUFillMode.SDL_GPU_FILLMODE_FILL,
                    cull_mode = !faceCulling ? SDL_GPUCullMode.SDL_GPU_CULLMODE_NONE
                        : culling switch
                        {
                            CullingMode.Front => SDL_GPUCullMode.SDL_GPU_CULLMODE_FRONT,
                            CullingMode.Back => SDL_GPUCullMode.SDL_GPU_CULLMODE_BACK,
                            _ => SDL_GPUCullMode.SDL_GPU_CULLMODE_NONE
                        },
                    front_face = SDL_GPUFrontFace.SDL_GPU_FRONTFACE_COUNTER_CLOCKWISE,
                    enable_depth_clip = true
                },
                multisample_state = new SDL_GPUMultisampleState
                {
                    sample_count = SampleCount(configuration.RenderSampleCount)
                },
                depth_stencil_state = new SDL_GPUDepthStencilState
                {
                    compare_op = SDL_GPUCompareOp.SDL_GPU_COMPAREOP_LESS_OR_EQUAL,
                    enable_depth_test = true,
                    enable_depth_write = false,
                    enable_stencil_test = false
                },
                target_info = new SDL_GPUGraphicsPipelineTargetInfo
                {
                    color_target_descriptions = &color,
                    num_color_targets = 1,
                    depth_stencil_format = _depthFormat,
                    has_depth_stencil_target = true
                }
            };
            SDL_GPUGraphicsPipeline* pipeline
                = SDL3.SDL_CreateGPUGraphicsPipeline(_device.Handle, &info);
            if (pipeline == null)
                throw new InvalidOperationException(
                    $"SDL distortion pipeline creation failed: {SDL3.SDL_GetError()}");
            _distortionPipelines.Add(key, (nint)pipeline);
            return pipeline;
        }

        private void EncodeEnhancedDistortion(SDL_GPUCommandBuffer* commandBuffer,
            RenderFrame frame, IReadOnlyDictionary<DrawSubmission, GpuMesh> meshes,
            SdlGpuSceneVertexFrameConstants vertexFrame)
        {
            if (!_distortionConfiguration.HasValue || _distortionColor == null)
                throw new InvalidOperationException(
                    "Enhanced distortion resources were not prepared.");
            EnhancedDistortionTargetConfiguration configuration
                = _distortionConfiguration.Value;
            SDL_GPUColorTargetInfo color = new()
            {
                texture = configuration.RequiresResolve
                    ? _distortionMultisampleColor : _distortionColor,
                clear_color = new SDL_FColor(),
                load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR,
                store_op = configuration.RequiresResolve
                    ? SDL_GPUStoreOp.SDL_GPU_STOREOP_RESOLVE
                    : SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
                resolve_texture = configuration.RequiresResolve
                    ? _distortionColor : null,
                cycle = false
            };
            SDL_GPUDepthStencilTargetInfo depth = new()
            {
                texture = _sceneDepth,
                load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_LOAD,
                store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
                stencil_load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_LOAD,
                stencil_store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
                cycle = false
            };
            SDL_GPURenderPass* pass = SDL3.SDL_BeginGPURenderPass(
                commandBuffer, &color, 1, &depth);
            if (pass == null)
                throw new InvalidOperationException(
                    $"SDL distortion vector pass failed: {SDL3.SDL_GetError()}");
            SdlGpuPassBindingCache passBindings = default;
            passBindings.Reset();
            SdlGpuTelemetryContext.RenderPass();
            try
            {
                SDL_GPUViewport viewport = new()
                {
                    w = configuration.Width, h = configuration.Height,
                    min_depth = 0, max_depth = 1
                };
                SDL3.SDL_SetGPUViewport(pass, &viewport);
                PushVertex(commandBuffer, 0, vertexFrame);
                int encoded = 0;
                foreach (EnhancedDistortionSubmission source
                    in frame.DistortionSubmissions.Items)
                {
                    DrawSubmission? draw = source.SourceDraw;
                    if (draw == null || !meshes.TryGetValue(draw,
                        out GpuMesh? mesh) || mesh.TriangleIndexCount == 0) continue;
                    if (++encoded > EnhancedDistortionSubmission.MaximumCount)
                        throw new InvalidOperationException(
                            "Enhanced distortion draw bound exceeded.");
                    BindPipeline(pass, ref passBindings,
                        GetDistortionPipeline(source.CullingMode,
                            frame.Options.FaceCulling, configuration));
                    SDL_GPUBufferBinding vertex = new()
                    {
                        buffer = mesh.VertexBuffer
                    };
                    SDL_GPUBufferBinding index = new()
                    {
                        buffer = mesh.TriangleIndexBuffer
                    };
                    BindVertexBuffer(pass, &vertex, 1);
                    BindIndexBuffer(pass, &index,
                        SDL_GPUIndexElementSize.SDL_GPU_INDEXELEMENTSIZE_32BIT);
                    DistortionVertexConstants vertexConstants
                        = BuildDistortionVertexConstants(frame, draw);
                    PushVertex(commandBuffer, 1, vertexConstants);
                    PushFragment(commandBuffer, 0,
                        new DistortionFragmentConstants
                        {
                            Options = new Vector4(source.Strength,
                                source.Falloff,
                                source.PresentationPhase, 0)
                        });
                    DrawIndexed(pass, ref passBindings,
                        checked((uint)mesh.TriangleIndexCount));
                }
            }
            finally
            {
                SDL3.SDL_EndGPURenderPass(pass);
            }
        }

        private static DistortionVertexConstants BuildDistortionVertexConstants(
            RenderFrame frame, DrawSubmission draw)
        {
            DistortionVertexConstants constants = default;
            constants.Transform = SdlGpuMatrixAbi.Upload(draw.Transform);
            constants.Billboard = draw.Material.BillboardMode switch
            {
                BillboardMode.Sphere => SdlGpuMatrixAbi.Upload(
                    frame.ViewInverseRotation),
                BillboardMode.Cylinder => SdlGpuMatrixAbi.Upload(
                    frame.ViewInverseRotationY),
                _ => SdlGpuMatrixAbi.Upload(Matrix4.Identity)
            };
            constants.TextureMatrix = SdlGpuMatrixAbi.Upload(
                draw.Material.TextureMatrix);
            constants.Options = new Vector4(
                SdlGpuDistortionTransformPolicy.UsesMatrixStack(
                    draw.MatrixStackCount) ? 1 : 0,
                0, 0, 0);
            float* destination = constants.MatrixStack;
            draw.MatrixStack.AsSpan().CopyTo(new Span<float>(destination,
                RenderFrame.MatrixStackFloats));
            return constants;
        }

        private bool TryPrepareDirectionalShadow(RenderFrame frame)
        {
            RenderDirectionalShadowState shadow = frame.DirectionalShadow;
            if (frame.Options.Quality.GraphicsPreset != GraphicsPreset.Enhanced
                || !shadow.Enabled || !_depthSampleable) return false;
            SdlGpuDirectionalShadowConfiguration configuration = new(
                shadow.MapSize, _depthFormat);
            if (!_shadowFailure.ShouldAttempt(configuration)) return false;
            try
            {
                EnsureShadowShaders();
                if (_shadowDepth == null || _shadowMapSize != shadow.MapSize)
                {
                    SDL_GPUTexture* replacement = CreateTarget(_depthFormat,
                        SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_DEPTH_STENCIL_TARGET
                            | SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER,
                        checked((uint)shadow.MapSize), checked((uint)shadow.MapSize),
                        sampleCount: 1);
                    if (!SDL3.SDL_WaitForGPUIdle(_device.Handle))
                    {
                        SDL3.SDL_ReleaseGPUTexture(_device.Handle, replacement);
                        throw new InvalidOperationException(
                            $"SDL shadow target resize wait failed: {SDL3.SDL_GetError()}");
                    }
                    if (_shadowDepth != null)
                        SDL3.SDL_ReleaseGPUTexture(_device.Handle, _shadowDepth);
                    _shadowDepth = replacement;
                    _shadowMapSize = shadow.MapSize;
                }
                foreach (DrawSubmission draw in frame.OpaqueItems)
                {
                    if (draw.Material.Alpha != 1) continue;
                    _ = GetShadowPipeline(ShadowPipelineKey(frame, draw));
                }
                _shadowFailure.RecordSuccess();
                _reportedShadowFailure = null;
                return true;
            }
            catch (Exception error) when (error is InvalidOperationException
                or IOException or PlatformNotSupportedException)
            {
                _shadowFailure.RecordFailure(configuration);
                if (_reportedShadowFailure != configuration)
                {
                    _reportedShadowFailure = configuration;
                    Console.Error.WriteLine(
                        $"[render] directional shadows disabled for this resource configuration: {error.Message}");
                }
                return false;
            }
        }

        private void EnsureShadowShaders()
        {
            if (_shadowVertexShader != null && _shadowFragmentShader != null) return;
            ShaderArtifactManifest.ValidateShadowFresh();
            (SDL_GPUShaderFormat format, string suffix) = SelectShaderFormat();
            string directory = ShaderArtifactManifest.Directory;
            SDL_GPUShader* vertex = CreateShader(_device.Handle, format,
                Path.Combine(directory, $"shadow.vert.{suffix}"), "main_vs",
                SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_VERTEX,
                samplers: 0, uniforms: 2);
            try
            {
                SDL_GPUShader* fragment = CreateShader(_device.Handle, format,
                    Path.Combine(directory, $"shadow.frag.{suffix}"), "main_ps",
                    SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_FRAGMENT,
                    samplers: checked((uint)SamplerBindingCount(1)), uniforms: 1);
                _shadowVertexShader = vertex;
                _shadowFragmentShader = fragment;
            }
            catch
            {
                SDL3.SDL_ReleaseGPUShader(_device.Handle, vertex);
                throw;
            }
        }

        private static SdlGpuShadowPipelineKey ShadowPipelineKey(
            RenderFrame frame, DrawSubmission draw)
            => new(draw.Material.CullingMode switch
            {
                CullingMode.Front => RenderCullMode.Front,
                CullingMode.Back => RenderCullMode.Back,
                _ => RenderCullMode.None
            }, frame.Options.FaceCulling);

        private void EncodeDirectionalShadow(SDL_GPUCommandBuffer* commandBuffer,
            RenderFrame frame, IReadOnlyDictionary<DrawSubmission, GpuMesh> meshes)
        {
            SDL_GPUDepthStencilTargetInfo depth = new()
            {
                texture = _shadowDepth,
                clear_depth = 1,
                clear_stencil = 0,
                load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR,
                store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
                stencil_load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR,
                stencil_store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
                cycle = false
            };
            SDL_GPURenderPass* pass = SDL3.SDL_BeginGPURenderPass(
                commandBuffer, null, 0, &depth);
            if (pass == null)
                throw new InvalidOperationException(
                    $"SDL directional shadow pass failed: {SDL3.SDL_GetError()}");
            SdlGpuPassBindingCache passBindings = default;
            passBindings.Reset();
            SdlGpuTelemetryContext.RenderPass();
            try
            {
                SDL_GPUViewport viewport = new()
                {
                    w = frame.DirectionalShadow.MapSize,
                    h = frame.DirectionalShadow.MapSize,
                    min_depth = 0,
                    max_depth = 1
                };
                SDL3.SDL_SetGPUViewport(pass, &viewport);
                ShadowFrameConstants shadowFrame = new()
                {
                    ViewProjection = SdlGpuMatrixAbi.Upload(
                        frame.DirectionalShadow.ViewProjection),
                    View = SdlGpuMatrixAbi.Upload(frame.ViewMatrix),
                    Options = new Vector4(frame.Options.Lighting ? 1 : 0, 0, 0, 0)
                };
                PushVertex(commandBuffer, 0, shadowFrame);
                int encoded = 0;
                int bindingCount = SamplerBindingCount(1);
                SDL_GPUTextureSamplerBinding* bindings
                    = stackalloc SDL_GPUTextureSamplerBinding[bindingCount];
                foreach (DrawSubmission draw in frame.OpaqueItems)
                {
                    if (!DirectionalShadowCasterPolicy.ShouldRender(draw)
                        || !meshes.TryGetValue(draw, out GpuMesh? mesh)
                        || mesh.TriangleIndexCount == 0) continue;
                    if (++encoded > RenderFrame.DefaultMaximumCapacity)
                        throw new InvalidOperationException(
                            "Directional shadow caster bound exceeded.");
                    BindPipeline(pass, ref passBindings,
                        GetShadowPipeline(ShadowPipelineKey(frame, draw)));
                    SDL_GPUBufferBinding vertex = new() { buffer = mesh.VertexBuffer };
                    SDL_GPUBufferBinding index = new() { buffer = mesh.TriangleIndexBuffer };
                    BindVertexBuffer(pass, &vertex, 1);
                    BindIndexBuffer(pass, &index,
                        SDL_GPUIndexElementSize.SDL_GPU_INDEXELEMENTSIZE_32BIT);
                    GpuTexture albedo = ResolveBoundTexture(frame, draw);
                    SDL_GPUSampler* sampler = GetSampler(
                        SdlGpuVisualStyleSampler.From(frame.Options, draw.Material));
                    bindings[0] = new()
                        { texture = albedo.Handle, sampler = sampler };
                    SdlGpuSamplerBindingAbi.Pad(bindings, 1, bindingCount,
                        _whiteTexture!.Handle, sampler);
                    BindFragmentSamplers(pass, ref passBindings, 0, bindings,
                        checked((uint)bindingCount));
                    LegacyDrawConstants constants = BuildLegacyDrawConstants(frame, draw,
                        RenderPassKind.Opaque, RenderTopology.Triangles);
                    PushVertex(commandBuffer, 1, constants);
                    PushFragment(commandBuffer, 0, constants);
                    DrawIndexed(pass, ref passBindings,
                        checked((uint)mesh.TriangleIndexCount));
                }
            }
            finally
            {
                SDL3.SDL_EndGPURenderPass(pass);
            }
        }

        private bool TryPrepareEnhancedSurface(uint width, uint height)
        {
            SdlGpuEnhancedSurfaceConfiguration configuration = new(width, height);
            if (!_surfaceFailure.ShouldAttempt(configuration)) return false;
            SdlGpuEnhancedSurfacePlan plan;
            try
            {
                plan = SdlGpuEnhancedSurfacePlan.Create(width, height);
            }
            catch (ArgumentOutOfRangeException error)
            {
                RecordSurfaceFailure(configuration, error.Message);
                return false;
            }
            if (_surfacePlan?.Configuration == configuration
                && _surfaceColor != null && _surfaceDepth != null
                && _surfaceFragmentShader != null) return true;

            try
            {
                EnsureSurfaceShader();
                SDL_GPUTexture* replacementColor = null;
                SDL_GPUTexture* replacementDepth = null;
                try
                {
                    replacementColor = CreateTarget(SdlGpuHdrPolicy.HdrFormat,
                        SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COLOR_TARGET
                            | SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER,
                        width, height, sampleCount: 1);
                    replacementDepth = CreateTarget(_depthFormat,
                        SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_DEPTH_STENCIL_TARGET,
                        width, height, sampleCount: 1);
                }
                catch
                {
                    if (replacementDepth != null)
                        SDL3.SDL_ReleaseGPUTexture(_device.Handle, replacementDepth);
                    if (replacementColor != null)
                        SDL3.SDL_ReleaseGPUTexture(_device.Handle, replacementColor);
                    throw;
                }
                if (!SDL3.SDL_WaitForGPUIdle(_device.Handle))
                {
                    SDL3.SDL_ReleaseGPUTexture(_device.Handle, replacementDepth);
                    SDL3.SDL_ReleaseGPUTexture(_device.Handle, replacementColor);
                    throw new InvalidOperationException(
                        $"SDL surface target resize wait failed: {SDL3.SDL_GetError()}");
                }
                if (_surfaceDepth != null)
                    SDL3.SDL_ReleaseGPUTexture(_device.Handle, _surfaceDepth);
                if (_surfaceColor != null)
                    SDL3.SDL_ReleaseGPUTexture(_device.Handle, _surfaceColor);
                _surfaceColor = replacementColor;
                _surfaceDepth = replacementDepth;
                _surfacePlan = plan;
                _surfaceFailure.RecordSuccess();
                _reportedSurfaceFailure = null;
                return true;
            }
            catch (Exception error) when (error is InvalidOperationException
                or IOException or PlatformNotSupportedException)
            {
                RecordSurfaceFailure(configuration, error.Message);
                return false;
            }
        }

        private void EnsureSurfaceShader()
        {
            if (_surfaceFragmentShader != null) return;
            ShaderArtifactManifest.ValidateSurfaceFresh();
            (SDL_GPUShaderFormat format, string suffix) = SelectShaderFormat();
            string path = Path.Combine(ShaderArtifactManifest.Directory,
                $"surface.frag.{suffix}");
            _surfaceFragmentShader = CreateShader(_device.Handle, format, path,
                "main_ps", SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_FRAGMENT,
                samplers: checked((uint)SamplerBindingCount(2)), uniforms: 1);
        }

        private bool TryEncodeEnhancedSurface(SDL_GPUCommandBuffer* commandBuffer,
            RenderFrame frame, IReadOnlyDictionary<DrawSubmission, GpuMesh> meshes,
            SdlGpuSceneVertexFrameConstants vertexFrame)
        {
            if (!_surfacePlan.HasValue || _surfaceColor == null
                || _surfaceDepth == null) return false;
            SdlGpuEnhancedSurfaceConfiguration configuration
                = _surfacePlan.Value.Configuration;
            if (!_surfaceFailure.ShouldAttempt(configuration)) return false;
            try
            {
                SDL_GPUColorTargetInfo color = new()
                {
                    texture = _surfaceColor,
                    clear_color = new SDL_FColor
                    {
                        r = 0.5f, g = 0.5f, b = 0, a = 0
                    },
                    load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR,
                    store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
                    cycle = false
                };
                SDL_GPUDepthStencilTargetInfo depth = new()
                {
                    texture = _surfaceDepth, clear_depth = 1,
                    clear_stencil = 0,
                    load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR,
                    store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
                    stencil_load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR,
                    stencil_store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
                    cycle = false
                };
                SDL_GPURenderPass* pass = SDL3.SDL_BeginGPURenderPass(
                    commandBuffer, &color, 1, &depth);
                if (pass == null)
                    throw new InvalidOperationException(
                        $"SDL enhanced surface pass failed: {SDL3.SDL_GetError()}");
                SdlGpuPassBindingCache passBindings = default;
                passBindings.Reset();
                SdlGpuTelemetryContext.RenderPass();
                try
                {
                    SDL_GPUViewport viewport = new()
                    {
                        w = configuration.Width, h = configuration.Height,
                        min_depth = 0, max_depth = 1
                    };
                    SDL3.SDL_SetGPUViewport(pass, &viewport);
                    PushVertex(commandBuffer, 0, vertexFrame);
                    int encoded = 0;
                    foreach (DrawSubmission draw in frame.OpaqueItems)
                    {
                        if (++encoded > RenderFrame.DefaultMaximumCapacity)
                            throw new InvalidOperationException(
                                "Enhanced surface draw bound exceeded.");
                        if (!meshes.TryGetValue(draw, out GpuMesh? mesh)) continue;
                        DrawSurface(commandBuffer, pass, ref passBindings,
                            frame, draw, mesh);
                    }
                }
                finally
                {
                    SDL3.SDL_EndGPURenderPass(pass);
                }
                return true;
            }
            catch (InvalidOperationException error)
            {
                RecordSurfaceFailure(configuration, error.Message);
                return false;
            }
        }

        private void DrawSurface(SDL_GPUCommandBuffer* commandBuffer,
            SDL_GPURenderPass* pass, ref SdlGpuPassBindingCache passBindings,
            RenderFrame frame, DrawSubmission draw, GpuMesh mesh)
        {
            if (mesh.TriangleIndexCount == 0
                || (draw.Primitive == RenderPrimitive.Ngon
                    && frame.Options.VolumeEdges == 1)) return;
            PipelineKey baseKey = PipelineKey.From(draw.Material, draw.Primitive,
                RenderPassKind.Opaque, sampleCount: 1,
                targetFormat: SdlGpuHdrPolicy.HdrFormat.ToString());
            ScenePipelineKey key = new(baseKey,
                frame.Options.Wireframe || draw.Material.Wireframe,
                frame.Options.FaceCulling, SdlGpuHdrPolicy.HdrFormat,
                SurfaceData: true);
            BindPipeline(pass, ref passBindings, GetPipeline(key));
            SDL3.SDL_SetGPUStencilReference(pass,
                checked((byte)Math.Clamp(draw.PolygonId, 0, 255)));

            SDL_GPUBufferBinding vertex = new() { buffer = mesh.VertexBuffer };
            SDL_GPUBufferBinding index = new()
            {
                buffer = mesh.TriangleIndexBuffer
            };
            BindVertexBuffer(pass, &vertex, 1);
            BindIndexBuffer(pass, &index,
                SDL_GPUIndexElementSize.SDL_GPU_INDEXELEMENTSIZE_32BIT);
            GpuTexture albedo = ResolveBoundTexture(frame, draw);
            GpuTexture normal = ResolveBoundNormalTexture(frame, draw);
            SDL_GPUSampler* sampler = GetSampler(
                SdlGpuVisualStyleSampler.From(frame.Options, draw.Material));
            int bindingCount = SamplerBindingCount(2);
            SDL_GPUTextureSamplerBinding* bindings
                = stackalloc SDL_GPUTextureSamplerBinding[bindingCount];
            bindings[0] = new() { texture = albedo.Handle, sampler = sampler };
            bindings[1] = new() { texture = normal.Handle, sampler = sampler };
            SdlGpuSamplerBindingAbi.Pad(bindings, 2, bindingCount,
                _whiteTexture!.Handle, sampler);
            BindFragmentSamplers(pass, ref passBindings, 0, bindings,
                checked((uint)bindingCount));

            LegacyDrawConstants constants = BuildLegacyDrawConstants(frame, draw,
                RenderPassKind.Opaque, RenderTopology.Triangles);
            PushVertex(commandBuffer, 1, constants);
            PushFragment(commandBuffer, 0, constants);
            DrawIndexed(pass, ref passBindings,
                checked((uint)mesh.TriangleIndexCount));
        }

        private void RecordSurfaceFailure(
            SdlGpuEnhancedSurfaceConfiguration configuration, string reason)
        {
            _surfaceFailure.RecordFailure(configuration);
            if (_reportedSurfaceFailure == configuration) return;
            _reportedSurfaceFailure = configuration;
            Console.Error.WriteLine(
                $"[render] Enhanced surface data disabled for this resource configuration: {reason}");
        }

        private void EncodePass(SDL_GPUCommandBuffer* commandBuffer, RenderFrame frame,
            Dictionary<DrawSubmission, GpuMesh> meshes, IReadOnlyList<DrawSubmission> draws,
            RenderPassKind passKind, bool clearColor, bool clearDepth, bool clearStencil,
            bool resolveColor, ref int encoded, SdlGpuSceneVertexFrameConstants vertexFrame,
            SdlGpuSceneFragmentFrameConstants fragmentFrame)
        {
            SDL_GPUColorTargetInfo colorTarget = new()
            {
                texture = SceneRenderColor,
                clear_color = SceneClearColor(frame),
                load_op = clearColor ? SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR : SDL_GPULoadOp.SDL_GPU_LOADOP_LOAD,
                store_op = resolveColor && _targetSampleCount > 1
                    ? SDL_GPUStoreOp.SDL_GPU_STOREOP_RESOLVE
                    : SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
                resolve_texture = resolveColor && _targetSampleCount > 1 ? _sceneColor : null,
                cycle = false
            };
            SDL_GPUDepthStencilTargetInfo depthTarget = new()
            {
                texture = _sceneDepth,
                clear_depth = 1,
                clear_stencil = 0,
                load_op = clearDepth ? SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR : SDL_GPULoadOp.SDL_GPU_LOADOP_LOAD,
                store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
                stencil_load_op = clearStencil ? SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR : SDL_GPULoadOp.SDL_GPU_LOADOP_LOAD,
                stencil_store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
                cycle = false
            };
            SDL_GPURenderPass* pass = SDL3.SDL_BeginGPURenderPass(commandBuffer, &colorTarget, 1, &depthTarget);
            if (pass == null) throw new InvalidOperationException($"SDL scene pass {passKind} failed: {SDL3.SDL_GetError()}");
            SdlGpuPassBindingCache passBindings = default;
            passBindings.Reset();
            SdlGpuTelemetryContext.RenderPass();
            try
            {
                SDL_GPUViewport viewport = new() { x = 0, y = 0, w = _targetWidth, h = _targetHeight, min_depth = 0, max_depth = 1 };
                SDL3.SDL_SetGPUViewport(pass, &viewport);
                PushVertex(commandBuffer, 0, vertexFrame);
                PushFragment(commandBuffer, 0, fragmentFrame);
                PushVertex(commandBuffer, 2, IdentityPalette());
                foreach (DrawSubmission draw in draws)
                {
                    if (++encoded > MaximumDraws) throw new InvalidOperationException("Encoded scene draw bound exceeded.");
                    if (!meshes.TryGetValue(draw, out GpuMesh? mesh)) continue;
                    Draw(commandBuffer, pass, ref passBindings, frame, draw, mesh,
                        passKind, RenderTopology.Triangles);
                    if (draw.Primitive == RenderPrimitive.Ngon && mesh.LineIndexCount > 0
                        && frame.Options.VolumeEdges != 2 && !draw.NoLines)
                    {
                        Draw(commandBuffer, pass, ref passBindings, frame, draw, mesh,
                            passKind, RenderTopology.Lines);
                    }
                }
            }
            finally
            {
                SDL3.SDL_EndGPURenderPass(pass);
            }
        }

        private SDL_FColor SceneClearColor(RenderFrame frame)
        {
            Vector3 rgb = frame.Options.Quality.GraphicsPreset == GraphicsPreset.Enhanced
                ? EnhancedColorMath.SrgbToLinear(frame.ClearColor.Xyz)
                : frame.ClearColor.Xyz;
            return new SDL_FColor
            {
                r = rgb.X,
                g = rgb.Y,
                b = rgb.Z,
                a = frame.ClearColor.W
            };
        }

        private void EncodeHudScene(SDL_GPUCommandBuffer* commandBuffer, RenderFrame frame,
            IReadOnlyDictionary<RenderHudSceneSubmission, GpuMesh> meshes,
            SDL_GPUTexture* target, SDL_GPUTexture* depth, uint width, uint height,
            SDL_GPUTextureFormat targetFormat, int sampleCount,
            SDL_GPUTexture* resolveTo, bool displayAssetsToLinear)
        {
            if (frame.HudSceneItems.Count == 0) return;
            SDL_GPUColorTargetInfo colorTarget = new()
            {
                texture = target,
                load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_LOAD,
                store_op = sampleCount > 1
                    ? SDL_GPUStoreOp.SDL_GPU_STOREOP_RESOLVE
                    : SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
                resolve_texture = sampleCount > 1 ? resolveTo : null,
                cycle = false
            };
            SDL_GPUDepthStencilTargetInfo depthTarget = new()
            {
                texture = depth,
                load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_LOAD,
                store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
                stencil_load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_LOAD,
                stencil_store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
                cycle = false
            };
            SDL_GPURenderPass* pass = SDL3.SDL_BeginGPURenderPass(commandBuffer,
                &colorTarget, 1, depth != null ? &depthTarget : null);
            if (pass == null) throw new InvalidOperationException($"SDL HUD scene pass failed: {SDL3.SDL_GetError()}");
            SdlGpuPassBindingCache passBindings = default;
            passBindings.Reset();
            SdlGpuTelemetryContext.RenderPass();
            try
            {
                SDL_GPUViewport viewport = new() { w = width, h = height, min_depth = 0, max_depth = 1 };
                SDL3.SDL_SetGPUViewport(pass, &viewport);
                SdlGpuSceneFragmentFrameConstants fragmentFrame
                    = SdlGpuSceneFragmentFrameConstants.Create(
                        Vector4.Zero, Vector4.Zero, Vector4.Zero, Vector3.Zero,
                        visualLights: null, enabled: false,
                        displayAssetsToLinear: displayAssetsToLinear);
                PushFragment(commandBuffer, 0, fragmentFrame);
                PushVertex(commandBuffer, 2, IdentityPalette());
                SDL_GPUTextureSamplerBinding* bindings
                    = stackalloc SDL_GPUTextureSamplerBinding[_sceneSamplerBindingCount];
                foreach (RenderHudSceneSubmission hud in frame.HudSceneItems)
                {
                    if (!meshes.TryGetValue(hud, out GpuMesh? mesh) || mesh.TriangleIndexCount == 0) continue;
                    RenderMaterial material = hud.Material;
                    PipelineKey baseKey = CreateHudPipelineKey(material,
                        targetFormat.ToString(), sampleCount);
                    BindPipeline(pass, ref passBindings,
                        GetPipeline(new ScenePipelineKey(baseKey,
                            frame.Options.Wireframe || material.Wireframe,
                            frame.Options.FaceCulling, targetFormat)));

                    SDL_GPUBufferBinding vertex = new() { buffer = mesh.VertexBuffer };
                    SDL_GPUBufferBinding index = new() { buffer = mesh.TriangleIndexBuffer };
                    BindVertexBuffer(pass, &vertex, 1);
                    BindIndexBuffer(pass, &index, SDL_GPUIndexElementSize.SDL_GPU_INDEXELEMENTSIZE_32BIT);
                    SDL_GPUTexture* texture = material.Textured
                        ? (SDL_GPUTexture*)ResolveTextureHandle(frame, hud.TextureIdentity,
                            $"HUD scene polygon {hud.PolygonId}")
                        : _whiteTexture!.Handle;
                    SDL_GPUSampler* sampler = GetSampler(new SamplerKey(
                        RenderFilterMode.Nearest, RepeatMode.Clamp, RepeatMode.Clamp));
                    bindings[SdlGpuSceneSamplerAbi.Albedo] = new() { texture = texture, sampler = sampler };
                    bindings[SdlGpuSceneSamplerAbi.Normal] = new() { texture = _flatNormalTexture!.Handle, sampler = sampler };
                    bindings[SdlGpuSceneSamplerAbi.Emissive] = new() { texture = _blackEmissiveTexture!.Handle, sampler = sampler };
                    bindings[SdlGpuSceneSamplerAbi.Reflection] = new() { texture = _blackReflectionTexture!.Handle, sampler = sampler };
                    bindings[SdlGpuSceneSamplerAbi.AmbientOcclusion] = new()
                    {
                        texture = _whiteTexture!.Handle, sampler = sampler
                    };
                    bindings[SdlGpuSceneSamplerAbi.Shadow] = new()
                    {
                        texture = _whiteTexture!.Handle, sampler = sampler
                    };
                    bindings[SdlGpuSceneSamplerAbi.SurfaceData] = new()
                    {
                        texture = _blackEmissiveTexture!.Handle, sampler = sampler
                    };
                    PadD3D12SceneSampler(bindings, sampler);
                    BindFragmentSamplers(pass, ref passBindings, 0, bindings,
                        checked((uint)_sceneSamplerBindingCount));

                    SdlGpuSceneVertexFrameConstants vertexFrame
                        = SdlGpuSceneVertexFrameConstants.Create(
                            hud.ViewMatrix, hud.ProjectionMatrix,
                            new Vector4(frame.Options.Lighting ? 1 : 0,
                                frame.Options.ShowColors ? 1 : 0,
                                frame.Options.ShowTextures ? 1 : 0, 0));
                    PushVertex(commandBuffer, 0, vertexFrame);
                    SceneDrawData constants = SplitDrawConstants(
                        BuildHudDrawConstants(frame, hud), hud.MatrixStack,
                        hud.MatrixStackCount);
                    PushVertex(commandBuffer, 1, constants.Vertex);
                    if (hud.MatrixStackCount > 0)
                        PushVertex(commandBuffer, 2, constants.Palette);
                    PushFragment(commandBuffer, 1, constants.Fragment);
                    DrawIndexed(pass, ref passBindings,
                        checked((uint)mesh.TriangleIndexCount));
                }
            }
            finally
            {
                SDL3.SDL_EndGPURenderPass(pass);
            }
        }

        private void EncodeBloom(SDL_GPUCommandBuffer* commandBuffer, RenderFrame frame,
            IReadOnlyDictionary<DrawSubmission, GpuMesh> meshes, SdlGpuBloomPlan plan,
            SdlGpuSceneVertexFrameConstants vertexFrame,
            SdlGpuSceneFragmentFrameConstants fragmentFrame)
        {
            if (!plan.Enabled || _bloomColor == null)
                throw new InvalidOperationException("SDL bloom pass requires allocated targets.");
            SDL_GPUTexture* renderColor = _bloomMultisampleColor != null
                ? _bloomMultisampleColor : _bloomColor;
            SDL_GPUColorTargetInfo colorTarget = new()
            {
                texture = renderColor,
                clear_color = new SDL_FColor { r = 0, g = 0, b = 0, a = 0 },
                load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR,
                store_op = plan.UsesResolve
                    ? SDL_GPUStoreOp.SDL_GPU_STOREOP_RESOLVE
                    : SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
                resolve_texture = plan.UsesResolve ? _bloomColor : null,
                cycle = false
            };
            SDL_GPUDepthStencilTargetInfo depthTarget = new()
            {
                texture = _sceneDepth,
                load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_LOAD,
                store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
                stencil_load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_LOAD,
                stencil_store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
                cycle = false
            };
            SDL_GPURenderPass* pass = SDL3.SDL_BeginGPURenderPass(commandBuffer,
                &colorTarget, 1, &depthTarget);
            if (pass == null)
                throw new InvalidOperationException($"SDL bloom emission pass failed: {SDL3.SDL_GetError()}");
            SdlGpuPassBindingCache passBindings = default;
            passBindings.Reset();
            SdlGpuTelemetryContext.RenderPass();
            try
            {
                SDL_GPUViewport viewport = new()
                {
                    w = _targetWidth, h = _targetHeight, min_depth = 0, max_depth = 1
                };
                SDL3.SDL_SetGPUViewport(pass, &viewport);
                PushVertex(commandBuffer, 0, vertexFrame);
                PushFragment(commandBuffer, 0, fragmentFrame);
                PushVertex(commandBuffer, 2, IdentityPalette());
                int emitted = 0;
                foreach (DrawSubmission draw in frame.Submissions)
                {
                    if (!SdlGpuBloomPlan.IsEligible(draw.Material,
                        frame.Options.Quality.GraphicsPreset)) continue;
                    if (++emitted > RenderFrame.DefaultMaximumCapacity)
                        throw new InvalidOperationException("Bloom draw count exceeds the bounded frame contract.");
                    if (!meshes.TryGetValue(draw, out GpuMesh? mesh))
                        throw new InvalidOperationException(
                            $"Bloom mesh was not resolved for polygon {draw.PolygonId}.");
                    DrawBloom(commandBuffer, pass, ref passBindings, frame, draw, mesh);
                }
            }
            finally
            {
                SDL3.SDL_EndGPURenderPass(pass);
            }
        }

        private void DrawBloom(SDL_GPUCommandBuffer* commandBuffer, SDL_GPURenderPass* pass,
            ref SdlGpuPassBindingCache passBindings, RenderFrame frame,
            DrawSubmission draw, GpuMesh mesh)
        {
            if (mesh.TriangleIndexCount == 0
                || (draw.Primitive == RenderPrimitive.Ngon && frame.Options.VolumeEdges == 1)) return;
            PipelineKey baseKey = new(RenderShaderVariant.Scene, RenderBlendMode.Opaque,
                draw.Material.CullingMode switch
                {
                    CullingMode.Front => RenderCullMode.Front,
                    CullingMode.Back => RenderCullMode.Back,
                    _ => RenderCullMode.None
                }, RenderDepthMode.LessOrEqual, depthWrite: false,
                RenderStencilMode.Disabled, RenderTopology.Triangles,
                _targetSampleCount, RenderAlphaTestMode.Disabled,
                RenderColorWriteMask.All, decalDepthBias: false,
                _sceneColorFormat.ToString());
            BindPipeline(pass, ref passBindings, GetPipeline(new ScenePipelineKey(
                baseKey, Wireframe: false, FaceCulling: frame.Options.FaceCulling,
                _sceneColorFormat, BloomEmission: true)));

            SDL_GPUBufferBinding vertex = new() { buffer = mesh.VertexBuffer };
            SDL_GPUBufferBinding index = new() { buffer = mesh.TriangleIndexBuffer };
            BindVertexBuffer(pass, &vertex, 1);
            BindIndexBuffer(pass, &index,
                SDL_GPUIndexElementSize.SDL_GPU_INDEXELEMENTSIZE_32BIT);
            GpuTexture texture = ResolveBoundTexture(frame, draw);
            GpuTexture normalTexture = ResolveBoundNormalTexture(frame, draw);
            GpuTexture emissiveTexture = ResolveBoundEmissiveTexture(frame, draw);
            SDL_GPUSampler* sampler = GetSampler(
                SdlGpuVisualStyleSampler.From(frame.Options, draw.Material));
            SDL_GPUTextureSamplerBinding* bindings
                = stackalloc SDL_GPUTextureSamplerBinding[_sceneSamplerBindingCount];
            bindings[SdlGpuSceneSamplerAbi.Albedo] = new() { texture = texture.Handle, sampler = sampler };
            bindings[SdlGpuSceneSamplerAbi.Normal] = new() { texture = normalTexture.Handle, sampler = sampler };
            bindings[SdlGpuSceneSamplerAbi.Emissive] = new() { texture = emissiveTexture.Handle, sampler = sampler };
            bindings[SdlGpuSceneSamplerAbi.Reflection] = new()
            {
                texture = ResolveBoundReflectionTexture(frame).Handle,
                sampler = GetSampler(new SamplerKey(RenderFilterMode.Linear,
                    RepeatMode.Clamp, RepeatMode.Clamp))
            };
            bindings[SdlGpuSceneSamplerAbi.AmbientOcclusion] = new()
            {
                texture = _whiteTexture!.Handle,
                sampler = GetSampler(new SamplerKey(RenderFilterMode.Linear,
                    RepeatMode.Clamp, RepeatMode.Clamp))
            };
            bindings[SdlGpuSceneSamplerAbi.Shadow] = new()
            {
                texture = _shadowAvailableForFrame ? _shadowDepth : _whiteTexture!.Handle,
                sampler = GetSampler(new SamplerKey(RenderFilterMode.Linear,
                    RepeatMode.Clamp, RepeatMode.Clamp))
            };
            bindings[SdlGpuSceneSamplerAbi.SurfaceData] = new()
            {
                texture = _surfaceAvailableForFrame ? _surfaceColor
                    : _blackEmissiveTexture!.Handle,
                sampler = GetSampler(new SamplerKey(RenderFilterMode.Linear,
                    RepeatMode.Clamp, RepeatMode.Clamp))
            };
            PadD3D12SceneSampler(bindings, sampler);
            BindFragmentSamplers(pass, ref passBindings, 0, bindings,
                checked((uint)_sceneSamplerBindingCount));

            SceneDrawData constants = BuildSceneDrawData(frame, draw, draw.Pass,
                RenderTopology.Triangles, SdlGpuBloomPlan.Strength(draw.Material,
                    frame.Options.Quality.GraphicsPreset));
            PushVertex(commandBuffer, 1, constants.Vertex);
            if (draw.MatrixStackCount > 0)
                PushVertex(commandBuffer, 2, constants.Palette);
            PushFragment(commandBuffer, 1, constants.Fragment);
            DrawIndexed(pass, ref passBindings,
                checked((uint)mesh.TriangleIndexCount));
        }

        internal static PipelineKey CreateHudPipelineKey(RenderMaterial material, string targetFormat,
            int sampleCount = 1)
            => new(RenderShaderVariant.Scene, RenderBlendMode.Alpha,
                material.CullingMode switch
                {
                    CullingMode.Front => RenderCullMode.Front,
                    CullingMode.Back => RenderCullMode.Back,
                    _ => RenderCullMode.None
                }, RenderDepthMode.Disabled, depthWrite: false,
                RenderStencilMode.Disabled, RenderTopology.Triangles, sampleCount,
                RenderAlphaTestMode.Disabled, RenderColorWriteMask.All,
                decalDepthBias: false, targetFormat);

        private static LegacyDrawConstants BuildHudDrawConstants(RenderFrame frame, RenderHudSceneSubmission hud)
        {
            RenderMaterial material = hud.Material;
            LegacyDrawConstants constants = default;
            constants.Transform = SdlGpuMatrixAbi.Upload(hud.Transform);
            constants.Billboard = SdlGpuMatrixAbi.Upload(Matrix4.Identity);
            constants.TextureMatrix = SdlGpuMatrixAbi.Upload(material.TextureMatrix);
            constants.Diffuse = hud.CurrentColor;
            constants.Ambient = new Vector4(material.Ambient, 1);
            constants.Specular = new Vector4(material.Specular,
                SdlGpuEnhancedLightingPolicy.DefaultSmoothness);
            constants.Emission = new Vector4(material.Emission, 1);
            constants.OverrideColor = material.ColorOverride ?? Vector4.One;
            constants.PaletteOverride = material.PaletteOverride ?? Vector4.One;
            constants.Light1Vector = new Vector4(hud.LightInfo.Light1Vector, 0);
            constants.Light1Color = new Vector4(hud.LightInfo.Light1Color, 1);
            constants.Light2Vector = new Vector4(hud.LightInfo.Light2Vector, 0);
            constants.Light2Color = new Vector4(hud.LightInfo.Light2Color, 1);
            constants.DrawOptions = new Vector4(material.Textured && frame.Options.ShowTextures ? 1 : 0,
                material.ColorOverride.HasValue ? 1 : 0, material.PaletteOverride.HasValue ? 1 : 0,
                hud.MatrixStackCount > 0 ? 1 : 0);
            constants.MaterialOptions = new Vector4(hud.Alpha, (float)material.PolygonMode,
                (float)material.TexgenMode, material.Lighting ? 1 : 0);
            constants.RenderOptions = Vector4.Zero;
            constants.FlatColor = Vector4.One;
            constants.EnhancedEmission = Vector4.Zero;
            constants.EnhancedOptions = Vector4.Zero;
            constants.ReflectionOptions = Vector4.Zero;
            float* destination = constants.MatrixStack;
            SdlGpuMatrixAbi.CopyStack(hud.MatrixStack, hud.MatrixStackCount,
                new Span<float>(destination, RenderFrame.MatrixStackFloats));
            return constants;
        }

        private void Draw(SDL_GPUCommandBuffer* commandBuffer, SDL_GPURenderPass* pass,
            ref SdlGpuPassBindingCache passBindings, RenderFrame frame,
            DrawSubmission draw, GpuMesh mesh, RenderPassKind passKind,
            RenderTopology topology)
        {
            int indexCount = topology == RenderTopology.Lines ? mesh.LineIndexCount : mesh.TriangleIndexCount;
            if (indexCount == 0 || (draw.Primitive == RenderPrimitive.Ngon && topology == RenderTopology.Triangles
                && frame.Options.VolumeEdges == 1)) return;

            bool wireframe = topology == RenderTopology.Triangles && (frame.Options.Wireframe || draw.Material.Wireframe);
            bool depthStencilPass = passKind is
                RenderPassKind.TransparentStencil or RenderPassKind.DepthRebuild;
            bool fullCoverageShader = depthStencilPass
                && RequiresFullAlphaCoverageShader(frame, draw, passKind);
            bool specializedDepthStencil = depthStencilPass
                && !fullCoverageShader;
            PipelineKey baseKey = PipelineKey.From(draw.Material, draw.Primitive, passKind,
                _targetSampleCount,
                _sceneColorFormat.ToString()).WithTopology(topology);
            ScenePipelineKey key = new(baseKey, wireframe,
                frame.Options.FaceCulling, _sceneColorFormat,
                FullCoverageShader: fullCoverageShader);
            SDL_GPUGraphicsPipeline* pipeline = GetPipeline(key);
            BindPipeline(pass, ref passBindings, pipeline);
            SDL3.SDL_SetGPUStencilReference(pass, checked((byte)Math.Clamp(draw.PolygonId, 0, 255)));

            SDL_GPUBufferBinding vertex = new() { buffer = mesh.VertexBuffer, offset = 0 };
            SDL_GPUBufferBinding index = new()
            {
                buffer = topology == RenderTopology.Lines ? mesh.LineIndexBuffer : mesh.TriangleIndexBuffer,
                offset = 0
            };
            BindVertexBuffer(pass, &vertex, 1);
            BindIndexBuffer(pass, &index, SDL_GPUIndexElementSize.SDL_GPU_INDEXELEMENTSIZE_32BIT);

            GpuTexture texture = ResolveBoundTexture(frame, draw);
            SDL_GPUSampler* sampler = GetSampler(
                SdlGpuVisualStyleSampler.From(frame.Options, draw.Material));
            if (specializedDepthStencil)
            {
                int bindingCount = SamplerBindingCount(1);
                SDL_GPUTextureSamplerBinding* bindings
                    = stackalloc SDL_GPUTextureSamplerBinding[bindingCount];
                bindings[0] = new()
                    { texture = texture.Handle, sampler = sampler };
                SdlGpuSamplerBindingAbi.Pad(bindings, 1, bindingCount,
                    _whiteTexture!.Handle, sampler);
                BindFragmentSamplers(pass, ref passBindings, 0, bindings,
                    checked((uint)bindingCount));
            }
            else
            {
                GpuTexture normalTexture = ResolveBoundNormalTexture(frame, draw);
                GpuTexture emissiveTexture = ResolveBoundEmissiveTexture(frame, draw);
                SDL_GPUTextureSamplerBinding* textureBindings
                    = stackalloc SDL_GPUTextureSamplerBinding[_sceneSamplerBindingCount];
                textureBindings[SdlGpuSceneSamplerAbi.Albedo] = new() { texture = texture.Handle, sampler = sampler };
                textureBindings[SdlGpuSceneSamplerAbi.Normal] = new() { texture = normalTexture.Handle, sampler = sampler };
                textureBindings[SdlGpuSceneSamplerAbi.Emissive] = new() { texture = emissiveTexture.Handle, sampler = sampler };
                textureBindings[SdlGpuSceneSamplerAbi.Reflection] = new()
                {
                    texture = ResolveBoundReflectionTexture(frame).Handle,
                    sampler = GetSampler(new SamplerKey(RenderFilterMode.Linear,
                        RepeatMode.Clamp, RepeatMode.Clamp))
                };
                textureBindings[SdlGpuSceneSamplerAbi.AmbientOcclusion] = new()
                {
                    texture = SdlGpuAmbientOcclusionPolicy.UsesForPass(passKind)
                        && _ambientOcclusionForFrame != null
                        ? _ambientOcclusionForFrame : _whiteTexture!.Handle,
                    sampler = GetSampler(new SamplerKey(RenderFilterMode.Linear,
                        RepeatMode.Clamp, RepeatMode.Clamp))
                };
                textureBindings[SdlGpuSceneSamplerAbi.Shadow] = new()
                {
                    texture = _shadowAvailableForFrame ? _shadowDepth : _whiteTexture!.Handle,
                    sampler = GetSampler(new SamplerKey(RenderFilterMode.Linear,
                        RepeatMode.Clamp, RepeatMode.Clamp))
                };
                textureBindings[SdlGpuSceneSamplerAbi.SurfaceData] = new()
                {
                    texture = _surfaceAvailableForFrame ? _surfaceColor
                        : _blackEmissiveTexture!.Handle,
                    sampler = GetSampler(new SamplerKey(RenderFilterMode.Linear,
                        RepeatMode.Clamp, RepeatMode.Clamp))
                };
                PadD3D12SceneSampler(textureBindings, sampler);
                BindFragmentSamplers(pass, ref passBindings, 0, textureBindings,
                    checked((uint)_sceneSamplerBindingCount));
            }

            SceneDrawData constants = BuildSceneDrawData(frame, draw, passKind, topology);
            PushVertex(commandBuffer, 1, constants.Vertex);
            if (draw.MatrixStackCount > 0)
                PushVertex(commandBuffer, 2, constants.Palette);
            if (specializedDepthStencil)
            {
                bool textured = draw.Material.Textured
                    && frame.Options.ShowTextures;
                float alpha = textured
                    ? draw.Material.Alpha
                        * (draw.Material.ColorOverride?.W ?? 1)
                    : draw.Material.ColorOverride?.W
                        ?? draw.Material.Alpha;
                PushFragment(commandBuffer, 0, new AlphaConstants
                {
                    Options = new Vector4(
                        textured ? 1 : 0,
                        baseKey.AlphaTestMode == RenderAlphaTestMode.EqualOne
                            ? 1 : 2,
                        alpha, 0)
                });
            }
            else PushFragment(commandBuffer, 1, constants.Fragment);
            DrawIndexed(pass, ref passBindings, checked((uint)indexCount));
        }

        internal static bool RequiresFullAlphaCoverageShader(RenderFrame frame,
            DrawSubmission draw, RenderPassKind passKind)
        {
            if (frame.Options.Quality.GraphicsPreset != GraphicsPreset.Enhanced)
                return false;
            if (draw.Material.EnhancedBeam.HasValue) return true;
            return passKind == RenderPassKind.TransparentStencil
                && draw.Primitive == RenderPrimitive.Particle
                && draw.SoftParticleProfile.HasValue;
        }

        private void PadD3D12SceneSampler(SDL_GPUTextureSamplerBinding* bindings,
            SDL_GPUSampler* sampler)
        {
            SdlGpuSamplerBindingAbi.Pad(bindings, SdlGpuSceneSamplerAbi.Count,
                _sceneSamplerBindingCount, _whiteTexture!.Handle, sampler);
        }

        private static void BindPipeline(SDL_GPURenderPass* pass,
            ref SdlGpuPassBindingCache passBindings,
            SDL_GPUGraphicsPipeline* pipeline)
        {
            if (!passBindings.ShouldBindPipeline(pipeline)) return;
            long nativeStart = SdlGpuTelemetryContext.BeginNativeCall();
            SDL3.SDL_BindGPUGraphicsPipeline(pass, pipeline);
            long nativeTicks = SdlGpuTelemetryContext.EndNativeCall(nativeStart);
            SdlGpuTelemetryContext.PipelineBindNative(nativeTicks);
        }

        private static void BindFragmentSamplers(SDL_GPURenderPass* pass,
            ref SdlGpuPassBindingCache passBindings, uint firstSlot,
            SDL_GPUTextureSamplerBinding* bindings, uint count)
        {
            // All current scene shaders bind at slot zero; the cache keeps the
            // first slot explicit so future multi-slot shaders remain correct.
            if (!passBindings.ShouldBindFragmentSamplers(firstSlot, count,
                bindings)) return;
            long nativeStart = SdlGpuTelemetryContext.BeginNativeCall();
            SDL3.SDL_BindGPUFragmentSamplers(pass, firstSlot, bindings, count);
            long nativeTicks = SdlGpuTelemetryContext.EndNativeCall(nativeStart);
            SdlGpuTelemetryContext.SamplerBindNative(checked((int)count), nativeTicks);
        }

        private static void BindVertexBuffer(SDL_GPURenderPass* pass,
            SDL_GPUBufferBinding* binding, uint count)
        {
            long nativeStart = SdlGpuTelemetryContext.BeginNativeCall();
            SDL3.SDL_BindGPUVertexBuffers(pass, 0, binding, count);
            long nativeTicks = SdlGpuTelemetryContext.EndNativeCall(nativeStart);
            SdlGpuTelemetryContext.VertexBufferBindNative(nativeTicks);
        }

        private static void BindIndexBuffer(SDL_GPURenderPass* pass,
            SDL_GPUBufferBinding* binding, SDL_GPUIndexElementSize elementSize)
        {
            long nativeStart = SdlGpuTelemetryContext.BeginNativeCall();
            SDL3.SDL_BindGPUIndexBuffer(pass, binding, elementSize);
            long nativeTicks = SdlGpuTelemetryContext.EndNativeCall(nativeStart);
            SdlGpuTelemetryContext.IndexBufferBindNative(nativeTicks);
        }

        private static void DrawIndexed(SDL_GPURenderPass* pass,
            ref SdlGpuPassBindingCache passBindings, uint indexCount)
        {
            long nativeStart = SdlGpuTelemetryContext.BeginNativeCall();
            SDL3.SDL_DrawGPUIndexedPrimitives(pass, indexCount, 1, 0, 0, 0);
            long nativeTicks = SdlGpuTelemetryContext.EndNativeCall(nativeStart);
            bool descriptorBearing = passBindings.ConsumeDescriptorBearing();
            SdlGpuTelemetryContext.IndexedDrawNative(nativeTicks, descriptorBearing);
        }

        private int SamplerBindingCount(int shaderSamplerCount)
            => SdlGpuSamplerBindingAbi.BindingCountForDriver(_device.Driver,
                shaderSamplerCount);

        private LegacyDrawConstants BuildLegacyDrawConstants(RenderFrame frame, DrawSubmission draw,
            RenderPassKind passKind, RenderTopology topology, float bloomStrength = 0)
        {
            LegacyDrawConstants constants = default;
            constants.Transform = SdlGpuMatrixAbi.Upload(draw.Transform);
            constants.Billboard = draw.Material.BillboardMode switch
            {
                BillboardMode.Sphere => SdlGpuMatrixAbi.Upload(frame.ViewInverseRotation),
                BillboardMode.Cylinder => SdlGpuMatrixAbi.Upload(frame.ViewInverseRotationY),
                _ => SdlGpuMatrixAbi.Upload(Matrix4.Identity)
            };
            constants.TextureMatrix = SdlGpuMatrixAbi.Upload(draw.Material.TextureMatrix);
            constants.Diffuse = new Vector4(draw.Material.Diffuse, 1);
            constants.Ambient = new Vector4(draw.Material.Ambient, 1);
            EnhancedMaterial? enhanced = frame.Options.Quality.GraphicsPreset
                == GraphicsPreset.Enhanced ? draw.Material.Enhanced : null;
            constants.Specular = new Vector4(draw.Material.Specular,
                enhanced?.Smoothness
                    ?? SdlGpuEnhancedLightingPolicy.DefaultSmoothness);
            constants.Emission = new Vector4(draw.Material.Emission, 1);
            constants.EnhancedEmission = enhanced.HasValue
                ? new Vector4(enhanced.Value.EmissionTint,
                    enhanced.Value.EmissionStrength)
                : Vector4.Zero;
            constants.EnhancedOptions = new Vector4(
                enhanced?.Emissive is TextureIdentity ? 1 : 0,
                draw.Material.EnhancedBeam.HasValue ? 1 : 0,
                draw.Material.EnhancedForceField.HasValue ? 1 : 0,
                _surfaceAvailableForFrame ? 1 : 0);
            if (frame.Options.Quality.GraphicsPreset == GraphicsPreset.Enhanced
                && draw.Material.EnhancedBeam is EnhancedBeamDrawState beam)
            {
                BeamVisualProfile beamProfile = beam.Profile;
                constants.BeamCore = new Vector4(beamProfile.CoreColor,
                    beamProfile.CoreWidth);
                constants.BeamGlow = new Vector4(beamProfile.GlowColor,
                    beamProfile.GlowWidth);
                constants.BeamOptions = new Vector4(beamProfile.NoiseStrength,
                    beamProfile.NoiseScale, beam.Sample.NoisePhase,
                    beam.Sample.Pulse);
            }
            if (frame.Options.Quality.GraphicsPreset == GraphicsPreset.Enhanced
                && draw.Material.EnhancedForceField
                    is EnhancedForceFieldDrawState forceField)
            {
                ForceFieldVisualProfile fieldProfile = forceField.Profile;
                constants.ForceFieldEmission = new Vector4(
                    fieldProfile.EmissionColor, fieldProfile.EmissionStrength);
                constants.ForceFieldOptions = new Vector4(fieldProfile.NoiseScale,
                    fieldProfile.NoiseStrength, forceField.Sample.NoisePhase,
                    fieldProfile.FresnelPower);
                constants.ForceFieldFlow = new Vector4(
                    forceField.Sample.UvOffset.X,
                    forceField.Sample.UvOffset.Y,
                    fieldProfile.FresnelStrength, fieldProfile.IntersectionStrength);
            }
            ReflectionSamplingPolicy reflection = enhanced.HasValue
                && IsReflectionAvailable(frame)
                ? ReflectionSamplingPolicy.FromMaterial(enhanced.Value,
                    frame.ReflectionProbe!.MipLevelCount)
                : default;
            constants.ReflectionOptions = reflection.Enabled
                ? new Vector4(1, reflection.Strength, reflection.Smoothness,
                    reflection.MipLevel)
                : Vector4.Zero;
            constants.SoftParticleOptions = frame.Options.Quality.GraphicsPreset
                    == GraphicsPreset.Enhanced
                && _surfaceAvailableForFrame
                && draw.Primitive == RenderPrimitive.Particle
                && passKind is RenderPassKind.TransparentStencil
                    or RenderPassKind.TransparentBehind
                    or RenderPassKind.TransparentFront
                && draw.SoftParticleProfile is SoftParticleProfile profile
                ? new Vector4(1, profile.FadeDistance, 0, 0)
                : Vector4.Zero;
            constants.OverrideColor = draw.Material.ColorOverride ?? Vector4.One;
            constants.PaletteOverride = draw.Material.PaletteOverride ?? Vector4.One;
            constants.Light1Vector = new Vector4(draw.LightInfo.Light1Vector, 0);
            constants.Light1Color = new Vector4(draw.LightInfo.Light1Color, 1);
            constants.Light2Vector = new Vector4(draw.LightInfo.Light2Vector, 0);
            constants.Light2Color = new Vector4(draw.LightInfo.Light2Color, 1);
            constants.DrawOptions = new Vector4(draw.Material.Textured && frame.Options.ShowTextures ? 1 : 0,
                draw.Material.ColorOverride.HasValue ? 1 : 0,
                draw.Material.PaletteOverride.HasValue ? 1 : 0,
                draw.MatrixStackCount > 0 ? 1 : 0);
            if (draw.Primitive == RenderPrimitive.Ngon && topology == RenderTopology.Lines)
            {
                constants.OverrideColor = draw.FrozenEdgeColor;
                constants.DrawOptions.Y = 1;
            }
            constants.MaterialOptions = new Vector4(draw.Material.Alpha, (float)draw.Material.PolygonMode,
                (float)draw.Material.TexgenMode, draw.Material.Lighting ? 1 : 0);
            constants.RenderOptions = new Vector4(passKind switch
            {
                RenderPassKind.Opaque or RenderPassKind.DepthRebuild => 1,
                RenderPassKind.TransparentStencil or RenderPassKind.TransparentBehind or RenderPassKind.TransparentFront => 2,
                _ => 0
            }, 0, bloomStrength,
                SdlGpuNormalMappingPolicy.IsEnabled(
                    frame.Options.Quality.GraphicsPreset, frame.Options.Lighting,
                    frame.Options.ShowTextures, draw.Material, topology) ? 1 : 0);
            if (SdlGpuCelSurface.TryGetFlatColor(frame.Options, frame.TextureResources,
                draw.Material, out Vector3 flatColor))
            {
                constants.RenderOptions.Y = 1;
                constants.FlatColor = new Vector4(flatColor, 1);
            }
            float* destination = constants.MatrixStack;
            SdlGpuMatrixAbi.CopyStack(draw.MatrixStack, draw.MatrixStackCount,
                new Span<float>(destination, RenderFrame.MatrixStackFloats));
            return constants;
        }

        private SceneDrawData BuildSceneDrawData(RenderFrame frame,
            DrawSubmission draw, RenderPassKind passKind, RenderTopology topology,
            float bloomStrength = 0)
            => SplitDrawConstants(BuildLegacyDrawConstants(frame, draw, passKind,
                topology, bloomStrength), draw.MatrixStack, draw.MatrixStackCount);

        private static unsafe SceneDrawData SplitDrawConstants(
            LegacyDrawConstants source, IReadOnlyList<float> matrixStack,
            int matrixStackCount)
        {
            SceneDrawData result = default;
            result.Vertex.Transform = source.Transform;
            result.Vertex.Billboard = source.Billboard;
            result.Vertex.TextureMatrix = source.TextureMatrix;
            result.Vertex.Diffuse = source.Diffuse;
            result.Vertex.Ambient = source.Ambient;
            result.Vertex.Specular = source.Specular;
            result.Vertex.Emission = source.Emission;
            result.Vertex.Light1Vector = source.Light1Vector;
            result.Vertex.Light1Color = source.Light1Color;
            result.Vertex.Light2Vector = source.Light2Vector;
            result.Vertex.Light2Color = source.Light2Color;
            result.Vertex.DrawOptions = source.DrawOptions;
            result.Vertex.MaterialOptions = source.MaterialOptions;

            result.Fragment.TextureMatrix = source.TextureMatrix;
            result.Fragment.Diffuse = source.Diffuse;
            result.Fragment.Specular = source.Specular;
            result.Fragment.Emission = source.Emission;
            result.Fragment.OverrideColor = source.OverrideColor;
            result.Fragment.PaletteOverride = source.PaletteOverride;
            result.Fragment.Light1Vector = source.Light1Vector;
            result.Fragment.Light1Color = source.Light1Color;
            result.Fragment.Light2Vector = source.Light2Vector;
            result.Fragment.Light2Color = source.Light2Color;
            result.Fragment.DrawOptions = source.DrawOptions;
            result.Fragment.MaterialOptions = source.MaterialOptions;
            result.Fragment.RenderOptions = source.RenderOptions;
            result.Fragment.FlatColor = source.FlatColor;
            result.Fragment.EnhancedEmission = source.EnhancedEmission;
            result.Fragment.EnhancedOptions = source.EnhancedOptions;
            result.Fragment.ReflectionOptions = source.ReflectionOptions;
            result.Fragment.SoftParticleOptions = source.SoftParticleOptions;
            result.Fragment.BeamCore = source.BeamCore;
            result.Fragment.BeamGlow = source.BeamGlow;
            result.Fragment.BeamOptions = source.BeamOptions;
            result.Fragment.ForceFieldEmission = source.ForceFieldEmission;
            result.Fragment.ForceFieldOptions = source.ForceFieldOptions;
            result.Fragment.ForceFieldFlow = source.ForceFieldFlow;

            float* destination = result.Palette.MatrixStack;
            if (matrixStackCount > 0)
            {
                SdlGpuMatrixAbi.CopyStack(matrixStack, matrixStackCount,
                    new Span<float>(destination, RenderFrame.MatrixStackFloats));
            }
            return result;
        }

        private bool TryResolveMesh(RenderFrame frame, DrawSubmission draw, SDL_GPUCommandBuffer* commandBuffer,
            out GpuMesh? result)
        {
            result = null;
            object? identity = draw.Primitive == RenderPrimitive.Mesh ? draw.GeometryIdentity : draw;
            if (identity == null || !frame.MeshResources.TryGetValue(identity, out CpuMesh? mesh)) return false;
            result = ResolveGpuMesh(identity, mesh, draw.Primitive == RenderPrimitive.Mesh, commandBuffer);
            return true;
        }

        private GpuMesh ResolveGpuMesh(object identity, CpuMesh mesh, bool isStatic,
            SDL_GPUCommandBuffer* commandBuffer)
        {
            GpuMesh result;
            if (isStatic)
            {
                if (!_staticMeshes.TryGetValue(identity, out result!))
                {
                    result = GpuMesh.Create(_device, mesh);
                    _staticMeshes.Add(identity, result);
                }
                _staticMeshLastUsed[identity] = _frameSerial;
            }
            else
            {
                int slotIndex = _device.FrameResources.CurrentSlotIndex;
                List<GpuMesh> slot = _dynamicSlots[slotIndex];
                int resourceIndex = _dynamicSlotUsed[slotIndex]++;
                if (resourceIndex == slot.Count)
                {
                    result = GpuMesh.Create(_device, mesh);
                    slot.Add(result);
                }
                else if (slot[resourceIndex].CanHold(mesh))
                {
                    result = slot[resourceIndex];
                    result.Reset(mesh);
                }
                else
                {
                    GpuMesh replacement = GpuMesh.Create(_device, mesh);
                    slot[resourceIndex].Dispose();
                    slot[resourceIndex] = replacement;
                    result = replacement;
                }
            }
            if (result.EnsureUploaded(commandBuffer))
            {
                if (isStatic) TrackUpload(result);
                else SdlGpuTelemetryContext.UploadScheduled(result.UploadBytes);
            }
            return result;
        }

        private void ResolveTexture(RenderFrame frame, DrawSubmission draw, SDL_GPUCommandBuffer* commandBuffer)
            => ResolveTexture(frame, draw.Material.Textured,
                ResolveAlbedoIdentity(frame, draw.Material), draw.PolygonId,
                commandBuffer);

        private void ResolveNormalTexture(RenderFrame frame, DrawSubmission draw,
            SDL_GPUCommandBuffer* commandBuffer)
        {
            if (frame.Options.Quality.GraphicsPreset == GraphicsPreset.Enhanced
                && draw.Material.Enhanced?.Normal is TextureIdentity normal)
            {
                ResolveTexture(frame, textured: true, normal, draw.PolygonId,
                    commandBuffer);
            }
        }

        private void ResolveEmissiveTexture(RenderFrame frame, DrawSubmission draw,
            SDL_GPUCommandBuffer* commandBuffer)
        {
            if (frame.Options.Quality.GraphicsPreset == GraphicsPreset.Enhanced
                && draw.Material.Enhanced?.Emissive is TextureIdentity emissive)
            {
                ResolveTexture(frame, textured: true, emissive, draw.PolygonId,
                    commandBuffer);
            }
        }

        private void ResolveReflectionTexture(RenderFrame frame,
            SDL_GPUCommandBuffer* commandBuffer)
        {
            if (frame.Options.Quality.GraphicsPreset != GraphicsPreset.Enhanced
                || frame.ReflectionProbe is not RenderReflectionProbe probe)
            {
                return;
            }
            SdlGpuReflectionResourceConfiguration configuration
                = SdlGpuReflectionResourceConfiguration.From(probe);
            if (!_reflectionFailure.ShouldAttempt(configuration)) return;

            GpuCubeTexture? previous = _reflectionTexture;
            GpuCubeTexture candidate = previous!;
            bool replacement = previous == null || !previous.Matches(probe);
            try
            {
                if (replacement)
                    candidate = GpuCubeTexture.Create(_device, probe);
                if (candidate.EnsureUploaded(commandBuffer)) TrackUpload(candidate);
            }
            catch (ReflectionProbeResourceUnavailableException error)
            {
                if (replacement) candidate?.Dispose();
                if (_reflectionFailure.RecordFailure(configuration))
                {
                    Console.WriteLine($"[render] reflection probe '{probe.Key}' "
                        + $"is unavailable; using black fallback ({error.Message}).");
                }
                return;
            }
            catch
            {
                // A copy-pass/command-buffer failure is not probe-local and
                // must retain the backend's existing fatal-submit behavior.
                if (replacement) candidate?.Dispose();
                throw;
            }

            if (replacement)
            {
                if (previous != null)
                {
                    // Only one room cube is retained. The previous handle is
                    // released after this frame slot's fence completes.
                    _retiredCubeTextureSlots[_device.FrameResources.CurrentSlotIndex]
                        .Add(previous);
                }
                _reflectionTexture = candidate;
            }
            _reflectionFailure.RecordSuccess(configuration);
        }

        private static TextureIdentity? ResolveAlbedoIdentity(RenderFrame frame,
            RenderMaterial material)
            => SdlGpuCelSurface.UsesEnhancedTextures(frame.Options)
                && material.Enhanced?.Albedo is TextureIdentity enhanced
                    ? enhanced : material.Texture;

        private void ResolveTexture(RenderFrame frame, bool textured, TextureIdentity? requested,
            int polygonId, SDL_GPUCommandBuffer* commandBuffer, bool? mipmapped = null)
        {
            if (!textured) return;
            if (requested is not TextureIdentity identity)
                throw new InvalidOperationException($"Textured scene submission polygon {polygonId} has no texture identity.");
            if (!frame.TextureResources.TryGetValue(identity, out RenderTexturePixels? pixels))
                throw new InvalidOperationException($"Sealed scene frame is missing texture pixels for {identity}.");
            bool generateMipmaps = mipmapped ?? frame.Options.Quality.UsesMipmaps;
            if (!_textures.TryGetValue(identity, out GpuTexture? texture)
                || !texture.Matches(pixels, generateMipmaps))
            {
                GpuTexture replacement = GpuTexture.Create(_device, pixels,
                    generateMipmaps);
                if (texture != null)
                {
                    // Keep the replaced generation alive through the fence
                    // of the command buffer that establishes the replacement.
                    // Queue ordering then proves every earlier reader is done.
                    _retiredTextureSlots[_device.FrameResources.CurrentSlotIndex].Add(texture);
                }
                texture = replacement;
                _textures[identity] = texture;
            }
            if (texture.EnsureUploaded(commandBuffer)) TrackUpload(texture);
            _textureLastUsed[identity] = _frameSerial;
        }

        private void PrepareUnmippedSkyTexture(RenderFrame frame,
            TextureIdentity identity)
        {
            if (!frame.TextureResources.TryGetValue(identity,
                    out RenderTexturePixels? pixels))
            {
                throw new InvalidOperationException(
                    $"Sealed scene frame is missing enhanced sky pixels for {identity}.");
            }
            if (!_textures.TryGetValue(identity, out GpuTexture? texture)
                || !texture.Matches(pixels, mipmapped: false))
            {
                GpuTexture replacement = GpuTexture.Create(_device, pixels,
                    mipmapped: false);
                if (texture != null)
                {
                    _retiredTextureSlots[_device.FrameResources.CurrentSlotIndex]
                        .Add(texture);
                }
                texture = replacement;
                _textures[identity] = texture;
            }
            texture.PrepareUploadStorage();
            _textureLastUsed[identity] = _frameSerial;
        }

        private void EncodeUnmippedSkyTextureUpload(RenderFrame frame,
            TextureIdentity identity, SDL_GPUCommandBuffer* commandBuffer)
        {
            if (!frame.TextureResources.TryGetValue(identity,
                    out RenderTexturePixels? pixels)
                || !_textures.TryGetValue(identity, out GpuTexture? texture)
                || !texture.Matches(pixels, mipmapped: false))
            {
                throw new InvalidOperationException(
                    $"Enhanced sky texture preparation was not retained for {identity}.");
            }
            if (texture.EncodePreparedUpload(commandBuffer)) TrackUpload(texture);
        }

        private GpuTexture ResolveBoundTexture(RenderFrame frame, DrawSubmission draw)
        {
            TextureIdentity? requested = ResolveAlbedoIdentity(frame, draw.Material);
            if (draw.Material.Textured && requested is TextureIdentity identity
                && frame.TextureResources.TryGetValue(identity, out RenderTexturePixels? pixels)
                && _textures.TryGetValue(identity, out GpuTexture? texture)
                && texture.Matches(pixels, frame.Options.Quality.UsesMipmaps))
            {
                return texture;
            }
            if (draw.Material.Textured)
                throw new InvalidOperationException($"Scene texture upload was not resolved for polygon {draw.PolygonId}.");
            return _whiteTexture!;
        }

        private GpuTexture ResolveBoundNormalTexture(RenderFrame frame,
            DrawSubmission draw)
        {
            if (frame.Options.Quality.GraphicsPreset != GraphicsPreset.Enhanced
                || draw.Material.Enhanced?.Normal is not TextureIdentity identity)
            {
                return _flatNormalTexture!;
            }
            if (frame.TextureResources.TryGetValue(identity,
                    out RenderTexturePixels? pixels)
                && _textures.TryGetValue(identity, out GpuTexture? texture)
                && texture.Matches(pixels, frame.Options.Quality.UsesMipmaps))
            {
                return texture;
            }
            throw new InvalidOperationException(
                $"Scene normal texture upload was not resolved for polygon {draw.PolygonId}.");
        }

        private GpuTexture ResolveBoundEmissiveTexture(RenderFrame frame,
            DrawSubmission draw)
        {
            if (frame.Options.Quality.GraphicsPreset != GraphicsPreset.Enhanced
                || draw.Material.Enhanced?.Emissive is not TextureIdentity identity)
            {
                return _blackEmissiveTexture!;
            }
            if (frame.TextureResources.TryGetValue(identity,
                    out RenderTexturePixels? pixels)
                && _textures.TryGetValue(identity, out GpuTexture? texture)
                && texture.Matches(pixels, frame.Options.Quality.UsesMipmaps))
            {
                return texture;
            }
            throw new InvalidOperationException(
                $"Scene emissive texture upload was not resolved for polygon {draw.PolygonId}.");
        }

        private GpuCubeTexture ResolveBoundReflectionTexture(RenderFrame frame)
        {
            if (frame.Options.Quality.GraphicsPreset == GraphicsPreset.Enhanced
                && frame.ReflectionProbe is RenderReflectionProbe probe)
            {
                if (IsReflectionAvailable(frame))
                    return _reflectionTexture!;
            }
            return _blackReflectionTexture!;
        }

        private bool IsReflectionAvailable(RenderFrame frame)
            => frame.Options.Quality.GraphicsPreset == GraphicsPreset.Enhanced
                && frame.ReflectionProbe is RenderReflectionProbe probe
                && _reflectionFailure.ShouldAttempt(
                    SdlGpuReflectionResourceConfiguration.From(probe))
                && _reflectionTexture != null
                && _reflectionTexture.IsUploaded
                && _reflectionTexture.Matches(probe);

        internal nint ResolveTextureHandle(RenderFrame frame, TextureIdentity? requested, string label)
        {
            if (requested is not TextureIdentity identity)
                throw new InvalidOperationException($"Textured {label} has no texture identity.");
            if (!frame.TextureResources.TryGetValue(identity, out RenderTexturePixels? pixels))
                throw new InvalidOperationException($"Sealed scene frame is missing texture pixels for {label} ({identity}).");
            if (!_textures.TryGetValue(identity, out GpuTexture? texture)
                || !texture.Matches(pixels, frame.Options.Quality.UsesMipmaps))
                throw new InvalidOperationException($"Texture upload was not resolved for {label} ({identity}).");
            return (nint)texture.Handle;
        }

        internal nint ResolveUnmippedTextureHandle(RenderFrame frame,
            TextureIdentity? requested, string label)
        {
            if (requested is not TextureIdentity identity)
                throw new InvalidOperationException($"Textured {label} has no texture identity.");
            if (!frame.TextureResources.TryGetValue(identity, out RenderTexturePixels? pixels))
                throw new InvalidOperationException($"Sealed scene frame is missing texture pixels for {label} ({identity}).");
            if (!_textures.TryGetValue(identity, out GpuTexture? texture)
                || !texture.Matches(pixels, mipmapped: false))
                throw new InvalidOperationException($"Unmipped texture upload was not resolved for {label} ({identity}).");
            return (nint)texture.Handle;
        }

        internal nint WhiteTextureHandle => (nint)_whiteTexture!.Handle;
        internal bool DepthSampleable => _depthSampleable
            && _targetSampleCount == 1;
        internal nint NearestClampSamplerHandle => (nint)GetSampler(new SamplerKey(
            RenderFilterMode.Nearest, RepeatMode.Clamp, RepeatMode.Clamp));
        internal nint LinearClampSamplerHandle => (nint)GetSampler(new SamplerKey(
            RenderFilterMode.Linear, RepeatMode.Clamp, RepeatMode.Clamp));
        internal nint SkySamplerHandle => (nint)GetSampler(new SamplerKey(
            RenderFilterMode.Linear, RepeatMode.Clamp, RepeatMode.Clamp));

        private void ResetDynamicSlot()
        {
            int slotIndex = _device.FrameResources.CurrentSlotIndex;
            foreach (IUploadResource resource in _uploadSlots[_device.FrameResources.CurrentSlotIndex])
                resource.ReleaseUploadStorage();
            _uploadSlots[_device.FrameResources.CurrentSlotIndex].Clear();
            _dynamicSlotUsed[slotIndex] = 0;
            foreach (GpuTexture texture in _retiredTextureSlots[_device.FrameResources.CurrentSlotIndex]) texture.Dispose();
            _retiredTextureSlots[_device.FrameResources.CurrentSlotIndex].Clear();
            foreach (GpuCubeTexture texture in _retiredCubeTextureSlots[slotIndex])
                texture.Dispose();
            _retiredCubeTextureSlots[slotIndex].Clear();
            foreach (GpuMesh mesh in _retiredMeshSlots[slotIndex]) mesh.Dispose();
            _retiredMeshSlots[slotIndex].Clear();
        }

        private void RetireUnusedStaticMeshes()
        {
            // A hidden/unloaded model gets a short grace period so ordinary
            // visibility changes do not churn uploads. Expired handles are
            // released only when the current frame-slot fence completes.
            const int graceFrames = 60;
            if (_staticMeshes.Count == 0) return;
            _expiredStaticMeshes.Clear();
            foreach (KeyValuePair<object, long> entry in _staticMeshLastUsed)
                if (_frameSerial - entry.Value > graceFrames) _expiredStaticMeshes.Add(entry.Key);
            foreach (object identity in _expiredStaticMeshes)
            {
                _retiredMeshSlots[_device.FrameResources.CurrentSlotIndex].Add(_staticMeshes[identity]);
                _staticMeshes.Remove(identity);
                _staticMeshLastUsed.Remove(identity);
            }
        }

        private void RetireUnusedTextures()
        {
            const int graceFrames = 60;
            _expiredTextures.Clear();
            foreach (KeyValuePair<TextureIdentity, long> entry in _textureLastUsed)
            {
                // Value-type variants are immutable per-revision snapshots
                // used by rewritten HUD surfaces. Keep all revisions needed
                // by this frame distinct, then retire them through a fenced
                // frame slot as soon as a later frame stops referencing them.
                int identityGrace = entry.Key.Variant?.GetType().IsValueType == true ? 0 : graceFrames;
                if (_frameSerial - entry.Value > identityGrace) _expiredTextures.Add(entry.Key);
            }
            foreach (TextureIdentity identity in _expiredTextures)
            {
                _retiredTextureSlots[_device.FrameResources.CurrentSlotIndex].Add(_textures[identity]);
                _textures.Remove(identity);
                _textureLastUsed.Remove(identity);
            }
        }

        private void TrackUpload(IUploadResource resource)
        {
            _uploadSlots[_device.FrameResources.CurrentSlotIndex].Add(resource);
            SdlGpuTelemetryContext.UploadScheduled(resource.UploadBytes);
        }

        public void InvalidatePendingUploads()
        {
            int slotIndex = _device.FrameResources.CurrentSlotIndex;
            foreach (IUploadResource resource in _uploadSlots[slotIndex])
                resource.InvalidateUpload();
            for (int i = 0; i < _dynamicSlotUsed[slotIndex]; i++)
                _dynamicSlots[slotIndex][i].InvalidateUpload();
            _postResources?.InvalidatePendingUploads();
            _ssaoResources.InvalidatePendingUploads();
        }

        private SDL_GPUSampler* GetSampler(SamplerKey key)
        {
            if (_effectiveSamplerKeys.TryGetValue(key, out SamplerKey effective)
                && _samplers.TryGetValue(effective, out nint mapped))
            {
                return (SDL_GPUSampler*)mapped;
            }

            if (key.Filter != RenderFilterMode.Anisotropic
                || key.Anisotropy == AnisotropyLevel.Off)
            {
                SDL_GPUSampler* direct = GetOrCreateSampler(key);
                if (direct == null)
                    throw new InvalidOperationException($"SDL scene sampler creation failed: {SDL3.SDL_GetError()}");
                _effectiveSamplerKeys[key] = key;
                return direct;
            }

            string lastError = string.Empty;
            for (int index = 0; SdlGpuSamplerPolicy.TryGetAnisotropyAttempt(
                key.Anisotropy, index, out AnisotropyLevel attempt); index++)
            {
                effective = key.WithAnisotropy(attempt);
                SDL_GPUSampler* sampler = GetOrCreateSampler(effective);
                if (sampler != null)
                {
                    _effectiveSamplerKeys[key] = effective;
                    ReportSamplerNegotiation(key.Anisotropy, attempt);
                    return sampler;
                }
                lastError = SDL3.SDL_GetError() ?? string.Empty;
            }
            throw new InvalidOperationException(
                $"SDL enhanced sampler creation failed after anisotropy fallback: {lastError}");
        }

        private SDL_GPUSampler* GetOrCreateSampler(SamplerKey key)
        {
            if (_samplers.TryGetValue(key, out nint cached)) return (SDL_GPUSampler*)cached;
            SdlGpuSamplerDescription description = SdlGpuSamplerPolicy.Describe(key);
            SDL_GPUFilter minFilter = description.MinFilter == SdlGpuSamplerFilter.Nearest
                ? SDL_GPUFilter.SDL_GPU_FILTER_NEAREST : SDL_GPUFilter.SDL_GPU_FILTER_LINEAR;
            SDL_GPUFilter magFilter = description.MagFilter == SdlGpuSamplerFilter.Nearest
                ? SDL_GPUFilter.SDL_GPU_FILTER_NEAREST : SDL_GPUFilter.SDL_GPU_FILTER_LINEAR;
            SDL_GPUSamplerCreateInfo info = new()
            {
                min_filter = minFilter,
                mag_filter = magFilter,
                mipmap_mode = description.MipFilter == SdlGpuSamplerMipFilter.Linear
                    ? SDL_GPUSamplerMipmapMode.SDL_GPU_SAMPLERMIPMAPMODE_LINEAR
                    : SDL_GPUSamplerMipmapMode.SDL_GPU_SAMPLERMIPMAPMODE_NEAREST,
                address_mode_u = Address(key.WrapX),
                address_mode_v = Address(key.WrapY),
                address_mode_w = SDL_GPUSamplerAddressMode.SDL_GPU_SAMPLERADDRESSMODE_CLAMP_TO_EDGE,
                min_lod = description.MinLod,
                max_lod = description.MaxLod,
                enable_anisotropy = description.EnableAnisotropy,
                max_anisotropy = description.MaxAnisotropy
            };
            SDL_GPUSampler* sampler = SDL3.SDL_CreateGPUSampler(_device.Handle, &info);
            if (sampler != null) _samplers.Add(key, (nint)sampler);
            return sampler;
        }

        private void ReportSamplerNegotiation(AnisotropyLevel requested,
            AnisotropyLevel effective)
        {
            if (_reportedSamplerNegotiations.Add((requested, effective)))
            {
                Console.WriteLine($"[render] SDL GPU anisotropy requested={(int)requested}x "
                    + $"effective={(effective == AnisotropyLevel.Off ? "off" : $"{(int)effective}x")}");
            }
        }

        private SDL_GPUGraphicsPipeline* GetPipeline(ScenePipelineKey key)
        {
            if (_pipelines.TryGetValue(key, out nint cached)) return (SDL_GPUGraphicsPipeline*)cached;
            SDL_GPUGraphicsPipeline* pipeline = CreatePipeline(key);
            _pipelines.Add(key, (nint)pipeline);
            return pipeline;
        }

        private SDL_GPUGraphicsPipeline* GetShadowPipeline(
            SdlGpuShadowPipelineKey key)
        {
            if (_shadowPipelines.TryGetValue(key, out nint cached))
                return (SDL_GPUGraphicsPipeline*)cached;
            SDL_GPUVertexBufferDescription vertexDescription = new()
            {
                slot = 0, pitch = (uint)sizeof(GpuVertex),
                input_rate = SDL_GPUVertexInputRate.SDL_GPU_VERTEXINPUTRATE_VERTEX
            };
            SDL_GPUVertexAttribute* attributes
                = stackalloc SDL_GPUVertexAttribute[SdlGpuSceneVertexAbi.AttributeCount];
            attributes[0] = Attribute(0, SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT3, 0);
            attributes[1] = Attribute(1, SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT4, 12);
            attributes[2] = Attribute(2, SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT3, 28);
            attributes[3] = Attribute(3, SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT2, 40);
            attributes[4] = Attribute(4, SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_UINT, 48);
            attributes[5] = Attribute(5, SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_UINT, 52);
            attributes[6] = Attribute(6, SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT4,
                SdlGpuSceneVertexAbi.TangentOffset);
            SDL_GPUVertexInputState vertexInput = new()
            {
                vertex_buffer_descriptions = &vertexDescription,
                num_vertex_buffers = 1,
                vertex_attributes = attributes,
                num_vertex_attributes = SdlGpuSceneVertexAbi.AttributeCount
            };
            SDL_GPUGraphicsPipelineCreateInfo info = new()
            {
                vertex_shader = _shadowVertexShader,
                fragment_shader = _shadowFragmentShader,
                vertex_input_state = vertexInput,
                primitive_type = SDL_GPUPrimitiveType.SDL_GPU_PRIMITIVETYPE_TRIANGLELIST,
                rasterizer_state = new SDL_GPURasterizerState
                {
                    fill_mode = SDL_GPUFillMode.SDL_GPU_FILLMODE_FILL,
                    cull_mode = !key.FaceCulling ? SDL_GPUCullMode.SDL_GPU_CULLMODE_NONE
                        : key.CullMode switch
                        {
                            RenderCullMode.Front => SDL_GPUCullMode.SDL_GPU_CULLMODE_FRONT,
                            RenderCullMode.Back => SDL_GPUCullMode.SDL_GPU_CULLMODE_BACK,
                            _ => SDL_GPUCullMode.SDL_GPU_CULLMODE_NONE
                        },
                    front_face = SDL_GPUFrontFace.SDL_GPU_FRONTFACE_COUNTER_CLOCKWISE,
                    depth_bias_constant_factor = 1.25f,
                    depth_bias_slope_factor = 1.75f,
                    enable_depth_bias = true,
                    enable_depth_clip = true
                },
                multisample_state = new SDL_GPUMultisampleState
                {
                    sample_count = SDL_GPUSampleCount.SDL_GPU_SAMPLECOUNT_1
                },
                depth_stencil_state = new SDL_GPUDepthStencilState
                {
                    compare_op = SDL_GPUCompareOp.SDL_GPU_COMPAREOP_LESS,
                    enable_depth_test = true,
                    enable_depth_write = true,
                    enable_stencil_test = false
                },
                target_info = new SDL_GPUGraphicsPipelineTargetInfo
                {
                    color_target_descriptions = null,
                    num_color_targets = 0,
                    depth_stencil_format = _depthFormat,
                    has_depth_stencil_target = true
                }
            };
            SDL_GPUGraphicsPipeline* pipeline = SDL3.SDL_CreateGPUGraphicsPipeline(
                _device.Handle, &info);
            if (pipeline == null)
                throw new InvalidOperationException(
                    $"SDL shadow pipeline creation failed: {SDL3.SDL_GetError()}");
            _shadowPipelines.Add(key, (nint)pipeline);
            return pipeline;
        }

        private SDL_GPUGraphicsPipeline* CreatePipeline(ScenePipelineKey sceneKey)
        {
            PipelineKey key = sceneKey.Key;
            SDL_GPUVertexBufferDescription vertexDescription = new()
            {
                slot = 0, pitch = (uint)sizeof(GpuVertex),
                input_rate = SDL_GPUVertexInputRate.SDL_GPU_VERTEXINPUTRATE_VERTEX
            };
            SDL_GPUVertexAttribute* attributes
                = stackalloc SDL_GPUVertexAttribute[SdlGpuSceneVertexAbi.AttributeCount];
            attributes[0] = Attribute(0, SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT3, 0);
            attributes[1] = Attribute(1, SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT4, 12);
            attributes[2] = Attribute(2, SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT3, 28);
            attributes[3] = Attribute(3, SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT2, 40);
            attributes[4] = Attribute(4, SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_UINT, 48);
            attributes[5] = Attribute(5, SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_UINT, 52);
            attributes[6] = Attribute(6, SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT4,
                SdlGpuSceneVertexAbi.TangentOffset);
            SDL_GPUVertexInputState vertexInput = new()
            {
                vertex_buffer_descriptions = &vertexDescription, num_vertex_buffers = 1,
                vertex_attributes = attributes,
                num_vertex_attributes = SdlGpuSceneVertexAbi.AttributeCount
            };
            bool blend = sceneKey.BloomEmission || key.BlendMode == RenderBlendMode.Alpha;
            bool additive = sceneKey.BloomEmission;
            RenderColorWriteMask writes = key.StencilMode is RenderStencilMode.MarkTransparent
                or RenderStencilMode.Preserve ? RenderColorWriteMask.None : key.ColorWriteMask;
            SDL_GPUColorTargetDescription colorTarget = new()
            {
                format = sceneKey.ColorFormat,
                blend_state = new SDL_GPUColorTargetBlendState
                {
                    src_color_blendfactor = additive ? SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE
                        : blend ? SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_SRC_ALPHA
                        : SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE,
                    dst_color_blendfactor = additive ? SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE
                        : blend ? SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE_MINUS_SRC_ALPHA
                        : SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ZERO,
                    color_blend_op = SDL_GPUBlendOp.SDL_GPU_BLENDOP_ADD,
                    src_alpha_blendfactor = additive ? SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE
                        : blend ? SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_SRC_ALPHA
                        : SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE,
                    dst_alpha_blendfactor = additive ? SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE
                        : blend ? SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE_MINUS_SRC_ALPHA
                        : SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ZERO,
                    alpha_blend_op = SDL_GPUBlendOp.SDL_GPU_BLENDOP_ADD,
                    color_write_mask = ColorMask(writes),
                    enable_blend = blend,
                    enable_color_write_mask = true
                }
            };
            SDL_GPUDepthStencilState depthStencil = CreateDepthStencil(key);
            bool hasDepthStencil = key.DepthMode != RenderDepthMode.Disabled
                || key.StencilMode != RenderStencilMode.Disabled;
            bool specializedDepthStencil = !sceneKey.SurfaceData
                && !sceneKey.BloomEmission
                && !sceneKey.FullCoverageShader
                && key.StencilMode is RenderStencilMode.MarkTransparent
                    or RenderStencilMode.Preserve;
            SDL_GPUGraphicsPipelineCreateInfo info = new()
            {
                vertex_shader = specializedDepthStencil
                    ? _depthStencilVertexShader : _vertexShader,
                fragment_shader = specializedDepthStencil
                    ? _depthStencilFragmentShader
                    : sceneKey.SurfaceData ? _surfaceFragmentShader
                        : _fragmentShader,
                vertex_input_state = vertexInput,
                primitive_type = key.Topology == RenderTopology.Lines
                    ? SDL_GPUPrimitiveType.SDL_GPU_PRIMITIVETYPE_LINELIST
                    : SDL_GPUPrimitiveType.SDL_GPU_PRIMITIVETYPE_TRIANGLELIST,
                rasterizer_state = new SDL_GPURasterizerState
                {
                    fill_mode = sceneKey.Wireframe ? SDL_GPUFillMode.SDL_GPU_FILLMODE_LINE : SDL_GPUFillMode.SDL_GPU_FILLMODE_FILL,
                    cull_mode = !sceneKey.FaceCulling ? SDL_GPUCullMode.SDL_GPU_CULLMODE_NONE : key.CullMode switch
                    {
                        RenderCullMode.Front => SDL_GPUCullMode.SDL_GPU_CULLMODE_FRONT,
                        RenderCullMode.Back => SDL_GPUCullMode.SDL_GPU_CULLMODE_BACK,
                        _ => SDL_GPUCullMode.SDL_GPU_CULLMODE_NONE
                    },
                    front_face = SDL_GPUFrontFace.SDL_GPU_FRONTFACE_COUNTER_CLOCKWISE,
                    depth_bias_constant_factor = key.DecalDepthBias ? -1 : 0,
                    depth_bias_slope_factor = key.DecalDepthBias ? -1 : 0,
                    enable_depth_bias = key.DecalDepthBias,
                    enable_depth_clip = true
                },
                multisample_state = new SDL_GPUMultisampleState
                {
                    sample_count = SampleCount(key.SampleCount)
                },
                depth_stencil_state = depthStencil,
                target_info = new SDL_GPUGraphicsPipelineTargetInfo
                {
                    color_target_descriptions = &colorTarget, num_color_targets = 1,
                    depth_stencil_format = hasDepthStencil ? _depthFormat
                        : SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_INVALID,
                    has_depth_stencil_target = hasDepthStencil
                }
            };
            SDL_GPUGraphicsPipeline* pipeline = SDL3.SDL_CreateGPUGraphicsPipeline(_device.Handle, &info);
            if (pipeline == null) throw new InvalidOperationException($"SDL scene pipeline creation failed ({key.StencilMode}/{key.Topology}): {SDL3.SDL_GetError()}");
            return pipeline;
        }

        private static SDL_GPUDepthStencilState CreateDepthStencil(PipelineKey key)
        {
            SDL_GPUStencilOpState stencil = new()
            {
                fail_op = key.StencilMode == RenderStencilMode.Clear ? SDL_GPUStencilOp.SDL_GPU_STENCILOP_ZERO : SDL_GPUStencilOp.SDL_GPU_STENCILOP_KEEP,
                depth_fail_op = key.StencilMode == RenderStencilMode.Clear ? SDL_GPUStencilOp.SDL_GPU_STENCILOP_ZERO : SDL_GPUStencilOp.SDL_GPU_STENCILOP_KEEP,
                pass_op = key.StencilMode switch
                {
                    RenderStencilMode.Clear => SDL_GPUStencilOp.SDL_GPU_STENCILOP_ZERO,
                    RenderStencilMode.MarkTransparent => SDL_GPUStencilOp.SDL_GPU_STENCILOP_REPLACE,
                    _ => SDL_GPUStencilOp.SDL_GPU_STENCILOP_KEEP
                },
                compare_op = key.StencilMode switch
                {
                    RenderStencilMode.MarkTransparent => SDL_GPUCompareOp.SDL_GPU_COMPAREOP_GREATER,
                    RenderStencilMode.EqualPolygon => SDL_GPUCompareOp.SDL_GPU_COMPAREOP_EQUAL,
                    RenderStencilMode.NotEqualPolygon => SDL_GPUCompareOp.SDL_GPU_COMPAREOP_NOT_EQUAL,
                    _ => SDL_GPUCompareOp.SDL_GPU_COMPAREOP_ALWAYS
                }
            };
            return new SDL_GPUDepthStencilState
            {
                compare_op = key.DepthMode == RenderDepthMode.Less
                    ? SDL_GPUCompareOp.SDL_GPU_COMPAREOP_LESS : SDL_GPUCompareOp.SDL_GPU_COMPAREOP_LESS_OR_EQUAL,
                enable_depth_test = key.DepthMode != RenderDepthMode.Disabled,
                enable_depth_write = key.DepthWrite,
                enable_stencil_test = key.StencilMode != RenderStencilMode.Disabled,
                front_stencil_state = stencil,
                back_stencil_state = stencil,
                compare_mask = 0xFF,
                write_mask = 0xFF
            };
        }

        private SdlGpuSampleNegotiation EnsureTargets(uint width, uint height,
            SdlGpuSceneColorPlan colorPlan, SdlGpuSampleNegotiation requestedNegotiation,
            SdlGpuSampleNegotiation ldrNegotiation,
            SdlGpuHdrConfiguration hdrConfiguration)
        {
            SDL_GPUTextureFormat format = colorPlan.Format;
            SdlGpuSampleNegotiation negotiation = requestedNegotiation;
            SdlGpuSceneTargetPlan plan = SdlGpuSceneTargetPlan.From(negotiation);
            if (_sceneColor != null && width == _targetWidth && height == _targetHeight
                && plan.RenderColorSamples == _targetSampleCount
                && format == _sceneColorFormat)
            {
                _sceneUsesHdr = colorPlan.UsesHdr;
                return negotiation;
            }

            if (!TryAllocateSceneTargets(format, width, height, plan,
                out SDL_GPUTexture* color, out SDL_GPUTexture* multisampleColor,
                out SDL_GPUTexture* depth, out string allocationError))
            {
                if (!colorPlan.UsesHdr)
                {
                    throw new InvalidOperationException(allocationError);
                }

                _failedHdrConfiguration = hdrConfiguration;

                format = _device.SwapchainFormat;
                negotiation = ldrNegotiation;
                plan = SdlGpuSceneTargetPlan.From(negotiation);
                if (!TryAllocateSceneTargets(format, width, height, plan,
                    out color, out multisampleColor, out depth,
                    out string ldrAllocationError))
                {
                    throw new InvalidOperationException(
                        $"SDL HDR scene target allocation failed ({allocationError}); "
                        + $"LDR fallback also failed ({ldrAllocationError}).");
                }
                ReportHdrAllocationFallback(allocationError, negotiation);
            }
            if (!SDL3.SDL_WaitForGPUIdle(_device.Handle))
            {
                SDL3.SDL_ReleaseGPUTexture(_device.Handle, depth);
                if (multisampleColor != null)
                    SDL3.SDL_ReleaseGPUTexture(_device.Handle, multisampleColor);
                SDL3.SDL_ReleaseGPUTexture(_device.Handle, color);
                throw new InvalidOperationException($"SDL scene target resize wait failed: {SDL3.SDL_GetError()}");
            }
            if (_sceneColor != null) SDL3.SDL_ReleaseGPUTexture(_device.Handle, _sceneColor);
            if (_sceneMultisampleColor != null)
                SDL3.SDL_ReleaseGPUTexture(_device.Handle, _sceneMultisampleColor);
            if (_sceneDepth != null) SDL3.SDL_ReleaseGPUTexture(_device.Handle, _sceneDepth);
            _sceneColor = color;
            _sceneMultisampleColor = multisampleColor;
            _sceneDepth = depth;
            _targetWidth = width;
            _targetHeight = height;
            _targetSampleCount = plan.RenderColorSamples;
            _sceneColorFormat = format;
            _sceneUsesHdr = format == SdlGpuHdrPolicy.HdrFormat;
            return negotiation;
        }

        private void ReportHdrAllocationFallback(string error,
            SdlGpuSampleNegotiation negotiation)
        {
            if (_loggedHdrAllocationFallback) return;
            _loggedHdrAllocationFallback = true;
            Console.Error.WriteLine("[render] SDL GPU HDR allocation failed; "
                + $"using {_device.SwapchainFormat} LDR with "
                + $"{negotiation.Effective}x MSAA. {error}");
        }

        private bool TryAllocateSceneTargets(SDL_GPUTextureFormat format,
            uint width, uint height, SdlGpuSceneTargetPlan plan,
            out SDL_GPUTexture* color, out SDL_GPUTexture* multisampleColor,
            out SDL_GPUTexture* depth, out string error)
        {
            color = null;
            multisampleColor = null;
            depth = null;
            try
            {
                color = CreateTarget(format,
                    SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COLOR_TARGET
                        | SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER,
                    width, height, plan.ResolveColorSamples);
                if (plan.UsesResolve)
                {
                    multisampleColor = CreateTarget(format,
                        SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COLOR_TARGET,
                        width, height, plan.RenderColorSamples);
                }
                depth = CreateTarget(_depthFormat,
                    SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_DEPTH_STENCIL_TARGET
                        | (_depthSampleable && plan.DepthSamples == 1
                            ? SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER : 0),
                    width, height, plan.DepthSamples);
                error = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                if (depth != null) SDL3.SDL_ReleaseGPUTexture(_device.Handle, depth);
                if (multisampleColor != null)
                    SDL3.SDL_ReleaseGPUTexture(_device.Handle, multisampleColor);
                if (color != null) SDL3.SDL_ReleaseGPUTexture(_device.Handle, color);
                color = null;
                multisampleColor = null;
                depth = null;
                error = ex.Message;
                return false;
            }
        }

        private void EnsureBloomTargets(uint width, uint height, SdlGpuBloomPlan plan)
        {
            if (!plan.Enabled) return;
            if (_bloomColor != null && width == _bloomTargetWidth
                && height == _bloomTargetHeight
                && plan.RenderSamples == _bloomTargetSampleCount
                && _bloomTargetFormat == _sceneColorFormat) return;
            SDL_GPUTexture* color = null;
            SDL_GPUTexture* multisampleColor = null;
            try
            {
                color = CreateTarget(_sceneColorFormat,
                    SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COLOR_TARGET
                        | SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER,
                    width, height, plan.ResolveSamples);
                if (plan.UsesResolve)
                {
                    multisampleColor = CreateTarget(_sceneColorFormat,
                        SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COLOR_TARGET,
                        width, height, plan.RenderSamples);
                }
            }
            catch
            {
                if (multisampleColor != null)
                    SDL3.SDL_ReleaseGPUTexture(_device.Handle, multisampleColor);
                if (color != null) SDL3.SDL_ReleaseGPUTexture(_device.Handle, color);
                throw;
            }
            if (!SDL3.SDL_WaitForGPUIdle(_device.Handle))
            {
                if (multisampleColor != null)
                    SDL3.SDL_ReleaseGPUTexture(_device.Handle, multisampleColor);
                SDL3.SDL_ReleaseGPUTexture(_device.Handle, color);
                throw new InvalidOperationException(
                    $"SDL bloom target resize wait failed: {SDL3.SDL_GetError()}");
            }
            if (_bloomMultisampleColor != null)
                SDL3.SDL_ReleaseGPUTexture(_device.Handle, _bloomMultisampleColor);
            if (_bloomColor != null)
                SDL3.SDL_ReleaseGPUTexture(_device.Handle, _bloomColor);
            _bloomColor = color;
            _bloomMultisampleColor = multisampleColor;
            _bloomTargetWidth = width;
            _bloomTargetHeight = height;
            _bloomTargetSampleCount = plan.RenderSamples;
            _bloomTargetFormat = _sceneColorFormat;
        }

        private SDL_GPUTexture* CreateTarget(SDL_GPUTextureFormat format,
            SDL_GPUTextureUsageFlags usage, uint width, uint height, int sampleCount)
        {
            SDL_GPUTextureCreateInfo info = new()
            {
                type = SDL_GPUTextureType.SDL_GPU_TEXTURETYPE_2D, format = format, usage = usage,
                width = width, height = height, layer_count_or_depth = 1, num_levels = 1,
                sample_count = SampleCount(sampleCount)
            };
            SDL_GPUTexture* texture = SDL3.SDL_CreateGPUTexture(_device.Handle, &info);
            if (texture == null) throw new InvalidOperationException($"SDL scene target creation failed: {SDL3.SDL_GetError()}");
            return texture;
        }

        private static SDL_GPUBlitRegion Region(SDL_GPUTexture* texture, uint width, uint height)
            => new() { texture = texture, mip_level = 0, layer_or_depth_plane = 0, w = width, h = height };

        private static SDL_GPUVertexAttribute Attribute(uint location, SDL_GPUVertexElementFormat format, uint offset)
            => new() { location = location, buffer_slot = 0, format = format, offset = offset };

        private static SDL_GPUColorComponentFlags ColorMask(RenderColorWriteMask mask)
        {
            SDL_GPUColorComponentFlags value = 0;
            if ((mask & RenderColorWriteMask.Red) != 0) value |= SDL_GPUColorComponentFlags.SDL_GPU_COLORCOMPONENT_R;
            if ((mask & RenderColorWriteMask.Green) != 0) value |= SDL_GPUColorComponentFlags.SDL_GPU_COLORCOMPONENT_G;
            if ((mask & RenderColorWriteMask.Blue) != 0) value |= SDL_GPUColorComponentFlags.SDL_GPU_COLORCOMPONENT_B;
            if ((mask & RenderColorWriteMask.Alpha) != 0) value |= SDL_GPUColorComponentFlags.SDL_GPU_COLORCOMPONENT_A;
            return value;
        }

        private static SDL_GPUSamplerAddressMode Address(RepeatMode mode) => mode switch
        {
            RepeatMode.Repeat => SDL_GPUSamplerAddressMode.SDL_GPU_SAMPLERADDRESSMODE_REPEAT,
            RepeatMode.Mirror => SDL_GPUSamplerAddressMode.SDL_GPU_SAMPLERADDRESSMODE_MIRRORED_REPEAT,
            _ => SDL_GPUSamplerAddressMode.SDL_GPU_SAMPLERADDRESSMODE_CLAMP_TO_EDGE
        };

        private static SDL_GPUSampleCount SampleCount(int count) => count switch
        {
            4 => SDL_GPUSampleCount.SDL_GPU_SAMPLECOUNT_4,
            2 => SDL_GPUSampleCount.SDL_GPU_SAMPLECOUNT_2,
            _ => SDL_GPUSampleCount.SDL_GPU_SAMPLECOUNT_1
        };

        private void ReportSampleNegotiation(SdlGpuSampleNegotiation negotiation)
        {
            if (negotiation.Requested <= 1 || !_reportedSampleNegotiations.Add(negotiation)) return;
            string reason = negotiation.Reason switch
            {
                SdlGpuMsaaFallbackReason.CelDepthSampling
                    => "cel outline depth sampling requires a single-sample depth target",
                SdlGpuMsaaFallbackReason.UnsupportedColorOrDepthFormat
                    => "color/depth format sample-count support",
                _ => "supported"
            };
            Console.WriteLine($"[render] SDL GPU MSAA requested={negotiation.Requested}x "
                + $"effective={negotiation.Effective}x reason={reason}");
        }

        private static void PushVertex<T>(SDL_GPUCommandBuffer* commandBuffer, uint slot, T value) where T : unmanaged
        {
            uint bytes = (uint)sizeof(T);
            long nativeStart = SdlGpuTelemetryContext.BeginNativeCall();
            SDL3.SDL_PushGPUVertexUniformData(commandBuffer, slot, (IntPtr)(&value), bytes);
            long nativeTicks = SdlGpuTelemetryContext.EndNativeCall(nativeStart);
            SdlGpuTelemetryContext.VertexUniformNative(bytes, nativeTicks);
        }

        private static void PushFragment<T>(SDL_GPUCommandBuffer* commandBuffer, uint slot, T value) where T : unmanaged
        {
            uint bytes = (uint)sizeof(T);
            long nativeStart = SdlGpuTelemetryContext.BeginNativeCall();
            SDL3.SDL_PushGPUFragmentUniformData(commandBuffer, slot, (IntPtr)(&value), bytes);
            long nativeTicks = SdlGpuTelemetryContext.EndNativeCall(nativeStart);
            SdlGpuTelemetryContext.FragmentUniformNative(bytes, nativeTicks);
        }

        private static unsafe MatrixPaletteConstants IdentityPalette()
        {
            MatrixPaletteConstants result = default;
            float* destination = result.MatrixStack;
            for (int matrix = 0; matrix < RenderFrame.MatrixStackFloats; matrix++)
            {
                destination[matrix] = matrix % 16 is 0 or 5 or 10 or 15
                    ? 1 : 0;
            }
            return result;
        }

        private static SDL_GPUShader* CreateShader(SDL_GPUDevice* device, SDL_GPUShaderFormat format,
            string path, string entrypoint, SDL_GPUShaderStage stage, uint samplers, uint uniforms)
        {
            byte[] code = File.ReadAllBytes(path);
            byte[] name = Encoding.UTF8.GetBytes(entrypoint + "\0");
            fixed (byte* codePtr = code)
            fixed (byte* namePtr = name)
            {
                SDL_GPUShaderCreateInfo info = new()
                {
                    code_size = (UIntPtr)code.Length, code = codePtr, entrypoint = namePtr,
                    format = format, stage = stage, num_samplers = samplers, num_uniform_buffers = uniforms
                };
                SDL_GPUShader* shader = SDL3.SDL_CreateGPUShader(device, &info);
                if (shader == null) throw new InvalidOperationException($"SDL scene shader creation failed for {path}: {SDL3.SDL_GetError()}");
                return shader;
            }
        }

        private static (SDL_GPUShaderFormat, string, string) SelectArtifacts(SdlGpuDevice device)
        {
            string directory = ShaderArtifactManifest.Directory;
            if ((device.ShaderFormats & SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_DXIL) != 0)
                return (SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_DXIL, Path.Combine(directory, "scene.vert.dxil"), Path.Combine(directory, "scene.frag.dxil"));
            if ((device.ShaderFormats & SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_MSL) != 0)
                return (SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_MSL, Path.Combine(directory, "scene.vert.msl"), Path.Combine(directory, "scene.frag.msl"));
            if ((device.ShaderFormats & SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_SPIRV) != 0)
                return (SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_SPIRV, Path.Combine(directory, "scene.vert.spv"), Path.Combine(directory, "scene.frag.spv"));
            throw new PlatformNotSupportedException($"SDL GPU shader formats {SdlGpuDevice.DescribeShaderFormats(device.ShaderFormats)} have no scene artifact.");
        }

        private (SDL_GPUShaderFormat Format, string Suffix) SelectShaderFormat()
        {
            if ((_device.ShaderFormats & SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_DXIL) != 0)
                return (SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_DXIL, "dxil");
            if ((_device.ShaderFormats & SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_MSL) != 0)
                return (SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_MSL, "msl");
            if ((_device.ShaderFormats & SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_SPIRV) != 0)
                return (SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_SPIRV, "spv");
            throw new PlatformNotSupportedException(
                "No generated surface shader format is supported.");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            SDL3.SDL_WaitForGPUIdle(_device.Handle);
            foreach (List<GpuMesh> slot in _dynamicSlots) foreach (GpuMesh mesh in slot) mesh.Dispose();
            foreach (List<GpuTexture> slot in _retiredTextureSlots) foreach (GpuTexture texture in slot) texture.Dispose();
            foreach (List<GpuCubeTexture> slot in _retiredCubeTextureSlots)
                foreach (GpuCubeTexture texture in slot) texture.Dispose();
            foreach (List<GpuMesh> slot in _retiredMeshSlots) foreach (GpuMesh mesh in slot) mesh.Dispose();
            foreach (GpuMesh mesh in _staticMeshes.Values) mesh.Dispose();
            foreach (GpuTexture texture in _textures.Values) texture.Dispose();
            _postResources?.Dispose();
            _ssaoResources.Dispose();
            _skyResources.Dispose();
            _whiteTexture?.Dispose();
            _flatNormalTexture?.Dispose();
            _blackEmissiveTexture?.Dispose();
            _blackReflectionTexture?.Dispose();
            _reflectionTexture?.Dispose();
            foreach (nint sampler in _samplers.Values) SDL3.SDL_ReleaseGPUSampler(_device.Handle, (SDL_GPUSampler*)sampler);
            foreach (nint pipeline in _pipelines.Values) SDL3.SDL_ReleaseGPUGraphicsPipeline(_device.Handle, (SDL_GPUGraphicsPipeline*)pipeline);
            foreach (nint pipeline in _shadowPipelines.Values)
                SDL3.SDL_ReleaseGPUGraphicsPipeline(_device.Handle,
                    (SDL_GPUGraphicsPipeline*)pipeline);
            foreach (nint pipeline in _distortionPipelines.Values)
                SDL3.SDL_ReleaseGPUGraphicsPipeline(_device.Handle,
                    (SDL_GPUGraphicsPipeline*)pipeline);
            if (_distortionMultisampleColor != null)
                SDL3.SDL_ReleaseGPUTexture(_device.Handle,
                    _distortionMultisampleColor);
            if (_distortionColor != null)
                SDL3.SDL_ReleaseGPUTexture(_device.Handle, _distortionColor);
            if (_distortionFragmentShader != null)
                SDL3.SDL_ReleaseGPUShader(_device.Handle,
                    _distortionFragmentShader);
            if (_distortionVertexShader != null)
                SDL3.SDL_ReleaseGPUShader(_device.Handle,
                    _distortionVertexShader);
            if (_sceneDepth != null) SDL3.SDL_ReleaseGPUTexture(_device.Handle, _sceneDepth);
            if (_surfaceDepth != null)
                SDL3.SDL_ReleaseGPUTexture(_device.Handle, _surfaceDepth);
            if (_surfaceColor != null)
                SDL3.SDL_ReleaseGPUTexture(_device.Handle, _surfaceColor);
            if (_shadowDepth != null)
                SDL3.SDL_ReleaseGPUTexture(_device.Handle, _shadowDepth);
            if (_bloomMultisampleColor != null)
                SDL3.SDL_ReleaseGPUTexture(_device.Handle, _bloomMultisampleColor);
            if (_bloomColor != null) SDL3.SDL_ReleaseGPUTexture(_device.Handle, _bloomColor);
            if (_sceneMultisampleColor != null)
                SDL3.SDL_ReleaseGPUTexture(_device.Handle, _sceneMultisampleColor);
            if (_sceneColor != null) SDL3.SDL_ReleaseGPUTexture(_device.Handle, _sceneColor);
            if (_fragmentShader != null) SDL3.SDL_ReleaseGPUShader(_device.Handle, _fragmentShader);
            if (_depthStencilFragmentShader != null)
                SDL3.SDL_ReleaseGPUShader(_device.Handle,
                    _depthStencilFragmentShader);
            if (_depthStencilVertexShader != null)
                SDL3.SDL_ReleaseGPUShader(_device.Handle,
                    _depthStencilVertexShader);
            if (_surfaceFragmentShader != null)
                SDL3.SDL_ReleaseGPUShader(_device.Handle, _surfaceFragmentShader);
            if (_shadowFragmentShader != null)
                SDL3.SDL_ReleaseGPUShader(_device.Handle, _shadowFragmentShader);
            if (_shadowVertexShader != null)
                SDL3.SDL_ReleaseGPUShader(_device.Handle, _shadowVertexShader);
            if (_vertexShader != null) SDL3.SDL_ReleaseGPUShader(_device.Handle, _vertexShader);
        }

        private readonly record struct ScenePipelineKey(PipelineKey Key, bool Wireframe,
            bool FaceCulling, SDL_GPUTextureFormat ColorFormat,
            bool BloomEmission = false, bool SurfaceData = false,
            bool FullCoverageShader = false);

        private interface IUploadResource
        {
            long UploadBytes { get; }
            void ReleaseUploadStorage();
            void InvalidateUpload();
        }

        [StructLayout(LayoutKind.Sequential)]
        private unsafe struct LegacyDrawConstants
        {
            public Matrix4 Transform;
            public Matrix4 Billboard;
            public fixed float MatrixStack[RenderFrame.MatrixStackFloats];
            public Matrix4 TextureMatrix;
            public Vector4 Diffuse, Ambient, Specular, Emission, OverrideColor, PaletteOverride;
            public Vector4 Light1Vector, Light1Color, Light2Vector, Light2Color;
            public Vector4 DrawOptions, MaterialOptions, RenderOptions, FlatColor;
            public Vector4 EnhancedEmission, EnhancedOptions, ReflectionOptions;
            public Vector4 SoftParticleOptions;
            public Vector4 BeamCore, BeamGlow, BeamOptions;
            public Vector4 ForceFieldEmission, ForceFieldOptions, ForceFieldFlow;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct VertexDrawConstants
        {
            public Matrix4 Transform;
            public Matrix4 Billboard;
            public Matrix4 TextureMatrix;
            public Vector4 Diffuse, Ambient, Specular, Emission;
            public Vector4 Light1Vector, Light1Color, Light2Vector, Light2Color;
            public Vector4 DrawOptions, MaterialOptions;
        }

        [StructLayout(LayoutKind.Sequential)]
        private unsafe struct MatrixPaletteConstants
        {
            public fixed float MatrixStack[RenderFrame.MatrixStackFloats];
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FragmentMaterialConstants
        {
            public Matrix4 TextureMatrix;
            public Vector4 Diffuse, Specular, Emission, OverrideColor,
                PaletteOverride;
            public Vector4 Light1Vector, Light1Color, Light2Vector, Light2Color;
            public Vector4 DrawOptions, MaterialOptions, RenderOptions, FlatColor;
            public Vector4 EnhancedEmission, EnhancedOptions, ReflectionOptions;
            public Vector4 SoftParticleOptions;
            public Vector4 BeamCore, BeamGlow, BeamOptions;
            public Vector4 ForceFieldEmission, ForceFieldOptions, ForceFieldFlow;
        }

        private struct SceneDrawData
        {
            public VertexDrawConstants Vertex;
            public FragmentMaterialConstants Fragment;
            public MatrixPaletteConstants Palette;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AlphaConstants
        {
            public Vector4 Options;
        }

        [StructLayout(LayoutKind.Sequential)]
        private unsafe struct DistortionVertexConstants
        {
            public Matrix4 Transform;
            public Matrix4 Billboard;
            public fixed float MatrixStack[RenderFrame.MatrixStackFloats];
            public Matrix4 TextureMatrix;
            public Vector4 Options;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DistortionFragmentConstants
        {
            public Vector4 Options;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ShadowFrameConstants
        {
            public Matrix4 ViewProjection;
            public Matrix4 View;
            public Vector4 Options;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct GpuVertex
        {
            public float Px, Py, Pz, Cr, Cg, Cb, Ca, Nx, Ny, Nz, U, V;
            public uint MatrixIndex, Flags;
            public float Tx, Ty, Tz, Tw;
            public GpuVertex(RenderVertex vertex)
            {
                Px = vertex.Position.X; Py = vertex.Position.Y; Pz = vertex.Position.Z;
                Cr = vertex.Color.X; Cg = vertex.Color.Y; Cb = vertex.Color.Z; Ca = vertex.Color.W;
                Nx = vertex.Normal.X; Ny = vertex.Normal.Y; Nz = vertex.Normal.Z;
                U = vertex.TexCoord.X; V = vertex.TexCoord.Y;
                MatrixIndex = vertex.MatrixIndex; Flags = (uint)vertex.Flags;
                Tx = vertex.Tangent.X; Ty = vertex.Tangent.Y;
                Tz = vertex.Tangent.Z; Tw = vertex.Tangent.W;
            }
        }

        private sealed class GpuMesh : IDisposable, IUploadResource
        {
            private readonly SdlGpuDevice _device;
            private CpuMesh _mesh;
            private SDL_GPUTransferBuffer* _transfer;
            private bool _uploaded;
            private readonly uint _vertexCapacity;
            private readonly uint _triangleCapacity;
            private readonly uint _lineCapacity;
            private readonly uint _transferCapacity;
            public SDL_GPUBuffer* VertexBuffer { get; private set; }
            public SDL_GPUBuffer* TriangleIndexBuffer { get; private set; }
            public SDL_GPUBuffer* LineIndexBuffer { get; private set; }
            public int TriangleIndexCount => _mesh.TriangleIndexCount;
            public int LineIndexCount => _mesh.LineIndexCount;
            public long UploadBytes => checked((long)_mesh.VertexCount * sizeof(GpuVertex)
                + ((long)_mesh.TriangleIndexCount + _mesh.LineIndexCount) * sizeof(uint));

            private GpuMesh(SdlGpuDevice device, CpuMesh mesh)
            {
                _device = device; _mesh = mesh;
                _vertexCapacity = checked((uint)Math.Max(sizeof(GpuVertex), mesh.VertexCount * sizeof(GpuVertex)));
                _triangleCapacity = checked((uint)Math.Max(sizeof(uint), mesh.TriangleIndexCount * sizeof(uint)));
                _lineCapacity = checked((uint)Math.Max(sizeof(uint), mesh.LineIndexCount * sizeof(uint)));
                _transferCapacity = checked((uint)Math.Max(4, mesh.VertexCount * sizeof(GpuVertex)
                    + (mesh.TriangleIndexCount + mesh.LineIndexCount) * sizeof(uint)));
                try
                {
                    VertexBuffer = Buffer(SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_VERTEX, _vertexCapacity);
                    TriangleIndexBuffer = Buffer(SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_INDEX, _triangleCapacity);
                    LineIndexBuffer = Buffer(SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_INDEX, _lineCapacity);
                    SDL_GPUTransferBufferCreateInfo transferInfo = new() { usage = SDL_GPUTransferBufferUsage.SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD, size = _transferCapacity };
                    _transfer = SDL3.SDL_CreateGPUTransferBuffer(device.Handle, &transferInfo);
                    if (VertexBuffer == null || TriangleIndexBuffer == null || LineIndexBuffer == null || _transfer == null)
                        throw new InvalidOperationException($"SDL scene mesh allocation failed: {SDL3.SDL_GetError()}");
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            public static GpuMesh Create(SdlGpuDevice device, CpuMesh mesh) => new(device, mesh);

            public bool CanHold(CpuMesh mesh)
                => mesh.VertexCount * sizeof(GpuVertex) <= _vertexCapacity
                    && mesh.TriangleIndexCount * sizeof(uint) <= _triangleCapacity
                    && mesh.LineIndexCount * sizeof(uint) <= _lineCapacity
                    && mesh.VertexCount * sizeof(GpuVertex)
                        + (mesh.TriangleIndexCount + mesh.LineIndexCount) * sizeof(uint) <= _transferCapacity;

            public void Reset(CpuMesh mesh)
            {
                if (!CanHold(mesh)) throw new ArgumentException("Dynamic mesh exceeds its retained slot capacity.", nameof(mesh));
                _mesh = mesh;
                _uploaded = false;
            }

            public bool EnsureUploaded(SDL_GPUCommandBuffer* commandBuffer)
            {
                if (_uploaded) return false;
                if (_transfer == null)
                {
                    SDL_GPUTransferBufferCreateInfo info = new()
                    {
                        usage = SDL_GPUTransferBufferUsage.SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD,
                        size = _transferCapacity
                    };
                    _transfer = SDL3.SDL_CreateGPUTransferBuffer(_device.Handle, &info);
                    if (_transfer == null) throw new InvalidOperationException($"SDL scene mesh upload allocation failed: {SDL3.SDL_GetError()}");
                }
                IntPtr memory = SDL3.SDL_MapGPUTransferBuffer(_device.Handle, _transfer, false);
                if (memory == IntPtr.Zero) throw new InvalidOperationException($"SDL scene mesh map failed: {SDL3.SDL_GetError()}");
                uint vertexBytes = checked((uint)(_mesh.VertexCount * sizeof(GpuVertex)));
                uint triangleBytes = checked((uint)(_mesh.TriangleIndexCount * sizeof(uint)));
                try
                {
                    GpuVertex* vertices = (GpuVertex*)memory;
                    for (int i = 0; i < _mesh.VertexCount; i++) vertices[i] = new GpuVertex(_mesh.Vertices[i]);
                    if (_mesh.TriangleIndexCount > 0) Marshal.Copy(_mesh.TriangleIndices, 0, memory + (int)vertexBytes, _mesh.TriangleIndexCount);
                    if (_mesh.LineIndexCount > 0) Marshal.Copy(_mesh.LineIndices, 0, memory + (int)(vertexBytes + triangleBytes), _mesh.LineIndexCount);
                }
                finally { SDL3.SDL_UnmapGPUTransferBuffer(_device.Handle, _transfer); }
                SDL_GPUCopyPass* copy = SDL3.SDL_BeginGPUCopyPass(commandBuffer);
                if (copy == null) throw new InvalidOperationException($"SDL scene mesh copy pass failed: {SDL3.SDL_GetError()}");
                Upload(copy, VertexBuffer, 0, 0, vertexBytes);
                if (triangleBytes > 0) Upload(copy, TriangleIndexBuffer, vertexBytes, 0, triangleBytes);
                uint lineBytes = checked((uint)(_mesh.LineIndexCount * sizeof(uint)));
                if (lineBytes > 0) Upload(copy, LineIndexBuffer, vertexBytes + triangleBytes, 0, lineBytes);
                SDL3.SDL_EndGPUCopyPass(copy);
                _uploaded = true;
                return true;
            }

            public void ReleaseUploadStorage()
            {
                if (_transfer != null) SDL3.SDL_ReleaseGPUTransferBuffer(_device.Handle, _transfer);
                _transfer = null;
            }

            public void InvalidateUpload() => _uploaded = false;

            private void Upload(SDL_GPUCopyPass* copy, SDL_GPUBuffer* destination, uint sourceOffset, uint destinationOffset, uint size)
            {
                SDL_GPUTransferBufferLocation source = new() { transfer_buffer = _transfer, offset = sourceOffset };
                SDL_GPUBufferRegion target = new() { buffer = destination, offset = destinationOffset, size = size };
                SDL3.SDL_UploadToGPUBuffer(copy, &source, &target, false);
            }

            private SDL_GPUBuffer* Buffer(SDL_GPUBufferUsageFlags usage, uint size)
            {
                SDL_GPUBufferCreateInfo info = new() { usage = usage, size = Math.Max(4, size) };
                return SDL3.SDL_CreateGPUBuffer(_device.Handle, &info);
            }

            public void Dispose()
            {
                ReleaseUploadStorage();
                if (LineIndexBuffer != null) SDL3.SDL_ReleaseGPUBuffer(_device.Handle, LineIndexBuffer);
                if (TriangleIndexBuffer != null) SDL3.SDL_ReleaseGPUBuffer(_device.Handle, TriangleIndexBuffer);
                if (VertexBuffer != null) SDL3.SDL_ReleaseGPUBuffer(_device.Handle, VertexBuffer);
                _transfer = null; LineIndexBuffer = null; TriangleIndexBuffer = null; VertexBuffer = null;
            }
        }

        private sealed class ReflectionProbeResourceUnavailableException
            : InvalidOperationException
        {
            public ReflectionProbeResourceUnavailableException(string message)
                : base(message) { }
        }

        private sealed class GpuCubeTexture : IDisposable, IUploadResource
        {
            private const int FaceCount = 6;
            private readonly SdlGpuDevice _device;
            private readonly ReflectionProbeKey _key;
            private readonly int _dimension;
            private readonly ulong _contentFingerprint;
            private readonly IReadOnlyList<ReadOnlyMemory<byte>> _faces;
            private readonly uint _mipLevelCount;
            private SDL_GPUTransferBuffer* _transfer;
            private bool _uploaded;

            public SDL_GPUTexture* Handle { get; private set; }
            public long UploadBytes => checked((long)_dimension * _dimension * 4 * FaceCount);

            private GpuCubeTexture(SdlGpuDevice device, ReflectionProbeKey key,
                int dimension, IReadOnlyList<ReadOnlyMemory<byte>> faces,
                bool mipmapped)
            {
                _device = device;
                _key = key;
                _dimension = dimension;
                _faces = faces;
                _contentFingerprint = key.IsValid
                    ? ComputeFingerprint(faces) : 0;
                _mipLevelCount = SdlGpuTextureQuality.MipLevelCount(
                    dimension, dimension, mipmapped);
                if (faces.Count != FaceCount)
                    throw new ArgumentException("A cube texture requires six faces.", nameof(faces));
                int faceBytes = checked(dimension * dimension * 4);
                for (int face = 0; face < faces.Count; face++)
                {
                    if (faces[face].Length != faceBytes)
                        throw new ArgumentException("Cube texture faces must be equal RGBA8 images.", nameof(faces));
                }
                try
                {
                    SDL_GPUTextureCreateInfo textureInfo = new()
                    {
                        type = SDL_GPUTextureType.SDL_GPU_TEXTURETYPE_CUBE,
                        format = SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R8G8B8A8_UNORM,
                        usage = SdlGpuTextureQuality.SceneTextureUsage(mipmapped),
                        width = checked((uint)dimension),
                        height = checked((uint)dimension),
                        layer_count_or_depth = FaceCount,
                        num_levels = _mipLevelCount,
                        sample_count = SDL_GPUSampleCount.SDL_GPU_SAMPLECOUNT_1
                    };
                    Handle = SDL3.SDL_CreateGPUTexture(device.Handle, &textureInfo);
                    SDL_GPUTransferBufferCreateInfo transferInfo = new()
                    {
                        usage = SDL_GPUTransferBufferUsage.SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD,
                        size = checked((uint)(faceBytes * FaceCount))
                    };
                    _transfer = SDL3.SDL_CreateGPUTransferBuffer(device.Handle,
                        &transferInfo);
                    if (Handle == null || _transfer == null)
                        throw new ReflectionProbeResourceUnavailableException(
                            $"SDL reflection cube allocation failed: {SDL3.SDL_GetError()}");
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            public static GpuCubeTexture Create(SdlGpuDevice device,
                RenderReflectionProbe probe)
                => new(device, probe.Key, probe.Dimension, probe.Faces,
                    mipmapped: true);

            public static GpuCubeTexture CreateFallback(SdlGpuDevice device)
            {
                ReadOnlyMemory<byte> black = new byte[] { 0, 0, 0, 255 };
                return new GpuCubeTexture(device, default, 1,
                    new[] { black, black, black, black, black, black },
                    mipmapped: false);
            }

            public bool Matches(RenderReflectionProbe probe)
                => _key == probe.Key && _dimension == probe.Dimension
                    && _mipLevelCount == probe.MipLevelCount
                    && _contentFingerprint == probe.ContentFingerprint;

            public bool IsUploaded => _uploaded;

            public bool EnsureUploaded(SDL_GPUCommandBuffer* commandBuffer)
            {
                if (_uploaded) return false;
                int faceBytes = checked(_dimension * _dimension * 4);
                if (_transfer == null)
                {
                    SDL_GPUTransferBufferCreateInfo info = new()
                    {
                        usage = SDL_GPUTransferBufferUsage.SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD,
                        size = checked((uint)(faceBytes * FaceCount))
                    };
                    _transfer = SDL3.SDL_CreateGPUTransferBuffer(_device.Handle, &info);
                    if (_transfer == null)
                        throw new ReflectionProbeResourceUnavailableException(
                            $"SDL reflection cube upload allocation failed: {SDL3.SDL_GetError()}");
                }
                IntPtr memory = SDL3.SDL_MapGPUTransferBuffer(_device.Handle,
                    _transfer, false);
                if (memory == IntPtr.Zero)
                    throw new ReflectionProbeResourceUnavailableException(
                        $"SDL reflection cube map failed: {SDL3.SDL_GetError()}");
                try
                {
                    for (int face = 0; face < FaceCount; face++)
                        SdlGpuMappedMemoryCopy.Copy(_faces[face], memory + face * faceBytes);
                }
                finally
                {
                    SDL3.SDL_UnmapGPUTransferBuffer(_device.Handle, _transfer);
                }
                SDL_GPUCopyPass* copy = SDL3.SDL_BeginGPUCopyPass(commandBuffer);
                if (copy == null)
                    throw new InvalidOperationException(
                        $"SDL reflection cube copy pass failed: {SDL3.SDL_GetError()}");
                for (int face = 0; face < FaceCount; face++)
                {
                    SDL_GPUTextureTransferInfo source = new()
                    {
                        transfer_buffer = _transfer,
                        offset = checked((uint)(face * faceBytes)),
                        pixels_per_row = checked((uint)_dimension),
                        rows_per_layer = checked((uint)_dimension)
                    };
                    SDL_GPUTextureRegion target = new()
                    {
                        texture = Handle,
                        mip_level = 0,
                        layer = checked((uint)face),
                        x = 0, y = 0, z = 0,
                        w = checked((uint)_dimension),
                        h = checked((uint)_dimension), d = 1
                    };
                    SDL3.SDL_UploadToGPUTexture(copy, &source, &target, false);
                }
                SDL3.SDL_EndGPUCopyPass(copy);
                if (_mipLevelCount > 1)
                    SDL3.SDL_GenerateMipmapsForGPUTexture(commandBuffer, Handle);
                _uploaded = true;
                return true;
            }

            public void ReleaseUploadStorage()
            {
                if (_transfer != null)
                    SDL3.SDL_ReleaseGPUTransferBuffer(_device.Handle, _transfer);
                _transfer = null;
            }

            public void InvalidateUpload() => _uploaded = false;

            public void Dispose()
            {
                ReleaseUploadStorage();
                if (Handle != null)
                    SDL3.SDL_ReleaseGPUTexture(_device.Handle, Handle);
                Handle = null;
            }

            private static ulong ComputeFingerprint(
                IReadOnlyList<ReadOnlyMemory<byte>> faces)
            {
                ulong hash = 14695981039346656037UL;
                foreach (ReadOnlyMemory<byte> face in faces)
                {
                    foreach (byte value in face.Span)
                    {
                        hash ^= value;
                        hash *= 1099511628211UL;
                    }
                }
                return hash;
            }
        }

        private sealed class GpuTexture : IDisposable, IUploadResource
        {
            private readonly SdlGpuDevice _device;
            private readonly RenderTexturePixels _pixels;
            private readonly uint _mipLevelCount;
            private SDL_GPUTransferBuffer* _transfer;
            private bool _uploadPrepared;
            private bool _uploaded;
            public SDL_GPUTexture* Handle { get; private set; }
            public long UploadBytes => _pixels.Rgba8.Length;
            public bool Matches(RenderTexturePixels pixels, bool mipmapped)
                => _pixels.Revision == pixels.Revision
                    && _pixels.Width == pixels.Width
                    && _pixels.Height == pixels.Height
                    && _mipLevelCount == SdlGpuTextureQuality.MipLevelCount(
                        pixels.Width, pixels.Height, mipmapped);

            private GpuTexture(SdlGpuDevice device, RenderTexturePixels pixels,
                bool mipmapped)
            {
                _device = device;
                _pixels = pixels;
                _mipLevelCount = SdlGpuTextureQuality.MipLevelCount(
                    pixels.Width, pixels.Height, mipmapped);
                try
                {
                    SDL_GPUTextureCreateInfo textureInfo = new()
                    {
                        type = SDL_GPUTextureType.SDL_GPU_TEXTURETYPE_2D,
                        format = SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R8G8B8A8_UNORM,
                        usage = SdlGpuTextureQuality.SceneTextureUsage(mipmapped),
                        width = checked((uint)pixels.Width), height = checked((uint)pixels.Height),
                        layer_count_or_depth = 1, num_levels = _mipLevelCount,
                        sample_count = SDL_GPUSampleCount.SDL_GPU_SAMPLECOUNT_1
                    };
                    Handle = SDL3.SDL_CreateGPUTexture(device.Handle, &textureInfo);
                    SDL_GPUTransferBufferCreateInfo transferInfo = new()
                    {
                        usage = SDL_GPUTransferBufferUsage.SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD,
                        size = checked((uint)pixels.Rgba8.Length)
                    };
                    _transfer = SDL3.SDL_CreateGPUTransferBuffer(device.Handle, &transferInfo);
                    if (Handle == null || _transfer == null) throw new InvalidOperationException($"SDL scene texture allocation failed: {SDL3.SDL_GetError()}");
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            public static GpuTexture Create(SdlGpuDevice device, RenderTexturePixels pixels,
                bool mipmapped) => new(device, pixels, mipmapped);

            public bool EnsureUploaded(SDL_GPUCommandBuffer* commandBuffer)
            {
                if (_uploaded) return false;
                PrepareUploadStorage();
                return EncodePreparedUpload(commandBuffer);
            }

            public void PrepareUploadStorage()
            {
                if (_uploaded || _uploadPrepared) return;
                if (_transfer == null)
                {
                    SDL_GPUTransferBufferCreateInfo info = new()
                    {
                        usage = SDL_GPUTransferBufferUsage.SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD,
                        size = checked((uint)_pixels.Rgba8.Length)
                    };
                    _transfer = SDL3.SDL_CreateGPUTransferBuffer(_device.Handle, &info);
                    if (_transfer == null) throw new InvalidOperationException($"SDL scene texture upload allocation failed: {SDL3.SDL_GetError()}");
                }
                IntPtr memory = SDL3.SDL_MapGPUTransferBuffer(_device.Handle, _transfer, false);
                if (memory == IntPtr.Zero) throw new InvalidOperationException($"SDL scene texture map failed: {SDL3.SDL_GetError()}");
                try
                {
                    SdlGpuMappedMemoryCopy.Copy(_pixels.Rgba8, memory);
                }
                finally { SDL3.SDL_UnmapGPUTransferBuffer(_device.Handle, _transfer); }
                _uploadPrepared = true;
            }

            public bool EncodePreparedUpload(SDL_GPUCommandBuffer* commandBuffer)
            {
                if (_uploaded) return false;
                if (!_uploadPrepared || _transfer == null)
                {
                    throw new InvalidOperationException(
                        "SDL scene texture upload was not prepared.");
                }
                SDL_GPUCopyPass* copy = SDL3.SDL_BeginGPUCopyPass(commandBuffer);
                if (copy == null) throw new InvalidOperationException($"SDL scene texture copy pass failed: {SDL3.SDL_GetError()}");
                SDL_GPUTextureTransferInfo sourceInfo = new()
                {
                    transfer_buffer = _transfer, offset = 0,
                    pixels_per_row = checked((uint)_pixels.Width), rows_per_layer = checked((uint)_pixels.Height)
                };
                SDL_GPUTextureRegion target = new()
                {
                    texture = Handle, mip_level = 0, layer = 0, x = 0, y = 0, z = 0,
                    w = checked((uint)_pixels.Width), h = checked((uint)_pixels.Height), d = 1
                };
                SDL3.SDL_UploadToGPUTexture(copy, &sourceInfo, &target, false);
                SDL3.SDL_EndGPUCopyPass(copy);
                if (_mipLevelCount > 1)
                {
                    // SDL requires mip generation outside copy/render passes.
                    // _uploaded makes this exactly once per successful texture
                    // revision submission, and failed submissions invalidate it.
                    SDL3.SDL_GenerateMipmapsForGPUTexture(commandBuffer, Handle);
                }
                _uploaded = true;
                return true;
            }

            public void ReleaseUploadStorage()
            {
                if (_transfer != null) SDL3.SDL_ReleaseGPUTransferBuffer(_device.Handle, _transfer);
                _transfer = null;
                _uploadPrepared = false;
            }

            public void InvalidateUpload() => _uploaded = false;

            public void Dispose()
            {
                ReleaseUploadStorage();
                if (Handle != null) SDL3.SDL_ReleaseGPUTexture(_device.Handle, Handle);
                _transfer = null; Handle = null;
            }
        }
    }
}
