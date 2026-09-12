using System;
using System.Collections.Generic;
using OpenTK.Mathematics;
using MphRead.Mods;

namespace MphRead
{
    /// <summary>Immutable Enhanced fog parameters captured before scene submission.</summary>
    public readonly record struct RenderEnhancedFogState
    {
        public RenderEnhancedFogState(bool enabled, Vector3 color, float density,
            float height, float falloff)
        {
            if (!ShadowMath.IsFinite(color) || !float.IsFinite(density)
                || density < 0 || !float.IsFinite(height) || !float.IsFinite(falloff)
                || falloff < 0)
                throw new ArgumentOutOfRangeException(nameof(density));
            Enabled = enabled && density > 0;
            Color = color;
            Density = density;
            Height = height;
            Falloff = falloff;
        }

        public bool Enabled { get; }
        public Vector3 Color { get; }
        public float Density { get; }
        public float Height { get; }
        public float Falloff { get; }
        public static RenderEnhancedFogState Disabled => default;
        public static RenderEnhancedFogState FromEnvironment(
            EnhancedEnvironment environment, bool enabled)
            => new(enabled, environment.FogColor, environment.FogDensity,
                environment.FogHeight, environment.FogFalloff);
    }

    /// <summary>One immutable directional shadow selected and projected pre-draw.</summary>
    public readonly record struct RenderDirectionalShadowState
    {
        public RenderDirectionalShadowState(int sourceIndex, Vector3 direction,
            Matrix4 viewProjection, int mapSize, int pcfRadius)
        {
            if (sourceIndex is not (0 or 1))
                throw new ArgumentOutOfRangeException(nameof(sourceIndex));
            if (!ShadowMath.TryNormalize(direction, out Vector3 normalized))
                throw new ArgumentException("Shadow direction is invalid.", nameof(direction));
            for (int row = 0; row < 4; row++)
            for (int column = 0; column < 4; column++)
                if (!float.IsFinite(viewProjection[row, column]))
                    throw new ArgumentException("Shadow projection is invalid.",
                        nameof(viewProjection));
            ShadowQualitySettings settings = new(true, mapSize, pcfRadius);
            SourceIndex = sourceIndex;
            Direction = normalized;
            ViewProjection = viewProjection;
            MapSize = settings.MapSize;
            PcfRadius = settings.PcfRadius;
            Enabled = true;
        }

        public bool Enabled { get; }
        public int SourceIndex { get; }
        public Vector3 Direction { get; }
        public Matrix4 ViewProjection { get; }
        public int MapSize { get; }
        public int PcfRadius { get; }
        public static RenderDirectionalShadowState Disabled => default;
    }

    /// <summary>
    /// One immutable, backend-neutral cubemap captured with a scene frame.
    /// Face order matches SDL's cube-layer order: +X, -X, +Y, -Y, +Z, -Z.
    /// </summary>
    public sealed class RenderReflectionProbe
    {
        private readonly ReadOnlyMemory<byte>[] _faces;

        public RenderReflectionProbe(ReflectionCubemapAsset cubemap)
        {
            ArgumentNullException.ThrowIfNull(cubemap);
            Key = cubemap.Key;
            Dimension = cubemap.Dimension;
            _faces = new ReadOnlyMemory<byte>[6];
            foreach (ReflectionCubemapFace face in Enum.GetValues<ReflectionCubemapFace>())
            {
                if (!cubemap.Faces.TryGetValue(face, out ReflectionProbeFaceAsset? asset)
                    || asset.Dimension != Dimension)
                {
                    throw new ArgumentException("Reflection probe must contain six equal faces.",
                        nameof(cubemap));
                }
                _faces[(int)face] = asset.Rgba8;
            }
            ContentFingerprint = ComputeFingerprint(_faces);
        }

        public ReflectionProbeKey Key { get; }
        public int Dimension { get; }
        public int MipLevelCount => 1 + (int)MathF.Floor(MathF.Log2(Dimension));
        public ulong ContentFingerprint { get; }
        public IReadOnlyList<ReadOnlyMemory<byte>> Faces => _faces;

