using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Launcher.Gui;

/// <summary>
/// UI callbacks and immutable state supplied by the Maps route owner. File
/// pickers and process launches stay outside the presentation layer.
/// </summary>
internal sealed record MapsPresentationContext(
    MapsState State,
    bool CaptureMode,
    Action<MapsTab>? SelectTab,
    Action? Refresh,
    Action? Install,
    Action? Create,
    Action? ImportQ3,
    Action<InstalledMap>? Play,
    Action<InstalledMap>? Build,
    Action<InstalledMap>? Edit,
    Action<InstalledMap>? Export,
    Action<InstalledMap>? AddTexture,
    Action<InstalledMap>? Remove,
    Action<InstalledMap>? Details,
    Func<InstalledMap, CancellationToken, Task<MapsPreviewLease?>>? LoadPreview);

/// <summary>Maps route composition over controller-owned state.</summary>
internal static class MapsPresentation
{
    internal static MapsPresentationView Build(MapsPresentationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new MapsPresentationView(context);
    }

    internal static string BuildStateLabel(MapBuildState state) => state switch
    {
        MapBuildState.Ready => "Ready",
        MapBuildState.NeedsBuild => "Needs build",
        MapBuildState.Building => "Building…",
        MapBuildState.Invalid => "Needs attention",
        MapBuildState.Unsupported => "Unsupported",
        MapBuildState.MissingDependency => "Missing dependency",
        _ => state.ToString()
    };

    internal static string ModeLabel(MapMode mode) => mode switch
    {
        MapMode.Battle => "Battle",
        MapMode.Survival => "Survival",
        MapMode.Capture => "Capture",
        MapMode.Bounty => "Bounty",
        MapMode.Nodes => "Nodes",
        _ => mode.ToString()
    };
}

/// <summary>
/// Owns the realized map cards and their preview leases. Replacing a route
/// page disposes this view so decoded bitmaps cannot outlive their cards.
/// </summary>
internal sealed class MapsPresentationView : UserControl, IDisposable
{
    private readonly MapsPresentationContext _context;
    private readonly List<MapsPreviewView> _previews = new();
    private ListBox? _items;
    private int _disposed;

    internal MapsPresentationView(MapsPresentationContext context)
    {
        _context = context;
        Content = BuildRoot();
    }

