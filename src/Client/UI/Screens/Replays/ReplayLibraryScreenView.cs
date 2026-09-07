using System;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using MphRead.Mods.UI.Components;
using MphRead.Mods.UI.State;
using MphRead.Mods.UI.Theme;

namespace MphRead.Mods.UI.Screens.Replays;

public sealed class ReplayLibraryScreenView : ScreenViewBase
{
    private readonly ReplayLibraryScreenModel _model;
    private readonly IReplayLibraryController _controller;
    private readonly StackPanel _list = new() { Spacing = UiSpacing.Space2 };
    private readonly StackPanel _actions = new() { Spacing = UiSpacing.Space2 };
    private readonly AsyncStatePresenter _state = new();
    private readonly Grid _layout;

    public ReplayLibraryScreenView(IReplayLibraryController controller,
        IReplayDeleteConfirmation confirmation)
        : base("Replays", "Review local Replay 2.0 recordings and their compatibility.", "replays:refresh")
    {
        _controller = controller;
        _model = new ReplayLibraryScreenModel(controller, confirmation);
        var refresh = Register("replays:refresh", new SecondaryButton
        {
            Content = "Refresh", AccessibleName = "Refresh replay library"
        });
        refresh.Click += async (_, _) => await LoadAsync();
        var import = Register("replays:import", new SecondaryButton
        {
            Content = "Import", AccessibleName = "Import replay"
        });
        import.Click += async (_, _) => await RunAsync(controller.ImportAsync);

        _layout = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,*"), ColumnSpacing = UiSpacing.Space4 };
        _layout.Children.Add(_list);
        _layout.Children.Add(_actions);
        Grid.SetColumn(_actions, 1);
        Body.Content = new StackPanel
        {
            Spacing = UiSpacing.Space3,
            Children =
            {
                new WrapPanel { Children = { refresh, import } },
                _state,
                _layout
            }
        };
        _state.Retry += async (_, _) => await LoadAsync();
        AttachedToVisualTree += async (_, _) =>
        {
            if (_model.Status.State is UiLoadState.Idle or UiLoadState.Loading) await LoadAsync();
        };
        DetachedFromVisualTree += (_, _) => _model.Dispose();
    }

    protected override void OnLayoutModeChanged(UiLayoutMode mode)
    {
        bool compact = mode == UiLayoutMode.Compact;
        _layout.ColumnDefinitions = compact ? new ColumnDefinitions("*") : new ColumnDefinitions("2*,*");
        _layout.RowDefinitions = compact ? new RowDefinitions("Auto,Auto") : new RowDefinitions("Auto");
        Grid.SetColumn(_actions, compact ? 0 : 1);
        Grid.SetRow(_actions, compact ? 1 : 0);
        _actions.Margin = compact ? new Thickness(0, UiSpacing.Space4, 0, 0) : default;
    }

    private async System.Threading.Tasks.Task LoadAsync()
    {
        await _model.LoadAsync(ScreenCancellation);
        _state.Show(_model.Status);
        Rebuild();
    }

    private void Rebuild()
    {
        _list.Children.Clear();
        _actions.Children.Clear();
        int index = 0;
        foreach (ReplayMetadata replay in _model.Replays.Take(200))
        {
            string warning = !replay.Compatible ? " · Incompatible"
                : replay.RecoveredTail ? " · Recovered tail" : string.Empty;
            var card = Register($"replays:item:{index++}", new FocusCard
            {
                Content = new StackPanel
                {
                    Spacing = UiSpacing.Space1,
                    Children =
                    {
                        Text($"{replay.Map} · {replay.Mode}"),
                        Text($"{replay.RecordedAt:g} · {replay.Duration:mm\\:ss} · {replay.Players} players"
                            + (replay.Result is null ? "" : $" · {replay.Result}") + warning, muted: true)
                    }
                },
                IsEnabled = replay.Compatible
            });
            card.Click += (_, _) => { _model.Select(replay); BuildActions(replay); };
            _list.Children.Add(card);
        }
        if (_model.Selected is { } selected) BuildActions(selected);
    }

    private void BuildActions(ReplayMetadata replay)
    {
        _actions.Children.Clear();
        _actions.Children.Add(Text(replay.FileName, size: UiTypography.TextHeading));
        AddAction("Play", replay, () => _controller.PlayAsync(replay, ScreenCancellation),
            primary: true, enabled: replay.Compatible);
        AddAction("Export / Share", replay, () => _controller.ExportAsync(replay, ScreenCancellation));
        AddAction("Delete", replay, () => _model.DeleteSelectedAsync(ScreenCancellation));
    }

    private void AddAction(string label, ReplayMetadata replay,
        Func<System.Threading.Tasks.Task<UiActionResult>> action, bool primary = false)
        => AddAction(label, replay, action, primary, enabled: true);

    private void AddAction(string label, ReplayMetadata replay,
        Func<System.Threading.Tasks.Task<UiActionResult>> action, bool primary, bool enabled)
    {
        UiActionButton button = primary ? new PrimaryButton() : new SecondaryButton();
        button.Content = label;
        button.AccessibleName = $"{label} {replay.FileName}";
        button.IsEnabled = enabled;
        Register($"replays:{label.ToLowerInvariant().Replace(' ', '-')}", button);
        button.Click += async (_, _) =>
        {
            try
            {
                UiActionResult result = await action();
                if (!result.Succeeded && result.Message != "Delete canceled.")
                {
                    _model.Status.Failed(result.Message);
                    _state.Show(_model.Status);
                }
                if (result.Succeeded && label == "Delete") Rebuild();
            }
            catch (Exception error)
            {
                _model.Status.Failed(AsyncScreenState.FriendlyFailure(error, label));
                _state.Show(_model.Status);
            }
        };
        _actions.Children.Add(button);
    }

    private async System.Threading.Tasks.Task RunAsync(
        Func<CancellationToken, System.Threading.Tasks.Task<UiActionResult>> action)
    {
        try
        {
            UiActionResult result = await action(ScreenCancellation);
            if (!result.Succeeded)
            {
                _model.Status.Failed(result.Message);
                _state.Show(_model.Status);
            }
            else await LoadAsync();
        }
        catch (Exception error)
        {
            _model.Status.Failed(AsyncScreenState.FriendlyFailure(error, "Replay import"));
            _state.Show(_model.Status);
        }
    }
}
