using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace MphRead.Mods.Launcher.Gui;
// Separate label/track/value cells prevent text collisions at every scale.
internal sealed class DecimalSliderRow : UserControl
{
    internal string Label { get; }
    private readonly Slider _slider;
    private readonly NumericUpDown _number;
    private bool _changing;
    public event EventHandler? ValueChanged;
    public float Value
    {
        get => (float)_slider.Value;
        set
        {
            value=Math.Clamp(float.IsFinite(value)?value:0,(float)_slider.Minimum,(float)_slider.Maximum);
            if(Math.Abs(_slider.Value-value)<.00001)return;
            _changing=true;_slider.Value=value;_number.Value=(decimal)value;_changing=false;
            ValueChanged?.Invoke(this,EventArgs.Empty);
        }
    }
    public DecimalSliderRow(string label,float value,float min,float max,float step=.01f,string unit="")
    {
        Label=label; MinWidth=220;Margin=new Thickness(0,3);
        var root=new Grid { RowDefinitions=new RowDefinitions("Auto,34"),ColumnDefinitions=new ColumnDefinitions("*,92") };
        var title=new TextBlock { Text=string.IsNullOrEmpty(unit)?label:label+" ("+unit+")",TextWrapping=Avalonia.Media.TextWrapping.Wrap,Margin=new Thickness(0,0,0,2) };
        Grid.SetColumnSpan(title,2);root.Children.Add(title);
        _slider=new Slider { Minimum=min,Maximum=max,Value=Math.Clamp(value,min,max),SmallChange=step,LargeChange=step*10,MinWidth=90,MaxWidth=210,HorizontalAlignment=HorizontalAlignment.Stretch,Margin=new Thickness(0,0,10,0) };
        _number=new NumericUpDown { Minimum=(decimal)min,Maximum=(decimal)max,Value=(decimal)Math.Clamp(value,min,max),Increment=(decimal)step,FormatString="0.####",ShowButtonSpinner=false,HorizontalAlignment=HorizontalAlignment.Stretch };
        Grid.SetRow(_slider,1);Grid.SetRow(_number,1);Grid.SetColumn(_number,1);root.Children.Add(_slider);root.Children.Add(_number);
        _slider.PropertyChanged+=(_,e)=> { if(e.Property==Slider.ValueProperty&&!_changing){_changing=true;_number.Value=(decimal)_slider.Value;_changing=false;ValueChanged?.Invoke(this,EventArgs.Empty);} };
        _number.ValueChanged+=(_,_)=> { if(!_changing&&_number.Value is decimal n) Value=(float)n; };
        Content=root;
    }
}