        private static ulong ComputeFingerprint(
            IReadOnlyList<ReadOnlyMemory<byte>> faces)
        {
            // Stable FNV-1a is sufficient for the local upload-generation
            // identity and avoids retaining another copy of the cube.
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

    /// <summary>
    /// A render-only point light captured from an already visible presentation
    /// object.  It is deliberately a value record: the backend never receives
    /// an entity, presentation, or other mutable gameplay owner.
    /// </summary>
    public readonly record struct RenderVisualLight
    {
        public RenderVisualLight(Vector3 position, Vector3 color, float radius,
            float intensity, int priority)
        {
            if (!IsFinite(position) || !IsFinite(color))
            {
                throw new ArgumentException("Visual light position and color must be finite.");
            }
            if (!float.IsFinite(radius) || radius <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(radius));
            }
            if (!float.IsFinite(intensity) || intensity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(intensity));
            }
            Position = position;
            Color = color;
            Radius = radius;
            Intensity = intensity;
            Priority = priority;
        }

        public Vector3 Position { get; }
        public Vector3 Color { get; }
        public float Radius { get; }
        public float Intensity { get; }
        public int Priority { get; }

        private static bool IsFinite(Vector3 value)
            => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    }

    /// <summary>
    /// Reusable, bounded per-frame submission storage.
    ///
    /// Submission objects and their maximum matrix stacks are allocated once
    /// and reset at the beginning of each picture.  A draw therefore does not
    /// allocate a matrix array, while the hard maximum makes an unexpected
    /// content spike visible instead of creating unbounded frame pressure.
    /// </summary>
    public sealed class RenderFrame
    {
        public const int DefaultCapacity = 200;
        public const int DefaultMaximumCapacity = 4096;
        public const int DefaultMaximumCaptureRequests = 8;
        public const int MaximumVisualLights = 32;
        public const int MatrixStackFloats = 16 * 31;

        private readonly List<DrawSubmission> _pool;
        private readonly List<DrawSubmission> _active;
        private readonly List<DrawSubmission> _opaque;
        private readonly List<DrawSubmission> _decals;
        private readonly List<DrawSubmission> _transparent;
        private readonly List<RenderHudSceneSubmission> _hudSceneItems;
        private readonly List<RenderOverlayCommand> _overlayCommands;
        private readonly List<RenderCaptureRequest> _captureRequests;
        private readonly List<RenderVisualLight> _visualLights;
        private readonly IReadOnlyList<RenderVisualLight> _visualLightsView;
        private readonly EnhancedDistortionSubmissionBuffer _distortionSubmissions
            = new();
        private readonly VisualLightCandidate[] _visualLightCandidates
            = new VisualLightCandidate[MaximumVisualLights];
        private readonly Dictionary<TextureIdentity, RenderTexturePixels> _textures = new();
        private readonly Dictionary<object, CpuMesh> _meshes
            = new(ReferenceEqualityComparer.Instance);
        private readonly int _maximumCapacity;
        private readonly int _maximumCaptureRequests;
        private int _count;
        private int _visualLightCandidateCount;
        private bool _sealed;

        public RenderFrame(int capacity = DefaultCapacity, int maximumCapacity = DefaultMaximumCapacity,
            int maximumCaptureRequests = DefaultMaximumCaptureRequests)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            if (maximumCapacity < capacity) throw new ArgumentOutOfRangeException(nameof(maximumCapacity));
            if (maximumCaptureRequests < 1) throw new ArgumentOutOfRangeException(nameof(maximumCaptureRequests));
            _maximumCapacity = maximumCapacity;
            _maximumCaptureRequests = maximumCaptureRequests;
            _pool = new List<DrawSubmission>(capacity);
            _active = new List<DrawSubmission>(capacity);
            _opaque = new List<DrawSubmission>(capacity);
            _decals = new List<DrawSubmission>();
            _transparent = new List<DrawSubmission>();
            _hudSceneItems = new List<RenderHudSceneSubmission>();
            _overlayCommands = new List<RenderOverlayCommand>();
            _captureRequests = new List<RenderCaptureRequest>(maximumCaptureRequests);
            _visualLights = new List<RenderVisualLight>(MaximumVisualLights);
            _visualLightsView = _visualLights.AsReadOnly();
            for (int i = 0; i < capacity; i++)
            {
                _pool.Add(new DrawSubmission(MatrixStackFloats));
            }
        }

