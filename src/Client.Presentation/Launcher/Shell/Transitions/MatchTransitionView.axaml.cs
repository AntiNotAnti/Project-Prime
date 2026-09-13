using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MphRead.Mods.Launcher.Theme;

namespace MphRead.Mods.Launcher.Gui;

/// <summary>
/// Branded shell overlay for facts reported by the match lifecycle. Stage
/// changes animate only their presentation and never gate match readiness.
/// </summary>
internal sealed partial class MatchTransitionView : UserControl, IDisposable
{
    private readonly bool _reducedMotion;
    private readonly List<MatchTransitionStage> _observedStages = new();
    private PrimeMotionLease? _stageMotion;
    private bool _disposed;

    internal MatchTransitionView(MatchTransitionState state,
        bool? reducedMotion = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        _reducedMotion = reducedMotion ?? PrimeMotion.ReducedMotion;
        InitializeComponent();
        PrimeAccessibility.SetName(this, "Match transition");
        PrimeAccessibility.SetDescription(this,
            "Current match loading and connection state.");
        PrimeAccessibility.SetName(ReturnToLobbyButton, "Return to lobby");
        ReturnToLobbyButton.Click += ReturnToLobbyClicked;
        Update(state);
    }

    internal event EventHandler? ReturnToLobbyRequested;

    internal MatchTransitionState State { get; private set; } = null!;
    internal IReadOnlyList<MatchTransitionStage> ObservedStages
        => _observedStages.ToArray();
    internal bool FailureVisible => FailurePanel.IsVisible;
    internal PrimeMotionSpec StageMotionSpec => PrimeMotion.Resolve(_reducedMotion,
        PrimeMotionPreset.Fade, PrimeMotion.FastDuration);

    internal void Update(MatchTransitionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (_disposed) return;

        Track(state.Stage);
        State = state;
        StageTitle.Text = state.Stage.Label();
        MapText.Text = Display(state.Map);
        ModeText.Text = Display(state.Mode);
        HunterText.Text = Display(state.Hunter);
        bool failed = state.Stage == MatchTransitionStage.Failed;
        DetailText.Text = state.Detail?.Trim() ?? String.Empty;
        DetailText.IsVisible = !failed && !String.IsNullOrWhiteSpace(state.Detail);
        FailurePanel.IsVisible = failed;
        FailureText.Text = failed
            ? Display(state.Detail, "The match could not continue.")
            : String.Empty;
        RebuildChecklist(failed);

        if (failed)
        {
            // The failure action did not exist in the modal focus snapshot
            // taken while loading. Move ownership to it as soon as it becomes
            // available so keyboard and controller activation cannot remain
            // stranded on the covered shell.
            ReturnToLobbyButton.Focus();
        }

        PrimeAccessibility.SetStatus(this, StageTitle.Text ?? "Match transition",
            failed
                ? PrimeStatusKind.Error : PrimeStatusKind.Info);
        _stageMotion?.Dispose();
        _stageMotion = PrimeMotion.AnimateEntry(StageBody,
            PrimeMotionPreset.Fade, _reducedMotion, PrimeMotion.FastDuration);
    }

    private void Track(MatchTransitionStage stage)
    {
        if (stage == MatchTransitionStage.Failed) return;
        if (stage.StartsWorkflow() && _observedStages.Count != 0
            && _observedStages[^1] != stage)
        {
            _observedStages.Clear();
        }
        if (_observedStages.Count == 0 || _observedStages[^1] != stage)
            _observedStages.Add(stage);
    }

    private void RebuildChecklist(bool failed)
    {
        ChecklistPanel.Children.Clear();
        if (_observedStages.Count == 0)
        {
            ChecklistPanel.Children.Add(BuildStageRow(MatchTransitionStage.Failed,
                completed: false, failed: true));
            return;
        }

        for (int i = 0; i < _observedStages.Count; i++)
        {
            bool current = i == _observedStages.Count - 1;
            ChecklistPanel.Children.Add(BuildStageRow(_observedStages[i],
                completed: !current, failed: failed && current));
        }
    }

    private static Border BuildStageRow(MatchTransitionStage stage,
        bool completed, bool failed)
    {
        IBrush markerBrush = failed ? GuiTheme.ErrorBrush
            : completed ? GuiTheme.SuccessBrush : GuiTheme.BrandBrush;
        string marker = failed ? "×" : completed ? "✓" : "›";
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("24,*"),
            ColumnSpacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };
        row.Children.Add(new TextBlock
        {
            Text = marker,
            Foreground = markerBrush,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        var label = new TextBlock
        {
            Text = stage.Label(),
            Foreground = completed ? GuiTheme.TextDimBrush : markerBrush,
            FontSize = 13,
            FontWeight = completed ? FontWeight.Normal : FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        row.Children.Add(label);
        Grid.SetColumn(label, 1);
        var border = new Border
        {
            Child = row,
            Background = completed ? Brushes.Transparent : GuiTheme.BrandSurfaceBrush,
            BorderBrush = failed ? GuiTheme.ErrorBrush
                : completed ? GuiTheme.GunmetalBrush : GuiTheme.BrandBrush,
            BorderThickness = new Thickness(2, 0, 0, 0),
            Padding = new Thickness(8, 6)
        };
        border.Classes.Add("match-transition-stage");
        PrimeAccessibility.SetStatus(row, $"{stage.Label()}: "
            + (failed ? "failed" : completed ? "complete" : "current"),
            failed ? PrimeStatusKind.Error
                : completed ? PrimeStatusKind.Success : PrimeStatusKind.Info);
        return border;
    }

    private static string Display(string? value, string fallback = "—")
        => String.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private void ReturnToLobbyClicked(object? sender,
        Avalonia.Interactivity.RoutedEventArgs args)
        => ReturnToLobbyRequested?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ReturnToLobbyButton.Click -= ReturnToLobbyClicked;
        _stageMotion?.Dispose();
        _stageMotion = null;
    }
}
