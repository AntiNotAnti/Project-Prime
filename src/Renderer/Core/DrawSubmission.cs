using System;
using OpenTK.Mathematics;

namespace MphRead
{
    /// <summary>
    /// Client-owned, backend-neutral description of one draw.
    ///
    /// Legacy resource handles are deliberately not part of this type.  The
    /// current compatibility renderer keeps those handles in a sidecar keyed
    /// by this submission while the portable contract carries only source
    /// identities.
    /// </summary>
    public sealed class DrawSubmission
    {
        public RenderPrimitive Primitive { get; set; }
        public int PolygonId { get; set; }
        public float Alpha { get; set; }
        public PolygonMode PolygonMode { get; set; }
        public RenderMode RenderMode { get; set; }
        public CullingMode CullingMode { get; set; }
        public BillboardMode BillboardMode { get; set; }
        public bool Wireframe { get; set; }
        public bool Lighting { get; set; }
        public bool NoLines { get; set; }
        /// <summary>
        /// Whether this world draw contributes to the enhanced directional
        /// shadow map. Presentation-only shells can opt out without changing
        /// their normal scene pass.
        /// </summary>
        public bool CastsDirectionalShadow { get; set; }
        public Vector3 Diffuse { get; set; }
        public Vector3 Ambient { get; set; }
        public Vector3 Specular { get; set; }
        public Vector3 Emission { get; set; }
        /// <summary>
        /// Neutral bloom metadata materialized with the submission. Model
        /// draws derive this from their existing emission; particles and
        /// trails set a restrained value at the presentation boundary.
        /// </summary>
        public bool BloomEligible { get; internal set; }
        public float BloomStrength { get; internal set; }
        public LightInfo LightInfo { get; set; }
        public TexgenMode TexgenMode { get; set; }
        public RepeatMode XRepeat { get; set; }
        public RepeatMode YRepeat { get; set; }
        public bool HasTexture { get; set; }
        public TextureIdentity? TextureIdentity { get; set; }
        /// <summary>Stable authoring identity, when the presentation source can prove one.</summary>
        public TextureAssetKey? TextureAssetKey { get; set; }
        /// <summary>Optional pack material resolved before the frame is sealed.</summary>
        public EnhancedMaterial? EnhancedMaterial { get; internal set; }
        /// <summary>Optional player skin layer, resolved after the base enhancement pack.</summary>
        public CosmeticMaterialOverride? CosmeticMaterialOverride { get; set; }
        /// <summary>Authoritative gameplay feedback that must remain visually dominant.</summary>
        public GameplayMaterialFeedback GameplayMaterialFeedback { get; set; }
        /// <summary>Explicit authored depth-fade opt-in for this particle draw.</summary>
        public SoftParticleProfile? SoftParticleProfile { get; internal set; }
        internal EnhancedBeamDrawState? EnhancedBeam { get; set; }
        internal EnhancedForceFieldDrawState? EnhancedForceField { get; set; }
        public Matrix4 TexcoordMatrix { get; set; }
        public Matrix4 Transform { get; set; }
        public object? GeometryIdentity { get; set; }
        public int MatrixStackCount { get; set; }
        public float[] MatrixStack { get; }
        public Vector4? OverrideColor { get; set; }
        public Vector4? PaletteOverride { get; set; }
        public Vector3[] Points { get; set; }
        // Number of segments for morph-ball trails, or total for other trails.
        public int ItemCount { get; set; }
        public float ScaleS { get; set; }
        public float ScaleT { get; set; }

        // Diagnostic N-gon edge colour is resolved while the frontend builds
        // the volume submission. Keep the mutable staging value separate from
        // the value consumed by a backend after RenderFrame.Add has frozen the
        // draw, just like Material below.
        private static readonly Vector4 DefaultEdgeColor = new(1f, 0f, 0f, 1f);
        public Vector4 EdgeColor { get; internal set; }
        public Vector4 FrozenEdgeColor { get; private set; }

        public RenderMaterial Material { get; private set; }
        public RenderPassKind Pass { get; set; }

        public DrawSubmission(int matrixStackFloats = RenderFrame.MatrixStackFloats)
        {
            if (matrixStackFloats < 0) throw new ArgumentOutOfRangeException(nameof(matrixStackFloats));
            MatrixStack = new float[matrixStackFloats];
            Points = Array.Empty<Vector3>();
            Transform = Matrix4.Identity;
            TexcoordMatrix = Matrix4.Identity;
            EdgeColor = DefaultEdgeColor;
            FrozenEdgeColor = DefaultEdgeColor;
            Pass = RenderPassKind.Opaque;
        }

        public void Reset()
        {
            Primitive = RenderPrimitive.Mesh;
            PolygonId = 0;
            Alpha = 1;
            PolygonMode = PolygonMode.Modulate;
            RenderMode = RenderMode.Normal;
            CullingMode = CullingMode.Back;
            BillboardMode = BillboardMode.None;
            Wireframe = false;
            Lighting = false;
            NoLines = false;
            CastsDirectionalShadow = true;
            Diffuse = Vector3.Zero;
            Ambient = Vector3.Zero;
            Specular = Vector3.Zero;
            Emission = Vector3.Zero;
            BloomEligible = false;
            BloomStrength = 0;
            LightInfo = LightInfo.Zero;
            TexgenMode = TexgenMode.None;
            XRepeat = RepeatMode.Clamp;
            YRepeat = RepeatMode.Clamp;
            HasTexture = false;
            TextureIdentity = null;
            TextureAssetKey = null;
            EnhancedMaterial = null;
            CosmeticMaterialOverride = null;
            GameplayMaterialFeedback = GameplayMaterialFeedback.None;
            SoftParticleProfile = null;
            EnhancedBeam = null;
            EnhancedForceField = null;
            TexcoordMatrix = Matrix4.Identity;
            Transform = Matrix4.Identity;
            GeometryIdentity = null;
            MatrixStackCount = 0;
            OverrideColor = null;
            PaletteOverride = null;
            Points = Array.Empty<Vector3>();
            ItemCount = 0;
            ScaleS = 1;
            ScaleT = 1;
            EdgeColor = DefaultEdgeColor;
            FrozenEdgeColor = DefaultEdgeColor;
            Material = default;
            Pass = RenderPassKind.Opaque;
        }

        /// <summary>
        /// Materialize the portable material once, after all legacy-facing
        /// submission fields have been populated. This is intentionally
        /// idempotent so compatibility callers can use it at either the
        /// scene-add or frame-seal boundary without diverging state.
        /// </summary>
        internal void FreezeMaterial()
        {
            if (Primitive == RenderPrimitive.Mesh && !BloomEligible)
            {
                BloomStrength = RenderMaterial.ModelBloomStrength(Emission);
                BloomEligible = BloomStrength > 0;
            }
            else if (!BloomEligible || !float.IsFinite(BloomStrength)
                || BloomStrength <= 0)
            {
                BloomStrength = 0;
                BloomEligible = false;
            }
            else BloomStrength = Math.Clamp(BloomStrength, 0, 1);
            Material = RenderMaterial.FromSubmission(this);
            FrozenEdgeColor = EdgeColor;
            Pass = RenderPassClassifier.Classify(Material);
        }
    }
}
