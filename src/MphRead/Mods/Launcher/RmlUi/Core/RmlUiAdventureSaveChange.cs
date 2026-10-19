using System;
using System.Linq;

namespace MphRead.Mods.Launcher.RmlUi.Core;

public enum RmlUiAdventureSaveAction { Copy, Archive }
public readonly record struct RmlUiAdventureSaveChange(RmlUiAdventureSaveAction Action, RmlUiAdventureSlot Source, RmlUiAdventureSlot? Destination);
public sealed record RmlUiAdventureSaveResult(RmlUiAdventureSaveChange Change, bool Success, string Status);

internal static class RmlUiAdventureSaveIdentity
{
    internal static bool Hash(string value) => value != null && value.Length == 64 && value.All(Uri.IsHexDigit);
    internal static bool Occupied(RmlUiAdventureSlot slot) => slot.Slot is >= 1 and <= 3 && Hash(slot.Fingerprint)
        && (slot.Used && !slot.Unreadable || !slot.Used && slot.Unreadable);
    internal static bool Valid(RmlUiAdventureSlot slot) => Occupied(slot)
        || slot.Slot is >= 1 and <= 3 && !slot.Used && !slot.Unreadable && slot.Fingerprint == "EMPTY";
    internal static bool Valid(RmlUiAdventureSaveChange change) => Occupied(change.Source) && (change.Action switch
    {
        RmlUiAdventureSaveAction.Copy => change.Source.Used && change.Destination is { } destination && Valid(destination) && destination.Slot != change.Source.Slot,
        RmlUiAdventureSaveAction.Archive => change.Destination == null,
        _ => false
    });
    internal static bool MatchesResult(RmlUiAdventureSaveChange change, RmlUiAdventureSlot[] slots)
    {
        if (!Valid(change) || slots == null) return false;
        var sources = slots.Where(slot => slot.Slot == change.Source.Slot).Take(2).ToArray();
        if (sources.Length != 1) return false;
        if (change.Action == RmlUiAdventureSaveAction.Archive)
            return !sources[0].Used && !sources[0].Unreadable && sources[0].Fingerprint == "EMPTY";
        var destination = change.Destination!.Value;
        var targets = slots.Where(slot => slot.Slot == destination.Slot).Take(2).ToArray();
        return sources[0] == change.Source && targets.Length == 1
            && targets[0] == change.Source with { Slot = destination.Slot };
    }
}
