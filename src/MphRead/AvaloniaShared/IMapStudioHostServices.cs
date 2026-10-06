#if MPHREAD_AVALONIA
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;

namespace MphRead.AvaloniaShared;

/// <summary>Optional keyboard semantics for a canonical editor dialog action.</summary>
public sealed record MapStudioDialogAction(Action Invoke,bool IsDefault=false);

/// <summary>Explicit desktop host boundary. No gameplay owner or authentication store belongs to an editor control.</summary>
public interface IMapStudioHostServices
{
    bool IsStandalone { get; }
    bool NativeFileDialogs { get; }
    bool GameFilesReady { get; }
    void ApplyGamePaths();
    string MapLibraryDirectory { get; }
    string UserMapDirectory { get; }
    string CommunitySettingsDirectory { get; }
    string StagingDirectory { get; }
    IMapBuildScheduler BuildScheduler { get; }
    MapContentIdentity? GetInstalledIdentity(Guid mapId);
    Task<string?> PickFileAsync(string title, bool save, IReadOnlyList<string> extensions, CancellationToken cancellation);
    bool ShowModal(Control content, bool fitContent, Action dismiss);
    Control? CreateDockLayout(Control hierarchy, Control viewport, Control inspector, Control problems);
    void ShowProblems(bool visible);
    void ToggleSidePanels();
    bool OpenAssetBrowser(Control content);
    void DismissModal();
    IDisposable? SubscribeFilesDropped(Action<IReadOnlyList<string>> handler);
    void SetAuthoringBackdrop();
    Task<string> GetCommunityTicketAsync(bool refresh, CancellationToken cancellation);
    Task PublishBuildAsync(MapBuildResult result, MapDefinition definition, CancellationToken cancellation);
    Task<MapDefinition> CommitPackageAsync(string path, MapContentIdentity identity, CancellationToken cancellation);
    Task RequestPlaytestAsync(MapProject project, CancellationToken cancellation);
    Task RequestHostAsync(string path, MapContentIdentity identity, string address, CancellationToken cancellation);
    Task<MapAuditResult> AuditAsync(MapProject project, CancellationToken cancellation);
    Task RunJobAsync(string label, Func<CancellationToken, Task> work, CancellationToken cancellation);
    Task OpenDetachedEditorAsync(string? project, CancellationToken cancellation);
}

#endif