    internal ListBox? ItemsControl => _items;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (_items != null) _items.ItemsSource = Array.Empty<InstalledMap>();
        foreach (MapsPreviewView preview in _previews) preview.Dispose();
        _previews.Clear();
    }

    private Control BuildRoot()
    {
        var root = new StackPanel
        {
            Spacing = 18,
            MaxWidth = 1180,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        root.Children.Add(PrimeControlFactory.PageHeading("Maps", "MAP LIBRARY",
            "Play installed maps or manage your local authoring projects."));
        root.Children.Add(new PrimeTabStrip(Enum.GetValues<MapsTab>().Select(tab =>
            new PrimeTabItem(MapsState.TabLabel(tab), _context.State.Tab == tab,
                () => _context.SelectTab?.Invoke(tab)))));

        if (!String.IsNullOrWhiteSpace(_context.State.Status))
            root.Children.Add(Text(_context.State.Status!, "prime-muted"));
        if (!String.IsNullOrWhiteSpace(_context.State.Error))
        {
            root.Children.Add(PrimeControlFactory.SectionPanel(Stack(
                Text("Could not update Maps.", "prime-body"),
                Text(_context.State.Error!, "prime-muted"),
                _context.Refresh == null ? null
                    : Button("Retry", _context.Refresh, primary: true))));
        }

        root.Children.Add(BuildActions());
        if (_context.State.Tab == MapsTab.Community)
        {
            root.Children.Add(PrimeControlFactory.SectionPanel(Stack(
                Text("Community maps", "prime-heading"),
                Text("Community maps are coming later.", "prime-muted"))));
            return root;
        }

        ImmutableArray<InstalledMap> maps = _context.State.VisibleMaps
            .OrderByDescending(map => map.BuildState == MapBuildState.Ready)
            .ThenBy(map => map.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(map => map.SourcePath, StringComparer.Ordinal)
            .ToImmutableArray();
        if (maps.IsDefaultOrEmpty)
        {
            string message = _context.State.Tab == MapsTab.Library
                ? "No installed maps yet. Install a package to play it."
                : _context.Create == null && _context.ImportQ3 == null
                    ? "Map authoring is available in the bundled desktop Project Prime Editor."
                    : "No local map projects yet. Create a map or import Quake 3 geometry.";
            root.Children.Add(PrimeControlFactory.SectionPanel(
                new PrimeEmptyState(message)));
            return root;
        }

        _items = new ListBox
        {
            ItemsSource = maps,
            ItemTemplate = new FuncDataTemplate<InstalledMap>((map, _) => BuildCard(map))
        };
        _items.ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel
        {
            Orientation = Orientation.Vertical,
            CacheLength = 1
        });
        _items.Classes.Add("prime-map-list");
        ScrollViewer.SetVerticalScrollBarVisibility(_items, ScrollBarVisibility.Auto);
        root.Children.Add(_items);
        return root;
    }

    private Control BuildActions()
    {
        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        switch (_context.State.Tab)
        {
            case MapsTab.Library:
                if (_context.Install != null)
                    actions.Children.Add(Button("Install Map", _context.Install, primary: true));
                break;
            case MapsTab.MyMaps:
                if (_context.Create != null)
                    actions.Children.Add(Button("Create Map", _context.Create, primary: true));
                if (_context.ImportQ3 != null)
                    actions.Children.Add(Button("Import Q3", _context.ImportQ3));
                break;
            case MapsTab.Community:
                break;
        }
        if (_context.Refresh != null)
            actions.Children.Add(Button("Refresh", _context.Refresh, quiet: true));
        return actions;
    }

    private Control BuildCard(InstalledMap map)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("160,*,Auto"),
            ColumnSpacing = 16
        };
        MapsPreviewView preview = new(map, _context.LoadPreview);
        _previews.Add(preview);
        grid.Children.Add(preview);

        var details = new StackPanel { Spacing = 4 };
        details.Children.Add(Text(map.DisplayName, "prime-title"));
        if (!String.IsNullOrWhiteSpace(map.Author))
            details.Children.Add(Text(map.Author, "prime-muted"));
        if (!map.SupportedModes.IsDefaultOrEmpty)
            details.Children.Add(Text(String.Join(" · ",
                map.SupportedModes.Select(MapsPresentation.ModeLabel)), "prime-body"));
        details.Children.Add(new PrimeStatusChip(
            MapsPresentation.BuildStateLabel(map.BuildState)));
        if (!String.IsNullOrWhiteSpace(map.Description))
            details.Children.Add(Text(map.Description.Trim(), "prime-muted"));
        grid.Children.Add(details);
        Grid.SetColumn(details, 1);

        var actions = new StackPanel { Spacing = 6, Width = 132 };
        if (_context.State.Tab == MapsTab.Library)
        {
            if (map.BuildState == MapBuildState.Ready && _context.Play != null)
                actions.Children.Add(Button("Play", () => _context.Play(map), primary: true));
            else if (_context.Build != null
                && (map.BuildState is MapBuildState.NeedsBuild
                    or MapBuildState.Invalid or MapBuildState.MissingDependency))
                actions.Children.Add(Button("Build", () => _context.Build(map), primary: true));
            if (_context.Details != null)
                actions.Children.Add(Button("Details", () => _context.Details(map), quiet: true));
            if (map.Source == MapInstallSource.InstalledPackage && _context.Remove != null)
                actions.Children.Add(Button("Remove", () => _context.Remove(map), quiet: true));
        }
        else
        {
            if (_context.Edit != null)
                actions.Children.Add(Button("Edit", () => _context.Edit(map), primary: true));
            if (_context.Build != null)
                actions.Children.Add(Button("Build", () => _context.Build(map)));
            if (_context.Export != null)
                actions.Children.Add(Button("Export", () => _context.Export(map)));
            if (_context.AddTexture != null && map.Project.Authoring != null)
                actions.Children.Add(Button("Add Texture", () => _context.AddTexture(map)));
            if (_context.Details != null)
                actions.Children.Add(Button("Details", () => _context.Details(map), quiet: true));
        }
        grid.Children.Add(actions);
        Grid.SetColumn(actions, 2);
        return new PrimeCard(grid);
    }

    private static StackPanel Stack(params Control?[] controls)
    {
        var stack = new StackPanel { Spacing = 8 };
        foreach (Control? control in controls)
        {
            if (control != null) stack.Children.Add(control);
        }
        return stack;
    }

    private static TextBlock Text(string value, string style)
        => new() { Text = value, TextWrapping = TextWrapping.Wrap, Classes = { style } };

    private static Avalonia.Controls.Button Button(string label, Action action,
        bool primary = false, bool quiet = false)
        => PrimeControlFactory.Button(label, action, primary, quiet);
}

