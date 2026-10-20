#if PROJECTPRIME_RMLUI_ANDROID_EXPERIMENT
using System;
using MphRead.Mods.Campaign;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Core;

namespace MphRead.Droid;

internal enum RmlUiAndroidLocalLaunchKind { Invalid, Offline, Adventure, Ship }

/// <summary>Copied admission values only. The existing launcher and scene owners perform every effect.</summary>
internal readonly record struct RmlUiAndroidLocalLaunchRequest
{
    internal RmlUiAndroidLocalLaunchKind Kind { get; private init; }
    internal RmlUiOfflineRequest Offline { get; private init; }
    internal RmlUiAdventureSlot Slot { get; private init; }
    internal bool NewGame { get; private init; }
    internal Hunter Hunter { get; private init; }
    internal CampaignDockReceipt Dock { get; private init; }
    internal string Fingerprint { get; private init; }
    internal LaunchPlan ShipPlan { get; private init; }

    internal static RmlUiAndroidLocalLaunchRequest ForOffline(RmlUiOfflineRequest request)
        => new() { Kind = RmlUiAndroidLocalLaunchKind.Offline, Offline = request, Hunter = request.Hunter };

    internal static bool TryAdventure(RmlUiAdventureSlot slot, bool newGame, Hunter hunter,
        out RmlUiAndroidLocalLaunchRequest request)
    {
        request = default;
        if (slot.Slot is < 1 or > 3 || (uint)hunter >= 7 || slot.Unreadable
            || !slot.Used && !newGame
            || !(slot.Used ? NativeCampaignShipController<LaunchPlan>.ValidFingerprint(slot.Fingerprint)
                : slot.Fingerprint == "EMPTY")) return false;
        request = new() { Kind = RmlUiAndroidLocalLaunchKind.Adventure, Slot = slot,
            NewGame = newGame, Hunter = hunter, Fingerprint = slot.Fingerprint };
        return true;
    }

    internal static bool TryShip(NativeCampaignShipEffect<LaunchPlan> effect,
        out RmlUiAndroidLocalLaunchRequest request)
    {
        request = default;
        var plan = effect.Plan;
        if (!effect.Launch || effect.Dock.Slot is < 1 or > 3 || effect.Dock.Planet is < 0 or > 4
            || !NativeCampaignShipController<LaunchPlan>.ValidFingerprint(effect.Fingerprint)
            || plan.Kind != LaunchKind.Adventure || plan.NewGame || (uint)plan.Hunter >= 7
            || plan.SaveSlot != effect.Dock.Slot || plan.AdventureDestination is not { } destination
            || (int)destination is < 0 or > 4
            || plan.ExitShipToSurface && (int)destination != effect.Dock.Planet) return false;
        request = new() { Kind = RmlUiAndroidLocalLaunchKind.Ship, ShipPlan = plan, Hunter = plan.Hunter,
            Dock = new(effect.Dock.Slot, (CampaignPlanet)effect.Dock.Planet), Fingerprint = effect.Fingerprint };
        return true;
    }

    internal bool MatchesPlan(LaunchPlan plan, int currentSuit)
    {
        if (Kind == RmlUiAndroidLocalLaunchKind.Offline)
            return plan.Kind == LaunchKind.Offline && plan.RoomKey == Offline.RoomKey && plan.Mode == Offline.Mode
                && Offline.Character is OfflineCharacterMode.Native or OfflineCharacterMode.TrooperWallClingPractice
                && plan.OfflineCharacter == Offline.Character
                && plan.IsPlaytest == (Offline.Character != OfflineCharacterMode.Native)
                && plan.Hunter == Offline.Hunter && plan.Bots == Offline.Bots && plan.BotLevel == Offline.BotLevel
                && currentSuit == Offline.Suit && plan.SaveSlot == 0 && !plan.NewGame
                && !plan.AdventureDestination.HasValue && !plan.ExitShipToSurface;
        if (plan.Kind != LaunchKind.Adventure || plan.Hunter != Hunter || plan.Spectate || plan.IsPlaytest
            || plan.OfflineCharacter != OfflineCharacterMode.Native) return false;
        return Kind == RmlUiAndroidLocalLaunchKind.Adventure
            ? plan.SaveSlot == Slot.Slot && plan.NewGame == NewGame && !plan.AdventureDestination.HasValue && !plan.ExitShipToSurface
            : Kind == RmlUiAndroidLocalLaunchKind.Ship && plan.SaveSlot == Dock.Slot && !plan.NewGame
                && plan.AdventureDestination == ShipPlan.AdventureDestination && plan.ExitShipToSurface == ShipPlan.ExitShipToSurface;
    }

    internal bool IsCurrent(Func<byte, RmlUiAdventureSlot> readSlot, Func<CampaignDockReceipt?> readDock)
    {
        if (Kind == RmlUiAndroidLocalLaunchKind.Offline) return true;
        if (Kind == RmlUiAndroidLocalLaunchKind.Adventure) return readSlot(Slot.Slot) == Slot;
        return Kind == RmlUiAndroidLocalLaunchKind.Ship && readDock() == Dock
            && readSlot(Dock.Slot) is { Used: true, Unreadable: false } current
            && current.Fingerprint == Fingerprint && readDock() == Dock;
    }

    internal static RmlUiAdventureSlot ReadSlot(byte slot)
    {
        var info = AdventureSave.Read(slot);
        string fingerprint = AdventureSave.ContentFingerprint(slot);
        return new(slot, info.Used, info.Unreadable || fingerprint == "UNAVAILABLE",
            info.Area ?? "", info.Octoliths, info.Health, info.HealthMax, fingerprint);
    }
}
#endif
