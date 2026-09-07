using System;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using MphRead.Mods.UI.Components;
using MphRead.Mods.UI.Navigation;
using MphRead.Mods.UI.State;
using MphRead.Mods.UI.Theme;

namespace MphRead.Mods.UI.Screens.PostMatch;

public sealed class PostMatchScreenView : ScreenViewBase
{
    private readonly UiRouter _router;
    private readonly IPostMatchScreenController _controller;
    private readonly PostMatchScreenModel _model;
    private readonly Grid _results = new();
    private readonly StackPanel _summary = new() { Spacing = UiSpacing.Space2 };
    private readonly StackPanel _rows = new() { Spacing = UiSpacing.Space2 };
    private readonly WrapPanel _actions = new();
    private readonly AsyncStatePresenter _state = new();
    private readonly ErrorBanner _feedback = new() { IsVisible = false };
    private bool _pending;

    public PostMatchScreenView(UiRouter router, IPostMatchScreenController controller)
        : base("Post-match Results", "Authoritative results and rating status from the completed match.",
            "postmatch:row:0")
    {
        _router = router;
        _controller = controller;
        _model = new PostMatchScreenModel(controller);
        _results.ColumnDefinitions = new ColumnDefinitions("320,*");
        _results.ColumnSpacing = UiSpacing.Space4;
        _results.Children.Add(_summary);
        _results.Children.Add(_rows);
        Grid.SetColumn(_rows, 1);
        BuildActions();
        Body.Content = new StackPanel
        {
            Spacing = UiSpacing.Space4,
            Children = { _state, _results, _actions, _feedback }
        };
        Rebuild();
        _controller.Changed += ControllerChanged;
        AttachedToVisualTree += async (_, _) => await RefreshRatingAsync();
    }

    protected override void OnLayoutModeChanged(UiLayoutMode mode)
    {
        bool compact = PostMatchScreenModel.ResultColumns(mode) == 1;
        _results.ColumnDefinitions = compact ? new ColumnDefinitions("*") : new ColumnDefinitions("320,*");
        _results.RowDefinitions = compact ? new RowDefinitions("Auto,Auto") : new RowDefinitions("Auto");
        Grid.SetColumn(_rows, compact ? 0 : 1);
        Grid.SetRow(_rows, compact ? 1 : 0);
        _rows.Margin = compact
            ? new Thickness(0, UiSpacing.Space4, 0, 0)
            : new Thickness(UiSpacing.Space4, 0, 0, 0);
    }

    private void BuildActions()
    {
        AddAction("postmatch:continue", "Continue", PostMatchAction.Continue, UiRoute.Lobby, primary: true);
        AddAction("postmatch:rematch", "Rematch", PostMatchAction.Rematch);
        AddAction("postmatch:next", "Next Map", PostMatchAction.NextMap);
        AddAction("postmatch:lobby", "Lobby", PostMatchAction.ReturnToLobby, UiRoute.Lobby);
        AddAction("postmatch:leave", "Leave", PostMatchAction.LeaveServer, UiRoute.Home);
        AddAction("postmatch:license", "Hunter License", PostMatchAction.HunterLicense,
            UiRoute.HunterLicense);
    }

    private void AddAction(string key, string label, PostMatchAction action,
        UiRoute? destination = null, bool primary = false)
    {
        UiActionButton button = primary ? new PrimaryButton() : new SecondaryButton();
        button.Content = label;
        button.AccessibleName = label;
        button.Margin = new Thickness(0, 0, UiSpacing.Space2, UiSpacing.Space2);
        button.Click += async (_, _) =>
        {
            if (_pending) return;
            _pending = true;
            SetActionsEnabled(false);
            UiActionResult result;
            try
            {
                result = await _model.InvokeAsync(action, ScreenCancellation);
            }
            catch (OperationCanceledException)
            {
                result = UiActionResult.Failure("Post-match action canceled.");
            }
            catch (Exception error)
            {
                result = UiActionResult.Failure(AsyncScreenState.FriendlyFailure(error,
                    "The post-match action"));
            }
            _pending = false;
            SetActionsEnabled(true);
            if (result.Succeeded && destination is { } route)
                _router.Navigate(route, sourceFocusKey: key);
            else if (!result.Succeeded)
            {
                _feedback.Message = result.Message;
                _feedback.IsVisible = true;
            }
        };
        _actions.Children.Add(Register(key, button));
    }

    private void SetActionsEnabled(bool enabled)
    {
        foreach (Control control in _actions.Children) control.IsEnabled = enabled;
    }

    private void Rebuild()
    {
        _state.Show(_model.Status);
        UiPostMatchSummary? summary = _model.Summary;
        _summary.Children.Clear();
        _rows.Children.Clear();
        if (summary is null)
        {
            _results.IsVisible = false;
            _actions.IsVisible = false;
            return;
        }
        _results.IsVisible = true;
        _actions.IsVisible = true;
        _summary.Children.Add(Text("MATCH", size: UiTypography.TextHeading));
        _summary.Children.Add(Text($"{summary.Map}\n{summary.Mode}\nDuration {summary.Duration:mm\\:ss}\n"
            + $"Ended: {summary.EndReason}"));
        _summary.Children.Add(Text(RatingText(summary), muted: summary.RatingState
            is RatingUpdateState.Pending or RatingUpdateState.Unavailable));

        _rows.Children.Add(Text("RESULTS", size: UiTypography.TextHeading));
        int rowIndex = 0;
        foreach (UiPostMatchRow row in summary.Rows.Take(PostMatchScreenModel.MaximumVisibleRows))
        {
            string bot = row.Bot ? " · Bot" : string.Empty;
            var result = new PlayerRow
            {
                Title = $"{row.Placement}. {row.Name}{bot}",
                Detail = $"{row.Hunter} · Team {row.Team + 1} · {row.Points} pts · "
                    + $"{row.Kills} K / {row.Deaths} D / {row.Assists} A · {row.Damage} damage · "
                    + $"{row.Headshots} headshots · Objectives {row.ObjectivePrimary}/"
                    + $"{row.ObjectiveSecondary}/{row.ObjectiveTertiary}"
            };
            AutomationProperties.SetName(result, $"Place {row.Placement}, {row.Name}, {row.Points} points");
            _rows.Children.Add(Register($"postmatch:row:{rowIndex++}", result));
        }
    }

    private async System.Threading.Tasks.Task RefreshRatingAsync()
    {
        if (_model.Summary?.RatingState != RatingUpdateState.Pending) return;
        Rebuild();
        await _model.RefreshRatingAsync(ScreenCancellation);
        Rebuild();
    }

    private void ControllerChanged()
        => Dispatcher.UIThread.Post(async () =>
        {
            _model.Refresh();
            Rebuild();
            await RefreshRatingAsync();
        });

    private static string RatingText(UiPostMatchSummary summary) => summary.RatingState switch
    {
        RatingUpdateState.Pending => "Rating update pending…",
        RatingUpdateState.Updated => $"Rating updated: {summary.RatingPoints ?? 0} "
            + $"({(summary.RatingDelta >= 0 ? "+" : string.Empty)}{summary.RatingDelta ?? 0})",
        RatingUpdateState.NotEligible => "Rating not eligible for this match.",
        _ => "Rating update unavailable."
    };
}
