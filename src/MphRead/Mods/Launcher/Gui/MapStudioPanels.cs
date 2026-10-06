using System;
using Avalonia;
using Avalonia.Controls;

namespace MphRead.Mods.Launcher.Gui;

/// <summary>Reusable panel topology. Stateful editor commands stay with their canonical document owner.</summary>
internal abstract class MapWorkspacePanel : UserControl
{
    protected MapWorkspacePanel(Control heading, Control content)
    {
        var body = new Grid { RowDefinitions = new("Auto,*"), RowSpacing = 8 };
        body.Children.Add(heading);
        Grid.SetRow(content,1);
        body.Children.Add(content);
        Content = new PrimePanel(body,raised:true) { Padding = new Thickness(8) };
    }
}
internal sealed class MapHierarchyPanel(Control heading, Control content) : MapWorkspacePanel(heading,content);
internal sealed class MapInspectorPanel(Control heading, Control content) : MapWorkspacePanel(heading,content);
internal sealed class MapViewportPanel(Control heading, Control content) : MapWorkspacePanel(heading,content);

internal sealed class MapAssetBrowserPanel : UserControl, IDisposable
{
    public bool IsDisposed {get; private set;}
    public event Action? Released;
    public MapAssetBrowserPanel(Control content) { Content=content; }
    public void Dispose() { if(IsDisposed)return; IsDisposed=true; Released?.Invoke(); Released=null; Content=null; }
}
