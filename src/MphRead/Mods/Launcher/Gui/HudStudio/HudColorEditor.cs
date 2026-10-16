using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;

namespace MphRead.Mods.Launcher.Gui;

internal sealed class HudColorEditor : StackPanel
{
    internal static readonly string[] Swatches = {
        "#FFFFFF","#D9D9D9","#999999","#555555","#222222","#000000",
        "#FF0000","#FF6B6B","#FF8800","#FFC857","#FFFF00","#FFF1A8",
        "#00FF00","#80FF80","#00AA55","#00FFFF","#80FFFF","#00AACC",
        "#0088FF","#80BFFF","#0000FF","#8888FF","#8800FF","#C080FF",
        "#FF00FF","#FF80FF","#FF0088","#FF99BB","#56B4E9","#D55E00" };
    private readonly TextBox _hex;
    private readonly Border _sample;
    private readonly TextBlock _message = new() { FontSize=11,TextWrapping=TextWrapping.Wrap };
    private readonly Action<string> _changed;
    private string _value;
    internal HudColorEditor(string label,string value,Action<string> changed)
    {
        Spacing=4; _changed=changed; _value=value;
        Children.Add(new TextBlock { Text=label+" color" });
        var row=new StackPanel { Orientation=Orientation.Horizontal,Spacing=6 };
        _sample=new Border { Width=28,Height=28,Background=Brush.Parse(value),BorderBrush=Brushes.Gray,BorderThickness=new Thickness(1) };
        _hex=new TextBox { Text=value,PlaceholderText="#RRGGBB",Width=116 };
        _hex.LostFocus+=(_,_)=>Apply(_hex.Text);
        _hex.KeyDown+=(_,e)=> { if(e.Key==Key.Enter) { Apply(_hex.Text);e.Handled=true; } };
        row.Children.Add(_sample);row.Children.Add(_hex);Children.Add(row);
        var swatches=new WrapPanel { MaxWidth=252 };
        foreach(string hex in Swatches)
        {
            var button=new Avalonia.Controls.Button { Width=32,Height=32,MinWidth=0,MinHeight=0,Padding=new Thickness(2),Margin=new Thickness(1),
                Content=new Border { Background=Brush.Parse(hex),Width=24,Height=24,BorderBrush=Brushes.Gray,BorderThickness=new Thickness(1) } };
            Avalonia.Automation.AutomationProperties.SetName(button,"Choose "+hex);
            ToolTip.SetTip(button,hex);button.Click+=(_,_)=>Apply(hex);swatches.Children.Add(button);
        }
        Children.Add(new Expander { Header="Choose from palette",Content=swatches,IsExpanded=false });
        var clipboard=new StackPanel { Orientation=Orientation.Horizontal,Spacing=6 };
        clipboard.Children.Add(new PrimeButton("Copy",async()=>
        {
            try {
#if MPHREAD_SHELL
                WriteSystemClipboard(_value); _message.Text="Color copied.";
#else
                if(TopLevel.GetTopLevel(this)?.Clipboard is {} cb) { await cb.SetTextAsync(_value);_message.Text="Color copied."; }
                else _message.Text="System clipboard is unavailable in this view.";
#endif
                await System.Threading.Tasks.Task.CompletedTask; }
            catch(Exception) { _message.Text="Could not access the system clipboard."; }
        }));
        clipboard.Children.Add(new PrimeButton("Paste",async()=>
        {
            try {
#if MPHREAD_SHELL
                Apply(ReadSystemClipboard());
#else
                if(TopLevel.GetTopLevel(this)?.Clipboard is {} cb) Apply(await cb.TryGetTextAsync());
                else _message.Text="System clipboard is unavailable in this view.";
#endif
                await System.Threading.Tasks.Task.CompletedTask; }
            catch(Exception) { _message.Text="Could not access the system clipboard."; }
        }));
        Children.Add(clipboard);Children.Add(_message);
    }
#if MPHREAD_SHELL
    // The in-game Avalonia host is headless; its clipboard is not the OS clipboard.
    private static unsafe string? ReadSystemClipboard() => OpenTK.Windowing.GraphicsLibraryFramework.GLFW.GetClipboardString(null);
    private static unsafe void WriteSystemClipboard(string text) => OpenTK.Windowing.GraphicsLibraryFramework.GLFW.SetClipboardString(null,text);
#endif
    internal static bool TryNormalize(string? text,out string color)
    {
        string value=(text??"").Trim().TrimStart('#');color="";
        if(value.Length==3) value=$"{value[0]}{value[0]}{value[1]}{value[1]}{value[2]}{value[2]}";
        if(value.Length!=6 || !uint.TryParse(value,NumberStyles.HexNumber,CultureInfo.InvariantCulture,out _)) return false;
        color="#"+value.ToUpperInvariant();return true;
    }
    internal bool Apply(string? text)
    {
        if(!TryNormalize(text,out string color)) { _message.Text="Use #RGB or #RRGGBB. The previous color is unchanged.";return false; }
        _hex.Text=color;_sample.Background=Brush.Parse(color);_message.Text="";
        if(color!=_value) { _value=color;_changed(color); } return true;
    }
}