        public int Capacity => _pool.Count;
        public int MaximumCapacity => _maximumCapacity;
        public int MaximumCaptureRequests => _maximumCaptureRequests;
        public int Count => _count;
        public IReadOnlyList<DrawSubmission> Submissions => _active;
        public IReadOnlyList<DrawSubmission> OpaqueItems => _opaque;
        public IReadOnlyList<DrawSubmission> DecalItems => _decals;
        public IReadOnlyList<DrawSubmission> TransparentItems => _transparent;
        /// <summary>
        /// HUD model draws kept separate from the six world passes. Legacy
        /// presets draw them into the scene before cel; Enhanced draws them
        /// after tone mapping so scene HDR cannot alter authored HUD values.
        /// </summary>
        public IReadOnlyList<RenderHudSceneSubmission> HudSceneItems => _hudSceneItems;
        public IReadOnlyList<RenderOverlayCommand> OverlayCommands => _overlayCommands;
        public IReadOnlyList<RenderCaptureRequest> CaptureRequests => _captureRequests;
        /// <summary>
        /// The bounded visual-light set in deterministic rank order. Legacy
        /// and profiled admission share the same fixed candidate buffer.
        /// </summary>
        public IReadOnlyList<RenderVisualLight> VisualLights => _visualLightsView;
        public IReadOnlyDictionary<TextureIdentity, RenderTexturePixels> TextureResources => _textures;
        public IReadOnlyDictionary<object, CpuMesh> MeshResources => _meshes;
        internal EnhancedDistortionSubmissionBatch DistortionSubmissions
            { get; private set; } = EnhancedDistortionSubmissionBatch.Empty;

        // The values below are copied by ScenePresentation while it is still
        // the sole producer of the frame. Backends only read this snapshot;
        // they never reach into Scene, players, HUD objects, or settings.
        public Matrix4 ViewMatrix { get; private set; } = Matrix4.Identity;
        public Matrix4 ViewInverseRotation { get; private set; } = Matrix4.Identity;
        public Matrix4 ViewInverseRotationY { get; private set; } = Matrix4.Identity;
        public Matrix4 ProjectionMatrix { get; private set; } = Matrix4.Identity;
        /// <summary>
        /// World-space position of the render-interpolated camera which produced
        /// <see cref="ViewMatrix"/>. Backends consume this explicit snapshot for
        /// view-dependent shading instead of reconstructing it from the matrix.
        /// </summary>
        public Vector3 CameraWorldPosition { get; private set; }
        public Vector2i DrawableSize { get; private set; }
        public Vector2i SceneTargetSize { get; private set; }
        public Vector4 ClearColor { get; private set; } = Vector4.UnitW;
        public Vector3 Light1Vector { get; private set; }
        public Vector3 Light1Color { get; private set; }
        public Vector3 Light2Vector { get; private set; }
        public Vector3 Light2Color { get; private set; }
        public bool HasFog { get; private set; }
        public Vector4 FogColor { get; private set; }
        public int FogOffset { get; private set; }
        public int FogSlope { get; private set; }
        /// <summary>
        /// Fixed, presentation-only exposure captured with the environment.
        /// VE2 deliberately has no automatic exposure so visibility cannot
        /// vary with frame history or presentation rate.
        /// </summary>
        public float Exposure { get; private set; } = EnhancedColorMath.DefaultExposure;
        public RenderColorGradeState ColorGrade { get; private set; }
            = RenderColorGradeState.Disabled;
        public RenderReflectionProbe? ReflectionProbe { get; private set; }
        public RenderEnhancedFogState EnhancedFog { get; private set; }
            = RenderEnhancedFogState.Disabled;
        public RenderDirectionalShadowState DirectionalShadow { get; private set; }
            = RenderDirectionalShadowState.Disabled;
        public RenderVisorState Visor { get; private set; }
            = RenderVisorState.Disabled;
        public RenderSkyState? Sky { get; private set; }
        public RenderFrameOptions Options { get; private set; }
        public RenderCelState CelState { get; private set; } = RenderCelState.Disabled;
        public RenderDisruptionState Disruption { get; private set; } = RenderDisruptionState.Disabled;
        public RenderCompositeState Composite { get; private set; }
        public RenderFadeState Fade { get; private set; } = RenderFadeState.None;

