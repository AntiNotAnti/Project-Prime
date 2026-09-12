using System;
using OpenTK.Mathematics;
using System.Runtime.CompilerServices;
using MphRead.Mods;

namespace MphRead
{
    /// <summary>
    /// Stable source identity for one resolved texture variant, independent of a
    /// GPU handle.  The source is normally the immutable parsed recolor/table
    /// object; the numeric fields describe the resolved texture, palette, and
    /// recolor, while Variant distinguishes effect or source overrides.
    /// </summary>
    public readonly struct TextureIdentity : IEquatable<TextureIdentity>
    {
        public object Source { get; }
        public int TextureId { get; }
        public int PaletteId { get; }
        public int RecolorId { get; }
        public object? Variant { get; }
        public Vector4? PaletteOverride { get; }

        public TextureIdentity(object source, int textureId = -1, int paletteId = -1, int recolorId = -1,
            object? variant = null, Vector4? paletteOverride = null)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            TextureId = textureId;
            PaletteId = paletteId;
            RecolorId = recolorId;
            Variant = variant;
            PaletteOverride = paletteOverride;
        }

        public bool Equals(TextureIdentity other)
            => ReferenceEquals(Source, other.Source)
                && TextureId == other.TextureId
                && PaletteId == other.PaletteId
                && RecolorId == other.RecolorId
                && SameVariant(Variant, other.Variant)
                && Nullable.Equals(PaletteOverride, other.PaletteOverride);

        private static bool SameVariant(object? left, object? right)
        {
            if (left is null || right is null)
            {
                return left is null && right is null;
            }

            // Source variants are normally runtime-owned reference identities
            // (for example an effect element). Value variants remain useful for
            // backend-neutral tests and descriptors, so compare those by value.
            return left.GetType().IsValueType && right.GetType() == left.GetType()
                ? left.Equals(right)
                : ReferenceEquals(left, right);
        }

