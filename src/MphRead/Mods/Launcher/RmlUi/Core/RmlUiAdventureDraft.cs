using System;
using System.Linq;

namespace MphRead.Mods.Launcher.RmlUi.Core;

/// <summary>
/// Immutable view of a real Project Prime StorySave. Content fingerprint comes
/// from the existing portable save path and must match again at handoff.
/// </summary>
public readonly record struct RmlUiAdventureSlot(
    byte Slot, bool Used, bool Unreadable, string Area,
    int Octoliths, int Health, int HealthMax, string Fingerprint);

public enum RmlUiAdventureNewRun { Refused, Ready, NeedsConfirmation }

/// <summary>
/// No save serializer, overwrite, network request or campaign simulation here.
/// All choices remain local until the existing AdventureLaunch.Create handoff.
/// </summary>
public sealed class RmlUiAdventureDraft
{
    private RmlUiAdventureSlot[] _slots;
    private byte _selected = 1;
    private Hunter _hunter;
    private RmlUiAdventureSlot? _confirmation;
    private bool _launched;
    private string _status = "SELECT A CAMPAIGN SAVE";

    public byte SelectedSlot => _selected;
    public Hunter Hunter => _hunter;
    public bool Confirming => _confirmation.HasValue;
    public RmlUiAdventureSlot? PendingOverwrite => _confirmation;
    public bool Launched => _launched;
    public string Status => _status;

    public RmlUiAdventureDraft(RmlUiAdventureSlot[] slots, Hunter hunter)
    {
        _hunter = (uint)hunter < 7 ? hunter : Hunter.Samus;
        _slots = Normalize(slots);
    }

    private static RmlUiAdventureSlot[] Normalize(RmlUiAdventureSlot[] slots)
    {
        ArgumentNullException.ThrowIfNull(slots);
        // Require exactly three, with explicit invalid/unreadable placeholders
        // for missing or duplicated responses.
        var result = new RmlUiAdventureSlot[AdventureSave.SlotCount];
        for (int i = 0; i < result.Length; i++)
        {
            byte id = (byte)(i + 1);
            var matches = slots.Where(s => s.Slot == id).Take(2).ToArray();
            result[i] = matches.Length == 1 ? matches[0]
                : new(id, false, true, "", 0, 0, 0, "UNAVAILABLE");
        }
        return result;
    }

    public RmlUiAdventureSlot Slot(int number)
        => number >= 1 && number <= _slots.Length
            ? _slots[number - 1] : new(0, false, true, "", 0, 0, 0, "INVALID");
    public RmlUiAdventureSlot Selected => Slot(_selected);
    public bool CanResume => Selected.Used && !Selected.Unreadable && !_confirmation.HasValue && !_launched;
    public bool CanStart => !Selected.Unreadable && !_confirmation.HasValue && !_launched;

    public bool Select(int index)
    {
        if (index is < 1 or > AdventureSave.SlotCount || _confirmation.HasValue || _launched)
            return false;
        _selected = (byte)index;
        _status = "SLOT " + index + " SELECTED";
        return true;
    }

    public bool NextHunter()
    {
        if (_confirmation.HasValue || _launched) return false;
        _hunter = (Hunter)(((int)_hunter + 1) % 7);
        return true;
    }

    public bool Refresh(RmlUiAdventureSlot[] slots)
    {
        if (_confirmation.HasValue || _launched) return false;
        _slots = Normalize(slots);
        _status = "SAVE SLOTS REFRESHED";
        return true;
    }

    public RmlUiAdventureNewRun RequestNew()
    {
        if (!CanStart) return RmlUiAdventureNewRun.Refused;
        if (!Selected.Used) return RmlUiAdventureNewRun.Ready;
        _confirmation = Selected;
        _status = "REVIEW BEFORE REPLACING AN EXISTING SAVE";
        return RmlUiAdventureNewRun.NeedsConfirmation;
    }

    public bool CancelConfirmation()
    {
        if (!_confirmation.HasValue) return false;
        _confirmation = null;
        _status = "NEW RUN CANCELLED // SAVE UNCHANGED";
        return true;
    }

    public RmlUiAdventureSlot? Confirm()
    {
        if (_confirmation is not { } selected || _launched) return null;
        _confirmation = null;
        return selected;
    }

    public void LaunchedSuccessfully()
    {
        _launched = true;
        _confirmation = null;
        _status = "ADVENTURE HANDOFF ACCEPTED";
    }

    public string Describe(RmlUiAdventureSlot info)
    {
        if (info.Unreadable) return "UNREADABLE // ARCHIVE SAVE BEFORE REPLACING";
        if (!info.Used) return "EMPTY // NEW EXPEDITION";
        return (info.Area ?? "UNKNOWN").ToUpperInvariant()
            + " // OCTOLITHS " + info.Octoliths + "/8"
            + " // ENERGY " + info.Health + "/" + info.HealthMax;
    }
}
