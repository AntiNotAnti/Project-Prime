using System;
using OpenTK.Mathematics;

namespace MphRead
{
    internal readonly record struct EnhancedSurfaceData(
        Vector3 WorldNormal,
        float LinearDepth,
        float MaterialValue);

    /// <summary>
    /// Reference packing for the single-sample RGBA surface buffer:
    /// oct-encoded world normal in RG, linear depth in B, and a normalized
    /// material value (such as smoothness or class) in A.
    /// </summary>
    internal static class EnhancedSurfaceMath
    {
        private const float Pi = MathF.PI;

        public static Vector2 EncodeOctNormal(Vector3 normal)
        {
            if (!IsFinite(normal))
            {
                throw new ArgumentOutOfRangeException(nameof(normal));
            }

            normal = NormalizeFinite(normal, nameof(normal));
            float inverseL1 = 1f / (MathF.Abs(normal.X) + MathF.Abs(normal.Y)
                + MathF.Abs(normal.Z));
            var encoded = new Vector2(normal.X * inverseL1, normal.Y * inverseL1);
            if (normal.Z < 0)
            {
                float x = (1 - MathF.Abs(encoded.Y)) * SignNotZero(encoded.X);
                float y = (1 - MathF.Abs(encoded.X)) * SignNotZero(encoded.Y);
                encoded = new Vector2(x, y);
            }
            return encoded * 0.5f + new Vector2(0.5f);
        }

        public static Vector3 DecodeOctNormal(Vector2 encoded)
        {
            ValidateUnit(encoded, nameof(encoded));
            Vector2 folded = encoded * 2 - Vector2.One;
            var normal = new Vector3(folded.X, folded.Y,
                1 - MathF.Abs(folded.X) - MathF.Abs(folded.Y));
            float correction = Math.Clamp(-normal.Z, 0, 1);
            normal.X += normal.X >= 0 ? -correction : correction;
            normal.Y += normal.Y >= 0 ? -correction : correction;
            return NormalizeFinite(normal, nameof(encoded));
        }

        public static Vector4 Pack(Vector3 worldNormal, float linearDepth,
            float materialValue)
        {
            if (!float.IsFinite(linearDepth) || linearDepth < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(linearDepth));
            }
            ValidateUnit(materialValue, nameof(materialValue));
            Vector2 encoded = EncodeOctNormal(worldNormal);
            return new Vector4(encoded.X, encoded.Y, linearDepth, materialValue);
        }

