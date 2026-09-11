using System;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MphRead.Mods.Accounts;
using MphRead.Mods.Launcher.Theme;

namespace MphRead.Mods.Launcher.Gui;

/// <summary>
/// A lightweight history row. Only its outcome label and leading edge carry
/// semantic color; the rest of the row remains a neutral reading surface.
/// </summary>
internal sealed class PrimeHistoryRow : Border
{
    public PrimeHistoryRow(MatchHistoryEntry match)
    {
        ArgumentNullException.ThrowIfNull(match);
        Presentation = PrimeMatchHistoryPresentation.From(match);
        Outcome = match.Outcome;
        Classes.Add("prime-history-row");

        string semanticClass = OutcomeClass(match.Outcome);
        Classes.Add(semanticClass);
        BorderThickness = new Thickness(3, 0, 0, 1);
        Padding = new Thickness(12);

        var outcome = Text(Presentation.Outcome.ToUpperInvariant(), "prime-label");
        outcome.Classes.Add(semanticClass);
        PrimeAccessibility.SetName(outcome, $"Outcome: {Presentation.Outcome}");

        var rating = Text(Presentation.RatingLine, "prime-label");
        rating.HorizontalAlignment = HorizontalAlignment.Right;

        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        heading.Children.Add(outcome);
        heading.Children.Add(rating);
        Grid.SetColumn(rating, 1);

        var content = new StackPanel { Spacing = 4 };
        content.Children.Add(heading);
        content.Children.Add(Text(Presentation.Mission, "prime-body"));
        content.Children.Add(Text(Presentation.CombatLine, "prime-muted"));
        content.Children.Add(Text(Presentation.DateLine, "prime-label"));
        Child = content;

        string description = $"{Presentation.Outcome}. {Presentation.Mission}. "
            + $"{Presentation.CombatLine}. {Presentation.RatingLine}. {Presentation.DateLine}.";
        AutomationProperties.SetName(this, description);
    }

    public CareerOutcome Outcome { get; }
    public PrimeMatchHistoryPresentation Presentation { get; }

    internal static string OutcomeClass(CareerOutcome outcome)
        => outcome switch
        {
            CareerOutcome.FinishedWin => "prime-status-success",
            CareerOutcome.FinishedLoss => "prime-status-error",
            CareerOutcome.Tie or CareerOutcome.Forfeit
                or CareerOutcome.DepartedGraceExpired => "prime-status-warning",
            CareerOutcome.NoContest => "prime-status-muted",
            _ => "prime-status-muted"
        };

    private static TextBlock Text(string value, string style)
        => new() { Text = value, TextWrapping = TextWrapping.Wrap, Classes = { style } };
}
