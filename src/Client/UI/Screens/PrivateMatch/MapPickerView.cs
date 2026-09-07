using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using MphRead.Mods.UI.Components;
using MphRead.Mods.UI.Navigation;
using MphRead.Mods.UI.State;
using MphRead.Mods.UI.Theme;

namespace MphRead.Mods.UI.Screens.PrivateMatch;

public sealed class MapPickerView : UserControl, IDisposable
{
    private readonly MapPickerScreenModel _model;
    private readonly WrapPanel _cards = new() { Orientation = Orientation.Horizontal };
    private readonly AsyncStatePresenter _state = new();
    private readonly List<Bitmap> _previews = [];
    private int _loadGeneration;

    public MapPickerView(IMapCatalogController controller)
    {
        _model = new MapPickerScreenModel(controller);
        Content = new StackPanel { Spacing = UiSpacing.Space3, Children = { _state, _cards } };
        _state.Retry += async (_, _) => await LoadAsync();
        AttachedToVisualTree += async (_, _) =>
        {
            if (_model.Status.State is UiLoadState.Idle or UiLoadState.Loading) await LoadAsync();
        };
        DetachedFromVisualTree += (_, _) => Dispose();
    }

    public event EventHandler<UiMapEntry>? SelectionChanged;
    public event Action<string, Control>? FocusTargetAdded;
    public UiMapEntry? Selected => _model.Selected;
    public int CachedThumbnailCount => _model.CachedThumbnailCount;

    public async System.Threading.Tasks.Task LoadAsync()
    {
        int generation = ++_loadGeneration;
        DisposePreviews();
        await _model.LoadAsync();
        if (generation != _loadGeneration) return;
        _state.Show(_model.Status);
        _cards.Children.Clear();
        int index = 0;
        foreach (UiMapEntry map in _model.Maps)
        {
            var card = new MapCard
            {
                Title = map.Name,
                Detail = $"{map.Mode} · {map.RecommendedPlayers}"
                    + (map.IsCustom ? $" · Custom{(map.Author is null ? "" : $" by {map.Author}")}" : " · Retail"),
                Width = 240,
                Margin = new Avalonia.Thickness(0, 0, UiSpacing.Space3, UiSpacing.Space3)
            };
            card.Click += (_, _) =>
            {
                _model.Select(map);
                SelectionChanged?.Invoke(this, map);
            };
            _cards.Children.Add(card);
            FocusTargetAdded?.Invoke($"map:{index++}", card);
            _ = MarkThumbnailAsync(card, map, generation);
        }
    }

    public void Dispose()
    {
        _loadGeneration++;
        DisposePreviews();
        _model.Dispose();
    }

    private async System.Threading.Tasks.Task MarkThumbnailAsync(MapCard card, UiMapEntry map,
        int generation)
    {
        try
        {
            byte[]? thumbnail = await _model.ThumbnailAsync(map);
            if (thumbnail is not { Length: > 0 } || generation != _loadGeneration) return;
            using var stream = new MemoryStream(thumbnail, writable: false);
            var bitmap = new Bitmap(stream);
            if (generation != _loadGeneration)
            {
                bitmap.Dispose();
                return;
            }
            _previews.Add(bitmap);
            card.ShowPreview(bitmap);
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            // The labeled card remains usable when a preview cannot be loaded.
        }
    }

    private void DisposePreviews()
    {
        foreach (Bitmap preview in _previews) preview.Dispose();
        _previews.Clear();
    }
}
