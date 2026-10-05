using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Launcher.Gui;

internal sealed partial class MapStudioScreen
{
    private enum CommunityDashboardTab
    {
        Discover,
        MyMaps,
        Favorites
    }

    private void ShowCommunityDashboard()
    {
        var root = new Grid
        {
            MinWidth = 760,
            MinHeight = 500,
            MaxWidth = 980,
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto"),
            RowSpacing = 10
        };

        var heading = PrimeChrome.Stack(
            PrimeChrome.Eyebrow("MAP STUDIO // COMMUNITY"),
            PrimeChrome.Title("COMMUNITY MAPS"),
            PrimeChrome.Text(
                "Discover maps, manage your published work, and prepare exact immutable revisions for online play.",
                PrimeTypography.BodySmall, PrimeTheme.TextSecondaryBrush));

        var refresh = new PrimeButton("REFRESH", compact: true);
        ControllerNav.Identify(refresh, "studio.community.refresh");
        var header = PrimeChrome.Columns("*,Auto", heading, refresh);
        root.Children.Add(header);

        var address = new TextBox
        {
            Text = MapCommunityClient.DefaultAddress,
            PlaceholderText = "https://maps.example.com/",
            MinWidth = 420
        };
        try
        {
            if (File.Exists(CommunitySettingsPath))
                address.Text = File.ReadAllText(CommunitySettingsPath).Trim();
        }
        catch (IOException) { }

        var servicePanel = new Expander
        {
            Header = "COMMUNITY SERVICE",
            IsExpanded = false,
            Content = PrimeChrome.Stack(
                PrimeChrome.Text(
                    "Most players should leave this on the default service. Change it only for a trusted self-hosted Community library.",
                    PrimeTypography.BodySmall, PrimeTheme.TextSecondaryBrush),
                address)
        };
        Grid.SetRow(servicePanel, 1);
        root.Children.Add(servicePanel);

        var tabs = new UiTabs(new[] { "DISCOVER", "MY MAPS", "FAVORITES" });
        var search = new TextBox
        {
            PlaceholderText = "Search maps, authors, versions",
            MinWidth = 230
        };
        var sort = new ComboBox
        {
            ItemsSource = new[] { "Name", "Recently updated", "Favorites" },
            SelectedIndex = 1,
            MinWidth = 150
        };
        var lifecycleFilter = new ComboBox
        {
            ItemsSource = new[] { "All", "Active", "Archived", "Deleted" },
            SelectedIndex = 0,
            MinWidth = 120,
            IsVisible = false
        };
        var upload = new PrimeButton("UPLOAD CURRENT", compact: true)
        {
            IsVisible = false
        };
        ControllerNav.Identify(upload, "studio.community.upload");
        var tools = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto"),
            ColumnSpacing = 8
        };
        tools.Children.Add(tabs);
        Grid.SetColumn(search, 1);
        tools.Children.Add(search);
        Grid.SetColumn(sort, 2);
        tools.Children.Add(sort);
        Grid.SetColumn(lifecycleFilter, 3);
        tools.Children.Add(lifecycleFilter);
        Grid.SetColumn(upload, 4);
        tools.Children.Add(upload);
        Grid.SetRow(tools, 2);
        root.Children.Add(tools);

        var list = new UiList { SpacingEms = 0.24 };
        var directoryState = new PrimeStatePanel(
            PrimeStateKind.Loading,
            "LOADING COMMUNITY",
            "Contacting the Community map service.");
        var listStage = new Grid();
        listStage.Children.Add(list);
        listStage.Children.Add(directoryState);
        var listPanel = new PrimePanel(listStage, raised: true)
        {
            Padding = new Thickness(10)
        };

        var detail = new StackPanel { Spacing = 8 };
        var detailScroll = new ScrollViewer
        {
            Content = detail,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        var detailPanel = new PrimePanel(detailScroll, raised: true)
        {
            Padding = new Thickness(14)
        };

        var body = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("5*,4*"),
            ColumnSpacing = 10,
            MinHeight = 315
        };
        body.Children.Add(listPanel);
        Grid.SetColumn(detailPanel, 1);
        body.Children.Add(detailPanel);
        Grid.SetRow(body, 3);
        root.Children.Add(body);

        var status = PrimeChrome.Text(
            "Loading Community maps…",
            PrimeTypography.BodySmall,
            PrimeTheme.TextSecondaryBrush);
        var import = new PrimeButton("IMPORT .PPMAP", compact: true);
        var close = new PrimeButton("CLOSE", Dismiss, compact: true);
        var footer = PrimeChrome.Columns("*,Auto,Auto", status, import, close);
        Grid.SetRow(footer, 4);
        root.Children.Add(footer);

        CommunityMapProject[] projects = Array.Empty<CommunityMapProject>();
        CommunityMapProject? selectedProject = null;
        CommunityDashboardTab CurrentTab() => (CommunityDashboardTab)tabs.Index;

        string Endpoint()
        {
            string endpoint = (address.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(endpoint))
                endpoint = MapCommunityClient.DefaultAddress;
            Directory.CreateDirectory(LauncherPrefs.Directory);
            File.WriteAllText(CommunitySettingsPath, endpoint);
            return endpoint;
        }

        async Task<T> WithClient<T>(bool authenticated, CancellationToken token,
            Func<MapCommunityClient, Task<T>> action)
        {
            string endpoint = Endpoint();
            if (authenticated)
                return await WithCommunityAuthentication(endpoint, token, action);
            using var client = new MapCommunityClient(endpoint);
            return await action(client);
        }

        CommunityMap Presentation(CommunityMapProject project)
            => CurrentTab() == CommunityDashboardTab.MyMaps
                ? project.LatestRevision.Package
                : project.CurrentRevision?.Package ?? project.LatestRevision.Package;

