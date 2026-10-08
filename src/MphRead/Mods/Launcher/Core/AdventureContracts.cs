using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace MphRead.Mods.Launcher.Core
{
    public enum AdventureIntentKind
    {
        SelectSlot, SelectHunter, NextHunter, Continue, NewRun, ConfirmNewRun, CancelNewRun, Refresh
    }

    /// <summary>A choice belongs to the page lifetime and revision that displayed it.</summary>
    public readonly record struct AdventureIntent(Guid Lifetime, long ExpectedRevision,
        AdventureIntentKind Kind, byte Slot = 0, Hunter Hunter = Hunter.Samus);

    public readonly record struct AdventureActionResult(bool Accepted, string Message)
    {
        public static AdventureActionResult Ok => new(true, "");
        public static AdventureActionResult Reject(string message) => new(false, message);
    }

    /// <summary>Copied save summaries only. The engine remains the owner of loaded story state.</summary>
    public sealed record AdventureSnapshot
    {
        public Guid Lifetime { get; init; }
        public long Revision { get; init; }
        public ImmutableArray<AdventureSave.SlotInfo> Slots { get; init; }
            = ImmutableArray<AdventureSave.SlotInfo>.Empty;
        public ImmutableArray<Hunter> HunterChoices { get; init; } = ImmutableArray<Hunter>.Empty;
        public byte SelectedSlot { get; init; } = 1;
        public Hunter Hunter { get; init; }
        public bool ConfirmOverwrite { get; init; }
        public byte OverwriteSlot { get; init; }
        public bool LaunchPending { get; init; }
        public bool ReadFailed { get; init; }
        public bool Closed { get; init; }
        public string FileProblem { get; init; } = "";
        public string Error { get; init; } = "";
        public AdventureSave.SlotInfo? SelectedSave
        {
            get
            {
                foreach (AdventureSave.SlotInfo slot in Slots)
                    if (slot.Slot == SelectedSlot) return slot;
                return null;
            }
        }
        public bool CanEdit => !Closed && !LaunchPending && !ConfirmOverwrite;
        public bool CanNewRun => CanEdit && !ReadFailed && FileProblem.Length == 0;
        public bool CanContinue => CanNewRun && SelectedSave is { Used: true };
    }

    /// <summary>Authoritative service boundary; no scene is loaded and no save is written here.</summary>
    public interface IAdventureBackend
    {
        IReadOnlyList<AdventureSave.SlotInfo> ReadSlots();
        string? GameFileProblem();
        LaunchPlan CreateLaunch(byte slot, bool newGame, Hunter hunter);
    }
}
