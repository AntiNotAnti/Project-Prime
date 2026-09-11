using System;
using System.Collections.Immutable;
using System.Linq;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Launcher.Gui;

/// <summary>
/// The three player-facing surfaces of the Maps route. Authoring controls are
/// deliberately kept out of <see cref="MapsTab.Library"/> and the future
/// community surface has no local-map fallback.
/// </summary>
public enum MapsTab
{
    Library,
    MyMaps,
    Community
}

/// <summary>
/// Immutable Maps route state. The catalog remains the authoritative map
/// source; this record only adds the selected surface and bounded UI state.
/// </summary>
public sealed record MapsState(
    MapsTab Tab,
    MapCatalogSnapshot Catalog,
    bool Loading,
    string? Status,
    string? Error,
    MapContentIdentity? SelectedIdentity)
{
    public static MapsState Initial { get; } = new(
        MapsTab.Library, MapCatalogSnapshot.Empty, false, null, null, null);

    public ImmutableArray<InstalledMap> VisibleMaps => MapsFor(Tab);

    public ImmutableArray<InstalledMap> MapsFor(MapsTab tab)
        => Catalog.Maps.Where(map => tab switch
        {
            MapsTab.Library => map.Source is MapInstallSource.InstalledPackage
                or MapInstallSource.BundledPackage or MapInstallSource.LegacyPackage,
            MapsTab.MyMaps => map.Source is MapInstallSource.LocalProject
                or MapInstallSource.LegacyRecipe,
            MapsTab.Community => false,
            _ => false
        }).ToImmutableArray();

    public bool IsSelected(InstalledMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return SelectedIdentity != null && SelectedIdentity == map.ContentIdentity;
    }

    public static string TabLabel(MapsTab tab) => tab switch
    {
        MapsTab.Library => "Library",
        MapsTab.MyMaps => "My Maps",
        MapsTab.Community => "Community",
        _ => tab.ToString()
    };
}
