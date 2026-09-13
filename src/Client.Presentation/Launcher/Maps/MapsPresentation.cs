using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
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
        MapBuildState.NeedsBuild => "Update available",
        MapBuildState.Building => "Building…",
        MapBuildState.Invalid => "Missing content",
        MapBuildState.Unsupported => "Incompatible",
        MapBuildState.MissingDependency => "Missing content",
        _ => state.ToString()
    };

    /// <summary>
    /// The gallery keeps cards readable at the shell's supported widths. The
    /// route still uses a virtualizing ListBox for the item host; these
    /// thresholds only decide how wide each realized card should be.
    /// </summary>
    internal static int ColumnsForWidth(double width)
    {
        if (Double.IsNaN(width) || Double.IsInfinity(width) || width < 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        return width >= 1120 ? 3 : width >= 640 ? 2 : 1;
    }

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
    private readonly List<PrimeCard> _cards = new();
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
        _cards.Clear();
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

        if (!String.IsNullOrWhiteSpace(_context.State.Status)
            && !String.Equals(_context.State.Status, _context.State.Error,
                StringComparison.Ordinal))
            root.Children.Add(Text(_context.State.Status!, "prime-muted"));
        if (!String.IsNullOrWhiteSpace(_context.State.Error))
        {
            string error = _context.State.Error!;
            root.Children.Add(PrimeControlFactory.SectionPanel(Stack(
                Text("Could not update maps.", "prime-body"),
                Text(PlayerFacingMapError(error), "prime-muted"),
                new Expander
                {
                    Header = "Details",
                    Content = Text(error, "prime-muted")
                },
                _context.Refresh == null ? null
                    : Button("Retry", _context.Refresh, primary: true))));
        }

        root.Children.Add(BuildActions());
        if (_context.State.Tab == MapsTab.Community)
        {
            root.Children.Add(PrimeControlFactory.SectionPanel(Stack(
                PrimeControlFactory.EmptyState("Community maps",
                    "Community maps are not available yet. Check back after the community service is ready."),
                HiddenText("Community maps are coming later."))));
            return root;
        }

        ImmutableArray<InstalledMap> maps = _context.State.VisibleMaps
            .OrderByDescending(map => map.BuildState == MapBuildState.Ready)
            .ThenBy(map => map.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(map => map.SourcePath, StringComparer.Ordinal)
            .ToImmutableArray();
        if (maps.IsDefaultOrEmpty)
        {
            if (_context.State.Loading)
            {
                root.Children.Add(PrimeControlFactory.SectionPanel(Stack(
                    Text("Loading maps…", "prime-heading"),
                    Text("Reading the maps installed on this device.",
                        "prime-muted"))));
                return root;
            }
            if (!String.IsNullOrWhiteSpace(_context.State.Error))
                return root;
            (string title, string message) = _context.State.Tab == MapsTab.Library
                ? ("No maps installed", "No installed maps yet. Install a package to play it.")
                : _context.Create == null && _context.ImportQ3 == null
                    ? ("No local maps yet", "Map authoring is available in the bundled desktop Project Prime Editor.")
                    : ("No local maps yet", "No local map projects yet. Create a map or import Quake 3 geometry.");
            Control? primary = null;
            if (_context.State.Tab == MapsTab.Library && _context.Install != null)
                primary = Button("Install map", _context.Install, primary: true);
            root.Children.Add(PrimeControlFactory.SectionPanel(
                PrimeControlFactory.EmptyState(title, message, primaryAction: primary)));
            return root;
        }

        _cards.Clear();
        _items = new ListBox
        {
            ItemsSource = maps,
            // Avalonia may briefly invoke a recycled container template with
            // no data while a virtualized list is being torn down.
            ItemTemplate = new FuncDataTemplate<InstalledMap>((map, _) => map is null
                ? new Border() : BuildCard(map)),
        };
        // A modest library benefits from the gallery's two/three-column
        // composition. Once a catalog becomes very large, keep the host
        // virtualized so off-screen previews are not all realized at once.
        bool useVirtualizedList = maps.Length > 48;
        _items.ItemsPanel = new FuncTemplate<Panel?>(() => useVirtualizedList
            ? new VirtualizingStackPanel
            {
                Orientation = Orientation.Vertical,
                CacheLength = 1
            }
            : new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Stretch
            });
        _items.Classes.Add("prime-map-list");
        _items.SizeChanged += (_, _) => ApplyCardWidths();
        ScrollViewer.SetHorizontalScrollBarVisibility(_items, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(_items, ScrollBarVisibility.Auto);
        root.Children.Add(_items);
        ApplyCardWidths();
        return root;
    }

    private void ApplyCardWidths()
    {
        if (_items == null || _cards.Count == 0) return;
        double width = _items.Bounds.Width;
        if (width <= 0) return;
        int columns = MapsPresentation.ColumnsForWidth(width);
        double gap = 12;
        // Let a genuinely narrow viewport shrink the card instead of forcing
        // horizontal overflow; desktop widths are capped for comfortable
        // reading while the 16:9 preview remains stable.
        double cardWidth = Math.Min(460,
            Math.Max(1, (width - gap * (columns - 1) - 8) / columns));
        foreach (PrimeCard card in _cards)
            card.Width = cardWidth;
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
        var content = new StackPanel { Spacing = 10 };
        MapsPreviewView preview = new(map, _context.LoadPreview);
        _previews.Add(preview);
        content.Children.Add(preview);

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
        content.Children.Add(details);

        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
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
        content.Children.Add(actions);
        var card = new PrimeCard(content)
        {
            Width = 360,
            MinHeight = 280,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Avalonia.Thickness(0, 0, 12, 12)
        };
        _cards.Add(card);
        // Item templates may be realized after the ListBox's first layout
        // pass. Apply the current measured width here as well as from the
        // host SizeChanged handler so the first visible cards are responsive.
        ApplyCardWidths();
        return card;
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

    private static TextBlock HiddenText(string value)
        => new() { Text = value, IsVisible = false, Classes = { "prime-muted" } };

    private static string PlayerFacingMapError(string error)
    {
        const string fallback = "The map content could not be loaded. Try again.";
        string value = error.Trim();
        if (value.Length == 0 || value.Contains("exception",
                StringComparison.OrdinalIgnoreCase)
            || value.Contains("stack trace", StringComparison.OrdinalIgnoreCase)
            || value.Contains("/", StringComparison.Ordinal))
            return fallback;
        return PrimeRoutePresentation.PlayerFacingNetworkError(value, fallback);
    }

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
        Height = 184.5;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Top;
        Classes.Add("prime-surface");
        Child = Fallback();
        SizeChanged += (_, _) => UpdateAspectRatio();
        AttachedToVisualTree += (_, _) => StartLoad();
        DetachedFromVisualTree += (_, _) => StopLoad();
    }

    private void UpdateAspectRatio()
    {
        if (Bounds.Width > 0)
            Height = Math.Max(1, Bounds.Width * 9d / 16d);
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
        if (Volatile.Read(ref _disposed) == 0) Child = Fallback();
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

    private Control Fallback()
        => new MapPreviewFallback(_map.DisplayName, _map.Source);
}

/// <summary>
/// Deterministic code-native map art used while authored previews are absent.
/// It gives every map a visual identity without allocating a bitmap or
/// invoking the renderer, and the same map always produces the same geometry.
/// </summary>
internal sealed class MapPreviewFallback : Control
{
    private readonly string _title;
    private readonly MapInstallSource _source;
    private readonly int _seed;

    internal MapPreviewFallback(string title, MapInstallSource source)
    {
        _title = String.IsNullOrWhiteSpace(title) ? "Map" : title.Trim();
        _source = source;
        _seed = StableSeed(_title);
        Classes.Add("prime-map-preview-fallback");
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Bounds.Width < 8 || Bounds.Height < 8) return;

        Rect area = new(0, 0, Bounds.Width, Bounds.Height);
        context.DrawRectangle(GuiTheme.InkBrush, null, area);
        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(70,
            GuiTheme.Tech.R, GuiTheme.Tech.G, GuiTheme.Tech.B)), 1);
        double spacing = Math.Max(18, Math.Min(30, area.Width / 10));
        for (double x = spacing / 2; x < area.Width; x += spacing)
            context.DrawLine(gridPen, new Point(x, 0), new Point(x, area.Height));
        for (double y = spacing / 2; y < area.Height; y += spacing)
            context.DrawLine(gridPen, new Point(0, y), new Point(area.Width, y));

        var amberPen = new Pen(GuiTheme.BrandBrush, 2);
        double inset = Math.Max(14, Math.Min(area.Width, area.Height) * 0.12);
        Rect route = area.Deflate(inset);
        double bend = 0.2 + (_seed % 4) * 0.1;
        var points = new[]
        {
            new Point(route.Left, route.Top + route.Height * bend),
            new Point(route.Left + route.Width * 0.35, route.Top),
            new Point(route.Right, route.Top + route.Height * 0.32),
            new Point(route.Left + route.Width * 0.68, route.Bottom),
            new Point(route.Left, route.Top + route.Height * 0.74),
            new Point(route.Left, route.Top + route.Height * bend)
        };
        for (int index = 1; index < points.Length; index++)
            context.DrawLine(amberPen, points[index - 1], points[index]);

        FormattedText title = TrackedText.Make(_title.ToUpperInvariant(), 14,
            bold: true, GuiTheme.TextBrush);
        title.MaxTextWidth = Math.Max(40, area.Width - 24);
        title.Trimming = TextTrimming.CharacterEllipsis;
        context.DrawText(title, new Point(12, Math.Max(8, area.Height - 40)));
        string source = _source switch
        {
            MapInstallSource.LocalProject or MapInstallSource.LegacyRecipe => "LOCAL MAP",
            _ => "PROJECT PRIME MAP"
        };
        FormattedText sourceText = TrackedText.Make(source, 9, bold: true,
            GuiTheme.TechBrush);
        context.DrawText(sourceText, new Point(12, Math.Max(8, area.Height - 20)));
    }

    private static int StableSeed(string value)
    {
        unchecked
        {
            int hash = 17;
            foreach (char character in value)
                hash = hash * 31 + character;
            return Math.Abs(hash == Int32.MinValue ? Int32.MaxValue : hash);
        }
    }
}
