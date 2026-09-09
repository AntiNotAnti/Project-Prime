using System;
using System.Collections.Generic;
using OpenTK.Mathematics;
using MphRead.Mods;

namespace MphRead
{
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
        public const int MaximumVisualLights = 8;
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
        private readonly Dictionary<TextureIdentity, RenderTexturePixels> _textures = new();
        private readonly Dictionary<object, CpuMesh> _meshes
            = new(ReferenceEqualityComparer.Instance);
        private readonly int _maximumCapacity;
        private readonly int _maximumCaptureRequests;
        private int _count;
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
        /// HUD model draws which stay in the scene target. They are deliberately
        /// separate from the world pass lists so the cel pass can remain between
        /// these draws and the full-resolution HUD overlays.
        /// </summary>
        public IReadOnlyList<RenderHudSceneSubmission> HudSceneItems => _hudSceneItems;
        public IReadOnlyList<RenderOverlayCommand> OverlayCommands => _overlayCommands;
        public IReadOnlyList<RenderCaptureRequest> CaptureRequests => _captureRequests;
        /// <summary>
        /// The bounded, ordered visual-light set.  When full, a candidate
        /// replaces the first lowest-priority light only when its priority is
        /// strictly higher; equal-priority candidates retain arrival order.
        /// </summary>
        public IReadOnlyList<RenderVisualLight> VisualLights => _visualLightsView;
        public IReadOnlyDictionary<TextureIdentity, RenderTexturePixels> TextureResources => _textures;
        public IReadOnlyDictionary<object, CpuMesh> MeshResources => _meshes;

        // The values below are copied by ScenePresentation while it is still
        // the sole producer of the frame. Backends only read this snapshot;
        // they never reach into Scene, players, HUD objects, or settings.
        public Matrix4 ViewMatrix { get; private set; } = Matrix4.Identity;
        public Matrix4 ViewInverseRotation { get; private set; } = Matrix4.Identity;
        public Matrix4 ViewInverseRotationY { get; private set; } = Matrix4.Identity;
        public Matrix4 ProjectionMatrix { get; private set; } = Matrix4.Identity;
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
            submission.FreezeMaterial();
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
            Matrix4 projection, Vector2i drawableSize, Vector2i sceneTargetSize, Vector4 clearColor,
            Vector3 light1Vector, Vector3 light1Color, Vector3 light2Vector, Vector3 light2Color,
            bool hasFog, Vector4 fogColor, int fogOffset, int fogSlope, RenderFrameOptions options)
        {
            if (_sealed) throw new InvalidOperationException("The render frame is already sealed.");
            ViewMatrix = view;
            ViewInverseRotation = inverseRotation;
            ViewInverseRotationY = inverseRotationY;
            ProjectionMatrix = projection;
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
            Options = options;
        }

        public void CaptureTexture(RenderTexturePixels texture)
        {
            if (texture == null) throw new ArgumentNullException(nameof(texture));
            if (_sealed) throw new InvalidOperationException("The render frame is already sealed.");
            _textures[texture.Identity] = texture;
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
        /// Admit one render-only light without allowing content spikes to
        /// grow the frame.  The first lowest-priority slot wins replacement,
        /// which makes ties stable and independent of dictionary ordering.
        /// </summary>
        public bool AddVisualLight(RenderVisualLight light)
        {
            if (_sealed) throw new InvalidOperationException("The render frame is already sealed.");
            if (_visualLights.Count < MaximumVisualLights)
            {
                _visualLights.Add(light);
                return true;
            }

            int replacement = 0;
            int lowestPriority = _visualLights[0].Priority;
            for (int i = 1; i < _visualLights.Count; i++)
            {
                int priority = _visualLights[i].Priority;
                if (priority < lowestPriority)
                {
                    lowestPriority = priority;
                    replacement = i;
                }
            }
            if (light.Priority <= lowestPriority)
            {
                return false;
            }
            _visualLights[replacement] = light;
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

        public void Seal() => _sealed = true;

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
            _textures.Clear();
            _meshes.Clear();
            _count = 0;
            _sealed = false;
            ViewMatrix = Matrix4.Identity;
            ViewInverseRotation = Matrix4.Identity;
            ViewInverseRotationY = Matrix4.Identity;
            ProjectionMatrix = Matrix4.Identity;
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