        public bool IsSealed => _sealed;

        public DrawSubmission Acquire()
        {
            if (_sealed) throw new InvalidOperationException("The render frame is already sealed.");
            if (_count >= _maximumCapacity)
            {
                throw new InvalidOperationException(
                    $"Render frame exceeded its {_maximumCapacity}-submission bound.");
            }
            if (_count == _pool.Count)
            {
                _pool.Add(new DrawSubmission(MatrixStackFloats));
            }
            DrawSubmission submission = _pool[_count++];
            submission.Reset();
            _active.Add(submission);
            return submission;
        }

        /// <summary>Add one fully populated submission in scene order.</summary>
        public void Add(DrawSubmission submission)
        {
            if (submission == null) throw new ArgumentNullException(nameof(submission));
            if (_sealed) throw new InvalidOperationException("The render frame is already sealed.");
            // Material is derived exactly once at the submission boundary.
            // Backends must use this value rather than reconstructing it from
            // the legacy fields (which may be mutated by compatibility code).
            if (Options.Quality.GraphicsPreset == GraphicsPreset.Enhanced
                && submission.EnhancedForceField.HasValue
                && !submission.BloomEligible)
            {
                submission.BloomStrength = RenderMaterial.ParticleBloomStrength;
                submission.BloomEligible = true;
            }
            submission.FreezeMaterial();
            if (Options.Quality.GraphicsPreset == GraphicsPreset.Enhanced
                && submission.Material.EnhancedForceField
                    is EnhancedForceFieldDrawState forceField
                && submission.GeometryIdentity != null
                && submission.Material.Alpha > 0
                && forceField.Profile.DistortionStrength > 0)
            {
                _distortionSubmissions.TryAdd(
                    EnhancedDistortionSubmission.FromDraw(
                    forceField.StableSourceKey, submission,
                    forceField.Profile.DistortionStrength
                        * Math.Clamp(submission.Material.Alpha, 0, 1),
                    forceField.Profile.FresnelPower,
                    forceField.Sample.NoisePhase));
            }
            switch (submission.Pass)
            {
                case RenderPassKind.Decal:
                    _decals.Add(submission);
                    break;
                case RenderPassKind.TransparentStencil:
                    _transparent.Add(submission);
                    break;
                default:
                    _opaque.Add(submission);
                    break;
            }
        }

        public void CaptureState(Matrix4 view, Matrix4 inverseRotation, Matrix4 inverseRotationY,
            Matrix4 projection, Vector3 cameraWorldPosition, Vector2i drawableSize,
            Vector2i sceneTargetSize, Vector4 clearColor,
            Vector3 light1Vector, Vector3 light1Color, Vector3 light2Vector, Vector3 light2Color,
            bool hasFog, Vector4 fogColor, int fogOffset, int fogSlope, RenderFrameOptions options,
            float exposure = EnhancedColorMath.DefaultExposure)
        {
            if (_sealed) throw new InvalidOperationException("The render frame is already sealed.");
            if (!float.IsFinite(exposure) || exposure <= 0)
                throw new ArgumentOutOfRangeException(nameof(exposure));
            ViewMatrix = view;
            ViewInverseRotation = inverseRotation;
            ViewInverseRotationY = inverseRotationY;
            ProjectionMatrix = projection;
            CameraWorldPosition = cameraWorldPosition;
            DrawableSize = drawableSize;
            SceneTargetSize = sceneTargetSize;
            ClearColor = clearColor;
            Light1Vector = light1Vector;
            Light1Color = light1Color;
            Light2Vector = light2Vector;
            Light2Color = light2Color;
            HasFog = hasFog;
            FogColor = fogColor;
            FogOffset = fogOffset;
            FogSlope = fogSlope;
            Exposure = exposure;
            Options = options;
        }

