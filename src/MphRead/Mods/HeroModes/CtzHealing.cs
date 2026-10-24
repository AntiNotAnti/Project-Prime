using MphRead.Entities;
using MphRead.Mods.Network;
namespace MphRead.Mods.HeroModes;

public static class CtzHealing
{
    internal static bool IsAuthority(Scene scene) => !scene.Services.IsReplica
        && (!NetSession.Active || NetSession.IsAuthority || NetSession.IsHost);

    /// <summary>One award per projectile/wave and allied player. A direct hammer hit
    /// takes precedence over splash, never adds a second full heal.</summary>
    internal static bool TryAllyImpact(BeamProjectileEntity beam, PlayerEntity target, bool headshot, bool splash)
    {
        var scene = target.OwningScene;
        if (!HeroRules.Active(scene.GameState.Mode)) return false;
        var healer = CtzSpawnRooms.Owner(beam);
        if (healer == null || healer == target || !scene.GameState.Teams || healer.TeamIndex != target.TeamIndex) return false;
        bool charged = beam.Flags.TestFlag(BeamFlags.Charged);
        bool aura = healer.Hunter == Hunter.Spire && beam.Beam == BeamType.Magmaul && charged;
        bool hammer = healer.Hunter == Hunter.Weavel && beam.Beam == BeamType.Battlehammer;
        int heal = healer.Hunter switch
        {
            Hunter.Noxus when beam.Beam == BeamType.Judicator && charged => CtzRules.NoxusFreezeHeal,
            Hunter.Noxus when beam.Beam == BeamType.Judicator && !splash => headshot ? CtzRules.NoxusHeadHeal : CtzRules.NoxusBodyHeal,
            Hunter.Weavel when hammer => splash ? CtzRules.WeavelSplashHeal : CtzRules.WeavelDirectHeal,
            Hunter.Spire when aura => CtzRules.SpireTotalHeal,
            Hunter.Spire when beam.Beam == BeamType.Magmaul && !charged && !splash => CtzRules.SpireNormalHeal,
            _ => 0
        };
        if (heal == 0) return false;
        // Support never damages/freezes/burns/pushes an ally, even with friendly fire enabled.
        if (target.Health <= 0 || !target.ModIsInPlay || (uint)target.SlotIndex >= 8) return true;
        ushort bit = (ushort)(1 << target.SlotIndex);
        if ((beam.ModCtzHealedSlots & bit) != 0)
        {
            // Robust to a splash callback preceding direct resolution: 3 + 1 = 4, not 3 + 4.
            if (!hammer || splash || (beam.ModCtzDirectHealedSlots & bit) != 0) return true;
            heal = CtzRules.WeavelDirectHeal - CtzRules.WeavelSplashHeal;
        }
        beam.ModCtzHealedSlots |= bit;
        if (!splash) beam.ModCtzDirectHealedSlots |= bit;
        healer.ModFocusCtzTeammate(target);
        if (!IsAuthority(scene)) return true;
        if (aura) target.ModStartCtzHealingAura(healer);
        else target.ModGainCtzHealth(heal, healer); // Normal Magmaul does not consume/reset a running aura.
        return true;
    }
}
