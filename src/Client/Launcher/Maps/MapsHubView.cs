using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MphRead.Mods.MapGen;
using AvaloniaButton = Avalonia.Controls.Button;

namespace MphRead.Mods.Launcher.Gui;

/// <summary>First-class installed/project map management backed by MapCatalog.</summary>
internal sealed class MapsHubView : UserControl, IDisposable
{
    private readonly MapCatalog? _catalog;
    private readonly StackPanel _maps = new() { Spacing = 14 };
    private readonly TextBlock _status = new() { Classes = { "prime-muted" } };
    private readonly CancellationTokenSource _lifetime = new();
    private readonly List<Bitmap> _previews = new();
    private long _operationGeneration;
    private bool _disposed;

    public MapsHubView(bool captureMode = false)
    {
        var root = new StackPanel { Spacing = 18, MaxWidth = 1180,
            HorizontalAlignment = HorizontalAlignment.Stretch };
        root.Children.Add(new TextBlock { Text = "Maps", Classes = { "prime-hero" } });
        root.Children.Add(new TextBlock
        {
            Text = "Install, build, verify, edit, and play exact Project Prime map packages.",
            Classes = { "prime-body" }
        });
        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        if (!OperatingSystem.IsAndroid())
        {
            actions.Children.Add(Button("Create Map", CreateMap));
            actions.Children.Add(Button("Import Q3", ImportQ3));
        }
        actions.Children.Add(Button("Install Map", InstallMap));
        actions.Children.Add(Button("Refresh", Refresh));
        root.Children.Add(actions);
        root.Children.Add(_status);
        root.Children.Add(_maps);
        Content = root;

        if (captureMode)
        {
            _status.Text = "Installed maps appear here. Community publishing is coming later.";
            _maps.Children.Add(Section("Installed", "No map packages installed."));
            _maps.Children.Add(Section("My Maps", "Create a native map or import a Quake 3 BSP/PK3."));
            _maps.Children.Add(Section("Community", "Coming soon"));
            return;
        }
        _catalog = new MapCatalog(new MapCatalogOptions
        {
            InstalledDirectory = MapStoragePaths.InstalledMaps,
            ProjectDirectories = [MapStoragePaths.Projects, CustomRooms.MapDirectory],
            CacheDirectory = MapStoragePaths.MapCache
        });
        _catalog.Changed += CatalogChanged;
        DetachedFromVisualTree += (_, _) => Dispose();
        _ = RefreshAsync();
    }

    private static Border Section(string title, string detail)
    {
        var body = new StackPanel { Spacing = 6 };
        body.Children.Add(new TextBlock { Text = title, Classes = { "prime-title" } });
        body.Children.Add(new TextBlock { Text = detail, Classes = { "prime-muted" } });
        return new Border { Classes = { "prime-card" }, Child = body, Padding = new Avalonia.Thickness(18) };
    }

    private void CatalogChanged(MapCatalogSnapshot snapshot)
        => Dispatcher.UIThread.Post(() => Render(snapshot));

