using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace MphRead
{
    /// <summary>
    /// The presentation stages which are not part of the six world passes.
    /// Keeping these boundaries explicit is important. Original/Performance
    /// keep HUD models inside the scene target before cel shading; Enhanced
    /// composites them after scene tone mapping. HUD quads and the fade remain
    /// after the scene composite in every mode.
    /// </summary>
    public enum RenderPresentationStage : byte
    {
        HudScene,
        Cel,
        SceneComposite,
        HudOverlay,
        SpectatorOverlay,
        ReplayOverlay,
        Fade
    }

    public enum RenderOverlayKind : byte
    {
        StageMarker,
        HudLayer,
        HudObject,
        FlatBox,
        Crosshair,
        RadialSector,
        HudTexture,
        HudGeometry,
        Fade
    }

    public readonly record struct HudGeometryVertex(Vector2 Position, Vector4 Color);

    public enum RenderCompositeFilter : byte
    {
        Nearest,
        Linear
    }

    /// <summary>A copied 2D/clip-space vertex for a full-resolution overlay.</summary>
    public readonly struct RenderOverlayVertex : IEquatable<RenderOverlayVertex>
    {
        public Vector3 Position { get; }
        public Vector2 TexCoord { get; }
        public Vector4 Color { get; }

        public RenderOverlayVertex(Vector3 position, Vector2 texCoord, Vector4 color)
        {
            Position = position;
            TexCoord = texCoord;
            Color = color;
        }

        public bool Equals(RenderOverlayVertex other)
            => Position == other.Position && TexCoord == other.TexCoord && Color == other.Color;
        public override bool Equals(object? obj) => obj is RenderOverlayVertex other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Position, TexCoord, Color);
        public static bool operator ==(RenderOverlayVertex left, RenderOverlayVertex right) => left.Equals(right);
        public static bool operator !=(RenderOverlayVertex left, RenderOverlayVertex right) => !left.Equals(right);
    }

    /// <summary>
    /// Immutable command for a full-resolution overlay. The renderer records
    /// final vertices and source identities rather than retaining a mutable
    /// HudObjectInstance, LayerInfo, Scene, or PlayerPresentation.
    /// </summary>
    public sealed class RenderOverlayCommand
    {
        private readonly RenderOverlayVertex[] _vertices;
        private readonly IReadOnlyList<RenderOverlayVertex> _verticesView;

        public RenderOverlayCommand(RenderOverlayKind kind, IReadOnlyList<RenderOverlayVertex>? vertices = null,
            TextureIdentity? texture = null, TextureIdentity? maskTexture = null, Vector4? color = null,
            float alpha = 1, bool useTexture = false, bool useMask = false, int sourceBindingId = -1,
            int mode = 0, float scale = 1, float scaleX = 0, float scaleY = 0,
            float shiftX = 0, float shiftY = 0,
            RenderPresentationStage stage = RenderPresentationStage.HudOverlay)
        {
            if (!float.IsFinite(alpha) || alpha < 0) throw new ArgumentOutOfRangeException(nameof(alpha));
            if (!float.IsFinite(scale) || scale < 0) throw new ArgumentOutOfRangeException(nameof(scale));
            Kind = kind;
            _vertices = vertices == null ? Array.Empty<RenderOverlayVertex>() : Copy(vertices);
            _verticesView = _vertices.Length == 0 ? _vertices : Array.AsReadOnly(_vertices);
            Texture = texture;
            MaskTexture = maskTexture;
            Color = color ?? Vector4.One;
            Alpha = alpha;
            UseTexture = useTexture;
            UseMask = useMask;
            SourceBindingId = sourceBindingId;
            Mode = mode;
            Scale = scale;
            ScaleX = scaleX;
            ScaleY = scaleY;
            ShiftX = shiftX;
            ShiftY = shiftY;
            Stage = stage;
        }

        public RenderOverlayKind Kind { get; }
        public IReadOnlyList<RenderOverlayVertex> Vertices => _verticesView;
        public TextureIdentity? Texture { get; }
        public TextureIdentity? MaskTexture { get; }
        public Vector4 Color { get; }
        public float Alpha { get; }
        public bool UseTexture { get; }
        public bool UseMask { get; }
        public int SourceBindingId { get; }
        public int Mode { get; }
        public float Scale { get; }
        public float ScaleX { get; }
        public float ScaleY { get; }
        public float ShiftX { get; }
        public float ShiftY { get; }
        public RenderPresentationStage Stage { get; }

        public static RenderOverlayCommand StageMarker(RenderPresentationStage stage)
            => new RenderOverlayCommand(RenderOverlayKind.StageMarker, stage: stage);

        private static RenderOverlayVertex[] Copy(IReadOnlyList<RenderOverlayVertex> vertices)
        {
            var copy = new RenderOverlayVertex[vertices.Count];
            for (int i = 0; i < copy.Length; i++) copy[i] = vertices[i];
            return copy;
        }
    }

    /// <summary>
    /// Immutable HUD model draw. Geometry and texture source identities are
    /// resolved by the frame resource tables; no device handle or mutable
    /// model instance crosses the renderer/backend boundary.
    /// </summary>
    public sealed class RenderHudSceneSubmission
    {
        private readonly float[] _matrixStack;
        private readonly Vector3[] _points;
        private readonly IReadOnlyList<float> _matrixStackView;
        private readonly IReadOnlyList<Vector3> _pointsView;

        public RenderHudSceneSubmission(RenderMaterial material, RenderPrimitive primitive, int polygonId,
            float alpha, Matrix4 transform, int matrixStackCount, IReadOnlyList<float>? matrixStack,
            object? geometryIdentity, TextureIdentity? textureIdentity, LightInfo lightInfo,
            Matrix4 viewMatrix, Matrix4 projectionMatrix, CpuMesh? inlineMesh = null,
            IReadOnlyList<Vector3>? points = null, int itemCount = 0, Vector4? currentColor = null)
        {
            if (matrixStackCount < 0) throw new ArgumentOutOfRangeException(nameof(matrixStackCount));
            int stackLength = checked(matrixStackCount * 16);
            if (stackLength > 0 && (matrixStack == null || matrixStack.Count < stackLength))
            {
                throw new ArgumentException("The HUD matrix stack is shorter than matrixStackCount.", nameof(matrixStack));
            }

            Material = material;
            Primitive = primitive;
            PolygonId = polygonId;
            Alpha = alpha;
            Transform = transform;
            MatrixStackCount = matrixStackCount;
            _matrixStack = stackLength == 0 ? Array.Empty<float>() : Copy(matrixStack!, stackLength);
            _matrixStackView = _matrixStack.Length == 0 ? _matrixStack : Array.AsReadOnly(_matrixStack);
            GeometryIdentity = geometryIdentity;
            TextureIdentity = textureIdentity;
            CurrentColor = currentColor ?? Vector4.One;
            LightInfo = lightInfo;
            ViewMatrix = viewMatrix;
            ProjectionMatrix = projectionMatrix;
            InlineMesh = inlineMesh == null ? null : new CpuMesh(
                (RenderVertex[])inlineMesh.Vertices.Clone(),
                (int[])inlineMesh.TriangleIndices.Clone(),
                (int[])inlineMesh.LineIndices.Clone());
            _points = points == null ? Array.Empty<Vector3>() : Copy(points);
            _pointsView = _points.Length == 0 ? _points : Array.AsReadOnly(_points);
            ItemCount = itemCount;
        }

        public RenderMaterial Material { get; }
        public RenderPrimitive Primitive { get; }
        public int PolygonId { get; }
        public float Alpha { get; }
        public Matrix4 Transform { get; }
        public int MatrixStackCount { get; }
        public IReadOnlyList<float> MatrixStack => _matrixStackView;
        public object? GeometryIdentity { get; }
        public TextureIdentity? TextureIdentity { get; }
        /// <summary>
        /// Fixed-function current colour for this HUD model draw. It is a
        /// modulation/current-vertex value, not a material colour replacement;
        /// uncoloured display-list vertices use it while explicit vertex
        /// colours remain authoritative.
        /// </summary>
        public Vector4 CurrentColor { get; }
        public LightInfo LightInfo { get; }
        public Matrix4 ViewMatrix { get; }
        public Matrix4 ProjectionMatrix { get; }
        public CpuMesh? InlineMesh { get; }
        public IReadOnlyList<Vector3> Points => _pointsView;
        public int ItemCount { get; }

        public static RenderHudSceneSubmission FromSubmission(DrawSubmission submission,
            Matrix4 viewMatrix, Matrix4 projectionMatrix, CpuMesh? inlineMesh = null)
        {
            if (submission == null) throw new ArgumentNullException(nameof(submission));
            submission.FreezeMaterial();
            return new RenderHudSceneSubmission(submission.Material, submission.Primitive,
                submission.PolygonId, submission.Alpha, submission.Transform, submission.MatrixStackCount,
                submission.MatrixStack, submission.GeometryIdentity, submission.TextureIdentity,
                submission.LightInfo, viewMatrix, projectionMatrix, inlineMesh,
                submission.Points, submission.ItemCount);
        }

        private static float[] Copy(IReadOnlyList<float> values, int count)
        {
            var copy = new float[count];
            for (int i = 0; i < count; i++) copy[i] = values[i];
            return copy;
        }

        private static Vector3[] Copy(IReadOnlyList<Vector3> values)
        {
            var copy = new Vector3[values.Count];
            for (int i = 0; i < copy.Length; i++) copy[i] = values[i];
            return copy;
        }
    }

    public readonly record struct RenderCelState(
        bool Enabled,
        float Outline,
        int Bands,
        Vector2 TexelSize,
        float NearPlane,
        float FarPlane,
        float DepthQuantum)
    {
        public static RenderCelState Disabled => new(false, 0, 0, Vector2.Zero, 0, 0, 0);
    }

    /// <summary>Copied scene disruption and whiteout inputs for one picture.</summary>
    public sealed class RenderDisruptionState
    {
        private readonly float[] _shiftTable;
        private readonly float[] _whiteoutTable;
        private readonly IReadOnlyList<float> _shiftTableView;
        private readonly IReadOnlyList<float> _whiteoutTableView;

        public RenderDisruptionState(bool enabled, float shiftFactor, int shiftIndex, float lerpFactor,
            float whiteoutFactor, IReadOnlyList<float>? shiftTable, IReadOnlyList<float>? whiteoutTable)
        {
            if (!float.IsFinite(shiftFactor) || !float.IsFinite(lerpFactor) || !float.IsFinite(whiteoutFactor))
            {
                throw new ArgumentException("Disruption factors must be finite.");
            }
            _shiftTable = Copy(shiftTable, expectedCount: 64);
            _whiteoutTable = Copy(whiteoutTable, expectedCount: 192);
            _shiftTableView = _shiftTable.Length == 0 ? _shiftTable : Array.AsReadOnly(_shiftTable);
            _whiteoutTableView = _whiteoutTable.Length == 0 ? _whiteoutTable : Array.AsReadOnly(_whiteoutTable);
            Enabled = enabled;
            ShiftFactor = shiftFactor;
            ShiftIndex = shiftIndex;
            LerpFactor = lerpFactor;
            WhiteoutFactor = whiteoutFactor;
        }

        public bool Enabled { get; }
        public float ShiftFactor { get; }
        public int ShiftIndex { get; }
        public float LerpFactor { get; }
        public float WhiteoutFactor { get; }
        public IReadOnlyList<float> ShiftTable => _shiftTableView;
        public IReadOnlyList<float> WhiteoutTable => _whiteoutTableView;

        public static RenderDisruptionState Disabled { get; }
            = new RenderDisruptionState(false, 0, 0, 0, 0, null, null);

        private static float[] Copy(IReadOnlyList<float>? values, int expectedCount)
        {
            var copy = new float[expectedCount];
            if (values == null) return copy;
            if (values.Count != expectedCount)
            {
                throw new ArgumentException($"Expected {expectedCount} disruption values, got {values.Count}.", nameof(values));
            }
            for (int i = 0; i < copy.Length; i++) copy[i] = values[i];
            return copy;
        }
    }

    /// <summary>Optional device-pixel destination for tools that share one window with native UI.</summary>
    public readonly record struct RenderDestinationViewport(int X, int Y, int Width, int Height)
    {
        public bool IsValid => X >= 0 && Y >= 0 && Width > 0 && Height > 0;
    }

    public readonly record struct RenderCompositeState(
        Vector2i DrawableSize,
        Vector2i SceneTargetSize,
        RenderCompositeFilter Filter,
        bool ClearDestination,
        RenderDestinationViewport? DestinationViewport = null);

    public readonly record struct RenderFadeState(
        bool Active,
        FadeType Type,
        float Color,
        bool FadeIn,
        float Percent,
        float Coverage)
    {
        public static RenderFadeState None => new(false, FadeType.None, 0, false, 0, 0);
    }
}
