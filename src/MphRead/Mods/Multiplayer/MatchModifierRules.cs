using MphRead.Mods.Network;

namespace MphRead.Mods.Multiplayer;

public static class MatchModifierRules
{
    public static bool UsesOctolith(GameMode mode) => mode is GameMode.Capture or GameMode.Bounty or GameMode.BountyTeams or GameMode.Relic;

    public static bool Validate(MatchDefinition match, out string reason)
    {
        match = match.NormalizeLegacy();
        reason = "";
        if (match.BalancedMode && (match.Fiesta || match.InstaGib || match.OneInTheChamber
            || match.Mode == GameMode.OneInTheChamber || match.Mode == GameMode.GunGame))
            reason = "Balanced Mode cannot be combined with Fiesta, Insta-Gib, One in the Chamber or Gun Game.";
        else if ((match.OneInTheChamber || match.Mode == GameMode.OneInTheChamber) && (match.InstaGib || match.NoImperialist || match.Fiesta))
            reason = "One in the Chamber cannot be combined with Insta-Gib, No Imp or Fiesta.";
        else if (match.Fiesta && match.InstaGib)
            reason = "Fiesta and Insta-Gib require different spawn loadouts.";
        else if (match.Mode == GameMode.GunGame && (match.InstaGib || match.NoImperialist || match.Fiesta || match.OneInTheChamber))
            reason = "Gun Game requires its complete weapon ladder; disable other loadout modifiers and No Imp.";
        else if (match.InstaGib && match.NoImperialist)
            reason = "Insta-Gib and No Imp cannot be enabled together.";
        else if (match.VanillaDuelResources && (match.Mode != GameMode.BattleTeams || match.Format != MatchFormat.OneVsOne))
            reason = "Vanilla 1v1 spawns/pickups are available only for Battle 1v1.";
        else if (match.OctolithAutoReset && !UsesOctolith(match.Mode))
            reason = "Octolith Auto Reset requires Capture, Bounty or Relic.";
        return reason.Length == 0;
    }
}
