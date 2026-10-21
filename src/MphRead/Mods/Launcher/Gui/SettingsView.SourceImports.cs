#if ANDROID
using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using MphRead.Mods.Import;
using MphRead.Mods.Settings;

namespace MphRead.Mods.Launcher.Gui;

internal sealed partial class SettingsView
{
    private readonly SetupViewWorkScope _sourceWork = new();
    private SourceCharacterImportWork.Operation? _sourceImport;
    private DispatcherTimer? _sourceTimer;
    private bool _sourceClosed;
    private void ResumeSourceWork()
    {
        if (_sourceClosed) return;
        _sourceWork.Resume(); _sourceTimer?.Start();
    }
    private void CloseSourceWork()
    {
        _sourceClosed = true; _sourceWork.TryClose(); RetireSourceWork();
    }
    private void RetireSourceWork()
    {
        _sourceWork.Retire(); _sourceImport?.Cancel(); _sourceImport = null; _sourceTimer?.Stop();
    }
    private void BuildAndroidSourceImports(StackPanel page)
    {
        Heading(page, "Additional character sources");
        Explain(page, "Import private appearance/source bundles from your own Prime or Echoes ISO, GCM or RVZ. "
            + "This does not add playable characters. A temporary source copy needs up to 2 GiB of internal storage and is deleted after importing.");
        var sources = RetroCharacterPreviewExporter.Sources;
        var choice = new ChoiceRow("Appearance", sources.Select(s => s.DisplayName).ToArray(), 0);
        page.Children.Add(choice);
        var status = Explain(page, "Choose an appearance, then import or check its local bundle.");
        var one = new HubNavButton("IMPORT / UPDATE SELECTED", "Choose the matching owned source disc", compact: true);
        var group = new HubNavButton("IMPORT SAME-SOURCE GROUP", "Import both Echoes appearances or all four Prime Troopers", compact: true);
        var check = new HubNavButton("CHECK SELECTED", "Verify installed bundle components", compact: true);
        var remove = new HubNavButton("REMOVE SELECTED", "Remove this appearance's current bundle selection", compact: true);
        var cancel = new HubNavButton("CANCEL IMPORT", compact: true);
        ControllerNav.Identify(choice, "settings.profile.source-appearance");
        ControllerNav.Identify(one, "settings.profile.source-import");
        ControllerNav.Identify(group, "settings.profile.source-import-group");
        ControllerNav.Identify(check, "settings.profile.source-check");
        ControllerNav.Identify(remove, "settings.profile.source-remove");
        ControllerNav.Identify(cancel, "settings.profile.source-cancel");
        foreach (var button in new[] { one, group, check, remove, cancel }) page.Children.Add(button);
        bool Ready(bool write)
        {
            if (_sourceClosed) return false;
            if (SourceCharacterImportWork.IsBusy) { status.Text = "The previous source operation is still finishing."; return false; }
            if (write && (_inGame || IsDirty || SettingsPersistence.RestartRequired))
            { status.Text = "Return to the launcher and apply or discard settings changes before changing imports."; return false; }
            return true;
        }
        async Task Import(bool sameSource)
        {
            if (!Ready(true)) return;
            var ticket = _sourceWork.Begin();
            try
            {
                var selected = sources[choice.Index];
                string[] ids = sameSource ? sources.Where(s => s.Game == selected.Game).Select(s => s.Id).ToArray() : [selected.Id];
                var operation = SourceCharacterImportWork.Start(RetroCharacterPreviewExporter.DefaultRoot, ids, ticket.Token,
                    sameSourceGroup: sameSource);
                _sourceImport = operation;
                var result = await operation.Completion;
                if (_sourceWork.Current(ticket)) status.Text = result.Message;
                if (ReferenceEquals(_sourceImport, operation)) _sourceImport = null;
            }
            catch (Exception) { if (_sourceWork.Current(ticket)) status.Text = "Source operation unavailable. Close any previous chooser and try again."; }
        }
        one.Click += async (_, _) => await Import(false);
        group.Click += async (_, _) => await Import(true);
        cancel.Click += (_, _) => { _sourceImport?.Cancel(); status.Text = "Cancelling; close the source chooser if it is still open."; };
        check.Click += async (_, _) =>
        {
            if (!Ready(false)) return;
            var ticket = _sourceWork.Begin();
            string id = sources[choice.Index].Id, root = RetroCharacterPreviewExporter.DefaultRoot;
            try
            {
                string result = await SourceCharacterImportWork.RunExclusive(() => RetroCharacterPreviewExporter.DescribeInspection(id,
                    RetroCharacterPreviewExporter.InspectBundle(root, id)), ticket.Token);
                if (_sourceWork.Current(ticket)) status.Text = result;
            }
            catch (Exception) { if (_sourceWork.Current(ticket)) status.Text = "Unable to check this bundle. Check local storage and try again."; }
        };
        remove.Click += (_, _) =>
        {
            if (!Ready(true)) return;
            string id = sources[choice.Index].Id, root = RetroCharacterPreviewExporter.DefaultRoot;
            var ticket = _sourceWork.Begin();
            object? previous = Content;
            var panel = new StackPanel { Spacing = 12, Margin = new Thickness(24) };
            Heading(panel, "Remove " + sources[choice.Index].DisplayName + "?");
            Explain(panel, "This removes the current local bundle selection. Your source document is unchanged. Retained generations may still be in use by a preview.");
            var confirm = new HubNavButton("CONFIRM REMOVE", compact: true);
            var back = new HubNavButton("CANCEL", compact: true);
            confirm.Click += async (_, _) =>
            {
                if (!_sourceWork.Current(ticket) || !Ready(true)) return;
                confirm.IsEnabled = false;
                back.IsEnabled = false;
                try
                {
                    bool removed = await SourceCharacterImportWork.RunExclusive(() => LocalImportBundle.Remove(root, id, ticket.Token), ticket.Token);
                    if (_sourceWork.Current(ticket)) { Content = previous; status.Text = removed ? "Current bundle selection removed." : "No current bundle selection was installed."; }
                }
                catch (Exception) { if (_sourceWork.Current(ticket)) { Content = previous; status.Text = "Unable to remove this bundle. Check local storage and try again."; } }
            };
            back.Click += (_, _) => { if (_sourceWork.Current(ticket)) Content = previous; };
            panel.Children.Add(confirm); panel.Children.Add(back); Content = panel;
        };
        _sourceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _sourceTimer.Tick += (_, _) =>
        {
            bool busy = SourceCharacterImportWork.IsBusy;
            bool write = !busy && !_inGame && !IsDirty && !SettingsPersistence.RestartRequired;
            one.IsEnabled = group.IsEnabled = remove.IsEnabled = write;
            check.IsEnabled = choice.IsEnabled = !busy;
            cancel.IsEnabled = _sourceImport is { Completion.IsCompleted: false };
            if (_sourceImport is { Completion.IsCompleted: false } operation)
                status.Text = operation.Phase + (operation.CopiedBytes == 0 ? "…" : $" ({operation.CopiedBytes / (1024 * 1024)} MiB copied)");
        };
        _sourceTimer.Start();
    }
}
#endif
