using System;
namespace MphRead.Mods.Multiplayer;

internal static class MatchObjectiveReport
{
    internal static (int A, int B, int C, int D) Values(SceneGameState s, int slot) => s.Mode switch
    {
        GameMode.Capture or GameMode.Bounty or GameMode.BountyTeams => (s.OctolithScores[slot], s.OctolithDrops[slot], s.OctolithStops[slot], Seconds(s.ObjectiveSeconds[slot])),
        GameMode.Nodes or GameMode.NodesTeams => (s.NodesCaptured[slot], s.NodesLost[slot], Seconds(s.ObjectiveSeconds[slot]), 0),
        GameMode.Defender or GameMode.DefenderTeams or GameMode.Hardpoint or GameMode.HardpointTeams => (Seconds(s.ObjectiveSeconds[slot]), s.ObjectiveContests[slot], 0, 0),
        GameMode.PrimeHunter => (Seconds(s.Time[slot]), s.KillsAsPrime[slot], s.PrimesKilled[slot], 0),
        GameMode.Relic => (Seconds(s.Time[slot]), s.ObjectivePickups[slot], s.OctolithDrops[slot], 0),
        GameMode.GunGame => (Math.Min(s.Points[slot] + 1, GunGameRules.StageCount), Seconds(s.FastestStageSeconds[slot]), 0, 0),
        GameMode.KillConfirmed or GameMode.KillConfirmedTeams => (s.TokenConfirms[slot], s.TokenDenies[slot], 0, 0),
        GameMode.Headhunter => (s.TokensCollected[slot], s.TokensBanked[slot], s.LargestBank[slot], 0),
        _ => default
    };
    private static int Seconds(float value) => (int)Math.Clamp(value, 0, int.MaxValue);
    internal static string Text(SceneGameState s, int slot)
    {
        var (a,b,c,d) = Values(s, slot);
        return s.Mode switch
        {
            GameMode.Capture or GameMode.Bounty or GameMode.BountyTeams => $"CAPTURES {a}   DROPS {b}   STOPS {c}   CARRY {d}s",
            GameMode.Nodes or GameMode.NodesTeams => $"CAPTURED {a}   LOST {b}   OBJECTIVE {c}s",
            GameMode.Defender or GameMode.DefenderTeams or GameMode.Hardpoint or GameMode.HardpointTeams => $"OBJECTIVE {a}s   CONTESTS {b}",
            GameMode.PrimeHunter => $"PRIME {a}s   PRIME KILLS {b}   PRIMES KILLED {c}",
            GameMode.Relic => $"RELIC {a}s   PICKUPS {b}   DROPS {c}",
            GameMode.GunGame => $"FINAL STAGE {a}   FASTEST STAGE {b}s",
            GameMode.KillConfirmed or GameMode.KillConfirmedTeams => $"CONFIRMS {a}   DENIES {b}",
            GameMode.Headhunter => $"COLLECTED {a}   BANKED {b}   LARGEST BANK {c}",
            _ => ""
        };
    }
    internal static void Apply(SceneGameState s, int slot, int a, int b, int c, int d)
    {
        switch(s.Mode)
        {
            case GameMode.Capture: case GameMode.Bounty: case GameMode.BountyTeams:
                s.OctolithScores[slot]=a; s.OctolithDrops[slot]=b; s.OctolithStops[slot]=c; s.ObjectiveSeconds[slot]=d; break;
            case GameMode.Nodes: case GameMode.NodesTeams:
                s.NodesCaptured[slot]=a; s.NodesLost[slot]=b; s.ObjectiveSeconds[slot]=c; break;
            case GameMode.Defender: case GameMode.DefenderTeams: case GameMode.Hardpoint: case GameMode.HardpointTeams:
                s.ObjectiveSeconds[slot]=a; s.ObjectiveContests[slot]=b; break;
            case GameMode.PrimeHunter: s.Time[slot]=a; s.KillsAsPrime[slot]=b; s.PrimesKilled[slot]=c; break;
            case GameMode.Relic: s.Time[slot]=a; s.ObjectivePickups[slot]=b; s.OctolithDrops[slot]=c; break;
            case GameMode.GunGame: s.FastestStageSeconds[slot]=b; break;
            case GameMode.KillConfirmed: case GameMode.KillConfirmedTeams: s.TokenConfirms[slot]=a; s.TokenDenies[slot]=b; break;
            case GameMode.Headhunter: s.TokensCollected[slot]=a; s.TokensBanked[slot]=b; s.LargestBank[slot]=c; break;
        }
    }
}
