using System;

namespace MphRead.Mods.Render
{
    /// <summary>
    /// API-neutral binding contract for the optional Enhanced GLES programs.
    /// The live GLES backend deliberately does not depend on this contract yet.
    /// </summary>
    internal static class GlesEnhancedShaderContract
    {
        public const int PositionAttribute = 0;
        public const int ColorAttribute = 1;
        public const int NormalAttribute = 2;
        public const int TexcoordAttribute = 3;
        public const int ExplicitColorAttribute = 4;
        public const int TangentAttribute = 5;

        public const int MatrixStackCount = 32;
        public const int MaximumPointLights = 8;
        public const int MaximumCachedTextures = 2048;

        public const int AlbedoTextureUnit = 0;
        public const int NormalTextureUnit = 1;
        public const int EmissiveTextureUnit = 2;
        public const int SceneTextureUnit = 0;
        public const int ColorGradeLutTextureUnit = 1;

        public const int ColorGradeDimension = 16;
        public const int ColorGradeTextureWidth = ColorGradeDimension * ColorGradeDimension;
        public const int ColorGradeTextureHeight = ColorGradeDimension;

        public static class Uniform
        {
            public const string AlbedoTexture = "albedo_tex";
            public const string NormalTexture = "normal_tex";
            public const string EmissiveTexture = "emissive_tex";
            public const string UseNormalMap = "use_normal_map";
            public const string UseEmissiveMap = "use_emissive_map";
            public const string EnhancedEmission = "enhanced_emission";
            public const string VisualLightCount = "visual_light_count";
            public const string VisualLightPositionRadius = "visual_light_position_radius";
            public const string VisualLightColorIntensity = "visual_light_color_intensity";
            public const string SceneTexture = "scene_tex";
            public const string ColorGradeLut = "color_grade_lut";
            public const string Exposure = "exposure";
            public const string ApplyToneMap = "apply_tone_map";
            public const string ColorGradeStrength = "color_grade_strength";
            public const string UseColorGradeLut = "use_color_grade_lut";
            public const string ApplyOutputTransfer = "apply_output_transfer";
        }

        // Sampling an attachment which is simultaneously used as the depth
        // target is undefined on GLES. Soft particles stay disabled until the
        // backend owns a depth copy or prepass rather than creating feedback.
        public const bool SupportsSoftParticles = false;

        public static int ClampPointLightCount(int count)
            => Math.Clamp(count, 0, MaximumPointLights);

        public static float PointLightAttenuation(float distance, float radius)
        {
            if (!float.IsFinite(distance) || distance < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(distance));
            }
            if (!float.IsFinite(radius) || radius <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(radius));
            }

            float normalized = Math.Clamp(1 - distance / radius, 0, 1);
            return normalized * normalized;
        }

        public static GlesEnhancedLutAddress AddressColorGradeLut(
            float red, float green, float blue)
        {
            ValidateUnit(red, nameof(red));
            ValidateUnit(green, nameof(green));
            ValidateUnit(blue, nameof(blue));

            float scaledBlue = blue * (ColorGradeDimension - 1);
            int lowerSlice = (int)MathF.Floor(scaledBlue);
            int upperSlice = Math.Min(lowerSlice + 1, ColorGradeDimension - 1);
            return new GlesEnhancedLutAddress(
                U(lowerSlice, red), V(green), U(upperSlice, red), V(green),
                scaledBlue - lowerSlice);
        }

        private static float U(int slice, float red)
            => (slice * ColorGradeDimension
                + red * (ColorGradeDimension - 1) + 0.5f) / ColorGradeTextureWidth;

        private static float V(float green)
            => (green * (ColorGradeDimension - 1) + 0.5f) / ColorGradeTextureHeight;

        private static void ValidateUnit(float value, string parameterName)
        {
            if (!float.IsFinite(value) || value < 0 || value > 1)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }
    }

    internal readonly record struct GlesEnhancedLutAddress(
        float LowerU,
        float LowerV,
        float UpperU,
        float UpperV,
        float SliceBlend);
}
