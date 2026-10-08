using System;
using System.Collections.Immutable;
using System.Linq;
using MphRead.Mods.Multiplayer;

namespace MphRead.Mods.Launcher.Core
{
    /// <summary>
    /// Adventure menu choices shared by presentations. Slot inspection delegates
    /// to AdventureSave, and the engine loads the chosen slot through MatchStart.
    /// </summary>
    public sealed class AdventureController : IDisposable
    {
        private sealed class AdventureBackend : IAdventureBackend
        {
            public System.Collections.Generic.IReadOnlyList<AdventureSave.SlotInfo> ReadSlots()
                => AdventureSave.ReadAll();
            public string? GameFileProblem() => GameFiles.Problem();
            public LaunchPlan CreateLaunch(byte slot, bool newGame, Hunter hunter)
                => AdventureLaunch.Create(slot, newGame, hunter);
        }

        private readonly IAdventureBackend _backend;
        private readonly int _ownerThread = Environment.CurrentManagedThreadId;
        private AdventureSnapshot _snapshot;
        private LaunchPlan? _pendingLaunch;
        private Hunter _overwriteHunter;

        public AdventureController(MenuSettings settings)
            : this(new AdventureBackend(), LauncherPrefs.LastHunter, settings.LowTier == "on") { }

        public AdventureController(IAdventureBackend backend, Hunter initialHunter = Hunter.Samus,
            bool lowTier = false)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            var choices = HunterRules.Pool(lowTier).Append(Hunter.Random).ToImmutableArray();
            _snapshot = new AdventureSnapshot
            {
                Lifetime = Guid.NewGuid(), HunterChoices = choices,
                Hunter = choices.Contains(initialHunter) ? initialHunter : choices[0]
            };
            Refresh();
        }

        public AdventureSnapshot Snapshot()
        {
            CheckOwner();
            return _snapshot;
        }

        public AdventureIntent Intent(AdventureIntentKind kind, byte slot = 0,
            Hunter hunter = Hunter.Samus)
        {
            CheckOwner();
            return new(_snapshot.Lifetime, _snapshot.Revision, kind, slot, hunter);
        }

        public AdventureActionResult SelectSlot(byte slot) => Dispatch(Intent(AdventureIntentKind.SelectSlot, slot));
        public AdventureActionResult SelectHunter(Hunter hunter)
            => Dispatch(Intent(AdventureIntentKind.SelectHunter, hunter: hunter));
        public AdventureActionResult NextHunter() => Dispatch(Intent(AdventureIntentKind.NextHunter));
        public AdventureActionResult RequestContinue() => Dispatch(Intent(AdventureIntentKind.Continue));
        public AdventureActionResult RequestNewRun() => Dispatch(Intent(AdventureIntentKind.NewRun));
        public AdventureActionResult ConfirmNewRun() => Dispatch(Intent(AdventureIntentKind.ConfirmNewRun));
        public AdventureActionResult CancelConfirmation() => Dispatch(Intent(AdventureIntentKind.CancelNewRun));

        public AdventureActionResult Dispatch(AdventureIntent intent)
        {
            CheckOwner();
            if (_snapshot.Closed || intent.Lifetime != _snapshot.Lifetime
                || intent.ExpectedRevision != _snapshot.Revision)
                return AdventureActionResult.Reject("This Adventure selection is no longer current.");
            if (!Enum.IsDefined(intent.Kind)) return AdventureActionResult.Reject("Unknown Adventure action.");
            if (_snapshot.LaunchPending) return AdventureActionResult.Reject("An Adventure launch is already pending.");
            if (_snapshot.ConfirmOverwrite && intent.Kind is not
                (AdventureIntentKind.ConfirmNewRun or AdventureIntentKind.CancelNewRun))
                return AdventureActionResult.Reject("Confirm or cancel the new run first.");

            switch (intent.Kind)
            {
                case AdventureIntentKind.SelectSlot:
                    if (intent.Slot < 1 || intent.Slot > AdventureSave.SlotCount)
                        return AdventureActionResult.Reject($"Adventure save slots are 1-{AdventureSave.SlotCount}.");
                    Commit(_snapshot with { SelectedSlot = intent.Slot, Error = "" });
                    Refresh();
                    return AdventureActionResult.Ok;
                case AdventureIntentKind.SelectHunter:
                    if (!_snapshot.HunterChoices.Contains(intent.Hunter))
                        return AdventureActionResult.Reject("Choose a playable Hunter or Random.");
                    Commit(_snapshot with { Hunter = intent.Hunter, Error = "" });
                    return AdventureActionResult.Ok;
                case AdventureIntentKind.NextHunter:
                    int index = (_snapshot.HunterChoices.IndexOf(_snapshot.Hunter) + 1)
                        % _snapshot.HunterChoices.Length;
                    Commit(_snapshot with { Hunter = _snapshot.HunterChoices[index], Error = "" });
                    return AdventureActionResult.Ok;
                case AdventureIntentKind.Refresh:
                    Commit(_snapshot with { Error = "" });
                    Refresh();
                    return _snapshot.ReadFailed || _snapshot.FileProblem.Length > 0
                        ? AdventureActionResult.Reject(_snapshot.Error.Length > 0
                            ? _snapshot.Error : _snapshot.FileProblem) : AdventureActionResult.Ok;
                case AdventureIntentKind.Continue:
                    Refresh();
                    if (!CanLaunch(out AdventureActionResult failure)) return failure;
                    if (_snapshot.SelectedSave is not { Used: true })
                        return Fail("This save slot is empty.");
                    return QueueLaunch(_snapshot.SelectedSlot, false, _snapshot.Hunter);
                case AdventureIntentKind.NewRun:
                    Refresh();
                    if (!CanLaunch(out failure)) return failure;
                    if (_snapshot.SelectedSave is { Used: true })
                    {
                        _overwriteHunter = _snapshot.Hunter;
                        Commit(_snapshot with { ConfirmOverwrite = true,
                            OverwriteSlot = _snapshot.SelectedSlot, Error = "" });
                        return AdventureActionResult.Ok;
                    }
                    return QueueLaunch(_snapshot.SelectedSlot, true, _snapshot.Hunter);
                case AdventureIntentKind.ConfirmNewRun:
                    if (!_snapshot.ConfirmOverwrite) return AdventureActionResult.Reject("No replacement is awaiting confirmation.");
                    byte slot = _snapshot.OverwriteSlot;
                    Hunter hunter = _overwriteHunter;
                    // Revalidate game files, but never load the save the player chose to discard.
                    Refresh();
                    if (!CanLaunch(out failure)) return failure;
                    return QueueLaunch(slot, true, hunter);
                case AdventureIntentKind.CancelNewRun:
                    Commit(_snapshot with { ConfirmOverwrite = false, OverwriteSlot = 0 });
                    return AdventureActionResult.Ok;
                default:
                    return AdventureActionResult.Reject("Unknown Adventure action.");
            }
        }

