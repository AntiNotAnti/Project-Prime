#if (MPHREAD_RMLUI_POC || MPHREAD_RMLUI) && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Update;

namespace MphRead.Mods.Launcher.RmlUi.Setup;

internal sealed class SetupPagePresenter : IDisposable
{
    private readonly RmlUiHost _host;
    private readonly RmlUiPageManager _pages;
    private readonly EngineSetupBackend _backend;
    private readonly SetupController _controller;
    private readonly Action _closed, _quit;
    private readonly Action? _beforeInstall;
    private readonly Action<string>? _reportFailure;
    private readonly bool _inGame;
    private RmlUiDocumentToken _page, _modal;
    private string _modalKind = "";
    private long _revision;
    private long _lastSnapshotRevision;
    private bool _disposed, _quitRequested, _automaticPrompt, _releaseFlow;
    private string _lastReportedFailure = "";
    internal RmlUiDocumentToken Document => _page;
    internal SetupController Controller => _controller;
    internal bool Busy => _controller.Busy;
    internal SetupPagePresenter(RmlUiHost host, RmlUiPageManager pages, Action closed, Action quit,
        Func<Task<Stream?>>? pickRom = null, bool inGame = false, bool required = false, Action? beforeInstall = null,
        Action<string>? reportFailure = null)
    {
        _host = host; _pages = pages; _closed = closed; _quit = quit; _inGame = inGame; _beforeInstall = beforeInstall; _reportFailure = reportFailure;
        _backend = new(pickRom); _controller = new(_backend, inGame, required);
    }
    internal void Open()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _page = _pages.OpenPage(new("setup", "pages/setup/setup.rml", "setup_pick_rom"));
        _lastSnapshotRevision = 0;
        _host.SetField(_page, "setup_revision", Paths.MphKey);
        _host.SetField(_page, "setup_data_path", Paths.FileSystem);
        Refresh();
    }
    internal void OpenLatest(UpdateInfo release, bool automatic = false)
    {
        Open(); _automaticPrompt = automatic; _releaseFlow = true;
        SetupResult available = _backend.UseAvailableRelease(release);
        _controller.PresentPublishedReleases(available.Releases!, available.Message);
        _controller.RequestPrepare(); Refresh();
    }
    internal bool HandleAction(in RmlUiIntent intent)
    {
        if (_disposed || _page == default || _pages.Page != _page
            || intent.Kind is not (RmlUiIntentKind.SetupAction or RmlUiIntentKind.SetupRelease) || !_pages.Accept(intent)) return false;
        if (_controller.Busy) return true;
        if (intent.Kind == RmlUiIntentKind.SetupRelease) _controller.SelectRelease(intent.Argument);
        else switch (intent.Argument)
        {
            case 0: Back(); break;
            case 1: _controller.PickRom(); break;
            case 2:
                if (_modalKind == "rom" && intent.Document == _modal) _controller.ConfirmRom();
                else _controller.RequestRom(_host.ReadField(_page, "setup_rom_path", 128 * 1024)); break;
            case 3: _controller.RefreshFiles(); break;
            case 4: _controller.Verify(); break;
            case 5: _controller.RenderPreviews(); break;
            case 6: _releaseFlow = false; _controller.CheckLatest(); break;
            case 7: _releaseFlow = false; _controller.LoadReleases(); break;
            case 8: _releaseFlow = true; _controller.RequestPrepare(); break;
            case 9: if (_modalKind == "update" && intent.Document == _modal) { _automaticPrompt = false; _releaseFlow = true; _controller.ConfirmPrepare(); } break;
            case 10:
                if (_controller.Snapshot().Prepared && !_inGame) { _releaseFlow = true; _beforeInstall?.Invoke(); _controller.InstallPrepared(); } break;
            case 11:
                _controller.CancelConfirmation(); _releaseFlow = false;
                if (_automaticPrompt) { _automaticPrompt = false; CloseModal(); Back(); } break;
            case 12: _controller.OpenRelease(); break;
            case 13: _controller.OpenFolder(); break;
            case 14: _controller.ConfigurePath(_host.ReadField(_page, "setup_revision", 128), _host.ReadField(_page, "setup_data_path", 128 * 1024)); break;
        }
        Refresh(); return true;
    }
    internal bool Back()
    {
        if (_disposed || _page == default || _pages.Page != _page) return false;
        if (_modal != default)
        {
            _controller.CancelConfirmation(); CloseModal();
            if (!_automaticPrompt) { Refresh(); return true; }
            _automaticPrompt = false;
        }
        if (!_controller.CanLeave) return true;
        if (_pages.ClosePage()) { _page = default; _closed(); }
        return true;
    }
    internal void Refresh()
    {
        if (_disposed || _page == default || _pages.Page != _page) return;
        _controller.Tick(); SetupSnapshot state = _controller.Snapshot();
        if (state.Revision == _lastSnapshotRevision) return;
        _lastSnapshotRevision = state.Revision;
        if (!String.IsNullOrWhiteSpace(state.Error) && state.Error != _lastReportedFailure)
        {
            _lastReportedFailure = state.Error;
            _reportFailure?.Invoke(state.Error);
        }
        string modal = state.ConfirmingRom ? "rom" : state.ConfirmingUpdate ? "update" : "";
        if (_modal != default && !_host.IsAlive(_modal)) { _modal = default; _modalKind = ""; _controller.CancelConfirmation(); modal = ""; }
        if (modal != _modalKind)
        {
            CloseModal();
            if (modal != "") { _modal = _pages.OpenModal(new("setup-" + modal, "pages/setup/" + modal + ".rml", "setup_confirm_cancel")); _modalKind = modal; }
        }
        var fields = new Dictionary<string, RmlUiBindingValue>(StringComparer.Ordinal);
        void Text(string id, string value) => fields[id] = RmlUiBindingValue.FromText(value.Length > 3800 ? value[..3800] : value);
        void Bool(string key, bool value) => fields[key] = RmlUiBindingValue.FromBoolean(value);
        Text("setup_files_status", state.Files.Description); Text("setup_root", state.Files.Root);
        Text("setup_status", state.Status); Text("setup_error", state.Error); Text("setup_progress", state.Progress); Text("setup_log", state.Log);
        Text("setup_build", BuildVersion.Display);
        Text("setup_picker_status", state.Files.CanPickRom ? "Choose a .nds dump from your own cartridge." : OperatingSystem.IsAndroid()
            ? "The system ROM picker is unavailable." : "The native file picker is unavailable. Enter the full .nds path below.");
        Bool("visible:setup_desktop_paths", !OperatingSystem.IsAndroid());
        Bool("disabled:setup_back", !state.CanLeave);
        Bool("disabled:setup_pick_rom", state.Busy || _inGame || !state.Files.CanPickRom);
        Bool("disabled:setup_install_rom", state.Busy || _inGame);
        Bool("disabled:setup_configure_path", state.Busy || _inGame);
        Bool("disabled:setup_refresh", state.Busy); Bool("disabled:setup_verify", state.Busy);
        Bool("disabled:setup_previews", state.Busy || _inGame || !state.Files.Ready || !state.Files.CanRenderPreviews);
        Bool("disabled:setup_check_update", state.Busy || state.Files.UpdatesDisabled);
        Bool("disabled:setup_versions", state.Busy || state.Files.UpdatesDisabled);
        Bool("disabled:setup_open_folder", state.Busy || OperatingSystem.IsAndroid());
        Bool("disabled:setup_prepare", state.Busy || _inGame || state.SelectedRelease < 0);
        Bool("disabled:setup_release_page", state.Busy || state.SelectedRelease < 0);
        Bool("visible:setup_install", state.Prepared); Bool("disabled:setup_install", state.Busy || !state.Prepared || _inGame);
        Text("setup_selected", state.SelectedRelease >= 0 ? state.Releases[state.SelectedRelease].Tag : "No published release selected");
        Text("setup_release_notes", state.SelectedRelease >= 0 ? state.Releases[state.SelectedRelease].Notes : "Check for updates or load published versions.");
        // Display the controller's actual progress. The overlay has no installer authority.
        bool flowVisible = _releaseFlow && (state.Busy || state.Prepared || state.WaitingForInstaller
            || !String.IsNullOrEmpty(state.Error));
        bool failed = !String.IsNullOrEmpty(state.Error);
        string progress = state.Progress;
        int percent = -1, marker = progress.LastIndexOf('%');
        if (marker > 0)
        {
            int start = marker - 1;
            while (start >= 0 && char.IsAsciiDigit(progress[start])) start--;
            if (start + 1 < marker && int.TryParse(progress[(start + 1)..marker], out int parsed))
                percent = Math.Clamp(parsed, 0, 100);
        }
        Bool("visible:setup_update_overlay", flowVisible);
        Bool("visible:setup_update_install", state.Prepared && !failed && !_inGame);
        Bool("visible:setup_update_release_page", failed && state.SelectedRelease >= 0);
        Bool("disabled:setup_update_install", state.Busy || !state.Prepared || _inGame);
        Text("setup_update_stage", failed ? "UPDATE FAILED" : state.Prepared ? "READY TO INSTALL"
            : state.WaitingForInstaller ? "WAITING FOR SYSTEM INSTALLER" : "DOWNLOADING & VERIFYING");
        Text("setup_update_percent", state.Prepared ? "VERIFIED" : percent < 0 ? "WORKING" : percent.ToString() + "%");
        Text("setup_update_message", failed ? state.Error : state.Prepared
            ? "The downloaded package passed verification. Install to complete the version switch."
            : state.WaitingForInstaller ? "Complete the system installer to finish the update."
            : progress.Length > 0 ? progress : state.Status);
        for (int i = 0; i < 12; i++)
            Bool("class:setup_progress_segment_" + i + ":filled", state.Prepared || percent >= (i + 1) * 100 / 12);
        for (int i = 0; i < 30; i++)
        {
            bool active = i < state.Releases.Count;
            Bool("visible:setup_release_" + i, active); Bool("disabled:setup_release_" + i, state.Busy);
            Bool("class:setup_release_" + i + ":selected", i == state.SelectedRelease);
            if (active) Text("setup_release_" + i, state.Releases[i].Tag + (state.Releases[i].Downgrade ? "  •  downgrade" : ""));
        }
        _pages.Present(_page, ++_revision, fields);
        if (_modal != default)
        {
            string message = state.ConfirmingRom ? "Install " + state.Rom + " into " + state.Files.Root + "? Existing extracted files for this revision will be replaced."
                : state.SelectedRelease >= 0 ? "Prepare " + state.Releases[state.SelectedRelease].Tag + "? " + (state.Releases[state.SelectedRelease].Downgrade
                    ? "This is a downgrade. " : "") + "The selected published package will be downloaded and verified before Install becomes available."
                : "Select a release first.";
            _pages.Present(_modal, ++_revision, new Dictionary<string, RmlUiBindingValue> { ["setup_confirm_message"] = RmlUiBindingValue.FromText(message) });
        }
        if (_controller.ExitRequested && !_quitRequested) { _quitRequested = true; _quit(); }
    }
    private void CloseModal()
    { if (_modal != default && _pages.Top == _modal) _pages.CloseModal(); _modal = default; _modalKind = ""; }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        _controller.Dispose(); _backend.Dispose(); CloseModal();
        if (_page != default && _pages.Page == _page) _pages.ClosePage(); _page = default;
    }
}
#endif
