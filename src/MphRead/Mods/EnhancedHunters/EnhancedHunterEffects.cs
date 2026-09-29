using MphRead.Entities;
using MphRead.Sound;

namespace MphRead.Mods.EnhancedHunters;
internal static class EnhancedHunterEffects
{
    internal static SfxId? Cue(PlayerEntity player, byte oldValue, byte oldFlags)
    {
        var s = player.EnhancedState;
        if (!EnhancedHunters.Enabled(player)) return null;
        return player.Hunter switch
        {
            Hunter.Samus when oldValue < 3 && s.ValueA == 3 => SfxId.MISSILE_CHARGE3,
            Hunter.Kanden when s.ValueA > oldValue => SfxId.LOB_GUN_CHARGE1,
            Hunter.Noxus when s.ValueA > oldValue => SfxId.SHOTGUN_CHARGE1,
            Hunter.Sylux when oldFlags == 0 && s.Flags != 0 => SfxId.BEAM_CHARGE1,
            Hunter.Weavel when s.ValueA > oldValue => SfxId.BEAM_CHARGE1,
            Hunter.Trace when oldFlags == 0 && s.Flags != 0 => SfxId.TURRET_LOCK_ON,
            _ => null
        };
    }
}