        public void Refresh()
        {
            CheckOwner();
            if (_snapshot.Closed) return;
            try
            {
                string problem = _backend.GameFileProblem() ?? "";
                var slots = _backend.ReadSlots().ToImmutableArray();
                if (slots.Length != AdventureSave.SlotCount
                    || slots.Where((slot, index) => slot.Slot != index + 1).Any())
                    throw new InvalidOperationException("The Adventure save slot list is invalid.");
                Commit(_snapshot with { Slots = slots, FileProblem = problem, ReadFailed = false });
            }
            catch (Exception ex)
            {
                Commit(_snapshot with { ReadFailed = true, Error = ex.Message });
            }
        }

        private bool CanLaunch(out AdventureActionResult failure)
        {
            if (_snapshot.ReadFailed) { failure = AdventureActionResult.Reject(_snapshot.Error); return false; }
            if (_snapshot.FileProblem.Length > 0) { failure = Fail(_snapshot.FileProblem); return false; }
            failure = AdventureActionResult.Ok;
            return true;
        }

        private AdventureActionResult QueueLaunch(byte slot, bool fresh, Hunter hunter)
        {
            try
            {
                LaunchPlan plan = _backend.CreateLaunch(slot, fresh, hunter);
                _pendingLaunch = plan;
                Commit(_snapshot with { LaunchPending = true, ConfirmOverwrite = false,
                    OverwriteSlot = 0, Error = "" });
                return AdventureActionResult.Ok;
            }
            catch (Exception ex) { return Fail(ex.Message); }
        }

        /// <summary>One handoff to the engine. Repeated clicks cannot issue another launch.</summary>
        public bool TryTakeLaunch(out LaunchPlan plan)
        {
            CheckOwner();
            plan = default;
            if (_snapshot.Closed || _pendingLaunch is not { } pending) return false;
            _pendingLaunch = null;
            plan = pending;
            return true;
        }

        /// <summary>The engine supplies its exact MatchStart/exception message after a failed load.</summary>
        public void ReportLaunchFailure(string message)
        {
            CheckOwner();
            if (_snapshot.Closed) return;
            _pendingLaunch = null;
            Commit(_snapshot with { LaunchPending = false, ConfirmOverwrite = false,
                OverwriteSlot = 0 });
            Refresh();
            // A failed follow-up scan must not replace the engine's actual load failure.
            Commit(_snapshot with { Error = message ?? "" });
        }

        /// <summary>Navigation discards a queued choice without touching a save or scene.</summary>
        public void CancelPending()
        {
            CheckOwner();
            _pendingLaunch = null;
            if (!_snapshot.Closed)
                Commit(_snapshot with { LaunchPending = false, ConfirmOverwrite = false, OverwriteSlot = 0 });
        }

        private AdventureActionResult Fail(string message)
        {
            Commit(_snapshot with { Error = message });
            return AdventureActionResult.Reject(message);
        }

        private void Commit(AdventureSnapshot next)
        {
            // ReadAll copies arrays each time. Equal save summaries do not dirty native DOM text.
            if (next.Slots.SequenceEqual(_snapshot.Slots)) next = next with { Slots = _snapshot.Slots };
            if (next == _snapshot) return;
            _snapshot = next with { Revision = _snapshot.Revision + 1 };
        }

        private void CheckOwner()
        {
            if (Environment.CurrentManagedThreadId != _ownerThread)
                throw new InvalidOperationException("Adventure commands must run on the engine owner thread.");
        }

        public void Dispose()
        {
            CheckOwner();
            if (_snapshot.Closed) return;
            _pendingLaunch = null;
            Commit(_snapshot with { Closed = true, LaunchPending = false,
                ConfirmOverwrite = false, OverwriteSlot = 0 });
        }
    }
}
