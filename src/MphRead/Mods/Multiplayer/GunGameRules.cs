using System;

namespace MphRead.Mods.Multiplayer;

internal static class GunGameRules
{
    internal const int StageCount = 7;
    private static readonly BeamType[] Ladder = [BeamType.PowerBeam, BeamType.VoltDriver,
        BeamType.Battlehammer, BeamType.Magmaul, BeamType.Judicator, BeamType.ShockCoil, BeamType.Imperialist];
    internal static BeamType Weapon(int stage) => Ladder[Math.Clamp(stage, 0, StageCount - 1)];
}
