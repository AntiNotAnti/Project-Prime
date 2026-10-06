using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using MphRead.AvaloniaShared;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Launcher.Gui;

// The embedded game compatibility adapter is the only map UI owner of game-shell services.
internal sealed class LegacyMapStudioHostServices(PrimeOverlayHost? overlays) : IMapStudioHostServices
{
    private Control? _sheet;
    public bool IsStandalone => false;
    public bool NativeFileDialogs => false;
    public bool GameFilesReady => GameFiles.Ready;
    public void ApplyGamePaths() => GameFiles.ApplyPaths();
    public string MapLibraryDirectory => CustomRooms.MapDirectory;
    public string UserMapDirectory => CustomRooms.UserMapDirectory;
    public string CommunitySettingsDirectory => LauncherPrefs.Directory;
    public string StagingDirectory => Path.GetTempPath();
    public IMapBuildScheduler BuildScheduler => MapBuildScheduler.Shared;
    public MapContentIdentity? GetInstalledIdentity(Guid mapId) => CustomRooms.Installed.TryGet(mapId,out var installed) ? installed.Identity : null;
    public Task<string?> PickFileAsync(string title, bool save, IReadOnlyList<string> extensions, CancellationToken cancellation)
    {
#if !ANDROID
        return NativeFilePicker.Available && !save ? NativeFilePicker.OpenFile(title, "Supported files", string.Join(";", extensions)) : Task.FromResult<string?>(null);
#else
        return Task.FromResult<string?>(null);
#endif
    }
    public bool ShowModal(Control content, bool fitContent, Action dismiss)
    {
        if (overlays is null) return false;
        DismissModal(); _sheet = content;
        overlays.Show(content, PrimeModalSize.Large, dismiss, fitContent);
        return true;
    }
    public Control? CreateDockLayout(Control hierarchy, Control viewport, Control inspector, Control problems) => null;
    public void ShowProblems(bool visible) { }
    public void ToggleSidePanels() { }
    public bool OpenAssetBrowser(Control content) => false;
    public void DismissModal() { if (_sheet is not null) overlays?.Close(_sheet); _sheet = null; }
    public IDisposable? SubscribeFilesDropped(Action<IReadOnlyList<string>> handler)
    {
#if MPHREAD_SHELL
        Shell.FilesDropped += handler;
        return new Subscription(() => Shell.FilesDropped -= handler);
#else
        return null;
#endif
    }
    public void SetAuthoringBackdrop() => LauncherBackdrop.Set(LauncherBackdropScene.MapEditor);
    public Task<string> GetCommunityTicketAsync(bool refresh, CancellationToken cancellation) => refresh
        ? HunterLicenseClient.RefreshCommunityMapTicketAsync(cancellation) : HunterLicenseClient.GetCommunityMapTicketAsync(cancellation);
    public Task PublishBuildAsync(MapBuildResult result, MapDefinition definition, CancellationToken cancellation)
    {
        MapBuildScheduler.Publish(result, definition, CustomRooms.ArchiveDirectory(definition), CustomRooms.EntityDirectory(), CustomRooms.NodeDirectory(), cancellation);
        Metadata.RegisterDownloadedMap(definition);
        return Task.CompletedTask;
    }
    public async Task<MapDefinition> CommitPackageAsync(string path, MapContentIdentity identity, CancellationToken cancellation)
    {
        using var prepared = await MapPackageInstaller.PrepareAsync(path, identity, cancellation);
        var definition = prepared.Commit(UserMapDirectory, cancellation: cancellation);
        Metadata.RegisterDownloadedMap(definition);
        return definition;
    }
    public Task RequestPlaytestAsync(MapProject project, CancellationToken cancellation) => Task.CompletedTask;
    public Task RequestHostAsync(string path, MapContentIdentity identity, string address, CancellationToken cancellation) => Task.CompletedTask;
    public Task<MapAuditResult> AuditAsync(MapProject project, CancellationToken cancellation) => MapAuditRunner.Run(project, cancellation);
    public Task RunJobAsync(string label, Func<CancellationToken, Task> work, CancellationToken cancellation) => work(cancellation);
    public Task OpenDetachedEditorAsync(string? project, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
#if MPHREAD_SHELL
        if (!StudioIntegration.StudioApplicationLauncher.TryOpen(project,false,out string? error))
            throw new IOException(error ?? "Project Prime Studio could not start.");
        return Task.CompletedTask;
#else
        return Task.FromException(new PlatformNotSupportedException("Separate editor windows require the desktop game build."));
#endif
    }
    private sealed class Subscription(Action close) : IDisposable { public void Dispose() => close(); }
}

internal sealed partial class MapStudioScreen
{
    public MapStudioScreen(PrimeOverlayHost? overlays = null, bool preview = false) : this(new LegacyMapStudioHostServices(overlays), preview) { }
}
