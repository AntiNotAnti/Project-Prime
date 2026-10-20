using System;

namespace MphRead.Mods.Launcher;

public readonly record struct LauncherLoadoutApplyResult(bool Accepted, bool IdentitySaved,
    bool CosmeticsStarted, string Status);

/// <summary>
/// One explicit local equip action across two existing persistence owners.
/// This is ordered application, not an atomic transaction or a cloud success claim.
/// </summary>
public static class LauncherLoadoutApply
{
    public static LauncherLoadoutApplyResult Apply(LauncherHunterChoiceController choice,
        LauncherCosmeticDraftController cosmetics)
    {
        ArgumentNullException.ThrowIfNull(choice);
        ArgumentNullException.ThrowIfNull(cosmetics);
        var identity = choice.Current();
        var loadout = cosmetics.Current();
        if (identity.Choice.Hunter != loadout.Hunter)
            return new(false, false, false, "HUNTER DRAFTS DIFFER // DISCARD TO REFRESH");
        if (!choice.CanApply(out string identityReason))
            return new(false, false, false, identityReason);
        if (!cosmetics.CanEquip(out string cosmeticReason))
            return new(false, false, false, cosmeticReason);
        if (!choice.Apply())
            return new(false, false, false, choice.Current().Status);
        bool identitySaved = identity.Dirty;
        if (!cosmetics.Equip())
            return new(false, identitySaved, false,
                (identitySaved ? "HUNTER/SUIT SAVED // COSMETICS NOT SAVED // " : "") + cosmetics.Current().Status);
        bool started = loadout.Dirty && cosmetics.Current().Busy;
        return new(true, identitySaved, started, started
            ? (identitySaved ? "HUNTER/SUIT SAVED // " : "") + "EQUIPPING COSMETICS LOCALLY"
            : loadout.PendingSync ? "LOADOUT EQUIPPED LOCALLY // ACCOUNT SYNC PENDING"
            : "LOADOUT EQUIPPED");
    }
}