/// <summary>
/// A realized card preview with cancellable generation-safe loading. It owns
/// only its current bitmap lease; the byte cache is controller-owned.
/// </summary>
internal sealed class MapsPreviewView : Border, IDisposable
{
    private readonly InstalledMap _map;
    private readonly Func<InstalledMap, CancellationToken, Task<MapsPreviewLease?>>? _load;
    private CancellationTokenSource? _loadCancellation;
    private MapsPreviewLease? _lease;
    private int _generation;
    private int _disposed;
    private bool _started;

    internal MapsPreviewView(InstalledMap map,
        Func<InstalledMap, CancellationToken, Task<MapsPreviewLease?>>? load)
    {
        _map = map;
        _load = load;
        Width = 160;
        Height = 92;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Top;
        Classes.Add("prime-surface");
        Child = Placeholder();
        AttachedToVisualTree += (_, _) => StartLoad();
        DetachedFromVisualTree += (_, _) => StopLoad();
        StartLoad();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        StopLoad();
    }

    private void StartLoad()
    {
        if (_started || Volatile.Read(ref _disposed) != 0
            || _load == null || String.IsNullOrWhiteSpace(_map.PreviewPath))
            return;
        _started = true;
        int generation = Interlocked.Increment(ref _generation);
        _loadCancellation = new CancellationTokenSource();
        _ = LoadAsync(generation, _loadCancellation.Token);
    }

    private void StopLoad()
    {
        _started = false;
        Interlocked.Increment(ref _generation);
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = null;
        _lease?.Dispose();
        _lease = null;
        if (Volatile.Read(ref _disposed) == 0) Child = Placeholder();
    }

    private async Task LoadAsync(int generation, CancellationToken cancellationToken)
    {
        MapsPreviewLease? lease = null;
        try
        {
            lease = await _load!(_map, cancellationToken).ConfigureAwait(false);
            if (lease == null) return;
            if (!IsCurrent(generation, cancellationToken))
            {
                lease.Dispose();
                lease = null;
                return;
            }
            bool applied = false;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!IsCurrent(generation, cancellationToken))
                    return;
                _lease?.Dispose();
                _lease = lease;
                Child = new Image
                {
                    Source = lease.Bitmap,
                    Stretch = Stretch.UniformToFill,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch
                };
                applied = true;
            });
            if (applied) lease = null;
        }
        catch (OperationCanceledException) { }
        catch (ArgumentException) { }
        catch (InvalidOperationException) { }
        catch (IOException) { }
        catch (PlatformNotSupportedException) { }
        finally
        {
            lease?.Dispose();
        }
    }

    private bool IsCurrent(int generation, CancellationToken cancellationToken)
        => Volatile.Read(ref _disposed) == 0
            && generation == Volatile.Read(ref _generation)
            && !cancellationToken.IsCancellationRequested;

    private static TextBlock Placeholder()
        => new()
        {
            Text = "Preview unavailable",
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Classes = { "prime-muted" }
        };
}
