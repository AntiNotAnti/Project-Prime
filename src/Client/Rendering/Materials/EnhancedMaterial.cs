using System;
using OpenTK.Mathematics;

namespace MphRead
{
    /// <summary>
    /// Backend-neutral material values used by the enhanced presentation path.
    /// Runtime texture identities remain optional so original content never
    /// depends on an enhancement pack.
    /// </summary>
    public readonly record struct EnhancedMaterial(
        TextureIdentity? Albedo,
        TextureIdentity? Normal,
        TextureIdentity? Emissive,
        float SpecularStrength,
        float Smoothness,
        float ReflectionStrength,
        Vector3 EmissionTint,
        float EmissionStrength)
    {
        public const float DefaultSmoothness = 0.25f;

        public static EnhancedMaterial FromOriginal(RenderMaterial original)
        {
            Vector3 specular = FinitePositive(original.Specular);
            float specularStrength = Math.Clamp(
                MathF.Max(specular.X, MathF.Max(specular.Y, specular.Z)), 0, 1);

            Vector3 emission = FinitePositive(original.Emission);
            float rawEmissionStrength = MathF.Max(emission.X, MathF.Max(emission.Y, emission.Z));
            Vector3 emissionTint = rawEmissionStrength > 0
                ? emission / rawEmissionStrength
                : Vector3.One;
            float emissionStrength = Math.Clamp(rawEmissionStrength, 0, 16);

            return new EnhancedMaterial(
                Albedo: original.Textured ? original.Texture : null,
                Normal: null,
                Emissive: null,
                SpecularStrength: specularStrength,
                Smoothness: DefaultSmoothness,
                ReflectionStrength: 0,
                EmissionTint: emissionTint,
                EmissionStrength: emissionStrength);
        }

        private static Vector3 FinitePositive(Vector3 value)
            => new(
                float.IsFinite(value.X) ? MathF.Max(0, value.X) : 0,
                float.IsFinite(value.Y) ? MathF.Max(0, value.Y) : 0,
                float.IsFinite(value.Z) ? MathF.Max(0, value.Z) : 0);
    }
}
