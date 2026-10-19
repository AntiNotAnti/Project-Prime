#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Update;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Updates;

/// <summary>Foreground catalog and frozen confirmation. Only the retained launcher owner installs.</summary>
internal sealed class NativeVersionManager : IDisposable
{
    private readonly RmlUiHost _host;
    private readonly RmlUiPageManager _pages;
    private readonly Func<bool> _ownerCurrent, _busy, _disabled;
    private readonly Func<string> _status;
    private readonly Action<UpdateInfo> _install;
    private readonly Func<Task<(IReadOnlyList<UpdateInfo> Releases, string Reason)>> _catalog;
    private readonly Version? _current;
    private string Installed => _current == null ? "a local build" : "v" + _current.ToString(3);
    private Task<(IReadOnlyList<UpdateInfo> Releases, string Reason)>? _load;
    private IReadOnlyList<UpdateInfo> _releases = Array.Empty<UpdateInfo>();
    private UpdateInfo? _selected;
    private bool _disposed;
    internal RmlUiDocumentToken Document { get; private set; }
    internal bool Closed { get; private set; }
    internal NativeVersionManager(RmlUiHost host, RmlUiPageManager pages, StartScreen owner)
        : this(host, pages, () => owner.RmlUiUpdateOwnerCurrent, () => owner.RmlUiVersionBusy,
            () => owner.RmlUiVersionStatus, owner.RmlUiInstallVersion,
            () => Task.Run(() => (UpdateCheck.Releases(30), UpdateCheck.LastReason ?? "No published releases were found.")),
            BuildVersion.Current, () => Updater.Disabled) { }
    internal NativeVersionManager(RmlUiHost host, RmlUiPageManager pages, Func<bool> ownerCurrent,
        Func<bool> busy, Func<string> status, Action<UpdateInfo> install,
        Func<Task<(IReadOnlyList<UpdateInfo> Releases, string Reason)>> catalog, Version? current,
        Func<bool>? disabled = null)
    {
        (_host, _pages, _ownerCurrent, _busy, _status, _install, _catalog) =
            (host, pages, ownerCurrent, busy, status, install, catalog);
        _current = current == null ? null : BuildVersion.Normalise(current);
        _disabled = disabled ?? (() => false);
    }
    internal void Open()
    {
        Document = _pages.OpenModal(new("version-manager", "pages/updates/versions.rml", "versions_close"));
        _host.SetText(Document, "versions_installed", Installed);
        Load();
    }
    private void Load()
    {
        if (_load != null || _busy()) return;
        _releases = Array.Empty<UpdateInfo>();
        _host.SetText(Document, "versions_status", _disabled() ? "Version checks are disabled for this run." : "Loading published releases...");
        ProjectRows();
        if (_disabled()) return;
        // No UI mutation escapes to a worker. Disposing abandons the result, never the install owner.
        try { _load = _catalog(); }
        catch (Exception error) { _host.SetText(Document, "versions_status", error.Message); }

    }
    internal void Tick()
    {
        if (_disposed || Closed) return;
        if (!_ownerCurrent()) { Closed = true; return; }
        if (_pages.Top != Document || _host.CurrentInputDocument != Document) return;
        if (_load is { IsCompleted: true, IsCompletedSuccessfully: false } failed)
        {
            _load = null;
            _host.SetText(Document, "versions_status", failed.IsCanceled ? "Release loading was canceled. Retry to check again."
                : failed.Exception?.GetBaseException().Message ?? "Release loading failed.");
        }
        if (_load is { IsCompletedSuccessfully: true } completed)
        {
            _load = null;
            // Snapshot a bounded catalog: provider-owned lists may mutate after completion.
            var snapshot = new List<UpdateInfo>(30);
            foreach (var release in completed.Result.Releases)
            {
                if (release.Version == null || string.IsNullOrWhiteSpace(release.Tag)) continue;
                snapshot.Add(release);
                if (snapshot.Count == 30) break;
            }
            _releases = snapshot;
            string reason = completed.Result.Reason;
            _host.SetText(Document, "versions_status", _releases.Count == 0 ? reason : "Select a published release to switch versions.");
            ProjectRows();
        }
        _host.SetText(Document, "versions_progress", _status());
        _host.SetBool(Document, "disabled:versions_retry", _load != null || _busy() || _disabled() || _selected != null);
        _host.SetBool(Document, "disabled:versions_confirm", _selected == null || _busy());
    }
    private void ProjectRows()
    {
        Version? current = _current;
        for (int i = 0; i < 30; i++)
        {
            bool visible = i < _releases.Count;
            _host.SetBool(Document, "visible:versions_row_" + i, visible);
            if (!visible) continue;
            var release = _releases[i]; bool installed = current != null && release.Version == current;
            _host.SetText(Document, "versions_row_" + i, release.Tag + (installed ? "  // CURRENT" : current != null && release.Version < current ? "  // DOWNGRADE" : "  // SWITCH"));
            _host.SetBool(Document, "disabled:versions_row_" + i, installed || _busy() || _selected != null);
        }
    }
    internal bool Handle(in RmlUiIntent intent)
    {
        if (_disposed || Closed || !_ownerCurrent() || intent.Document != Document || _pages.Top != Document
            || _host.CurrentInputDocument != Document || intent.Kind is not (RmlUiIntentKind.SettingsClose
                or RmlUiIntentKind.SettingsDiscard or RmlUiIntentKind.SettingsApply or RmlUiIntentKind.SettingsAction)) return false;
        if (intent.Kind == RmlUiIntentKind.SettingsAction && (intent.Argument < 0 || intent.Argument > 31)) return false;
        if (!_pages.Accept(intent)) return false;
        int releaseIndex = intent.Argument > 15 ? intent.Argument - 1 : intent.Argument;
        if (intent.Kind == RmlUiIntentKind.SettingsClose) Back();
        else if (intent.Kind == RmlUiIntentKind.SettingsDiscard) { _selected = null; ProjectRows(); _host.SetText(Document, "versions_confirmation", ""); }
        else if (intent.Kind == RmlUiIntentKind.SettingsApply && _selected is { } target && !_busy())
        {
            _selected = null;
            _host.SetText(Document, "versions_confirmation", "");
            _install(target);
            ProjectRows();
        }
        else if (intent.Kind == RmlUiIntentKind.SettingsAction && intent.Argument == 31 && _selected == null) Load();
        else if (intent.Kind == RmlUiIntentKind.SettingsAction && _selected == null && !_busy()
            && intent.Argument >= 0 && releaseIndex < _releases.Count)
        {
            var candidate = _releases[releaseIndex];
            Version? current = _current;
            if (candidate.Version == current) return true;
            _selected = candidate;
            string verb = current != null && candidate.Version < current ? "Downgrade" : "Switch";
            _host.SetText(Document, "versions_confirmation", $"{verb} from {Installed} to {candidate.Tag}? Any active lobby will be left. "
                + (OperatingSystem.IsMacOS() ? "The signed application bundle must be replaced from the release page." : "The application will restart when an in-place switch is supported."));
            ProjectRows();
        }
        Tick();
        return true;
    }
    internal void Back()
    {
        if (_disposed || Closed || _pages.Top != Document || _host.CurrentInputDocument != Document) return;
        if (_selected != null) { _selected = null; _host.SetText(Document, "versions_confirmation", ""); ProjectRows(); }
        else Closed = true;
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = Closed = true; _load = null; _selected = null;
        if (_pages.Top == Document) _pages.CloseModal();
    }
}
#endif