        string Visibility(CommunityMap package)
            => package.Draft ? "DRAFT" : package.Listed ? "PUBLISHED" : "UNLISTED";

        string Lifecycle(CommunityMapProject project)
            => project.DeletedAt != null ? "DELETED"
                : project.ArchivedAt != null ? "ARCHIVED" : "ACTIVE";

        bool MatchesLifecycle(CommunityMapProject project)
        {
            if (CurrentTab() != CommunityDashboardTab.MyMaps) return true;
            return lifecycleFilter.SelectedIndex switch
            {
                1 => project.ArchivedAt == null && project.DeletedAt == null,
                2 => project.ArchivedAt != null && project.DeletedAt == null,
                3 => project.DeletedAt != null,
                _ => true
            };
        }

        string InstalledState(CommunityMap package)
        {
            if (!CustomRooms.Installed.TryGet(package.MapId, out var installed))
                return "Not installed";
            return installed.Identity.PackageHash.ToString() == package.Hash
                ? "Exact revision installed"
                : "Different revision installed";
        }

        string RevisionLabel(CommunityMapProject project)
        {
            if (project.CurrentRevision == null)
                return $"Revision {project.LatestRevision.RevisionNumber} · no public release";
            if (project.LatestRevision.RevisionNumber == project.CurrentRevision.RevisionNumber)
                return $"Revision {project.CurrentRevision.RevisionNumber} · current";
            return $"Revision {project.CurrentRevision.RevisionNumber} current · revision {project.LatestRevision.RevisionNumber} latest";
        }

        bool Matches(CommunityMapProject project)
        {
            string query = search.Text?.Trim() ?? "";
            if (query.Length == 0) return true;
            CommunityMap package = Presentation(project);
            return (project.Name + " " + project.DisplayName + " " + project.Author
                + " " + package.Version + " " + string.Join(" ", package.SupportedModes))
                .Contains(query, StringComparison.OrdinalIgnoreCase);
        }

        IEnumerable<CommunityMapProject> Sorted(IEnumerable<CommunityMapProject> source)
        {
            return sort.SelectedIndex switch
            {
                2 => source.OrderByDescending(p => Presentation(p).FavoriteCount)
                    .ThenBy(p => p.DisplayName ?? p.Name, StringComparer.OrdinalIgnoreCase),
                1 => source.OrderByDescending(p => p.UpdatedAt)
                    .ThenBy(p => p.DisplayName ?? p.Name, StringComparer.OrdinalIgnoreCase),
                _ => source.OrderBy(p => p.DisplayName ?? p.Name, StringComparer.OrdinalIgnoreCase)
            };
        }

        void ShowEmptyDetail(string title, string copy)
        {
            selectedProject = null;
            detail.Children.Clear();
            detail.Children.Add(PrimeChrome.Eyebrow("COMMUNITY"));
            detail.Children.Add(PrimeChrome.Title(title));
            detail.Children.Add(PrimeChrome.Text(
                copy, PrimeTypography.BodySmall, PrimeTheme.TextSecondaryBrush));
            if (CurrentTab() == CommunityDashboardTab.MyMaps)
                detail.Children.Add(new PrimeButton("UPLOAD CURRENT PROJECT",
                    () => ShowUploadForm(), primary: true));
        }