        public static EnhancedSurfaceData Unpack(Vector4 packed)
        {
            if (!IsFinite(packed) || packed.Z < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(packed));
            }
            ValidateUnit(new Vector2(packed.X, packed.Y), nameof(packed));
            ValidateUnit(packed.W, nameof(packed));
            return new EnhancedSurfaceData(
                DecodeOctNormal(new Vector2(packed.X, packed.Y)), packed.Z, packed.W);
        }

        /// <summary>
        /// CPU reference for the Enhanced specular-gloss GGX shader path.
        /// Authored specular RGB is treated as dielectric F0 and smoothness is
        /// converted to perceptual roughness; Original and Performance never
        /// use this calculation.
        /// </summary>
        public static Vector3 EvaluateGgx(Vector3 normal, Vector3 lightDirection,
            Vector3 viewDirection, Vector3 diffuseColor, Vector3 specularColor,
            float smoothness)
        {
            normal = NormalizeFinite(normal, nameof(normal));
            lightDirection = NormalizeFinite(lightDirection,
                nameof(lightDirection));
            viewDirection = NormalizeFinite(viewDirection,
                nameof(viewDirection));
            if (!IsFinite(diffuseColor) || !IsFinite(specularColor)
                || !float.IsFinite(smoothness))
            {
                throw new ArgumentOutOfRangeException(nameof(smoothness));
            }

            float normalDotLight = Math.Clamp(Vector3.Dot(normal,
                lightDirection), 0, 1);
            float normalDotView = Math.Clamp(Vector3.Dot(normal,
                viewDirection), 0, 1);
            if (normalDotLight <= 0 || normalDotView <= 0) return Vector3.Zero;

            Vector3 halfVector = NormalizeFinite(lightDirection + viewDirection,
                nameof(lightDirection));
            float normalDotHalf = Math.Clamp(Vector3.Dot(normal, halfVector), 0, 1);
            float viewDotHalf = Math.Clamp(Vector3.Dot(viewDirection, halfVector), 0, 1);
            float roughness = Math.Max(1 - Math.Clamp(smoothness, 0, 1), 0.045f);
            Vector3 reflectance = ClampUnit(specularColor);
            Vector3 fresnel = reflectance + (Vector3.One - reflectance)
                * MathF.Pow(1 - viewDotHalf, 5);
            float distribution = DistributionGgx(normalDotHalf, roughness);
            float geometry = GeometrySchlickGgx(normalDotView, roughness)
                * GeometrySchlickGgx(normalDotLight, roughness);
            Vector3 specular = fresnel * (distribution * geometry
                / Math.Max(4 * normalDotView * normalDotLight, 0.0001f));
            Vector3 diffuse = (Vector3.One - fresnel) * diffuseColor;
            return (diffuse + specular) * normalDotLight;
        }

        public static float DistributionGgx(float normalDotHalf,
            float roughness)
        {
            if (!float.IsFinite(normalDotHalf) || !float.IsFinite(roughness))
                throw new ArgumentOutOfRangeException(nameof(roughness));
            normalDotHalf = Math.Clamp(normalDotHalf, 0, 1);
            roughness = Math.Max(roughness, 0.045f);
            float alpha = Math.Max(roughness * roughness, 0.002025f);
            float alphaSquared = alpha * alpha;
            float denominator = normalDotHalf * normalDotHalf
                * (alphaSquared - 1) + 1;
            return alphaSquared / Math.Max(Pi * denominator * denominator,
                0.000001f);
        }

        private static float GeometrySchlickGgx(float normalDotDirection,
            float roughness)
        {
            float shifted = roughness + 1;
            float k = shifted * shifted * 0.125f;
            return normalDotDirection / Math.Max(
                normalDotDirection * (1 - k) + k, 0.000001f);
        }

        private static Vector3 ClampUnit(Vector3 value)
            => new(Math.Clamp(value.X, 0, 1), Math.Clamp(value.Y, 0, 1),
                Math.Clamp(value.Z, 0, 1));

        private static float SignNotZero(float value) => value >= 0 ? 1 : -1;

        private static void ValidateUnit(float value, string parameterName)
        {
            if (!float.IsFinite(value) || value < 0 || value > 1)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }

        private static void ValidateUnit(Vector2 value, string parameterName)
        {
            if (!float.IsFinite(value.X) || !float.IsFinite(value.Y)
                || value.X < 0 || value.X > 1 || value.Y < 0 || value.Y > 1)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }

        private static bool IsFinite(Vector3 value)
            => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

        private static bool IsFinite(Vector4 value)
            => float.IsFinite(value.X) && float.IsFinite(value.Y)
                && float.IsFinite(value.Z) && float.IsFinite(value.W);

        private static Vector3 NormalizeFinite(Vector3 value, string parameterName)
        {
            float maximum = Math.Max(MathF.Abs(value.X),
                Math.Max(MathF.Abs(value.Y), MathF.Abs(value.Z)));
            if (maximum <= 0)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }

            var scaled = new Vector3(value.X / maximum, value.Y / maximum,
                value.Z / maximum);
            double length = Math.Sqrt((double)scaled.X * scaled.X
                + (double)scaled.Y * scaled.Y + (double)scaled.Z * scaled.Z);
            return new Vector3((float)(scaled.X / length), (float)(scaled.Y / length),
                (float)(scaled.Z / length));
        }
    }
}