        public void CaptureTexture(RenderTexturePixels texture)
        {
            if (texture == null) throw new ArgumentNullException(nameof(texture));
            if (_sealed) throw new InvalidOperationException("The render frame is already sealed.");
            _textures[texture.Identity] = texture;
        }

        public void CaptureColorGrade(RenderColorGradeState state)
        {
            if (_sealed) throw new InvalidOperationException("The render frame is already sealed.");
            ColorGrade = state;
        }

        public void CaptureReflectionProbe(RenderReflectionProbe probe)
        {
            ArgumentNullException.ThrowIfNull(probe);
            if (_sealed) throw new InvalidOperationException("The render frame is already sealed.");
            ReflectionProbe = probe;
        }

        public void CaptureEnhancedFog(RenderEnhancedFogState state)
        {
            if (_sealed) throw new InvalidOperationException("The render frame is already sealed.");
            EnhancedFog = state;
        }

        public void CaptureDirectionalShadow(RenderDirectionalShadowState state)
        {
            if (_sealed) throw new InvalidOperationException("The render frame is already sealed.");
            DirectionalShadow = state;
        }

        public void CaptureVisor(RenderVisorState state)
        {
            if (_sealed) throw new InvalidOperationException("The render frame is already sealed.");
            Visor = state;
        }

        public void CaptureSky(RenderSkyState sky)
        {
            ArgumentNullException.ThrowIfNull(sky);
            if (_sealed) throw new InvalidOperationException("The render frame is already sealed.");
            foreach (TextureIdentity identity in sky.TextureIdentities)
            {
                if (identity.Source is not EnhancedSkyTextureAsset asset)
                {
                    throw new ArgumentException(
                        "Sky state contains a texture outside its validated pack.",
                        nameof(sky));
                }
                CaptureTexture(asset.Pixels);
            }
            Sky = sky;
        }

        public void AddHudSceneSubmission(RenderHudSceneSubmission submission)
        {
            if (submission == null) throw new ArgumentNullException(nameof(submission));
            if (_sealed) throw new InvalidOperationException("The render frame is already sealed.");
            _hudSceneItems.Add(submission);
        }

        public void AddOverlayCommand(RenderOverlayCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (_sealed) throw new InvalidOperationException("The render frame is already sealed.");
            _overlayCommands.Add(command);
        }

        public void AddCaptureRequest(RenderCaptureRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (_sealed) throw new InvalidOperationException("The render frame is already sealed.");
            if (_captureRequests.Count >= _maximumCaptureRequests)
            {
                throw new InvalidOperationException(
                    $"Render frame exceeded its {_maximumCaptureRequests}-capture-request bound.");
            }
            _captureRequests.Add(request);
        }

        /// <summary>
        /// Compatibility admission for continuously submitted legacy lights.
        /// Exact duplicate values collapse, while distinct values remain
        /// separate even if their compact compatibility keys collide.
        /// </summary>
        public bool AddVisualLight(RenderVisualLight light)
            => AddVisualLightCandidateCore(VisualLightCandidate.FromLegacy(light));

        /// <summary>
        /// Rank one presentation-only light into the fixed bounded Enhanced
        /// set. Selection uses the captured interpolated camera and stable
        /// source key; no entity or mutable gameplay owner enters the frame.
        /// </summary>
        public bool AddVisualLightCandidate(VisualLightCandidate candidate)
        {
            if (_sealed) throw new InvalidOperationException("The render frame is already sealed.");
            RenderQualitySnapshot quality = Options.Quality;
            if (quality.GraphicsPreset != GraphicsPreset.Enhanced
                || !quality.DynamicVisualLights)
            {
                return false;
            }
            return AddVisualLightCandidateCore(candidate);
        }