        async Task CopyLink(CommunityMap package)
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(
                    Endpoint().TrimEnd('/') + "/packages/" + package.Hash);
                status.Text = "Exact package link copied.";
            }
        }

        void Install(CommunityMap package, bool host)
        {
            _ = Job("Installing community map", async token =>
            {
                status.Text = "Downloading and validating " + (package.DisplayName ?? package.Name) + "…";
                if (!GameFiles.Ready)
                    throw new IOException("Set up game files before installing playable maps.");
                EnsureMapInstallationAllowed();
                GameFiles.ApplyPaths();
                MapDefinition installed = await WithClient(package.Draft, token,
                    client => client.InstallAsync(package, UserMapLibrary, token,
                        progress: (received, total) =>
                            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                                status.Text = $"Downloading… {received / 1048576d:0.0}/{total / 1048576d:0.0} MiB · {(total > 0 ? received * 100d / total : 0):0}%")));
                GuardJob(token);
                Metadata.RegisterDownloadedMap(installed);
                status.Text = "Installed " + (package.DisplayName ?? package.Name) + ".";
                if (host)
                {
                    Dismiss();
                    HostRequested?.Invoke(this, installed);
                }
                else if (selectedProject != null)
                {
                    RenderProject(selectedProject);
                }
            });
        }

        async Task ReloadProjects(CancellationToken token, Guid? preserve = null)
        {
            CommunityDashboardTab tab = CurrentTab();
            bool authenticated = tab != CommunityDashboardTab.Discover;
            projects = await WithClient(authenticated, token,
                client => client.BrowseProjectsAsync(token,
                    mine: tab == CommunityDashboardTab.MyMaps,
                    favorites: tab == CommunityDashboardTab.Favorites,
                    sort: "name"));
            GuardJob(token);
            RenderList(preserve);
        }

        void Refresh(Guid? preserve = null)
        {
            _ = Job("Loading community maps", async token =>
            {
                refresh.IsEnabled = false;
                tabs.IsEnabled = false;
                directoryState.Set(
                    PrimeStateKind.Loading,
                    "LOADING COMMUNITY",
                    "Contacting the Community map service.",
                    visible: true,
                    showActions: false);
                try
                {
                    await ReloadProjects(token, preserve);
                    status.Text = projects.Length == 1
                        ? "1 map available."
                        : $"{projects.Length} maps available.";
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    directoryState.Set(
                        PrimeStateKind.Error,
                        "COMMUNITY UNAVAILABLE",
                        ex.Message + " Check the Community service address or retry.",
                        visible: true,
                        showActions: false);
                    status.Text = "Community unavailable: " + ex.Message;
                    throw;
                }
                finally
                {
                    refresh.IsEnabled = true;
                    tabs.IsEnabled = true;
                }
            });
        }

        void RenderList(Guid? preserve = null)
        {
            Guid? wanted = preserve ?? selectedProject?.MapId;
            list.Clear();
            CommunityMapProject[] filtered = Sorted(
                projects.Where(Matches).Where(MatchesLifecycle)).ToArray();
            if (filtered.Length == 0)
            {
                string title = CurrentTab() switch
                {
                    CommunityDashboardTab.MyMaps => "NO MAPS PUBLISHED YET",
                    CommunityDashboardTab.Favorites => "NO FAVORITES YET",
                    _ => "NO COMMUNITY MAPS"
                };
                string copy = CurrentTab() switch
                {
                    CommunityDashboardTab.MyMaps =>
                        "Upload the project currently open in Map Studio to create your first Community map.",
                    CommunityDashboardTab.Favorites =>
                        "Favorite a map from Discover and it will appear here.",
                    _ => "No published map matches the current search."
                };
                directoryState.Set(PrimeStateKind.Empty, title, copy, visible: true,
                    showActions: false);
                ShowEmptyDetail(title, copy);
                return;
            }

            directoryState.IsVisible = false;
            var rows = new Dictionary<Guid, UiListRow>();
            foreach (CommunityMapProject project in filtered)
            {
                CommunityMap package = Presentation(project);
                string detailText = CurrentTab() == CommunityDashboardTab.MyMaps
                    ? $"{Lifecycle(project)} · {Visibility(package)} · {RevisionLabel(project)} · {project.UpdatedAt.LocalDateTime:g}"
                    : $"{project.Author ?? "Unknown author"} · {package.MinPlayers}–{package.MaxPlayers} players · ★ {package.FavoriteCount}";
                var row = new UiListRow(project.DisplayName ?? project.Name,
                    detailText, spacious: true)
                {
                    Choice = project.MapId
                };
                rows[project.MapId] = row;
                list.Add(row, _ => RenderProject(project));
            }

            if (wanted is Guid mapId
                && filtered.FirstOrDefault(p => p.MapId == mapId) is { } kept)
            {
                list.SelectTag(mapId);
                RenderProject(kept);
            }
        }

        void AddDivider()
        {
            detail.Children.Add(new Border
            {
                Height = 1,
                Margin = new Thickness(0, 4),
                Background = PrimeTheme.BorderBrush
            });
        }

        void RenderProject(CommunityMapProject project)
        {
            selectedProject = project;
            detail.Children.Clear();
            CommunityMap package = Presentation(project);
            CommunityMapRevision? current = project.CurrentRevision;

            detail.Children.Add(new PrimeBadge(
                CurrentTab() == CommunityDashboardTab.MyMaps
                    ? project.DeletedAt != null ? "DELETED"
                        : project.ArchivedAt != null ? "ARCHIVED"
                        : Visibility(package)
                    : "COMMUNITY MAP"));
            detail.Children.Add(PrimeChrome.Title(project.DisplayName ?? project.Name));
            detail.Children.Add(PrimeChrome.Text(
                project.Author ?? "Unknown author",
                PrimeTypography.BodySmall,
                PrimeTheme.TextSecondaryBrush));
            detail.Children.Add(PrimeChrome.Text(
                $"{package.MinPlayers}–{package.MaxPlayers} players"
                + (package.SupportedModes.Length == 0
                    ? ""
                    : " · " + string.Join(", ", package.SupportedModes)),
                PrimeTypography.DataSmall,
                PrimeTheme.HighlightBrush,
                data: true));

            AddDivider();
            detail.Children.Add(PrimeChrome.Eyebrow("RELEASE STATUS"));
            detail.Children.Add(PrimeChrome.Text(RevisionLabel(project),
                PrimeTypography.BodySmall));
            if (current != null)
            {
                detail.Children.Add(PrimeChrome.Text(
                    $"Current package · {(current.Package.Version ?? "1")} · {current.Package.Bytes / 1048576d:0.0} MiB",
                    PrimeTypography.BodySmall, PrimeTheme.TextSecondaryBrush));
            }
            if (project.LatestRevision.Hash != current?.Hash)
            {
                detail.Children.Add(PrimeChrome.Text(
                    $"Latest creator revision · {Visibility(project.LatestRevision.Package)} · {project.LatestRevision.Package.Bytes / 1048576d:0.0} MiB",
                    PrimeTypography.BodySmall, PrimeTheme.TextSecondaryBrush));
            }
            detail.Children.Add(PrimeChrome.Text(
                InstalledState(package),
                PrimeTypography.BodySmall,
                PrimeTheme.TextSecondaryBrush));
            if (project.DeletedAt is DateTimeOffset deletedAt)
            {
                detail.Children.Add(PrimeChrome.Text(
                    "Deleted " + deletedAt.LocalDateTime.ToString("g")
                    + (project.DeleteAfter is DateTimeOffset deleteAfter
                        ? " · permanent purge after " + deleteAfter.LocalDateTime.ToString("g")
                        : ""),
                    PrimeTypography.BodySmall, PrimeTheme.WarningBrush));
            }
            else if (project.ArchivedAt is DateTimeOffset archivedAt)
            {
                detail.Children.Add(PrimeChrome.Text(
                    "Archived " + archivedAt.LocalDateTime.ToString("g")
                    + " · hidden from discovery and new lobby selection",
                    PrimeTypography.BodySmall, PrimeTheme.TextSecondaryBrush));
            }

            AddDivider();
            var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Left };
            if (CurrentTab() != CommunityDashboardTab.MyMaps)
            {
                actions.Children.Add(new PrimeButton(
                    package.Favorited ? "UNFAVORITE" : "FAVORITE",
                    () => ToggleFavorite(project), compact: true));
                actions.Children.Add(new PrimeButton("INSTALL",
                    () => Install(package, false), primary: true, compact: true));
                actions.Children.Add(new PrimeButton("INSTALL & HOST",
                    () => Install(package, true), compact: true));
                actions.Children.Add(new PrimeButton("REPORT",
                    () => ShowReportForm(project), compact: true));
            }
            else
            {
                if (project.DeletedAt != null)
                {
                    if (project.CanManageLifecycle)
                        actions.Children.Add(new PrimeButton("RESTORE MAP",
                            () => RestoreProject(project), primary: true, compact: true));
                    actions.Children.Add(new PrimeButton("INSTALL LATEST",
                        () => Install(project.LatestRevision.Package, false), compact: true));
                }
                else
                {
                    actions.Children.Add(new PrimeButton("UPLOAD CURRENT",
                        ShowUploadForm, primary: true, compact: true));
                    actions.Children.Add(new PrimeButton(
                        project.CurrentHash == project.LatestHash ? "LATEST IS CURRENT" : "MAKE LATEST CURRENT",
                        () => PromoteRevision(project, project.LatestRevision),
                        compact: true)
                    {
                        IsEnabled = project.CurrentHash != project.LatestHash
                    });
                    actions.Children.Add(new PrimeButton("SET UNLISTED",
                        () => ChangeVisibility(project.LatestRevision.Package, "Unlisted"),
                        compact: true));
                    actions.Children.Add(new PrimeButton("SAVE AS DRAFT",
                        () => ChangeVisibility(project.LatestRevision.Package, "Draft"),
                        compact: true));
                    actions.Children.Add(new PrimeButton("INSTALL LATEST",
                        () => Install(project.LatestRevision.Package, false), compact: true));
                    if (project.CanManageLifecycle)
                    {
                        actions.Children.Add(project.ArchivedAt != null
                            ? new PrimeButton("RESTORE MAP",
                                () => RestoreProject(project), compact: true)
                            : new PrimeButton("ARCHIVE MAP",
                                () => ArchiveProject(project), compact: true));
                        actions.Children.Add(new PrimeButton("DELETE MAP",
                            () => ConfirmDeleteProject(project), danger: true, compact: true));
                    }
                }
            }
            actions.Children.Add(new PrimeButton("REVISIONS",
                () => ShowRevisionHistory(project), compact: true));
            actions.Children.Add(new PrimeButton("COPY LINK",
                () => _ = CopyLink(package), compact: true));
            detail.Children.Add(actions);
        }

        void ShowLifecycleConfirm(string eyebrow, string title,
            string copy, string actionLabel, Action action,
            CommunityMapProject project)
        {
            detail.Children.Clear();
            detail.Children.Add(PrimeChrome.Eyebrow(eyebrow, PrimeTheme.WarningBrush));
            detail.Children.Add(PrimeChrome.Title(title));
            detail.Children.Add(PrimeChrome.Text(
                copy, PrimeTypography.BodySmall, PrimeTheme.TextSecondaryBrush));
            detail.Children.Add(PrimeChrome.Columns("*,*",
                new PrimeButton("CANCEL", () => RenderProject(project), compact: true),
                new PrimeButton(actionLabel, action, danger: true, compact: true)));
        }

        void ArchiveProject(CommunityMapProject project)
        {
            _ = Job("Archiving Community map", async token =>
            {
                await WithClient(true, token,
                    client => client.ArchiveMapAsync(project.MapId, token));
                await ReloadProjects(token, project.MapId);
                status.Text = "Map archived. Exact package links remain valid.";
            });
        }

        void RestoreProject(CommunityMapProject project)
        {
            _ = Job("Restoring Community map", async token =>
            {
                await WithClient(true, token,
                    client => client.RestoreMapAsync(project.MapId, token));
                await ReloadProjects(token, project.MapId);
                status.Text = "Map restored to active Community management.";
            });
        }

        void ConfirmDeleteProject(CommunityMapProject project)
        {
            ShowLifecycleConfirm(
                "COMMUNITY // DELETE MAP",
                "MOVE MAP TO DELETED MAPS",
                "This hides the map immediately but keeps immutable package bytes available by exact hash for 30 days. You can restore it during that window.",
                "DELETE MAP",
                () => DeleteProject(project),
                project);
        }

        void DeleteProject(CommunityMapProject project)
        {
            _ = Job("Deleting Community map", async token =>
            {
                await WithClient(true, token,
                    client => client.DeleteMapAsync(project.MapId, token));
                lifecycleFilter.SelectedIndex = 3;
                await ReloadProjects(token, project.MapId);
                status.Text = "Map moved to Deleted Maps. Restore is available for 30 days.";
            });
        }

        void ConfirmDeleteRevision(CommunityMapProject project,
            CommunityMapRevision revision)
        {
            ShowLifecycleConfirm(
                "COMMUNITY // DELETE REVISION",
                $"DELETE REVISION {revision.RevisionNumber}",
                "The revision disappears from active history immediately, but its exact package remains available during the 30-day restore window.",
                "DELETE REVISION",
                () => DeleteRevision(project, revision),
                project);
        }

        void DeleteRevision(CommunityMapProject project,
            CommunityMapRevision revision)
        {
            _ = Job("Deleting Community revision", async token =>
            {
                await WithClient(true, token,
                    client => client.DeleteRevisionAsync(
                        project.MapId, revision.RevisionNumber, token));
                await ReloadProjects(token, project.MapId);
                CommunityMapProject? refreshed =
                    projects.FirstOrDefault(p => p.MapId == project.MapId);
                if (refreshed != null) ShowRevisionHistory(refreshed);
                status.Text = $"Revision {revision.RevisionNumber} moved to Deleted revisions.";
            });
        }

        void RestoreRevision(CommunityMapProject project,
            CommunityMapRevision revision)
        {
            _ = Job("Restoring Community revision", async token =>
            {
                await WithClient(true, token,
                    client => client.RestoreRevisionAsync(
                        project.MapId, revision.RevisionNumber, token));
                await ReloadProjects(token, project.MapId);
                CommunityMapProject? refreshed =
                    projects.FirstOrDefault(p => p.MapId == project.MapId);
                if (refreshed != null) ShowRevisionHistory(refreshed);
                status.Text = $"Revision {revision.RevisionNumber} restored.";
            });
        }

        void ToggleFavorite(CommunityMapProject project)
        {
            CommunityMap package = Presentation(project);
            _ = Job("Updating favorite", async token =>
            {
                await WithClient(true, token, async client =>
                {
                    await client.SetFavoriteAsync(package.MapId, !package.Favorited, token);
                    return true;
                });
                await ReloadProjects(token, project.MapId);
                status.Text = package.Favorited ? "Favorite removed." : "Map favorited.";
            });
        }

        void ChangeVisibility(CommunityMap package, string visibility)
        {
            _ = Job("Updating map visibility", async token =>
            {
                await WithClient(true, token, async client =>
                {
                    await client.SetVisibilityAsync(package.Hash, visibility, token);
                    return true;
                });
                await ReloadProjects(token, package.MapId);
                status.Text = visibility == "Unlisted"
                    ? "Revision is now unlisted."
                    : "Revision saved as a private draft.";
            });
        }

        void PromoteRevision(CommunityMapProject project,
            CommunityMapRevision revision)
        {
            _ = Job("Promoting map revision", async token =>
            {
                CommunityMapProject? promoted = await WithClient(true, token,
                    client => client.PromoteRevisionAsync(
                        project.MapId, revision.RevisionNumber, token));
                if (promoted == null)
                    throw new IOException("Community could not find that revision.");
                GuardJob(token);
                projects = await WithCommunityAuthentication(
                    Endpoint(), token,
                    client => client.BrowseProjectsAsync(token,
                        mine: true, sort: "name"));
                GuardJob(token);
                RenderList(project.MapId);
                status.Text = revision.RevisionNumber == project.LatestRevision.RevisionNumber
                    ? $"Revision {revision.RevisionNumber} is now the current release."
                    : $"Rolled back to revision {revision.RevisionNumber}. Immutable newer revisions were kept.";
            });
        }

        void ShowRevisionHistory(CommunityMapProject project)
        {
            _ = Job("Loading revision history", async token =>
            {
                bool authenticated = CurrentTab() == CommunityDashboardTab.MyMaps;
                CommunityMapRevision[] revisions = await WithClient(authenticated, token,
                    client => client.GetRevisionsAsync(project.MapId, token));
                GuardJob(token);

                detail.Children.Clear();
                detail.Children.Add(PrimeChrome.Eyebrow("COMMUNITY // REVISION HISTORY"));
                detail.Children.Add(PrimeChrome.Title(project.DisplayName ?? project.Name));
                detail.Children.Add(PrimeChrome.Text(
                    authenticated
                        ? "Immutable package history. Publishing a revision changes the map's current pointer without rewriting older packages."
                        : "Published immutable revisions available for this map.",
                    PrimeTypography.BodySmall, PrimeTheme.TextSecondaryBrush));
                detail.Children.Add(new PrimeButton("BACK TO MAP",
                    () => RenderProject(project), compact: true));

                if (revisions.Length == 0)
                {
                    detail.Children.Add(new PrimeStatePanel(
                        PrimeStateKind.Empty,
                        "NO REVISIONS",
                        "No revision is visible for this map."));
                    return;
                }

                foreach (CommunityMapRevision revision in revisions)
                {
                    CommunityMap package = revision.Package;
                    bool current = project.CurrentHash == revision.Hash;
                    var copy = PrimeChrome.Stack(
                        PrimeChrome.Eyebrow(
                            current
                                ? $"REVISION {revision.RevisionNumber} // CURRENT"
                                : $"REVISION {revision.RevisionNumber}"),
                        PrimeChrome.Text(
                            $"{Visibility(package)} · v{package.Version ?? "1"} · {package.Bytes / 1048576d:0.0} MiB",
                            PrimeTypography.BodySmall),
                        PrimeChrome.Text(
                            revision.CreatedAt.LocalDateTime.ToString("g")
                            + (revision.ParentHash == null ? " · first revision" : " · parent " + revision.ParentHash[..8]),
                            PrimeTypography.DataSmall,
                            PrimeTheme.TextSecondaryBrush,
                            data: true));
                    if (!string.IsNullOrWhiteSpace(revision.ReleaseNotes))
                    {
                        copy.Children.Add(PrimeChrome.Text(
                            revision.ReleaseNotes,
                            PrimeTypography.BodySmall,
                            PrimeTheme.TextSecondaryBrush));
                    }

                    var revisionActions = new WrapPanel();
                    revisionActions.Children.Add(new PrimeButton("INSTALL",
                        () => Install(package, false), compact: true));
                    revisionActions.Children.Add(new PrimeButton("COPY LINK",
                        () => _ = CopyLink(package), compact: true));
                    if (authenticated)
                    {
                        revisionActions.Children.Add(new PrimeButton(
                            current ? "CURRENT" : "MAKE CURRENT",
                            () => PromoteRevision(project, revision), compact: true)
                        {
                            IsEnabled = !current
                        });
                        revisionActions.Children.Add(new PrimeButton("UNLIST",
                            () => ChangeVisibility(package, "Unlisted"), compact: true));
                        revisionActions.Children.Add(new PrimeButton("DRAFT",
                            () => ChangeVisibility(package, "Draft"), compact: true));
                    }
                    copy.Children.Add(revisionActions);
                    detail.Children.Add(new PrimePanel(copy)
                    {
                        Padding = new Thickness(10)
                    });
                }
            });
        }

        void ShowReportForm(CommunityMapProject project)
        {
            CommunityMap package = Presentation(project);
            detail.Children.Clear();
            detail.Children.Add(PrimeChrome.Eyebrow("COMMUNITY // REPORT"));
            detail.Children.Add(PrimeChrome.Title(project.DisplayName ?? project.Name));
            detail.Children.Add(PrimeChrome.Text(
                "Reports are reviewed by Community moderators and never automatically remove a map.",
                PrimeTypography.BodySmall, PrimeTheme.TextSecondaryBrush));
            var reason = new ComboBox
            {
                ItemsSource = MapCreatorCatalog.ReportReasons,
                SelectedIndex = 0
            };
            var reportDetails = new TextBox
            {
                AcceptsReturn = true,
                Height = 110,
                MaxLength = 4000,
                PlaceholderText = "Describe the problem"
            };
            detail.Children.Add(reason);
            detail.Children.Add(reportDetails);
            detail.Children.Add(PrimeChrome.Columns("*,*",
                new PrimeButton("CANCEL", () => RenderProject(project), compact: true),
                new PrimeButton("SUBMIT REPORT", () =>
                {
                    _ = Job("Submitting report", async token =>
                    {
                        await WithClient(true, token, async client =>
                        {
                            await client.ReportAsync(project.MapId,
                                new MapReportRequest(package.Version,
                                    (string)reason.SelectedItem!,
                                    reportDetails.Text ?? ""),
                                token);
                            return true;
                        });
                        GuardJob(token);
                        status.Text = "Report submitted for moderation.";
                        RenderProject(project);
                    });
                }, primary: true, compact: true)));
        }

        void ShowPublishConflict(CommunityRevisionConflict conflict,
            string selectedVisibility, string releaseNotes,
            Guid mapId, bool existingMap, string? expectedParentHash,
            string? pendingPath = null, string? pendingHash = null)
        {
            detail.Children.Clear();
            detail.Children.Add(PrimeChrome.Eyebrow("COMMUNITY // REVISION CONFLICT",
                PrimeTheme.WarningBrush));
            detail.Children.Add(PrimeChrome.Title("A NEWER REVISION EXISTS"));
            detail.Children.Add(PrimeChrome.Text(
                conflict.Message,
                PrimeTypography.BodySmall, PrimeTheme.TextSecondaryBrush));
            if (conflict.LatestRevisionNumber is int latestRevision)
            {
                detail.Children.Add(PrimeChrome.Text(
                    $"Server latest · revision {latestRevision}"
                    + (conflict.LatestHash is { Length: >= 8 }
                        ? " · " + conflict.LatestHash[..8] : ""),
                    PrimeTypography.DataSmall,
                    PrimeTheme.HighlightBrush,
                    data: true));
            }
            if (expectedParentHash is { Length: >= 8 })
            {
                detail.Children.Add(PrimeChrome.Text(
                    "Your edit base · " + expectedParentHash[..8],
                    PrimeTypography.DataSmall,
                    PrimeTheme.TextSecondaryBrush,
                    data: true));
            }

            void FinishPending(Action after)
            {
                if (pendingPath == null || pendingHash == null)
                {
                    after();
                    return;
                }
                _ = Job("Discarding pending revision", async token =>
                {
                    await WithCommunityAuthentication(
                        Endpoint(), token, async client =>
                        {
                            await client.DiscardPendingUploadAsync(
                                pendingHash, token);
                            return true;
                        });
                    if (File.Exists(pendingPath)) File.Delete(pendingPath);
                    after();
                });
            }

            void ReviewLatest()
            {
                _ = Job("Refreshing revision history", async token =>
                {
                    if (pendingPath != null && pendingHash != null)
                    {
                        await WithCommunityAuthentication(
                            Endpoint(), token, async client =>
                            {
                                await client.DiscardPendingUploadAsync(
                                    pendingHash, token);
                                return true;
                            });
                        if (File.Exists(pendingPath)) File.Delete(pendingPath);
                    }
                    await ReloadProjects(token, mapId);
                    CommunityMapProject? refreshed =
                        projects.FirstOrDefault(p => p.MapId == mapId);
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        if (refreshed != null) ShowRevisionHistory(refreshed);
                        else ShowUploadForm();
                    });
                    status.Text = "Community history refreshed.";
                });
            }

            var conflictActions = new WrapPanel();
            conflictActions.Children.Add(new PrimeButton(
                "REVIEW LATEST", ReviewLatest, primary: true, compact: true));

            if (conflict.Code == "stale_parent")
            {
                conflictActions.Children.Add(new PrimeButton(
                    "PUBLISH ANYWAY",
                    () =>
                    {
                        if (pendingPath != null)
                            PublishPrepared(pendingPath, selectedVisibility,
                                releaseNotes, mapId, expectedParentHash,
                                allowStaleParent: true);
                        else
                            PublishCurrent(selectedVisibility, releaseNotes,
                                mapId, existingMap: true,
                                expectedParentHash: expectedParentHash,
                                allowStaleParent: true);
                    },
                    compact: true));
                conflictActions.Children.Add(new PrimeButton(
                    "SAVE BRANCH AS DRAFT",
                    () =>
                    {
                        if (pendingPath != null)
                            PublishPrepared(pendingPath, "Draft",
                                releaseNotes, mapId, expectedParentHash,
                                allowStaleParent: true);
                        else
                            PublishCurrent("Draft", releaseNotes,
                                mapId, existingMap: true, expectedParentHash,
                                allowStaleParent: true);
                    },
                    compact: true));
            }
            else if (conflict.Code == "map_already_exists"
                && conflict.LatestHash != null)
            {
                conflictActions.Children.Add(new PrimeButton(
                    "UPLOAD AS REVISION",
                    () =>
                    {
                        if (pendingPath != null)
                            PublishPrepared(pendingPath, selectedVisibility,
                                releaseNotes, mapId, conflict.LatestHash,
                                allowStaleParent: false);
                        else
                            PublishCurrent(selectedVisibility, releaseNotes,
                                mapId, existingMap: true,
                                expectedParentHash: conflict.LatestHash,
                                allowStaleParent: false);
                    },
                    compact: true));
            }

            conflictActions.Children.Add(new PrimeButton(
                "CANCEL", () => FinishPending(ShowUploadForm), compact: true));
            detail.Children.Add(conflictActions);
            status.Text = "Revision conflict needs your decision.";
        }

        void PublishPrepared(string path, string selectedVisibility,
            string releaseNotes, Guid mapId, string? expectedParentHash,
            bool allowStaleParent)
        {
            _ = Job("Completing map revision", async token =>
            {
                try
                {
                    bool draft = selectedVisibility == "Draft";
                    bool listed = selectedVisibility == "Published";
                    CommunityPublishResult result =
                        await WithCommunityAuthentication(
                            Endpoint(), token,
                            client => client.PublishAsync(
                                path,
                                new CommunityPublishRequest(
                                    ExistingMap: true,
                                    ExpectedParentHash: expectedParentHash,
                                    ReleaseNotes: releaseNotes,
                                    AllowStaleParent: allowStaleParent),
                                token,
                                listed: listed,
                                draft: draft,
                                progress: (sent, total) =>
                                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                                        status.Text =
                                            $"Completing stored upload… {sent / 1048576d:0.0}/{total / 1048576d:0.0} MiB")));
                    GuardJob(token);
                    projects = await WithCommunityAuthentication(
                        Endpoint(), token,
                        client => client.BrowseProjectsAsync(
                            token, mine: true, sort: "name"));
                    GuardJob(token);
                    RenderList(result.Package.MapId);
                    string number = result.Revision is { } revision
                        ? $"Revision {revision.RevisionNumber}"
                        : "Revision";
                    status.Text = selectedVisibility switch
                    {
                        "Draft" => number + " saved as a private draft from the stored upload.",
                        "Unlisted" => number + " saved unlisted from the stored upload.",
                        _ => number + " published from the stored upload."
                    };
                }
                finally
                {
                    if (File.Exists(path)) File.Delete(path);
                }
            });
        }

        void PublishCurrent(string selectedVisibility, string releaseNotes,
            Guid mapId, bool existingMap, string? expectedParentHash,
            bool allowStaleParent = false)
        {
            _ = Work("Publishing map", async (project, token) =>
            {
                if (project.Definition.MapId != mapId)
                    throw new InvalidOperationException(
                        "The open Map Studio project changed before publishing. Reopen the publish form.");

                string temporary = Path.Combine(Path.GetTempPath(),
                    Guid.NewGuid().ToString("N") + ".ppmap");
                bool keepTemporary = false;
                try
                {
                    status.Text = existingMap
                        ? "Building immutable map revision…"
                        : "Building new Community map…";
                    await MapBuildScheduler.Shared.PackageAsync(
                        MapBuildSnapshot.Capture(project), temporary, token);
                    GuardJob(token);

                    bool draft = selectedVisibility == "Draft";
                    bool listed = selectedVisibility == "Published";
                    string packageHash = MapBuildFingerprint.HashFile(temporary);
                    CommunityPublishResult result;
                    try
                    {
                        result = await WithCommunityAuthentication(
                            Endpoint(), token,
                            client => client.PublishAsync(
                                temporary,
                                new CommunityPublishRequest(
                                    existingMap,
                                    expectedParentHash,
                                    releaseNotes,
                                    allowStaleParent),
                                token,
                                listed: listed,
                                draft: draft,
                                progress: (sent, total) =>
                                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                                        status.Text =
                                            $"Uploading… {sent / 1048576d:0.0}/{total / 1048576d:0.0} MiB · {(total > 0 ? sent * 100d / total : 0):0}%")));
                    }
                    catch (CommunityRevisionConflictException ex)
                    {
                        GuardJob(token);
                        keepTemporary = ex.Conflict.ResumeAvailable;
                        ShowPublishConflict(ex.Conflict,
                            selectedVisibility, releaseNotes,
                            mapId, existingMap, expectedParentHash,
                            keepTemporary ? temporary : null,
                            keepTemporary ? packageHash : null);
                        return;
                    }

                    GuardJob(token);
                    projects = await WithCommunityAuthentication(
                        Endpoint(), token,
                        client => client.BrowseProjectsAsync(
                            token, mine: true, sort: "name"));
                    GuardJob(token);
                    upload.IsVisible = true;
                    RenderList(result.Package.MapId);

                    string revisionText = result.Revision == null
                        ? ""
                        : $" Revision {result.Revision.RevisionNumber}.";
                    status.Text = selectedVisibility == "Published"
                        ? (existingMap ? "Revision published as the current release."
                            : "Map published to Community.") + revisionText
                        : selectedVisibility == "Unlisted"
                            ? "Uploaded as an unlisted revision." + revisionText
                            : "Uploaded as a private draft." + revisionText;
                }
                finally
                {
                    if (!keepTemporary && File.Exists(temporary))
                        File.Delete(temporary);
                }
            });
        }

        void ShowUploadForm()
        {
            detail.Children.Clear();
            Guid mapId = _document?.Project.Definition.MapId ?? Guid.Empty;
            if (mapId == Guid.Empty)
            {
                detail.Children.Add(new PrimeStatePanel(
                    PrimeStateKind.Error,
                    "PROJECT NEEDS AN IDENTITY",
                    "Save or upgrade this Map Studio project before publishing it."));
                detail.Children.Add(new PrimeButton(
                    "BACK", () =>
                    {
                        if (selectedProject != null) RenderProject(selectedProject);
                        else ShowEmptyDetail("MY MAPS",
                            "Upload the project currently open in Map Studio to create your first Community map.");
                    }, compact: true));
                return;
            }

            CommunityMapProject? target =
                projects.FirstOrDefault(p => p.MapId == mapId);
            bool existingMap = target != null;
            string? expectedParentHash = target?.LatestHash;
            int nextRevision = (target?.LatestRevision.RevisionNumber ?? 0) + 1;

            detail.Children.Add(PrimeChrome.Eyebrow(
                existingMap
                    ? "COMMUNITY // REVISION PUBLISHING"
                    : "COMMUNITY // NEW MAP"));
            detail.Children.Add(PrimeChrome.Title(
                existingMap
                    ? $"UPLOAD REVISION {nextRevision}"
                    : "PUBLISH NEW MAP"));
            detail.Children.Add(PrimeChrome.Text(
                existingMap
                    ? $"Detected published MapId. This revision will be based on revision {target!.LatestRevision.RevisionNumber}; the server verifies that parent again when publication completes."
                    : "This MapId is not in My Maps, so Community will create a new map project if it is still available when the upload begins.",
                PrimeTypography.BodySmall, PrimeTheme.TextSecondaryBrush));
            detail.Children.Add(PrimeChrome.Text(
                "The human-facing Version field no longer has to change for each Community revision. Immutable server revision numbers track history independently.",
                PrimeTypography.BodySmall, PrimeTheme.TextSecondaryBrush));

            var releaseNotes = new TextBox
            {
                AcceptsReturn = true,
                Height = 100,
                MaxLength = 4000,
                PlaceholderText = existingMap
                    ? "What changed in this revision?"
                    : "Initial release notes (optional)"
            };
            var visibility = new ComboBox
            {
                ItemsSource = new[] { "Published", "Unlisted", "Draft" },
                SelectedIndex = 0
            };
            detail.Children.Add(PrimeChrome.Eyebrow("RELEASE NOTES"));
            detail.Children.Add(releaseNotes);
            detail.Children.Add(PrimeChrome.Eyebrow("VISIBILITY"));
            detail.Children.Add(visibility);

            detail.Children.Add(PrimeChrome.Columns("*,*",
                new PrimeButton("CANCEL", () =>
                {
                    if (selectedProject != null) RenderProject(selectedProject);
                    else ShowEmptyDetail("MY MAPS",
                        "Upload the project currently open in Map Studio to create your first Community map.");
                }, compact: true),
                new PrimeButton(
                    existingMap ? "BUILD & UPLOAD REVISION" : "BUILD & PUBLISH",
                    () => PublishCurrent(
                        (string?)visibility.SelectedItem ?? "Published",
                        releaseNotes.Text ?? "",
                        mapId,
                        existingMap,
                        expectedParentHash),
                    primary: true, compact: true)));
        }

        refresh.Click += (_, _) => Refresh(selectedProject?.MapId);
        tabs.Changed += (_, _) =>
        {
            bool creatorTab = CurrentTab() == CommunityDashboardTab.MyMaps;
            upload.IsVisible = creatorTab;
            lifecycleFilter.IsVisible = creatorTab;
            selectedProject = null;
            Refresh();
        };
        search.TextChanged += (_, _) => RenderList(selectedProject?.MapId);
        sort.SelectionChanged += (_, _) => RenderList(selectedProject?.MapId);
        lifecycleFilter.SelectionChanged += (_, _) =>
            RenderList(selectedProject?.MapId);
        upload.Click += (_, _) => ShowUploadForm();

        import.Click += (_, _) => Browse("Install map package", false,
            path => _ = Job("Installing map package", async token =>
            {
                if (!GameFiles.Ready)
                    throw new IOException("Set up game files before installing playable maps.");
                EnsureMapInstallationAllowed();
                GameFiles.ApplyPaths();
                using var package = new MapPackageReader(path);
                var manifest = package.Manifest
                    ?? throw new IOException("Rebuild this legacy package in Map Studio before sharing.");
                var installed = await Task.Run(() => MapPackageInstaller.Install(
                    path, manifest.MapId, manifest.ContentHash,
                    MapBuildFingerprint.HashFile(path), UserMapLibrary), token);
                GuardJob(token);
                Metadata.RegisterDownloadedMap(installed);
                status.Text = "Installed " + installed.Name + ".";
                if (selectedProject != null) RenderProject(selectedProject);
            }), ".ppmap");

        ShowEmptyDetail("LOADING COMMUNITY",
            "Select a map to view installation, publishing, and revision controls.");
        Modal(root);
        Refresh();
    }
}
