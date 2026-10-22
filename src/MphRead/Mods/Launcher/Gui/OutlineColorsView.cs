#if MPHREAD_AVALONIA
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using MphRead.Mods.Render;

namespace MphRead.Mods.Launcher.Gui;

/// <summary>Local-only two-column color editor; no network identities are changed.</summary>
internal sealed class OutlineColorsView : UserControl
{
    private readonly Action<OutlineColorSettings?> _close;
    private readonly Action<OutlineColorSettings>? _preview;
    private readonly StackPanel _options = new() { Spacing = 6, Margin = new Thickness(6) };
    private readonly StackPanel _editor = new() { Spacing = 8, Margin = new Thickness(12) };
    private readonly TextBlock _countdown = new() { Text = "", Margin = new Thickness(6) };
    private OutlineColorSettings _draft;
    private bool _teams = true;
    private int _selected;

    internal OutlineColorsView(OutlineColorSettings original,
        Action<OutlineColorSettings?> close, Action<OutlineColorSettings>? preview)
    {
        _draft = original.Copy();
        _close = close;
        _preview = preview;
        SetValue(ControllerNav.ModalProperty, true);

        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Margin = new Thickness(12) };
        var toolbar = new WrapPanel();
        toolbar.Children.Add(new TextBlock { Text = "OUTLINE COLORS", FontSize = 22, Margin = new Thickness(6) });
        toolbar.Children.Add(new PrimeButton("Use in settings", () => _close(_draft.Copy())));
        toolbar.Children.Add(new PrimeButton("Preview 60s", () => _preview?.Invoke(_draft.Copy())));
        toolbar.Children.Add(new PrimeButton("Cancel", () => _close(null)));
        toolbar.Children.Add(_countdown);
        root.Children.Add(toolbar);

        var content = new Grid { ColumnDefinitions = new ColumnDefinitions("230,*") };
        Grid.SetRow(content, 1);
        root.Children.Add(content);
        var left = new StackPanel { Spacing = 8 };
        left.Children.Add(new PrimeButton("Teams 1-4", () => { _teams = true; _selected = 0; Refresh(); }));
        left.Children.Add(new PrimeButton("FFA players 1-8", () => { _teams = false; _selected = 0; Refresh(); }));
        left.Children.Add(_options);
        content.Children.Add(new ScrollViewer { Content = left });
        var right = new ScrollViewer { Content = _editor };
        Grid.SetColumn(right, 1);
        content.Children.Add(right);
        Content = root;
        Refresh();
    }

    internal void SetCountdown(int seconds) =>
        _countdown.Text = seconds > 0 ? $"Temporary preview: {seconds}s remaining" : "";

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { _close(null); e.Handled = true; }
        base.OnKeyDown(e);
    }

    private string Title(int index) => (_teams ? "Team " : "FFA player ") + (index + 1);

    private void Refresh()
    {
        _options.Children.Clear();
        _editor.Children.Clear();
        string[] palette = _teams ? _draft.Teams : _draft.Players;
        for (int i = 0; i < palette.Length; i++)
        {
            int slot = i;
            var button = new PrimeButton(Title(i) + "  " + palette[i],
                () => { _selected = slot; Refresh(); }) { Selected = i == _selected };
            ControllerNav.Identify(button, "outline." + (_teams ? "team." : "player.") + i);
            _options.Children.Add(button);
        }
        int selected = _selected;
        bool teams = _teams;
        string title = Title(selected);
        _editor.Children.Add(new TextBlock { Text = title, FontSize = 20 });
        _editor.Children.Add(new TextBlock
        {
            Text = teams ? "Team-color outline in team matches."
                : "Fixed server slot, not the hunter, team, or scoreboard rank.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });
        _editor.Children.Add(new HudColorEditor(title, palette[selected], hex =>
        {
            (teams ? _draft.Teams : _draft.Players)[selected] = hex;
            if (_teams == teams && selected < _options.Children.Count)
                ((PrimeButton)_options.Children[selected]).Label = title + "  " + hex;
        }));
        _editor.Children.Add(new PrimeButton("Reset this color", () =>
        {
            var defaults = new OutlineColorSettings();
            (teams ? _draft.Teams : _draft.Players)[selected] =
                (teams ? defaults.Teams : defaults.Players)[selected];
            Refresh();
        }));
        _editor.Children.Add(new PrimeButton(teams ? "Reset all team colors" : "Reset all FFA colors", () =>
        {
            var defaults = new OutlineColorSettings();
            if (teams) _draft.Teams = defaults.Teams;
            else _draft.Players = defaults.Players;
            Refresh();
        }));
    }
}
#endif
