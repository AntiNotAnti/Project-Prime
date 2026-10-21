using System;
using System.Collections.Generic;
using MphRead.Entities;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Network;

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

        // Pure validation candidate for native and legacy launchers. Call this
        // before mutating preferences, then Create only after accepting the map
        // and match modifier rules.
        public static LaunchPlan Preview(MenuSettings settings, string roomKey,
            GameMode mode, Hunter hunter, int suit, int bots, int botLevel,
            OfflineCharacterMode character = OfflineCharacterMode.Native)
        {
            if ((character is OfflineCharacterMode.TrooperScoutDroneCombatPractice or OfflineCharacterMode.TrooperArsenalPractice)
                && (hunter != Hunter.Weavel || suit is < 0 or > 3 || bots is < 0 or > 7 || botLevel is < 0 or > 3))
                throw new ArgumentException(character == OfflineCharacterMode.TrooperArsenalPractice
                    ? "Trooper arsenal practice requires Weavel, a valid suit, zero to seven opponents and a valid difficulty."
                    : "Drone combat practice requires Weavel, a valid suit, zero to seven opponents and a valid difficulty.");
            if (!Movement.MovementModelRules.TryParse(settings.MovementModel, out var movementModel))
                throw new ArgumentException("MovementModel must be Native, Momentum, Quake3, Traversal or Parkour.", nameof(settings));
            var plan = new LaunchPlan
            {
                Kind = LaunchKind.Offline,
                OfflineCharacter = character,
                IsPlaytest = character != OfflineCharacterMode.Native,
                Hunter = Multiplayer.HunterRules.Resolve(hunter, settings.LowTier == "on"),
                MatchRules = new Network.MatchDefinition
                {
                    Mode = mode,
                    FriendlyFire = settings.FriendlyFire == "on",
                    AffinityWeapons = settings.AffinityWeapons == "on",
                    Fiesta = settings.Fiesta == "on",
                    InstaGib = settings.InstaGib == "on" || mode == GameMode.InstaGib,
                    LowTier = settings.LowTier == "on",
                    OctolithAutoReset = Multiplayer.MatchModifierRules.UsesOctolith(mode) && settings.AutoReset == "on",
                    NoImperialist = settings.NoImperialist == "on" && settings.InstaGib != "on" && mode != GameMode.InstaGib,
                    BalancedMode = settings.BalancedMode == "on",
                    RiftlineM7Available = settings.RiftlineM7Available,
                    ThirdPersonCamera = settings.ThirdPersonCamera == "on",
                    MovementModel = mode == GameMode.SinglePlayer
                        ? Movement.MovementModel.Native : movementModel,
                    ShadowFreeze = settings.ShadowFreeze == "on",
                    SpawnProtection = settings.SpawnProtection == "on"
                },
                PlayerName = LauncherPrefs.PlayerName,
                RoomKey = roomKey,
                Mode = mode == GameMode.InstaGib ? GameMode.Battle : mode,
                Bots = Math.Clamp(bots, 0, PlayerEntity.SlotCapacity - 1),
                BotLevel = Math.Clamp(botLevel, 0, 3)
            };
            if (mode != GameMode.SinglePlayer
                && !Multiplayer.MatchModifierRules.Validate(plan.MatchRules, out string reason))
                throw new ArgumentException(reason, nameof(settings));
            if (plan.MatchRules.RiftlineM7Available)
            {
                // These legacy settings are applied separately at load time;
                // they must still participate in M7 admission.
                var modifiers = plan.MatchRules.Rules
                    | (settings.OneInTheChamber == "on" ? MatchModifierFlags.OneInTheChamber : 0)
                    | (settings.EnhancedHunters == "on" ? MatchModifierFlags.EnhancedHunters : 0);
                if (!CustomWeaponMatchPolicy.TryAdmit(plan.Mode, modifiers, online: false, plan.Bots, out string customReason))
                    throw new ArgumentException(customReason);
                if (ReplayCapture.LiveRecordingActive)
                    throw new ArgumentException(CustomReplayBoundary.Error);
            }
            if (!OfflineCharacterGameplay.TryAdmit(plan, out string characterReason))
                throw new ArgumentException(characterReason);
            return plan;
        }

        public static LaunchPlan Create(MenuSettings settings, string roomKey,
            GameMode mode, Hunter hunter, int suit, int bots, int botLevel,
            OfflineCharacterMode character = OfflineCharacterMode.Native)
        {
            // Revalidate before persistence, including legacy callers. Public
            // UI owners catch refused previews and report the admission reason.
            LaunchPlan plan = Preview(settings, roomKey, mode, hunter, suit, bots, botLevel, character);
            settings.RoomKey = roomKey;
            LauncherPrefs.LastHunter = hunter;
            LauncherPrefs.LastColor = Math.Clamp(suit, 0, 3);
            LauncherPrefs.Bots = Math.Clamp(bots, 0, PlayerEntity.SlotCapacity - 1);
            LauncherPrefs.BotLevel = Math.Clamp(botLevel, 0, 3);
            LauncherPrefs.LastKind = (int)LaunchKind.Offline;
            LauncherPrefs.LastSelectedMap = roomKey;
            LauncherPrefs.Save();
            return plan;
        }
    }
}
