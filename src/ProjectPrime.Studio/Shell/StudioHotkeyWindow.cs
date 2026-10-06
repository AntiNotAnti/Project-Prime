using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using ProjectPrime.Studio.Settings;

namespace ProjectPrime.Studio.Shell;

public sealed class StudioHotkeyWindow : Window
{
    public StudioHotkeyWindow(StudioSettings settings, Action persist)
    {
        Title="Studio keyboard shortcuts"; Width=640; Height=650; MinWidth=480; MinHeight=350;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var root=new DockPanel { Margin=new Thickness(18), LastChildFill=true };
        var footer=new StackPanel { Orientation=Orientation.Horizontal,Spacing=10,HorizontalAlignment=HorizontalAlignment.Right };
        var error=new TextBlock { TextWrapping=Avalonia.Media.TextWrapping.Wrap,Margin=new Thickness(0,8) };
        var rows=new StackPanel { Spacing=8 };
        var fields=new Dictionary<StudioCommand,TextBox>();
        foreach (StudioCommand command in Enum.GetValues<StudioCommand>())
        {
            var row=new Grid { ColumnDefinitions=new("*,220"),ColumnSpacing=12 };
            row.Children.Add(new TextBlock { Text=StudioGlobalSearchWindow.CommandLabel(command),VerticalAlignment=VerticalAlignment.Center });
            var input=new TextBox { Text=StudioHotkeys.Binding(settings,command),PlaceholderText="No shortcut" };
            Grid.SetColumn(input,1); row.Children.Add(input); rows.Children.Add(row); fields.Add(command,input);
        }
        var save=new Button { Content="Save shortcuts" };
        save.Click+=(_,_)=>
        {
            var bindings=fields.ToDictionary(item=>item.Key.ToString(),item=>item.Value.Text?.Trim()??"",StringComparer.Ordinal);
            if (StudioHotkeys.Validate(bindings) is { } problem) { error.Text=problem; return; }
            settings.CustomHotkeys=bindings; persist(); Close();
        };
        var defaults=new Button { Content="Restore defaults" };
        defaults.Click+=(_,_)=> { foreach(var item in fields) item.Value.Text=StudioHotkeys.Defaults.TryGetValue(item.Key,out var value)?value:""; error.Text=""; };
        var cancel=new Button { Content="Cancel" }; cancel.Click+=(_,_)=>Close();
        footer.Children.Add(defaults);footer.Children.Add(cancel);footer.Children.Add(save);
        DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        DockPanel.SetDock(error,Dock.Bottom);root.Children.Add(error);
        root.Children.Add(new ScrollViewer { Content=rows });Content=root;
    }
}