        public override bool Equals(object? obj) => obj is TextureIdentity other && Equals(other);
        public override int GetHashCode()
        {
            HashCode hash = new HashCode();
            hash.Add(Source is null ? 0 : RuntimeHelpers.GetHashCode(Source));
            hash.Add(TextureId);
            hash.Add(PaletteId);
            hash.Add(RecolorId);
            if (Variant is not null)
            {
                hash.Add(Variant.GetType().IsValueType
                    ? Variant.GetHashCode()
                    : RuntimeHelpers.GetHashCode(Variant));
            }
            hash.Add(PaletteOverride);
            return hash.ToHashCode();
        }
        public static bool operator ==(TextureIdentity left, TextureIdentity right) => left.Equals(right);
        public static bool operator !=(TextureIdentity left, TextureIdentity right) => !left.Equals(right);
        public TextureIdentity WithPaletteOverride(Vector4? paletteOverride)
            => new TextureIdentity(Source, TextureId, PaletteId, RecolorId, Variant, paletteOverride);
        public override string ToString()
            => $"{Source?.GetType().Name ?? "<none>"}[tex={TextureId},pal={PaletteId},recolor={RecolorId}]";
    }

    /// <summary>
    /// Material values needed by Project Prime's current DS-style renderer.
    /// It deliberately excludes backend enums and resource handles.
    /// </summary>
    public struct RenderMaterial : IEquatable<RenderMaterial>
    {
        // These values are intentionally small.  Particles and trails are
        // already emissive-looking presentation primitives, but they should
        // not dominate a future bloom pass the way a model's authored
        // emission can.
        internal const float ParticleBloomStrength = 0.18f;
        internal const float TrailBloomStrength = 0.12f;

        public Vector3 Diffuse { get; set; }
        public Vector3 Ambient { get; set; }
        public Vector3 Specular { get; set; }
        public Vector3 Emission { get; set; }
        public bool BloomEligible { get; set; }
        public float BloomStrength { get; set; }
        public float Alpha { get; set; }
        public bool Lighting { get; set; }
        public bool Textured { get; set; }
        public PolygonMode PolygonMode { get; set; }
        public RenderMode RenderMode { get; set; }
        public CullingMode CullingMode { get; set; }
        public BillboardMode BillboardMode { get; set; }
        public TexgenMode TexgenMode { get; set; }
        public RepeatMode WrapX { get; set; }
        public RepeatMode WrapY { get; set; }
        public TextureIdentity? Texture { get; set; }
        public TextureAssetKey? TextureAssetKey { get; set; }
        public EnhancedMaterial? Enhanced { get; set; }
        internal EnhancedBeamDrawState? EnhancedBeam { get; set; }
        internal EnhancedForceFieldDrawState? EnhancedForceField { get; set; }
        public Matrix4 TextureMatrix { get; set; }
        public Vector4? ColorOverride { get; set; }
        public Vector4? PaletteOverride { get; set; }
        public bool Wireframe { get; set; }
        public bool NoLines { get; set; }

        public bool Equals(RenderMaterial other)
        {
            return Diffuse == other.Diffuse
                && Ambient == other.Ambient
                && Specular == other.Specular
                && Emission == other.Emission
                && BloomEligible == other.BloomEligible
                && BloomStrength.Equals(other.BloomStrength)
                && Alpha.Equals(other.Alpha)
                && Lighting == other.Lighting
                && Textured == other.Textured
                && PolygonMode == other.PolygonMode
                && RenderMode == other.RenderMode
                && CullingMode == other.CullingMode
                && BillboardMode == other.BillboardMode
                && TexgenMode == other.TexgenMode
                && WrapX == other.WrapX
                && WrapY == other.WrapY
                && Nullable.Equals(Texture, other.Texture)
                && Nullable.Equals(TextureAssetKey, other.TextureAssetKey)
                && Nullable.Equals(Enhanced, other.Enhanced)
                && Nullable.Equals(EnhancedBeam, other.EnhancedBeam)
                && Nullable.Equals(EnhancedForceField, other.EnhancedForceField)
                && TextureMatrix == other.TextureMatrix
                && Nullable.Equals(ColorOverride, other.ColorOverride)
                && Nullable.Equals(PaletteOverride, other.PaletteOverride)
                && Wireframe == other.Wireframe
                && NoLines == other.NoLines;
        }

        public override bool Equals(object? obj) => obj is RenderMaterial other && Equals(other);
        public override int GetHashCode()
        {
            HashCode hash = new HashCode();
            hash.Add(Diffuse); hash.Add(Ambient); hash.Add(Specular); hash.Add(Emission);
            hash.Add(BloomEligible); hash.Add(BloomStrength);
            hash.Add(Alpha); hash.Add(Lighting); hash.Add(Textured); hash.Add(PolygonMode);
            hash.Add(RenderMode); hash.Add(CullingMode); hash.Add(BillboardMode); hash.Add(TexgenMode);
            hash.Add(WrapX); hash.Add(WrapY); hash.Add(Texture); hash.Add(TextureMatrix);
            hash.Add(TextureAssetKey); hash.Add(Enhanced);
            hash.Add(EnhancedBeam); hash.Add(EnhancedForceField);
            hash.Add(ColorOverride); hash.Add(PaletteOverride); hash.Add(Wireframe); hash.Add(NoLines);
            return hash.ToHashCode();
        }

        public static bool operator ==(RenderMaterial left, RenderMaterial right) => left.Equals(right);
        public static bool operator !=(RenderMaterial left, RenderMaterial right) => !left.Equals(right);

        internal static float ModelBloomStrength(Vector3 emission)
        {
            if (!float.IsFinite(emission.X) || !float.IsFinite(emission.Y)
                || !float.IsFinite(emission.Z))
            {
                return 0;
            }
            float strength = MathF.Max(MathF.Abs(emission.X),
                MathF.Max(MathF.Abs(emission.Y), MathF.Abs(emission.Z)));
            return Math.Clamp(strength, 0, 1);
        }

        internal static float SingleParticleBloomStrength(SingleType type)
            => type == SingleType.Fuzzball ? ParticleBloomStrength : 0;

        internal static RenderMaterial FromSubmission(DrawSubmission submission)
        {
            return new RenderMaterial
            {
                Diffuse = submission.Diffuse,
                Ambient = submission.Ambient,
                Specular = submission.Specular,
                Emission = submission.Emission,
                BloomEligible = submission.BloomEligible,
                BloomStrength = submission.BloomStrength,
                Alpha = submission.Alpha,
                Lighting = submission.Lighting,
                Textured = submission.HasTexture,
                PolygonMode = submission.PolygonMode,
                RenderMode = submission.RenderMode,
                CullingMode = submission.CullingMode,
                BillboardMode = submission.BillboardMode,
                TexgenMode = submission.TexgenMode,
                WrapX = submission.XRepeat,
                WrapY = submission.YRepeat,
                Texture = submission.HasTexture ? submission.TextureIdentity : null,
                TextureAssetKey = submission.TextureAssetKey,
                Enhanced = submission.EnhancedMaterial,
                EnhancedBeam = submission.EnhancedBeam,
                EnhancedForceField = submission.EnhancedForceField,
                TextureMatrix = submission.TexcoordMatrix,
                ColorOverride = submission.OverrideColor,
                PaletteOverride = submission.PaletteOverride,
                Wireframe = submission.Wireframe,
                NoLines = submission.NoLines
            };
        }
    }

    public readonly struct MaterialState : IEquatable<MaterialState>
    {
        public RenderMaterial Material { get; }
        public PipelineKey Pipeline { get; }
        public SamplerKey Sampler { get; }

        public MaterialState(RenderMaterial material, RenderPrimitive primitive, RenderPassKind pass,
            int sampleCount = 1, bool filtering = false, string? targetFormat = null)
        {
            Material = material;
            Pipeline = PipelineKey.From(material, primitive, pass, sampleCount, targetFormat);
            Sampler = SamplerKey.From(material, filtering);
        }

        public MaterialState(RenderMaterial material, RenderPrimitive primitive, RenderPassKind pass,
            RenderQualitySnapshot quality, string? targetFormat = null)
        {
            Material = material;
            Pipeline = PipelineKey.From(material, primitive, pass, quality, targetFormat);
            Sampler = SamplerKey.From(material, quality);
        }

        public bool Equals(MaterialState other)
            => Material == other.Material && Pipeline == other.Pipeline && Sampler == other.Sampler;
        public override bool Equals(object? obj) => obj is MaterialState other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Material, Pipeline, Sampler);
        public static bool operator ==(MaterialState left, MaterialState right) => left.Equals(right);
        public static bool operator !=(MaterialState left, MaterialState right) => !left.Equals(right);
    }
}
