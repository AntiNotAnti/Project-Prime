#if MPHREAD_AVALONIA
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace MphRead.Mods.Launcher.Gui;

/// <summary>
/// Label, short slider and exact editable number in separate cells. Shared by
/// settings drafts so fractional input does not quantize to integer sliders.
/// </summary>
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
            value = Math.Clamp(float.IsFinite(value) ? value : 1,
                (float)_slider.Minimum, (float)_slider.Maximum);
            if (Math.Abs(_slider.Value - value) < .00001) return;
            _changing = true;
            _slider.Value = value;
            _number.Value = (decimal)value;
            _changing = false;
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public DecimalSliderRow(string label, float value, float min, float max,
        float step = .01f, string unit = "")
    {
        Label = label;
        MinWidth = 220;
        Margin = new Thickness(0, 3);
        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,34"),
            ColumnDefinitions = new ColumnDefinitions("*,92")
        };
        var title = new TextBlock
        {
            Text = string.IsNullOrEmpty(unit) ? label : label + " (" + unit + ")",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };
        Grid.SetColumnSpan(title, 2);
        grid.Children.Add(title);
        float initial = Math.Clamp(float.IsFinite(value) ? value : 1, min, max);
        _slider = new Slider
        {
            Minimum = min, Maximum = max, Value = initial,
            SmallChange = step, LargeChange = step * 10,
            MinWidth = 90, MaxWidth = 210,
            Margin = new Thickness(0, 0, 10, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _number = new NumericUpDown
        {
            Minimum = (decimal)min, Maximum = (decimal)max,
            Value = (decimal)initial, Increment = (decimal)step,
            FormatString = "0.####", ShowButtonSpinner = false,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        Grid.SetRow(_slider, 1); Grid.SetRow(_number, 1);
        Grid.SetColumn(_number, 1);
        grid.Children.Add(_slider);
        grid.Children.Add(_number);
        _slider.PropertyChanged += (_, e) =>
        {
            if (e.Property != Slider.ValueProperty || _changing) return;
            _changing = true;
            _number.Value = (decimal)_slider.Value;
            _changing = false;
            ValueChanged?.Invoke(this, EventArgs.Empty);
        };
        _number.ValueChanged += (_, _) =>
        {
            if (!_changing && _number.Value is decimal value)
                Value = (float)value;
        };
        Content = grid;
    }
}
#endif
