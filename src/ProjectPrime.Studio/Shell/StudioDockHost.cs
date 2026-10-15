using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace ProjectPrime.Studio.Shell;

public enum StudioDockRegion { Left, Center, Right, Bottom, Floating }
public sealed class StudioDockLayout
{
    public double LeftWidth { get; set; } = 220;
    public double RightWidth { get; set; } = 280;
    public double BottomHeight { get; set; } = 150;
    public bool LeftVisible { get; set; } = true;
    public bool RightVisible { get; set; } = true;
    public bool BottomVisible { get; set; } = true;
    public List<StudioDockRegion> Detached { get; set; } = [];
    public Dictionary<StudioDockRegion,StudioToolWindowLayout> DetachedWindows { get; set; } = [];
    public StudioToolWindowLayout AssetsWindow { get; set; } = new();
    public void Normalize()
    {
        LeftWidth = double.IsFinite(LeftWidth) ? Math.Clamp(LeftWidth, 160, 600) : 220;
        RightWidth = double.IsFinite(RightWidth) ? Math.Clamp(RightWidth, 180, 600) : 280;
        BottomHeight = double.IsFinite(BottomHeight) ? Math.Clamp(BottomHeight, 90, 400) : 150;
        AssetsWindow ??= new(); AssetsWindow.Normalize();
        Detached = (Detached ?? []).Where(region => region is StudioDockRegion.Left or StudioDockRegion.Right or StudioDockRegion.Bottom).Distinct().ToList();
        DetachedWindows = (DetachedWindows ?? []).Where(pair=>pair.Key is StudioDockRegion.Left or StudioDockRegion.Right or StudioDockRegion.Bottom && pair.Value!=null).ToDictionary(pair=>pair.Key,pair=>pair.Value);
        foreach(var state in DetachedWindows.Values)state.Normalize();
    }
}

public sealed class StudioToolWindowLayout
{
    public int X {get;set;}
    public int Y {get;set;}
    public double Width {get;set;}=480;
    public double Height {get;set;}=650;
    public bool Visible {get;set;}
    public bool HasPosition {get;set;}
    public void Normalize() { Width=double.IsFinite(Width)?Math.Clamp(Width,300,2000):480; Height=double.IsFinite(Height)?Math.Clamp(Height,240,1800):650; X=Math.Clamp(X,-100000,100000);Y=Math.Clamp(Y,-100000,100000); }
}

