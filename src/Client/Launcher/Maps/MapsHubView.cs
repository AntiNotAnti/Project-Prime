using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Launcher.Gui;

/// <summary>
/// Thin shell adapter for the Maps route. Platform storage pickers and
/// external editor/client launches live here; catalog and presentation state
/// are owned by <see cref="MapsController"/> and <see cref="MapsPresentation"/>.
/// </summary>
internal sealed class MapsHubView : UserControl, IDisposable
{
    private readonly bool _captureMode;
    private readonly CancellationTokenSource _lifetime = new();
    private MapsController? _controller;
    private MapsPresentationView? _presentation;
    private long _viewGeneration;
    private int _renderQueued;
    private int _disposed;

    public MapsHubView(bool captureMode = false)
    {
        _captureMode = captureMode;
        DetachedFromVisualTree += (_, _) => Dispose();
        if (captureMode)
        {
            Rebuild();
            return;
        }

        _controller = new MapsController();
        _controller.Changed += ControllerChanged;
        Rebuild();
        StartTask(RefreshAsync);
    }

    private void ControllerChanged(object? sender, EventArgs args)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        if (Interlocked.Exchange(ref _renderQueued, 1) != 0) return;
        Dispatcher.UIThread.Post(() =>
        {
            Interlocked.Exchange(ref _renderQueued, 0);
            Rebuild();
        });
    }

    private void Rebuild()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        Interlocked.Increment(ref _viewGeneration);
        bool creatorTools = !_captureMode && !OperatingSystem.IsAndroid();
        MapsState state = _controller?.State ?? MapsState.Initial with
        {
            Status = "Installed maps appear here."
        };
        var next = MapsPresentation.Build(new MapsPresentationContext(
            state,
            _captureMode,
            _captureMode ? null : SelectTab,
            _captureMode ? null : RefreshMaps,
            _captureMode ? null : InstallMap,
            creatorTools ? CreateMap : null,
            creatorTools ? ImportQ3 : null,
            _captureMode ? null : PlayMap,
            _captureMode ? null : BuildMap,
            creatorTools ? EditMap : null,
            creatorTools ? ExportMap : null,
            creatorTools ? AddTexture : null,
            _captureMode ? null : RemoveMap,
            ShowDetails,
            _captureMode ? null : LoadPreviewAsync));
        MapsPresentationView? previous = _presentation;
        _presentation = next;
        Content = next;
        previous?.Dispose();
    }

    private void SelectTab(MapsTab tab) => _controller?.SelectTab(tab);

    private void RefreshMaps() => StartTask(RefreshAsync);

    private Task RefreshAsync()
        => _controller?.RefreshAsync(_lifetime.Token) ?? Task.CompletedTask;

    private void InstallMap() => StartTask(InstallMapAsync);

    private async Task InstallMapAsync()
    {
        if (_controller is not { } controller
            || TopLevel.GetTopLevel(this)?.StorageProvider is not { CanOpen: true } storage)
            return;
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
            if (Volatile.Read(ref _disposed) != 0 || files.Count == 0) return;
            string path = await LocalPathAsync(files[0], controller.LifetimeToken,
                "prime-map-import").ConfigureAwait(false);
            scratch = path == files[0].TryGetLocalPath() ? null : path;
            await controller.InstallAsync(path, controller.LifetimeToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception exception)
        {
            controller.SetError("Install rejected: " + exception.Message);
        }
        finally
        {
            DeleteScratch(scratch);
        }
    }

    private void CreateMap() => StartTask(CreateMapAsync);

    private async Task CreateMapAsync()
    {
        if (_controller is not { } controller) return;
        string? projectPath = await controller.CreateProjectAsync(controller.LifetimeToken)
            .ConfigureAwait(false);
        if (projectPath == null || Volatile.Read(ref _disposed) != 0) return;
        try { LaunchEditor(projectPath); }
        catch (Exception exception) { controller.SetError(exception.Message); }
    }

    private void ImportQ3() => StartTask(ImportQ3Async);

    private async Task ImportQ3Async()
    {
        if (_controller is not { } controller
            || TopLevel.GetTopLevel(this)?.StorageProvider is not { CanOpen: true } storage)
            return;
        string? scratch = null;
        try
        {
            IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = "Import Quake 3 BSP or PK3",
                    AllowMultiple = false,
                    FileTypeFilter =
                    [new FilePickerFileType("Quake 3 map") { Patterns = ["*.bsp", "*.pk3"] }]
                });
            if (Volatile.Read(ref _disposed) != 0 || files.Count == 0) return;
            string path = await LocalPathAsync(files[0], controller.LifetimeToken,
                "prime-q3-import").ConfigureAwait(false);
            scratch = path == files[0].TryGetLocalPath() ? null : path;
            string? projectPath = await controller.ImportQ3Async(path,
                controller.LifetimeToken).ConfigureAwait(false);
            if (projectPath != null && Volatile.Read(ref _disposed) == 0)
                LaunchEditor(projectPath);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception exception)
        {
            controller.SetError("Q3 import failed: " + exception.Message);
        }
        finally
        {
            DeleteScratch(scratch);
        }
    }

    private void PlayMap(InstalledMap map) => StartTask(() => PlayMapAsync(map));

    private async Task PlayMapAsync(InstalledMap map)
    {
        if (_controller is not { } controller) return;
        long generation = Interlocked.Read(ref _viewGeneration);
        MapBuildResult result = await controller.BuildAsync(map, controller.LifetimeToken)
            .ConfigureAwait(false);
        if (!result.Success || Volatile.Read(ref _disposed) != 0
            || generation != Interlocked.Read(ref _viewGeneration))
            return;
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (Volatile.Read(ref _disposed) == 0
                && generation == Interlocked.Read(ref _viewGeneration))
                LaunchClientMap(map.SourcePath);
        });
    }

    private void BuildMap(InstalledMap map)
        => StartTask(async () =>
        {
            if (_controller is { } controller)
                _ = await controller.BuildAsync(map, controller.LifetimeToken)
                    .ConfigureAwait(false);
        });

    private void EditMap(InstalledMap map)
    {
        if (_controller is not { } controller) return;
        try { LaunchEditor(map.SourcePath); }
        catch (Exception exception) { controller.SetError(exception.Message); }
    }

    private void ExportMap(InstalledMap map)
        => StartTask(async () =>
        {
            if (_controller is { } controller)
                _ = await controller.ExportAsync(map, controller.LifetimeToken)
                    .ConfigureAwait(false);
        });

    private void RemoveMap(InstalledMap map)
        => StartTask(async () =>
        {
            if (_controller is { } controller)
                _ = await controller.RemoveAsync(map, controller.LifetimeToken)
                    .ConfigureAwait(false);
        });

    private void AddTexture(InstalledMap map) => StartTask(() => AddTextureAsync(map));

    private async Task AddTextureAsync(InstalledMap map)
    {
        if (_controller is not { } controller
            || TopLevel.GetTopLevel(this)?.StorageProvider is not { CanOpen: true } storage)
            return;
        string? scratch = null;
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
            if (Volatile.Read(ref _disposed) != 0 || files.Count == 0) return;
            string path = await LocalPathAsync(files[0], controller.LifetimeToken,
                "prime-texture-import").ConfigureAwait(false);
            scratch = path == files[0].TryGetLocalPath() ? null : path;
            await controller.AddTextureAsync(map, path, controller.LifetimeToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception exception)
        {
            controller.SetError("Texture import failed: " + exception.Message);
        }
        finally
        {
            DeleteScratch(scratch);
        }
    }

    private Task<MapsPreviewLease?> LoadPreviewAsync(InstalledMap map,
        CancellationToken cancellationToken)
        => _controller?.LoadPreviewAsync(map, cancellationToken)
            ?? Task.FromResult<MapsPreviewLease?>(null);

    private async Task<string> LocalPathAsync(IStorageFile file,
        CancellationToken cancellationToken, string prefix)
    {
        string? local = file.TryGetLocalPath();
        if (local != null) return local;
        string scratch = Path.Combine(Path.GetTempPath(),
            $"{prefix}-{Guid.NewGuid():N}{Path.GetExtension(file.Name)}");
        try
        {
            await using Stream source = await file.OpenReadAsync();
            await using Stream destination = File.Create(scratch);
            await source.CopyToAsync(destination, cancellationToken);
            return scratch;
        }
        catch
        {
            DeleteScratch(scratch);
            throw;
        }
    }

    private static void DeleteScratch(string? path)
    {
        if (path == null) return;
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void StartTask(Func<Task> operation)
    {
        try { _ = ObserveAsync(operation); }
        catch (Exception exception) { _controller?.SetError(exception.Message); }
    }

    private async Task ObserveAsync(Func<Task> operation)
    {
        try { await operation().ConfigureAwait(false); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception exception) { _controller?.SetError(exception.Message); }
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
        Add("DESCRIPTION", String.IsNullOrWhiteSpace(map.Description)
            ? "No description" : map.Description);
        Add("STABLE ID", map.ContentIdentity.Identity.StableId);
        Add("VERSION", map.ContentIdentity.Identity.Version.ToString());
        Add("CONTENT HASH", map.ContentIdentity.ContentHash);
        Add("ARTIFACT HASH", map.ArtifactHash ?? "Editable local project");
        Add("SUPPORTED MODES", String.Join(", ", map.SupportedModes));
        Add("PACKAGE SIZE", $"{map.PackageSize:N0} bytes");
        Add("INSTALLED", map.InstalledAt?.ToLocalTime().ToString("g") ?? "Not installed");
        Add("BUILD STATE", map.BuildState.ToString());
        Add("BUILD STATISTICS", statistics);
        Add("SOURCE PATH", map.SourcePath);
        Add("DIAGNOSTICS", map.Diagnostics.IsEmpty ? "None"
            : String.Join(Environment.NewLine, map.Diagnostics.Select(value =>
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

    private static void LaunchClientMap(string path)
        => StartProcess(Environment.GetCommandLineArgs()[0],
            ["-editorplaytest", path, "-bots", "-seconds", "86400"]);

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
            else throw new FileNotFoundException(
                "Project Prime Editor is not installed with the client.");
        }
        StartProcess(executable, [path]);
    }

    private static void StartProcess(string executable, IEnumerable<string> arguments)
    {
        System.Diagnostics.ProcessStartInfo start;
        if (Path.GetExtension(executable).Equals(".dll", StringComparison.OrdinalIgnoreCase))
        {
            start = new System.Diagnostics.ProcessStartInfo("dotnet");
            start.ArgumentList.Add(executable);
        }
        else start = new System.Diagnostics.ProcessStartInfo(executable);
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        start.UseShellExecute = false;
        _ = System.Diagnostics.Process.Start(start)
            ?? throw new InvalidOperationException("Process could not be started.");
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Interlocked.Increment(ref _viewGeneration);
        _lifetime.Cancel();
        _presentation?.Dispose();
        _presentation = null;
        if (_controller is { } controller)
        {
            controller.Changed -= ControllerChanged;
            controller.Dispose();
            _controller = null;
        }
        _lifetime.Dispose();
    }
}
