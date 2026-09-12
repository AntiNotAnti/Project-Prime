using OpenTK.Mathematics;

namespace MphRead
{
    public static class AmbientVisualLightProfiles
    {
        private static readonly VisualLightProfile _stinglarvaBomb = new(
            new Vector3(1, 0.25f, 0.05f), radius: 1.5f,
            intensity: 0.4f, priority: 30, lifetime: 0.3f, falloff: 2);
        private static readonly VisualLightProfile _lockjawBomb = new(
            new Vector3(0.15f, 0.7f, 1), radius: 1.5f,
            intensity: 0.4f, priority: 30, lifetime: 0.3f, falloff: 2);

        public static VisualLightProfile Teleporter { get; } = new(
            new Vector3(0.2f, 0.72f, 1), radius: 3, intensity: 0.3f,
            priority: 8, lifetime: 1, falloff: 2);

        public static VisualLightProfile Bomb(BombType type)
            => type switch
            {
                BombType.MorphBall => WeaponVisualLightProfiles.Bomb,
                BombType.Stinglarva => _stinglarvaBomb,
                BombType.Lockjaw => _lockjawBomb,
                _ => throw new System.ArgumentOutOfRangeException(nameof(type))
            };
    }
}
