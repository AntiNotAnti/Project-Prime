using System;

namespace MphRead.Mods.Multiplayer;

internal static class SpawnLoadoutRules
{
    internal static (BeamType First, BeamType Second) Fiesta(ushort match, uint life, int slot, bool noImperialist)
    {
        // Stable FNV mixing; never consume the scene's gameplay RNG.
        uint seed = 2166136261;
        seed = unchecked((seed ^ match) * 16777619);
        seed = unchecked((seed ^ life) * 16777619);
        seed = unchecked((seed ^ (uint)slot) * 16777619);
        int count = noImperialist ? GunGameRules.StageCount - 1 : GunGameRules.StageCount;
        int first = (int)(seed % (uint)count);
        seed = unchecked((seed ^ 0x9e3779b9) * 16777619);
        int second = (int)(seed % (uint)(count - 1));
        if (second >= first) second++;
        return (GunGameRules.Weapon(first), GunGameRules.Weapon(second));
    }
}
