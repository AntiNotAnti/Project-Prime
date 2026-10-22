using MphRead.Mods.Network;

namespace MphRead.Mods.Multiplayer;

/// <summary>New modes must opt in after balance validation; bot rounds never qualify.</summary>
public static class CareerMatchEligibility
{
    public static bool IsStandard(MatchDefinition match, bool containsBots)
    {
        match = match.NormalizeLegacy();
        if (containsBots || match.Fiesta || match.OneInTheChamber || match.InstaGib || match.LowTier || match.NoImperialist) return false;
        return match.Mode is GameMode.Battle or GameMode.BattleTeams or GameMode.Survival or GameMode.SurvivalTeams
            or GameMode.Capture or GameMode.Bounty or GameMode.BountyTeams or GameMode.Nodes or GameMode.NodesTeams
            or GameMode.Defender or GameMode.DefenderTeams or GameMode.PrimeHunter;
    }
}