/// <summary>Internal docking foundation: resizable regions, tabs, hide/show and native detached tool windows.</summary>
public sealed class StudioDockHost : UserControl, IDisposable
{
    private readonly Window _owner;
    private readonly Grid _grid;
    private readonly StudioDockLayout _layout;
    private readonly StudioDockLayout _defaults;
    private readonly Dictionary<StudioDockRegion, Border> _panels = [];
    private readonly Dictionary<StudioDockRegion, Window> _floating = [];
    private bool _disposed;
    private bool _workspaceFocus;
    public void SetWorkspaceFocus(bool focus) { CaptureSizes(); _workspaceFocus=focus; UpdateRegions(); }
    public ContentControl Center { get; } = new() { Name = "StudioDockCenter" };
    public event Action? LayoutChanged;
    public StudioDockHost(Window owner, StudioDockLayout layout, Control left, Control right, Control bottom, StudioDockLayout? defaults = null, string leftTitle = "Workspace", string bottomTitle = "Jobs")
    {
        _owner = owner;
        _layout = layout;
        _defaults = defaults ?? new();
        _layout.Normalize();
        _grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("220,5,*,5,280"),
            RowDefinitions = new RowDefinitions("*,5,150")
        };
        AddPanel(StudioDockRegion.Left, leftTitle, left, 0, 0);
        AddPanel(StudioDockRegion.Right, "Inspector", right, 4, 0);
        AddPanel(StudioDockRegion.Bottom, bottomTitle, bottom, 0, 2, 5);
        Grid.SetColumn(Center, 2);
        _grid.Children.Add(Center);
        _grid.Children.Add(Splitter(1, 0, false));
        _grid.Children.Add(Splitter(3, 0, false));
        _grid.Children.Add(Splitter(0, 1, true));
        Content = _grid;
        UpdateRegions();
    }
    public void RestoreFloating()
    {
        foreach (StudioDockRegion region in _layout.Detached.ToArray()) Detach(region);
    }
    public bool IsRegionVisible(StudioDockRegion region) => region switch
    {
        StudioDockRegion.Left => _layout.LeftVisible,
        StudioDockRegion.Right => _layout.RightVisible,
        StudioDockRegion.Bottom => _layout.BottomVisible,
        _ => true
    };
    public void Show(StudioDockRegion region)
    {
        CaptureSizes();
        _workspaceFocus=false;
        SetVisible(region, true);
        if (_floating.TryGetValue(region, out Window? window)) { window.Show(); window.Activate(); }
        UpdateRegions();
        LayoutChanged?.Invoke();
    }
    public void Hide(StudioDockRegion region)
    {
        CaptureSizes();
        SetVisible(region, false);
        if (_floating.TryGetValue(region, out Window? window)) window.Hide();
        UpdateRegions();
        LayoutChanged?.Invoke();
    }
    public void Detach(StudioDockRegion region)
    {
        if (!_panels.TryGetValue(region, out Border? panel) || _floating.ContainsKey(region)) return;
        CaptureSizes();
        _grid.Children.Remove(panel);
        if(!_layout.DetachedWindows.TryGetValue(region,out var state))_layout.DetachedWindows[region]=state=new() {Width=420,Height=500};
        state.Normalize();bool opening=true;int savedX=state.X,savedY=state.Y;bool hadPosition=state.HasPosition;
        Window window = new() { Title = $"Project Prime Studio · {region}", Width = state.Width, Height = state.Height, MinWidth = 220, MinHeight = 180, Content = panel,WindowStartupLocation=WindowStartupLocation.CenterOwner };
        void CaptureWindow()
        {
            if(opening || _disposed)return;
            state.X=window.Position.X;state.Y=window.Position.Y;state.HasPosition=true;
            state.Width=window.Width;state.Height=window.Height;LayoutChanged?.Invoke();
        }
        window.Opened+=(_,_)=> {if(hadPosition && window.Screens.All.Any(screen=>screen.WorkingArea.Contains(new PixelPoint(savedX+40,savedY+40))))window.Position=new(savedX,savedY);opening=false;CaptureWindow();};
        window.PositionChanged+=(_,_)=>CaptureWindow();window.SizeChanged+=(_,_)=>CaptureWindow();
        _floating.Add(region, window);
        if (!_layout.Detached.Contains(region)) _layout.Detached.Add(region);
        SetVisible(region, true);
        window.Closed += (_, _) =>
        {
            CaptureWindow();
            window.Content = null;
            _floating.Remove(region);
            _layout.Detached.Remove(region);
            if (_disposed) return;
            _grid.Children.Add(panel);
            SetVisible(region, true);
            UpdateRegions();
            LayoutChanged?.Invoke();
        };
        UpdateRegions();
        window.Show(_owner);
        LayoutChanged?.Invoke();
    }
    public void RestoreDefaults()
    {
        foreach (Window window in _floating.Values.ToArray()) window.Close();
        _layout.LeftWidth = _defaults.LeftWidth;
        _layout.RightWidth = _defaults.RightWidth;
        _layout.BottomHeight = _defaults.BottomHeight;
        _layout.LeftVisible = _defaults.LeftVisible;
        _layout.RightVisible = _defaults.RightVisible;
        _layout.BottomVisible = _defaults.BottomVisible;
        _layout.Detached.Clear();
        _layout.DetachedWindows.Clear();
        UpdateRegions();
        LayoutChanged?.Invoke();
    }
    public StudioDockLayout CaptureLayout() { CaptureSizes(); return _layout; }
    private void CaptureSizes()
    {
        if (_panels[StudioDockRegion.Left].IsVisible && !_floating.ContainsKey(StudioDockRegion.Left)) _layout.LeftWidth = Math.Clamp(_grid.ColumnDefinitions[0].Width.Value, 160, 600);
        if (_panels[StudioDockRegion.Right].IsVisible && !_floating.ContainsKey(StudioDockRegion.Right)) _layout.RightWidth = Math.Clamp(_grid.ColumnDefinitions[4].Width.Value, 180, 600);
        if (_panels[StudioDockRegion.Bottom].IsVisible && !_floating.ContainsKey(StudioDockRegion.Bottom)) _layout.BottomHeight = Math.Clamp(_grid.RowDefinitions[2].Height.Value, 90, 400);
    }
    private void SetVisible(StudioDockRegion region, bool visible)
    {
        if (region == StudioDockRegion.Left) _layout.LeftVisible = visible;
        if (region == StudioDockRegion.Right) _layout.RightVisible = visible;
        if (region == StudioDockRegion.Bottom) _layout.BottomVisible = visible;
    }
    private void UpdateRegions()
    {
        bool left = !_workspaceFocus && _layout.LeftVisible && !_floating.ContainsKey(StudioDockRegion.Left);
        bool right = !_workspaceFocus && _layout.RightVisible && !_floating.ContainsKey(StudioDockRegion.Right);
        bool bottom = !_workspaceFocus && _layout.BottomVisible && !_floating.ContainsKey(StudioDockRegion.Bottom);
        _grid.ColumnDefinitions[0].Width = new GridLength(left ? _layout.LeftWidth : 0);
        _grid.ColumnDefinitions[1].Width = new GridLength(left ? 5 : 0);
        _grid.ColumnDefinitions[4].Width = new GridLength(right ? _layout.RightWidth : 0);
        _grid.ColumnDefinitions[3].Width = new GridLength(right ? 5 : 0);
        _grid.RowDefinitions[2].Height = new GridLength(bottom ? _layout.BottomHeight : 0);
        _grid.RowDefinitions[1].Height = new GridLength(bottom ? 5 : 0);
        foreach ((StudioDockRegion region, Border panel) in _panels) panel.IsVisible = !_workspaceFocus && IsRegionVisible(region);
    }
    private void AddPanel(StudioDockRegion region, string title, Control content, int column, int row, int span = 1)
    {
        DockPanel dock = new();
        StackPanel tools = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
        Button detach = new() { Content = "↗", Padding = new Thickness(6, 2),MinHeight=22,Height=22,FontSize=11 };
        ToolTip.SetTip(detach, "Detach panel");
        detach.Click += (_, _) => Detach(region);
        Button hide = new() { Content = "×", Padding = new Thickness(6, 2),MinHeight=22,Height=22,FontSize=11 };
        ToolTip.SetTip(hide, "Hide panel");
        hide.Click += (_, _) => Hide(region);
        tools.Children.Add(detach);
        tools.Children.Add(hide);
        DockPanel header = new() { Margin = new Thickness(8, 3) };
        DockPanel.SetDock(tools, Dock.Right);
        header.Children.Add(tools);
        header.Children.Add(new TextBlock { Text = title.ToUpperInvariant(), FontSize = 11, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        DockPanel.SetDock(header, Dock.Top);
        dock.Children.Add(header);
        dock.Children.Add(content);
        Border panel = new() { Child = dock, Background = new SolidColorBrush(Color.Parse("#182131")), BorderBrush = new SolidColorBrush(Color.Parse("#2C3A50")), BorderThickness = new Thickness(1) };
        Grid.SetColumn(panel, column);
        Grid.SetRow(panel, row);
        Grid.SetColumnSpan(panel, span);
        _panels.Add(region, panel);
        _grid.Children.Add(panel);
    }
    private static GridSplitter Splitter(int column, int row, bool horizontal)
    {
        GridSplitter splitter = new() { Background = new SolidColorBrush(Color.Parse("#0D1420")), HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
            ResizeDirection = horizontal ? GridResizeDirection.Rows : GridResizeDirection.Columns, ResizeBehavior = GridResizeBehavior.PreviousAndNext };
        Grid.SetColumn(splitter, column);
        Grid.SetRow(splitter, row);
        if (horizontal) Grid.SetColumnSpan(splitter, 5);
        return splitter;
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (Window window in _floating.Values.ToArray()) window.Close();
        _floating.Clear();
    }
}
