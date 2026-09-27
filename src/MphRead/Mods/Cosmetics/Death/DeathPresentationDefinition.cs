using OpenTK.Mathematics;
namespace MphRead.Mods.Cosmetics.Death
{
    public enum DeathPoseStyle { Classic, Backfall, KneelCollapse, Float, ShatterFreeze, Disintegrate }
    public sealed record DeathPresentationDefinition(string Key, ushort WireId, string DisplayName)
        : CosmeticDefinition(Key, WireId, DisplayName)
    {
        public DeathPoseStyle PoseStyle { get; init; }
        public SurfaceStyle SurfaceEffect { get; init; }
        public ParticleStyle ParticleEffect { get; init; }
        public Vector3 LightEffect { get; init; }
        public string? SoundEffect { get; init; }
        public float Duration { get; init; } = 1.2f;
        public float FadeStart { get; init; } = 0.25f;
        public float HideBodyAt { get; init; } = 0.85f;
        public float CameraShake { get; init; }
    }
}
