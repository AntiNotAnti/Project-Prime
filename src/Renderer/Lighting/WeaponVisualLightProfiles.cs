using System.Collections.Generic;
using OpenTK.Mathematics;

namespace MphRead
{
    /// <summary>
    /// Initial conservative visual-light data for Project Prime weapons.
    /// These values establish safe renderer inputs; they are not final art
    /// tuning and must be calibrated from representative gameplay captures.
    /// </summary>
    public static class WeaponVisualLightProfiles
    {
        private static readonly VisualLightProfile _powerBeam = new(
            new Vector3(1f, 0.72f, 0.28f), 1.2f, 0.32f, 20, 0.12f, 2f);
        private static readonly VisualLightProfile _voltDriver = new(
            new Vector3(0.22f, 0.75f, 1f), 1.4f, 0.38f, 22, 0.16f, 2f);
        private static readonly VisualLightProfile _missile = new(
            new Vector3(1f, 0.38f, 0.08f), 1.8f, 0.5f, 30, 0.24f, 2f);
        private static readonly VisualLightProfile _battlehammer = new(
            new Vector3(0.32f, 1f, 0.28f), 1.5f, 0.4f, 24, 0.2f, 2f);
        private static readonly VisualLightProfile _imperialist = new(
            new Vector3(1f, 0.12f, 0.16f), 1.4f, 0.4f, 24, 0.14f, 2f);
        private static readonly VisualLightProfile _judicator = new(
            new Vector3(0.25f, 0.9f, 1f), 1.5f, 0.4f, 24, 0.2f, 2f);
        private static readonly VisualLightProfile _magmaul = new(
            new Vector3(1f, 0.24f, 0.04f), 1.7f, 0.46f, 26, 0.24f, 2f);
        private static readonly VisualLightProfile _shockCoil = new(
            new Vector3(0.72f, 0.3f, 1f), 1.5f, 0.42f, 25, 0.12f, 2f);

        private static readonly IReadOnlyList<BeamType> _supportedBeamTypes
            = System.Array.AsReadOnly(new[]
            {
                BeamType.PowerBeam,
                BeamType.VoltDriver,
                BeamType.Missile,
                BeamType.Battlehammer,
                BeamType.Imperialist,
                BeamType.Judicator,
                BeamType.Magmaul,
                BeamType.ShockCoil
            });

        public static IReadOnlyList<BeamType> SupportedBeamTypes => _supportedBeamTypes;

        public static VisualLightProfile Bomb { get; } = new(
            new Vector3(0.55f, 0.72f, 1f), radius: 1.5f, intensity: 0.4f,
            priority: 30, lifetime: 0.3f, falloff: 2f);

        public static bool TryGet(BeamType beam, out VisualLightProfile profile)
        {
            profile = beam switch
            {
                BeamType.PowerBeam => _powerBeam,
                BeamType.VoltDriver => _voltDriver,
                BeamType.Missile => _missile,
                BeamType.Battlehammer => _battlehammer,
                BeamType.Imperialist => _imperialist,
                BeamType.Judicator => _judicator,
                BeamType.Magmaul => _magmaul,
                BeamType.ShockCoil => _shockCoil,
                _ => default
            };
            return beam is BeamType.PowerBeam or BeamType.VoltDriver or BeamType.Missile
                or BeamType.Battlehammer or BeamType.Imperialist or BeamType.Judicator
                or BeamType.Magmaul or BeamType.ShockCoil;
        }
    }
}
