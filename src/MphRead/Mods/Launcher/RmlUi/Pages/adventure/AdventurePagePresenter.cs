#if MPHREAD_RMLUI_POC
using System;
using System.Collections.Generic;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Adventure
{
    /// <summary>Native document bindings only. AdventureController owns every save and launch choice.</summary>
    public sealed class AdventurePagePresenter : IDisposable
    {
        private static readonly RmlUiPageSpec PageSpec
            = new("adventure", "pages/adventure/saves.rml", "adventure_slot1");
        private static readonly RmlUiPageSpec ConfirmationSpec
            = new("adventure-overwrite", "pages/adventure/confirm.rml", "adventure_cancel_new_run");
        private readonly RmlUiHost _host;
        private readonly RmlUiPageManager _pages;
        private readonly AdventureController _controller;
        private readonly bool _ownsPages;
        private RmlUiDocumentToken _document;
        private RmlUiDocumentToken _confirmation;
        private bool _disposed;

        public AdventurePagePresenter(RmlUiHost host, AdventureController controller)
            : this(host, new RmlUiPageManager(host), controller, true) { }

        public AdventurePagePresenter(RmlUiHost host, RmlUiPageManager pages, AdventureController controller)
            : this(host, pages, controller, false) { }

        private AdventurePagePresenter(RmlUiHost host, RmlUiPageManager pages,
            AdventureController controller, bool ownsPages)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _pages = pages ?? throw new ArgumentNullException(nameof(pages));
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
            _ownsPages = ownsPages;
        }

        public RmlUiDocumentToken Document => _document;
        public RmlUiDocumentToken ConfirmationDocument => _confirmation;
        public bool IsOpen => !_disposed && _document != default && _host.IsAlive(_document)
            && _pages.Page == _document;

        public void Open()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(AdventurePagePresenter));
            _controller.Refresh();
            _document = _pages.OpenPage(PageSpec);
            Present();
            _host.FocusDocument(_document, "adventure_slot" + _controller.Snapshot().SelectedSlot);
        }

        public bool Present()
        {
            if (!IsOpen) return false;
            AdventureSnapshot snapshot = _controller.Snapshot();
            if (_confirmation != default && !_host.IsAlive(_confirmation))
            {
                // Escape/Back may retire the modal through the host before this page gets a frame.
                _confirmation = default;
                if (snapshot.ConfirmOverwrite) _controller.CancelConfirmation();
                snapshot = _controller.Snapshot();
                _host.FocusDocument(_document, "adventure_new_run");
            }
            _pages.Present(_document, snapshot.Revision, Bindings(snapshot));
            if (snapshot.ConfirmOverwrite)
            {
                if (_confirmation == default) _confirmation = _pages.OpenModal(ConfirmationSpec);
                _pages.Present(_confirmation, snapshot.Revision, ConfirmationBindings(snapshot));
            }
            else if (_confirmation != default)
            {
                if (!_pages.CloseModal()) return false;
                _confirmation = default;
            }
            return true;
        }

        public bool HandleIntent(in RmlUiIntent intent)
        {
            if (!IsOpen || !TryAction(intent, out AdventureIntentKind kind, out byte slot)) return false;
            bool modalAction = kind is AdventureIntentKind.ConfirmNewRun or AdventureIntentKind.CancelNewRun;
            if (modalAction ? intent.Document != _confirmation : intent.Document != _document) return false;
            if (!_pages.Accept(intent)) return false;
            _controller.Dispatch(_controller.Intent(kind, slot));
            Present();
            return true;
        }

        /// <summary>Only the Adventure replacement modal consumes Back; the app router handles the page.</summary>
        public bool Back()
        {
            if (!IsOpen || !_controller.Snapshot().ConfirmOverwrite) return false;
            _controller.CancelConfirmation();
            Present();
            return true;
        }

        public bool TryTakeLaunch(out LaunchPlan plan) => _controller.TryTakeLaunch(out plan);

        public void ReportLaunchFailure(string message)
        {
            _controller.ReportLaunchFailure(message);
            Present();
        }

        public bool Close()
        {
            _controller.CancelPending();
            if (_document != default && _pages.Page == _document && !_pages.ClosePage()) return false;
            _document = _confirmation = default;
            return true;
        }

        public void Dispose()
        {
            if (_disposed) return;
            if (!Close()) throw new InvalidOperationException("The Adventure page could not be closed.");
            if (_ownsPages) _pages.Dispose();
            _disposed = true;
        }

        private static bool TryAction(in RmlUiIntent intent, out AdventureIntentKind kind, out byte slot)
        {
            slot = 0;
            switch (intent.Kind)
            {
                case RmlUiIntentKind.AdventureSelectSlot:
                    kind = AdventureIntentKind.SelectSlot;
                    slot = (byte)intent.Argument;
                    return intent.Argument is >= 1 and <= AdventureSave.SlotCount;
                case RmlUiIntentKind.AdventureNextHunter: kind = AdventureIntentKind.NextHunter; break;
                case RmlUiIntentKind.AdventureContinue: kind = AdventureIntentKind.Continue; break;
                case RmlUiIntentKind.AdventureNewRun: kind = AdventureIntentKind.NewRun; break;
                case RmlUiIntentKind.AdventureConfirmNewRun: kind = AdventureIntentKind.ConfirmNewRun; break;
                case RmlUiIntentKind.AdventureCancelNewRun: kind = AdventureIntentKind.CancelNewRun; break;
                case RmlUiIntentKind.AdventureRefresh: kind = AdventureIntentKind.Refresh; break;
                default: kind = default; return false;
            }
            return intent.Argument == 0;
        }

        private static IEnumerable<KeyValuePair<string, RmlUiBindingValue>> Bindings(AdventureSnapshot state)
        {
            yield return Text("adventure_hunter", state.Hunter.ToString().ToUpperInvariant());
            yield return Text("adventure_selected_slot", $"SAVE SLOT {state.SelectedSlot:00}");
            foreach (AdventureSave.SlotInfo slot in state.Slots)
            {
                yield return Text($"adventure_slot{slot.Slot}_label",
                    $"SLOT {slot.Slot:00} // " + (slot.Used ? slot.Area.ToUpperInvariant() : "EMPTY"));
                yield return Visible($"class:adventure_slot{slot.Slot}:selected", slot.Slot == state.SelectedSlot);
                yield return Visible($"disabled:adventure_slot{slot.Slot}", !state.CanEdit);
            }
            AdventureSave.SlotInfo? selected = state.SelectedSave;
            yield return Text("adventure_save_detail", selected is { Used: true } save
                ? $"{save.Area}\nOCTOLITHS: {save.Octoliths} / 8\nENERGY: {save.Health} / {save.HealthMax}"
                : state.ReadFailed ? "" : "Initialize a fresh expedition from Celestial Archives.");
            yield return Text("adventure_file_problem", state.FileProblem);
            yield return Visible("adventure_files_error", state.FileProblem.Length > 0);
            yield return Text("adventure_error_text", state.Error);
            yield return Visible("adventure_error", state.Error.Length > 0);
            yield return Visible("disabled:adventure_continue", !state.CanContinue);
            yield return Visible("disabled:adventure_new_run", !state.CanNewRun);
            yield return Visible("disabled:adventure_next_hunter", !state.CanEdit);
            yield return Visible("disabled:adventure_refresh", !state.CanEdit);
            yield return Visible("adventure_launch_status", state.LaunchPending);
        }

        private static IEnumerable<KeyValuePair<string, RmlUiBindingValue>> ConfirmationBindings(AdventureSnapshot state)
        {
            yield return Text("adventure_overwrite_title", "REPLACE SAVE SLOT " + state.OverwriteSlot + "?");
            yield return Text("adventure_confirmation_error_text", state.Error);
            yield return Visible("adventure_confirmation_error", state.Error.Length > 0);
            yield return Visible("disabled:adventure_confirm_new_run", state.ReadFailed || state.FileProblem.Length > 0);
        }

        private static KeyValuePair<string, RmlUiBindingValue> Text(string id, string value)
            => new(id, RmlUiBindingValue.FromText(value));
        private static KeyValuePair<string, RmlUiBindingValue> Visible(string id, bool value)
            => new(id, RmlUiBindingValue.FromBoolean(value));
    }
}
#endif
