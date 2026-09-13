using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MphRead.Cosmetics;
using MphRead.Mods.Launcher.Theme;
using AvaloniaButton = Avalonia.Controls.Button;

namespace MphRead.Mods.Launcher.Gui;

internal sealed record HunterAppearancePresentationContext(
    HunterAppearanceState State,
    Action PreviousSkin,
    Action NextSkin,
    Action PreviousArmorEffect,
    Action NextArmorEffect,
    Action PreviousDeathEffect,
    Action NextDeathEffect,
    bool CanPreviewDeath,
    Action PreviewDeath,
    Action Equip,
    Action ResetPreview);

/// <summary>Pure control composition for one Hunter's local appearance.</summary>
internal static class HunterAppearancePresentation
{
    public static Control Build(HunterAppearancePresentationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        HunterAppearanceState state = context.State;
        var body = Stack(
            Text("Appearance", "prime-heading"),
            Text("Cosmetics are presentation-only and never change Hunter gameplay.",
                "prime-muted"),
            Selector("Skin", SkinName(state), context.PreviousSkin, context.NextSkin),
            Selector("Armor Effect", ArmorName(state), context.PreviousArmorEffect,
                context.NextArmorEffect),
            Selector("Death Effect", DeathName(state), context.PreviousDeathEffect,
                context.NextDeathEffect));

        if (state.Error != null)
            body.Children.Add(Text(state.Error, "prime-status-error"));

        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        AvaloniaButton previewDeath = Button("Preview Death", context.PreviewDeath);
        previewDeath.IsEnabled = context.CanPreviewDeath;
        PrimeAccessibility.SetName(previewDeath, context.CanPreviewDeath
            ? "Preview selected death effect"
            : "Select a cosmetic death effect to preview");
        actions.Children.Add(previewDeath);
        actions.Children.Add(Button("Equip", context.Equip, primary: true));
        if (state.Dirty)
            actions.Children.Add(Button("Cancel", context.ResetPreview, quiet: true));
        body.Children.Add(actions);
        body.Children.Add(Text(state.Dirty
            ? "Previewing changes · choose Equip to save them for this Hunter."
            : "Equipped for this Hunter.", "prime-muted"));
        return PrimeControlFactory.SectionPanel(body);
    }

    internal static string SkinName(HunterAppearanceState state)
        => state.Skins.FirstOrDefault(value => value.Key == state.Preview.SkinKey)
            ?.DisplayName ?? "Base";

    internal static string ArmorName(HunterAppearanceState state)
        => state.ArmorEffects.FirstOrDefault(value =>
            value.Key == state.Preview.ArmorEffectKey)?.DisplayName ?? "None";

    internal static string DeathName(HunterAppearanceState state)
        => state.DeathEffects.FirstOrDefault(value =>
            value.Key == state.Preview.DeathEffectKey)?.DisplayName ?? "Default";

    private static Control Selector(string label, string value, Action previous,
        Action next)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("96,Auto,*,Auto"),
            ColumnSpacing = 8
        };
        TextBlock name = Text(label, "prime-label");
        name.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(name);

        AvaloniaButton back = Button("‹", previous, quiet: true);
        PrimeAccessibility.SetName(back, $"Previous {label}");
        grid.Children.Add(back);
        Grid.SetColumn(back, 1);

        TextBlock selection = Text(value, "prime-body");
        selection.VerticalAlignment = VerticalAlignment.Center;
        selection.TextAlignment = TextAlignment.Center;
        grid.Children.Add(selection);
        Grid.SetColumn(selection, 2);

        AvaloniaButton forward = Button("›", next, quiet: true);
        PrimeAccessibility.SetName(forward, $"Next {label}");
        grid.Children.Add(forward);
        Grid.SetColumn(forward, 3);
        return grid;
    }

    private static StackPanel Stack(params Control[] controls)
    {
        var stack = new StackPanel { Spacing = 10 };
        foreach (Control control in controls) stack.Children.Add(control);
        return stack;
    }

    private static TextBlock Text(string value, string style)
        => new() { Text = value, TextWrapping = TextWrapping.Wrap, Classes = { style } };

    private static AvaloniaButton Button(string label, Action action,
        bool primary = false, bool quiet = false)
        => PrimeControlFactory.Button(label, action, primary, quiet);
}