    private async void Refresh(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
        => await RefreshAsync().ConfigureAwait(false);

    private async Task RefreshAsync()
    {
        if (_catalog == null || _disposed) return;
        try
        {
            await _catalog.RefreshAsync(_lifetime.Token).ConfigureAwait(false);
            PostStatus($"{_catalog.Snapshot.Maps.Length} maps cataloged");
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception exception)
        {
            PostStatus("Map catalog refresh failed: " + exception.Message);
        }
    }

    private void Render(MapCatalogSnapshot snapshot)
    {
        if (_disposed) return;
        DisposePreviews();
        _maps.Children.Clear();
        AddSection("Installed", snapshot.Maps.Where(map => map.Source is
            MapInstallSource.InstalledPackage or MapInstallSource.BundledPackage
                or MapInstallSource.LegacyPackage));
        AddSection("My Maps", snapshot.Maps.Where(map => map.Source is
            MapInstallSource.LocalProject or MapInstallSource.LegacyRecipe));
        _maps.Children.Add(Section("Community", "Community catalog services are not enabled yet."));
    }

    private void AddSection(string title, IEnumerable<InstalledMap> maps)
    {
        InstalledMap[] values = maps.ToArray();
        var section = new StackPanel { Spacing = 10 };
        section.Children.Add(new TextBlock { Text = title, Classes = { "prime-title" } });
        if (values.Length == 0)
            section.Children.Add(new TextBlock { Text = "None", Classes = { "prime-muted" } });
        foreach (InstalledMap map in values) section.Children.Add(Card(map));
        _maps.Children.Add(section);
    }

    private Control Card(InstalledMap map)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("112,*,Auto"),
            ColumnSpacing = 16 };
        Control preview = Preview(map) ?? new Border { Width = 112, Height = 68,
            Classes = { "prime-surface" } };
        grid.Children.Add(preview);
        var details = new StackPanel { Spacing = 3 };
        details.Children.Add(new TextBlock { Text = map.DisplayName, Classes = { "prime-title" } });
        details.Children.Add(new TextBlock
        {
            Text = $"{map.Author} · {map.ContentIdentity.Identity.Version} · "
                + string.Join(", ", map.SupportedModes), Classes = { "prime-body" }
        });
        details.Children.Add(new TextBlock
        {
            Text = $"{map.BuildState} · {map.ContentIdentity.Identity.StableId} · "
                + map.ContentIdentity.ContentHash[..12], Classes = { "prime-muted" }
        });
        grid.Children.Add(details);
        Grid.SetColumn(details, 1);
        var actions = new StackPanel { Spacing = 6, Width = 120 };
        if (!OperatingSystem.IsAndroid())
            actions.Children.Add(Button("Play", (_, _) => _ = PlayAsync(map)));
        actions.Children.Add(Button("Build", (_, _) =>
            _ = BuildAsync(map, _lifetime.Token)));
        if (!OperatingSystem.IsAndroid() && map.Source == MapInstallSource.LocalProject)
        {
            actions.Children.Add(Button("Edit", (_, _) => OpenEditor(map.SourcePath)));
            if (map.Project.Authoring != null)
                actions.Children.Add(Button("Add Texture", (_, _) => _ = AddTextureAsync(map)));
            actions.Children.Add(Button("Export", (_, _) => _ = ExportAsync(map)));
        }
        if (map.Source == MapInstallSource.InstalledPackage)
            actions.Children.Add(Button("Remove", (_, _) => _ = RemoveAsync(map)));
        actions.Children.Add(Button("Details", (_, _) => ShowDetails(map)));
        grid.Children.Add(actions);
        Grid.SetColumn(actions, 2);
        return new Border { Classes = { "prime-card" }, Padding = new Avalonia.Thickness(16), Child = grid };
    }

    private Control? Preview(InstalledMap map)
    {
        if (map.PreviewPath == null) return null;
        try
        {
            byte[] bytes;
            int separator = map.PreviewPath.IndexOf("::", StringComparison.Ordinal);
            if (separator >= 0)
                bytes = new MapBundleReader().Read(map.PreviewPath[..separator])
                    .ReadDeclaredFile(map.PreviewPath[(separator + 2)..]);
            else bytes = File.ReadAllBytes(map.PreviewPath);
            var bitmap = new Bitmap(new MemoryStream(bytes));
            _previews.Add(bitmap);
            return new Image { Source = bitmap, Width = 112, Height = 68,
                Stretch = Avalonia.Media.Stretch.UniformToFill };
        }
        catch { return null; }
    }

    private async void InstallMap(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        if (_catalog == null || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        string? scratch = null;
        try
        {
            IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = "Install Project Prime Map",
                    AllowMultiple = false,
                    FileTypeFilter = OperatingSystem.IsAndroid() ? null :
                    [new FilePickerFileType("Project Prime map")
                        { Patterns = ["*.fpmap"] }]
                });
            if (_disposed || files.Count == 0) return;
            string? path = files[0].TryGetLocalPath();
            if (path == null)
            {
                scratch = Path.Combine(Path.GetTempPath(),
                    $"prime-map-import-{Guid.NewGuid():N}{MapBundle.Extension}");
                await using (Stream source = await files[0].OpenReadAsync())
                await using (Stream destination = File.Create(scratch))
                {
                    await source.CopyToAsync(destination, _lifetime.Token);
                }
                path = scratch;
            }
            _status.Text = "Validating and installing map…";
            InstalledMap map = await _catalog.InstallAsync(path, _lifetime.Token);
            if (!_disposed)
                _status.Text = $"Installed {map.DisplayName} {map.ContentIdentity.Identity.Version}";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (!_disposed) _status.Text = "Install rejected: " + exception.Message;
        }
        finally
        {
            if (scratch != null)
            {
                try { File.Delete(scratch); }
                catch (IOException) { }
            }
        }
    }

    private void CreateMap(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        try
        {
            string suffix = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
            string id = "community.map-" + suffix.ToLowerInvariant();
            string directory = Path.Combine(MapStoragePaths.Projects, id);
            string projectPath = Path.Combine(directory, "map.project.json");
            Directory.CreateDirectory(directory);
            MapProject project = NewProject(id, "New Map");
            MapProjectIO.Save(project, projectPath);
            LaunchEditor(projectPath);
            _ = RefreshAsync();
        }
        catch (Exception exception)
        {
            _status.Text = "Map creation or editor launch failed: " + exception.Message;
        }
    }

    private async void ImportQ3(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        try
        {
            IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = "Import Quake 3 BSP or PK3",
                    AllowMultiple = false,
                    FileTypeFilter = [new FilePickerFileType("Quake 3 map")
                        { Patterns = ["*.bsp", "*.pk3"] }]
                });
            if (_disposed || files.Count == 0) return;
            string source = files[0].TryGetLocalPath()
                ?? throw new InvalidOperationException("The selected map must be available as a local file.");
            string name = Path.GetFileNameWithoutExtension(source);
            string canonicalName = MapIdentity.FromLegacyName(name)["legacy.".Length..];
            string stableId = "community." + canonicalName;
            string directory = Path.Combine(MapStoragePaths.Projects, stableId);
            if (Directory.Exists(directory))
            {
                stableId = (stableId.Length > 48 ? stableId[..48].TrimEnd('.', '-', '_') : stableId)
                    + "-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                directory = Path.Combine(MapStoragePaths.Projects, stableId);
            }
            string projectPath = Path.Combine(directory, "map.project.json");
            Directory.CreateDirectory(directory);
            MapProject project = Q3MapProjectFactory.Create(source, projectPath, stableId, name);
            MapProjectIO.Save(project, projectPath);
            LaunchEditor(projectPath);
            _ = RefreshAsync();
        }
        catch (Exception exception)
        {
            if (!_disposed) _status.Text = "Q3 import failed: " + exception.Message;
        }
    }

    private static MapProject NewProject(string id, string name)
    {
        var project = new MapProject
        {
            StableId = id,
            Metadata = new MapProjectMetadata { Name = name, Author = Environment.UserName },
            Environment = new MapEnvironment { KillHeight = -10 },
            Map = new MapDefinition { Name = name.ToUpperInvariant(), InGameName = name,
                TextureSource = "MP3 PROVING GROUND", KillHeight = -10 },
            Authoring = new MapAuthoringScene()
        };
        project.Authoring.Materials.Add(new MapAuthoringMaterial { Id = "material.default",
            Name = "Default", SourceRoom = project.Map.TextureSource, SourceMaterial = 1 });
        project.Authoring.Brushes.Add(ConvexBrushFactory.Box("brush.floor", "material.default",
            new OpenTK.Mathematics.Vector3(16, 1, 16)));
        project.Authoring.Brushes[0].Transform.Position = [0, -0.5f, 0];
        for (int index = 0; index < 4; index++)
            project.Authoring.Entities.Add(new MapEntityDefinition { Id = $"spawn.{index + 1}",
                Kind = MapEntityKind.PlayerSpawn,
                Transform = new MapTransform { Position = [index % 2 == 0 ? -3 : 3, 0.1f,
                    index < 2 ? -3 : 3] } });
        return project;
    }

    private async Task<MapBuildResult> BuildAsync(InstalledMap map,
        CancellationToken cancellationToken = default)
    {
        if (_catalog == null) throw new InvalidOperationException();
        try
        {
            _catalog.PublishBuildState(map.ContentIdentity, MapBuildState.Building, null, []);
            MapBuildResult result = await new MapCompiler().CompileAsync(map.Project,
                new MapBuildOptions { CacheDirectory = MapStoragePaths.MapCache,
                    BaseContentIdentity = ContentEnvironment.GetContentIdentity().ContentHash },
                cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            _catalog.PublishBuildState(map.ContentIdentity,
                result.Success ? MapBuildState.Ready : MapBuildState.Invalid,
                result.Statistics, result.Diagnostics);
            PostStatus(result.Success
                ? $"{map.DisplayName} is ready" : $"{map.DisplayName} failed validation");
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new MapBuildResult(false, false, "", null, null, [], null, []);
        }
        catch (Exception exception)
        {
            if (_disposed || cancellationToken.IsCancellationRequested)
                return new MapBuildResult(false, false, "", null, null, [], null, []);
            _catalog.PublishBuildState(map.ContentIdentity, MapBuildState.Invalid, null,
                [new("MAP-CMP-999", MapDiagnosticSeverity.Error, exception.Message)]);
            PostStatus("Build failed: " + exception.Message);
            return new MapBuildResult(false, false, "", null, null,
                [new("MAP-CMP-999", MapDiagnosticSeverity.Error, exception.Message)], null, []);
        }
    }

    private async Task PlayAsync(InstalledMap map)
    {
        long generation = Interlocked.Read(ref _operationGeneration);
        CancellationToken token = _lifetime.Token;
        MapBuildResult result = await BuildAsync(map, token).ConfigureAwait(false);
        if (!result.Success || token.IsCancellationRequested || _disposed
            || generation != Interlocked.Read(ref _operationGeneration)) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (!_disposed && !token.IsCancellationRequested
                && generation == Interlocked.Read(ref _operationGeneration))
                LaunchClientMap(map.SourcePath);
        });
    }

    private async Task ExportAsync(InstalledMap map)
    {
        CancellationToken token = _lifetime.Token;
        MapBuildResult result = await BuildAsync(map, token).ConfigureAwait(false);
        if (!result.Success || token.IsCancellationRequested || _disposed) return;
        string destination = Path.ChangeExtension(map.SourcePath, MapBundle.Extension);
        MapBundleWriteResult package = MapPackageBuilder.Cook(map.Project, map.SourcePath, destination);
        PostStatus($"Exported {Path.GetFileName(destination)} · {package.ArtifactHash[..12]}");
    }

    private async Task AddTextureAsync(InstalledMap map)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage
            || map.Project.Authoring == null) return;
        string? temporary = null;
        try
        {
            IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = "Add Custom Map Texture",
                    AllowMultiple = false,
                    FileTypeFilter = [new FilePickerFileType("Image")
                        { Patterns = ["*.png", "*.jpg", "*.jpeg"] }]
                });
            if (_disposed || files.Count == 0) return;
            string projectDirectory = Path.GetDirectoryName(map.SourcePath)!;
            string textures = Path.Combine(projectDirectory, "textures");
            Directory.CreateDirectory(textures);
            string extension = Path.GetExtension(files[0].Name).ToLowerInvariant();
            string stem = MapIdentity.FromLegacyName(Path.GetFileNameWithoutExtension(files[0].Name))
                ["legacy.".Length..];
            if (stem.Length > 50) stem = stem[..50].TrimEnd('-', '.', '_');
            string materialId = "material." + stem;
            for (int suffix = 2; map.Project.Authoring.Materials.Any(value => value.Id == materialId); suffix++)
                materialId = $"material.{stem}-{suffix}";
            string destination = Path.Combine(textures, materialId["material.".Length..] + extension);
            temporary = destination + ".import-" + Guid.NewGuid().ToString("N");
            await using (Stream source = await files[0].OpenReadAsync())
            await using (var output = new FileStream(temporary, FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous))
            {
                await source.CopyToAsync(output, _lifetime.Token);
                await output.FlushAsync(_lifetime.Token);
            }
            File.Move(temporary, destination, overwrite: false);
            temporary = null;
            map.Project.Authoring.Materials.Add(new MapAuthoringMaterial
            {
                Id = materialId,
                Name = Path.GetFileNameWithoutExtension(files[0].Name),
                CustomImage = Path.GetRelativePath(projectDirectory, destination).Replace('\\', '/'),
                Tiling = 16,
                Terrain = "Metal"
            });
            MapProjectIO.Save(map.Project, map.SourcePath);
            await RefreshAsync().ConfigureAwait(false);
            PostStatus($"Added {files[0].Name}; select a brush in the editor and apply {materialId}");
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception exception)
        {
            PostStatus("Texture import failed: " + exception.Message);
        }
        finally
        {
            if (temporary != null)
            {
                try { File.Delete(temporary); }
                catch (IOException) { }
            }
        }
    }

    private async Task RemoveAsync(InstalledMap map)
    {
        if (_catalog == null) return;
        try
        {
            await _catalog.RemoveAsync(map.ContentIdentity, _lifetime.Token).ConfigureAwait(false);
            PostStatus($"Removed {map.DisplayName}; source projects were untouched");
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
    }

    private void ShowDetails(InstalledMap map)
    {
        string statistics = map.Statistics == null ? "Not built"
            : $"{map.Statistics.RenderTriangles:N0} triangles · "
                + $"{map.Statistics.CollisionFaces:N0} collision faces · "
                + $"{map.Statistics.Entities:N0} entities";
        var body = new StackPanel { Spacing = 8, Margin = new Avalonia.Thickness(22) };
        void Add(string label, string value)
        {
            body.Children.Add(new TextBlock { Text = label, Classes = { "prime-kicker" } });
            body.Children.Add(new TextBlock { Text = value, Classes = { "prime-body" },
                TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        }
        Add("NAME", map.DisplayName);
        Add("AUTHOR", map.Author);
        Add("DESCRIPTION", string.IsNullOrWhiteSpace(map.Description) ? "No description" : map.Description);
        Add("STABLE ID", map.ContentIdentity.Identity.StableId);
        Add("VERSION", map.ContentIdentity.Identity.Version.ToString());
        Add("CONTENT HASH", map.ContentIdentity.ContentHash);
        Add("ARTIFACT HASH", map.ArtifactHash ?? "Editable local project");
        Add("SUPPORTED MODES", string.Join(", ", map.SupportedModes));
        Add("PACKAGE SIZE", $"{map.PackageSize:N0} bytes");
        Add("INSTALLED", map.InstalledAt?.ToLocalTime().ToString("g") ?? "Not installed");
        Add("BUILD STATE", map.BuildState.ToString());
        Add("BUILD STATISTICS", statistics);
        Add("DIAGNOSTICS", map.Diagnostics.IsEmpty ? "None"
            : string.Join(Environment.NewLine, map.Diagnostics.Select(value =>
                $"{value.Severity} {value.Code}: {value.Message}")));
        var window = new Window
        {
            Title = $"{map.DisplayName} - Map Details",
            Width = 680,
            Height = 760,
            Content = new ScrollViewer { Content = body }
        };
        if (TopLevel.GetTopLevel(this) is Window owner) window.Show(owner);
        else window.Show();
    }

    private static AvaloniaButton Button(string text,
        EventHandler<Avalonia.Interactivity.RoutedEventArgs> handler)
    {
        var button = new AvaloniaButton { Content = text, Classes = { "prime-button", "prime-quiet" },
            MinHeight = 40, Margin = new Avalonia.Thickness(0, 0, 8, 0) };
        button.Click += handler;
        return button;
    }

    private static void LaunchClientMap(string path)
        => StartProcess(Environment.GetCommandLineArgs()[0], ["-editorplaytest", path, "-bots", "-seconds", "86400"]);

    private static void LaunchEditor(string path)
    {
        string? configured = Environment.GetEnvironmentVariable("PRIME_EDITOR_PATH");
        string editorName = OperatingSystem.IsWindows() ? "ProjectPrime.Editor.exe" : "ProjectPrime.Editor";
        string executable = configured ?? Path.Combine(AppContext.BaseDirectory, "editor", editorName);
        if (!File.Exists(executable))
        {
            string beside = Path.Combine(AppContext.BaseDirectory, editorName);
            string packagedDll = Path.Combine(AppContext.BaseDirectory, "editor", "ProjectPrime.Editor.dll");
            string besideDll = Path.Combine(AppContext.BaseDirectory, "ProjectPrime.Editor.dll");
            if (File.Exists(beside)) executable = beside;
            else if (File.Exists(packagedDll)) executable = packagedDll;
            else if (File.Exists(besideDll)) executable = besideDll;
            else throw new FileNotFoundException("Project Prime Editor is not installed with the client.");
        }
        StartProcess(executable, [path]);
    }

    private static void StartProcess(string executable, IEnumerable<string> arguments)
    {
        ProcessStartInfo start;
        if (Path.GetExtension(executable).Equals(".dll", StringComparison.OrdinalIgnoreCase))
        {
            start = new ProcessStartInfo("dotnet");
            start.ArgumentList.Add(executable);
        }
        else start = new ProcessStartInfo(executable);
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        start.UseShellExecute = false;
        _ = Process.Start(start) ?? throw new InvalidOperationException("Process could not be started.");
    }

    private void OpenEditor(string path)
    {
        try { LaunchEditor(path); }
        catch (Exception exception)
        {
            _status.Text = "Editor could not open: " + exception.Message;
        }
    }

    private void PostStatus(string message)
        => Dispatcher.UIThread.Post(() =>
        {
            if (!_disposed) _status.Text = message;
        });

    private void DisposePreviews()
    {
        foreach (Bitmap preview in _previews) preview.Dispose();
        _previews.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Interlocked.Increment(ref _operationGeneration);
        _lifetime.Cancel();
        if (_catalog != null) _catalog.Changed -= CatalogChanged;
        _catalog?.Dispose();
        DisposePreviews();
        _lifetime.Dispose();
    }
}
