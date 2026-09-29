using System;
using System.Collections.Generic;
using MphRead.Entities;

namespace MphRead.Mods.Launcher
{
    public readonly record struct OfflineModeOption(string Label, GameMode Mode);

    public static class OfflineLaunch
    {
        public static readonly IReadOnlyList<OfflineModeOption> Modes =
            new OfflineModeOption[]
            {
                new("Battle", GameMode.Battle),
                new("Battle teams", GameMode.BattleTeams),
                new("Survival", GameMode.Survival),
                new("Survival teams", GameMode.SurvivalTeams),
                new("Capture", GameMode.Capture),
                new("Bounty", GameMode.Bounty),
                new("Bounty teams", GameMode.BountyTeams),
                new("Defender", GameMode.Defender),
                new("Defender teams", GameMode.DefenderTeams),
                new("Nodes", GameMode.Nodes),
                new("Nodes teams", GameMode.NodesTeams),
                new("Hardpoint", GameMode.Hardpoint),
                new("Hardpoint teams", GameMode.HardpointTeams),
                new("Gun Game", GameMode.GunGame),
                new("One in the Chamber", GameMode.OneInTheChamber),
                new("Kill Confirmed", GameMode.KillConfirmed),
                new("Kill Confirmed teams", GameMode.KillConfirmedTeams),
                new("Headhunter", GameMode.Headhunter),
                new("Relic", GameMode.Relic),
                new("Prime hunter", GameMode.PrimeHunter)
            };

        public static LaunchPlan Create(MenuSettings settings, string roomKey,
            GameMode mode, Hunter hunter, int suit, int bots, int botLevel)
        {
            settings.RoomKey = roomKey;
            LauncherPrefs.LastHunter = hunter;
            LauncherPrefs.LastColor = Math.Clamp(suit, 0, 3);
            LauncherPrefs.Bots = Math.Clamp(bots, 0, PlayerEntity.SlotCapacity - 1);
            LauncherPrefs.BotLevel = Math.Clamp(botLevel, 0, 3);
            LauncherPrefs.LastKind = (int)LaunchKind.Offline;
            LauncherPrefs.Save();
            return new LaunchPlan
            {
                Kind = LaunchKind.Offline,
                Hunter = Multiplayer.HunterRules.Resolve(hunter, settings.LowTier == "on"),
                MatchRules = new Network.MatchDefinition { Mode = mode,
                    FriendlyFire = settings.FriendlyFire == "on", AffinityWeapons = settings.AffinityWeapons == "on",
                    Fiesta = settings.Fiesta == "on",
                    InstaGib = settings.InstaGib == "on" || mode == GameMode.InstaGib,
                    LowTier = settings.LowTier == "on",
                    OctolithAutoReset = Multiplayer.MatchModifierRules.UsesOctolith(mode) && settings.AutoReset == "on",
                    NoImperialist = settings.NoImperialist == "on" && settings.InstaGib != "on" && mode != GameMode.InstaGib,
                    ShadowFreeze = settings.ShadowFreeze == "on", SpawnProtection = settings.SpawnProtection == "on" },
                PlayerName = LauncherPrefs.PlayerName,
                RoomKey = roomKey,
                Mode = mode == GameMode.InstaGib ? GameMode.Battle : mode,
                Bots = LauncherPrefs.Bots,
                BotLevel = LauncherPrefs.BotLevel
            };
        }
    }
}
