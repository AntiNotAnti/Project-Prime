#if MPHREAD_AVALONIA
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using MphRead.Mods.MapGen;
using MphRead.Mods.Network;

namespace MphRead.Mods.Launcher.Gui;

internal sealed partial class MapCardPicker
{
    private readonly bool _includeCommunity;
    private readonly Func<string, string?>? _incompatibility;
    private readonly Func<CancellationToken, Task<CommunityMap[]>> _browseCommunity;
    private readonly Func<CommunityMap, CancellationToken, Task<string>> _installCommunity;
    private readonly CancellationTokenSource _communityLifetime = new();
    private readonly Dictionary<DeckTile, CommunityMap> _communityCards = new();
    private readonly Note _communityStatus = new("");
    private CommunityMap? _selectedCommunity;
    private bool _communityStarted, _communityLoading, _communityFailed, _installing;
    private string _communityError = "";

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _communityLifetime.Cancel();
        base.OnDetachedFromVisualTree(e);
    }

    private void FilterCards()
    {
        string query = _search.Text?.Trim() ?? "";
        foreach (var tile in _grid.Children.OfType<DeckTile>())
            tile.IsVisible = tile.RoomKey.Contains(query, StringComparison.OrdinalIgnoreCase)
                || tile.Blurb.Contains(query, StringComparison.OrdinalIgnoreCase)
                || (_communityCards.TryGetValue(tile, out var map)
                    && (map.Author?.Contains(query, StringComparison.OrdinalIgnoreCase) == true
                        || "community".Contains(query, StringComparison.OrdinalIgnoreCase)));
        RefreshDirectoryState();
    }

    private static async Task<CommunityMap[]> BrowseCommunityAsync(CancellationToken token)
    {
        using var client = new MapCommunityClient(NetworkMapIdentity.ConfiguredDownloadSource());
        return await client.BrowseAsync(token);
    }

    private async Task LoadCommunityAsync()
    {
        if (_communityLoading || _installing || _communityLifetime.IsCancellationRequested) return;
        _communityLoading = true;
        _communityFailed = false;
        _communityError = "";
        _communityStatus.Text = "Loading Community maps…";
        RefreshDirectoryState();
        try
        {
            CommunityMap[] maps = await _browseCommunity(_communityLifetime.Token);
            if (_communityLifetime.IsCancellationRequested) return;
            // Retain the selected immutable version during a refresh. A newly
            // published version must not silently replace the player's choice.
            foreach (var tile in _communityCards.Keys.ToArray())
                if (!_communityCards[tile].Equals(_selectedCommunity))
                { _grid.Children.Remove(tile); _communityCards.Remove(tile); }
            foreach (CommunityMap map in maps.Where(m => m.Listed && !m.Draft)
                .GroupBy(m => m.MapId).Select(g => g.OrderByDescending(m => m.PublishedAt).First())
                .OrderBy(m => m.DisplayName ?? m.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (Metadata.IsBuiltInRoom(map.Name) || _communityCards.Values.Any(m => m.MapId == map.MapId)) continue;
                // An exact installed version is already represented by its local
                // card. Different published versions remain explicit choices.
                if (CustomRooms.Installed.TryGet(map.MapId, out var installed)
                    && installed.Identity.PackageHash.ToString() == map.Hash
                    && _grid.Children.OfType<DeckTile>().Any(t => t.RoomKey == map.Name)) continue;
                var card = new DeckTile(map.Name, "COMMUNITY")
                {
                    Blurb = map.DisplayName ?? map.Name, Tactical = true,
                    Verb = "SELECT", ChosenVerb = "SELECTED"
                };
                card.Click += (_, _) => Select(card);
                _communityCards.Add(card, map);
                _grid.Children.Add(card);
            }
            _communityStatus.Text = maps.Length == 0 ? "No published Community maps. Local maps are still available."
                : "Community maps included · select one, then Use Map to download.";
            _communityStatus.Foreground = GuiTheme.TextDimBrush;
            _communityFailed = false;
            FilterCards();
        }
        catch (OperationCanceledException) when (_communityLifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (_communityLifetime.IsCancellationRequested) return;
            _communityError = ex.Message;
            _communityFailed = true;
            _communityStatus.Text = "Community maps unavailable: " + ex.Message + " · Refresh to retry.";
            _communityStatus.Foreground = GuiTheme.WarmBrush;
            if (!_grid.Children.OfType<DeckTile>().Any(tile => tile.IsVisible))
            {
                _directoryState.Set(
                    PrimeStateKind.Error,
                    "COMMUNITY MAPS UNAVAILABLE",
                    ex.Message + " Local arenas remain usable if installed; Refresh Community retries the catalog.",
                    showActions: false);
            }
        }
        finally
        {
            _communityLoading = false;
            if (!_communityFailed) RefreshDirectoryState();
        }
    }

    private async Task ConfirmSelectionAsync()
    {
        if (_installing || _communityLifetime.IsCancellationRequested || String.IsNullOrWhiteSpace(_selected)) return;
        if (_selectedCommunity is not { } map)
        {
            if (Incompatibility(_selected) == null) Done?.Invoke(this, _selected);
            return;
        }
        _installing = true;
        _use.IsEnabled = false;
        _note.Text = "Downloading and preparing " + (map.DisplayName ?? map.Name) + "…";
        try
        {
            string room = await _installCommunity(map, _communityLifetime.Token);
            if (_communityLifetime.IsCancellationRequested) return;
            string? reason = _incompatibility?.Invoke(room);
            _incompatibilities[room] = reason;
            if (reason != null)
            {
                _note.Text = reason;
                _note.Foreground = GuiTheme.WarmBrush;
                return;
            }
            Done?.Invoke(this, room);
        }
        catch (OperationCanceledException) when (_communityLifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (_communityLifetime.IsCancellationRequested) return;
            _note.Text = "Could not prepare map: " + ex.Message + " · Use Map to retry.";
            _note.Foreground = GuiTheme.WarmBrush;
        }
        finally
        {
            _installing = false;
            if (!_communityLifetime.IsCancellationRequested) _use.IsEnabled = true;
        }
    }

    private static async Task<string> InstallCommunityAsync(CommunityMap map, CancellationToken token)
    {
        if (map.MinimumProtocol > NetConfig.ProtocolVersion)
            throw new InvalidDataException("Update Project Prime before installing this map.");
        var identity = new MapContentIdentity(map.MapId, map.Name,
            MapHash256.Parse(map.ContentHash), MapHash256.Parse(map.Hash), true);
        if (CustomRooms.Installed.HasExact(identity)) return identity.RoomKey;
        if (!GameFiles.Ready) throw new IOException("Set up game files before installing Community maps.");
        MapRuntimeUsage.RequireInstallationAllowed(identity.RoomKey);
        GameFiles.ApplyPaths();
        using var client = new MapCommunityClient(NetworkMapIdentity.ConfiguredDownloadSource());
        using var prepared = await client.PrepareExactAsync(identity, token);
        token.ThrowIfCancellationRequested();
        MapDefinition installed = prepared.Commit(CustomRooms.UserMapDirectory, cancellation: token);
        Metadata.RegisterDownloadedMap(installed);
        return installed.Name;
    }
}
#endif
