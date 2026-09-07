using System;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Layout;
using MphRead.Mods.UI.Components;
using MphRead.Mods.UI.State;
using MphRead.Mods.UI.Theme;

namespace MphRead.Mods.UI.Screens.HunterLicense;

public sealed class HunterLicenseScreenView : ScreenViewBase
{
    private readonly HunterLicenseScreenModel _model;
    private readonly StackPanel _results = new() { Spacing = UiSpacing.Space3 };
    private readonly AsyncStatePresenter _state = new();

    public HunterLicenseScreenView(IHunterLicenseController controller)
        : base("Hunter License", "Verified career history and attributed performance.", "license:tab:overview")
    {
        _model = new HunterLicenseScreenModel(controller);
        var tabs = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (HunterLicenseTab tab in Enum.GetValues<HunterLicenseTab>())
        {
            HunterLicenseTab selected = tab;
            var button = Register($"license:tab:{tab.ToString().ToLowerInvariant()}", new SecondaryButton
            {
                Content = tab.ToString(), AccessibleName = $"{tab} tab",
                Margin = new Avalonia.Thickness(0, 0, UiSpacing.Space2, UiSpacing.Space2)
            });
            button.Click += async (_, _) => await SelectTabAsync(selected);
            tabs.Children.Add(button);
        }
        _state.Retry += async (_, _) => await SelectTabAsync(_model.Tab);
        Body.Content = new StackPanel { Spacing = UiSpacing.Space4, Children = { tabs, _state, _results } };
        AttachedToVisualTree += async (_, _) =>
        {
            if (_model.Status.State is UiLoadState.Idle or UiLoadState.Loading)
                await SelectTabAsync(HunterLicenseTab.Overview);
        };
        DetachedFromVisualTree += (_, _) => _model.Dispose();
    }

    private async System.Threading.Tasks.Task SelectTabAsync(HunterLicenseTab tab)
    {
        await _model.SelectTabAsync(tab, ScreenCancellation);
        _state.Show(_model.Status);
        RebuildResults();
    }

    private void RebuildResults()
    {
        _results.Children.Clear();
        if (_model.Status.State != UiLoadState.Ready) return;
        if (_model.Tab == HunterLicenseTab.Overview && _model.Overview is { } overview)
        {
            _results.Children.Add(Text($"{overview.DisplayName} · {overview.RankTitle}",
                size: UiTypography.TextHeading));
            _results.Children.Add(Text($"{overview.RankingPoints} RP"
                + (overview.NextThreshold is int next ? $" · {next - overview.RankingPoints} to next rank" : " · Maximum rank")));
            _results.Children.Add(Text(
                $"{overview.Wins} wins · {overview.Losses} losses · {overview.WinRatio:P1} win rate\n"
                + $"{overview.KillDeathRatio:0.00} K/D · {overview.PlayTime.TotalHours:0.0} hours\n"
                + $"Favorite Hunter: {overview.FavoriteHunter ?? "Unavailable"}\n"
                + $"Favorite map: {overview.FavoriteMap ?? "Unavailable"} · weapon: {overview.FavoriteWeapon ?? "Unavailable"} · mode: {overview.FavoriteMode ?? "Unavailable"}\n"
                + $"Longest kill streak: {overview.LongestKillStreak} · win streak: {overview.LongestWinStreak}"));
            return;
        }
        if (_model.Tab == HunterLicenseTab.Matches)
        {
            int index = 0;
            foreach (HunterLicenseMatch match in _model.Matches)
            {
                _results.Children.Add(Register($"license:match:{index++}", new FocusCard
                {
                    Content = new StackPanel
                    {
                        Spacing = UiSpacing.Space1,
                        Children =
                        {
                            Text($"{match.Result} · {match.Map} · {match.Mode}"),
                            Text($"{match.EndedAt:d} · {match.Score} · {match.RatingStatus}", muted: true)
                        }
                    }
                }));
            }
            if (_model.CanLoadMore)
            {
                var more = Register("license:matches:more", new SecondaryButton
                {
                    Content = "Load More", AccessibleName = "Load more match history"
                });
                more.Click += async (_, _) =>
                {
                    await _model.LoadMoreAsync(ScreenCancellation);
                    _state.Show(_model.Status);
                    RebuildResults();
                };
                _results.Children.Add(more);
            }
            return;
        }
        int statIndex = 0;
        foreach (HunterLicenseStat stat in _model.Stats)
        {
            _results.Children.Add(Register($"license:stat:{statIndex++}", new FocusCard
            {
                Content = new StackPanel
                {
                    Spacing = UiSpacing.Space1,
                    Children = { Text($"{stat.Label}: {stat.Value}"), Text(stat.Detail ?? stat.Key, muted: true) }
                }
            }));
        }
    }
}