        private bool AddVisualLightCandidateCore(VisualLightCandidate candidate)
        {
            if (_sealed) throw new InvalidOperationException("The render frame is already sealed.");
            if (!VisualLightSelection.TryInsert(candidate, CameraWorldPosition,
                _visualLightCandidates, ref _visualLightCandidateCount))
            {
                return false;
            }

            _visualLights.Clear();
            for (int i = 0; i < _visualLightCandidateCount; i++)
            {
                VisualLightCandidate selected = _visualLightCandidates[i];
                _visualLights.Add(selected.RenderLight);
            }
            return true;
        }

        public void CaptureCelState(RenderCelState state)
        {
            if (_sealed) throw new InvalidOperationException("The render frame is already sealed.");
            CelState = state;
        }

        public void CaptureDisruption(RenderDisruptionState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (_sealed) throw new InvalidOperationException("The render frame is already sealed.");
            Disruption = state;
        }

        public void CaptureComposite(RenderCompositeState state)
        {
            if (_sealed) throw new InvalidOperationException("The render frame is already sealed.");
            Composite = state;
        }

        public void CaptureFade(RenderFadeState state)
        {
            if (_sealed) throw new InvalidOperationException("The render frame is already sealed.");
            Fade = state;
        }

        public void CaptureMesh(object identity, CpuMesh mesh)
        {
            if (identity == null) throw new ArgumentNullException(nameof(identity));
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            if (_sealed) throw new InvalidOperationException("The render frame is already sealed.");
            _meshes[identity] = mesh;
        }

        public void Seal()
        {
            DistortionSubmissions = _distortionSubmissions.Seal();
            _sealed = true;
        }

        public void Reset()
        {
            for (int i = 0; i < _count; i++)
            {
                _pool[i].Reset();
            }
            _active.Clear();
            _opaque.Clear();
            _decals.Clear();
            _transparent.Clear();
            _hudSceneItems.Clear();
            _overlayCommands.Clear();
            _captureRequests.Clear();
            _visualLights.Clear();
            _visualLightCandidateCount = 0;
            _distortionSubmissions.Clear();
            DistortionSubmissions = EnhancedDistortionSubmissionBatch.Empty;
            _textures.Clear();
            _meshes.Clear();
            _count = 0;
            _sealed = false;
            ViewMatrix = Matrix4.Identity;
            ViewInverseRotation = Matrix4.Identity;
            ViewInverseRotationY = Matrix4.Identity;
            ProjectionMatrix = Matrix4.Identity;
            CameraWorldPosition = Vector3.Zero;
            DrawableSize = default;
            SceneTargetSize = default;
            ClearColor = Vector4.UnitW;
            Light1Vector = Vector3.Zero;
            Light1Color = Vector3.Zero;
            Light2Vector = Vector3.Zero;
            Light2Color = Vector3.Zero;
            HasFog = false;
            FogColor = Vector4.Zero;
            FogOffset = 0;
            FogSlope = 0;
            Exposure = EnhancedColorMath.DefaultExposure;
            ColorGrade = RenderColorGradeState.Disabled;
            ReflectionProbe = null;
            EnhancedFog = RenderEnhancedFogState.Disabled;
            DirectionalShadow = RenderDirectionalShadowState.Disabled;
            Visor = RenderVisorState.Disabled;
            Sky = null;
            Options = default;
            CelState = RenderCelState.Disabled;
            Disruption = RenderDisruptionState.Disabled;
            Composite = default;
            Fade = RenderFadeState.None;
        }
    }

    public readonly record struct RenderFrameOptions(
        bool ShowTextures,
        bool ShowColors,
        bool Wireframe,
        bool FaceCulling,
        bool Filtering,
        bool Lighting,
        bool Fog,
        bool CelShading,
        int CelBands,
        float CelEdge,
        int VolumeEdges,
        bool ShowInvisible,
        bool NoLines,
        RenderQualitySnapshot Quality = default);
}
