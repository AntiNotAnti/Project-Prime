using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Interactivity;

namespace MphRead.Mods.Launcher.Gui;

internal sealed partial class MapStudioScreen
{
    private readonly List<MapViewport> _views = new();
    private void ReleaseViews()
    {
        foreach (var view in _views) view.DetachDocument();
        _views.Clear();
    }
    private void ActivateViewport(object? sender, RoutedEventArgs e)
    {
        if (sender is not MapViewport view || _viewport == view) return;
        if (_viewport is { } previous)
        {
            view.Tool=previous.Tool;view.ElementMode=previous.ElementMode;view.Axes=previous.Axes;
            view.Snap=previous.Snap;view.AngleSnap=previous.AngleSnap;view.ScaleSnap=previous.ScaleSnap;
            view.PivotMode=previous.PivotMode;view.LocalAxes=previous.LocalAxes;view.CursorPivot=previous.CursorPivot;
            view.PlacementMode=previous.PlacementMode;
        }
        _viewport=view;ShowInspectorPage(_inspectorPage,false);
    }
    internal void ToggleFourViews()
    {
        if (_document == null || _viewport == null) return;
        if (_views.Count > 1)
        {
            foreach (var view in _views) if (view != _viewport) view.DetachDocument();
            _views.Clear(); _views.Add(_viewport);
            // Remove the active viewport from its pane before reparenting it.
            if (_viewport.Parent is DockPanel pane) pane.Children.Remove(_viewport);
            _viewportHost.Children.Clear(); _viewportHost.Children.Add(_viewport);
            return;
        }
        var primary = _viewport;
        _viewportHost.Children.Clear();
        var grid = new Grid { ColumnDefinitions=new("*,5,*"), RowDefinitions=new("*,5,*") };
        string[] names={"Perspective","Top","Front","Side"};
        for(int i=0;i<4;i++)
        {
            var view=i==0?primary:new MapViewport(_document);
            if(i>0)
            {
                _views.Add(view);
                view.SelectionChanged+=()=>{RefreshHierarchy();ShowInspectorPage(_inspectorPage,false);};
                view.MaterialPicked+=hit=>{_pickedMaterialHit=hit;_inspectorPage="Materials";MaterialInspector();};
            }
            view.SetView(names[i]);view.FrameAll();view.Wireframe=i>0;
            view.GotFocus -= ActivateViewport;
            view.GotFocus += ActivateViewport;
            var pane=new DockPanel();
            var label=Text(names[i].ToUpperInvariant());label.Margin=new Thickness(6,3);
            DockPanel.SetDock(label,Dock.Top);pane.Children.Add(label);pane.Children.Add(view);
            Grid.SetColumn(pane,(i%2)*2);Grid.SetRow(pane,(i/2)*2);grid.Children.Add(pane);
        }
        var vertical=new GridSplitter { Width=5,HorizontalAlignment=HorizontalAlignment.Stretch,Background=PrimeTheme.BorderBrush };
        Grid.SetColumn(vertical,1);Grid.SetRowSpan(vertical,3);grid.Children.Add(vertical);
        var horizontal=new GridSplitter { Height=5,VerticalAlignment=VerticalAlignment.Stretch,HorizontalAlignment=HorizontalAlignment.Stretch,ResizeDirection=GridResizeDirection.Rows,Background=PrimeTheme.BorderBrush };
        Grid.SetRow(horizontal,1);Grid.SetColumnSpan(horizontal,3);grid.Children.Add(horizontal);
        _viewportHost.Children.Add(grid);
        if(_document.Project.Definition.Import!=null||_document.Project.Definition.NativeRoom!=null)_=PreviewImport();
        _status.Text="Four views · click a viewport to edit in it · drag dividers to resize · Four views returns to the active view.";
    }
}
