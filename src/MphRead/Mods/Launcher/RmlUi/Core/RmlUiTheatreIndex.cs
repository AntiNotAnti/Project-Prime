using System;
using System.Collections.Generic;
using System.Linq;

namespace MphRead.Mods.Launcher.RmlUi.Core;

/// <summary>Metadata from the existing replay library, never a native-supplied path.</summary>
public readonly record struct RmlUiReplayEntry(
    string Path, string Title, string Detail, string Room,
    long RecordedTicks, long Bytes, long LastWriteTicks,
    bool Favorite, bool Clip, bool Recoverable, bool Playable);

/// <summary>
/// Bounded search, filtering, paging and selection over one immutable library
/// snapshot. It owns no replay reader, file handle, decoder or background pump.
/// </summary>
public sealed class RmlUiTheatreIndex
{
    public const int PageSize = 8;
    private RmlUiReplayEntry[] _all = Array.Empty<RmlUiReplayEntry>();
    private RmlUiReplayEntry[] _filtered = Array.Empty<RmlUiReplayEntry>();
    private string _selected = "";
    private string _search = "";
    private int _filter, _sort, _page;
    public bool Launched { get; private set; }
    public int TotalCount => _all.Length;
    public int Count => _filtered.Length;
    public int Page => _page;
    public int Pages => Math.Max(1, (Count + PageSize - 1) / PageSize);
    public bool CanPrevious => _page > 0;
    public bool CanNext => _page + 1 < Pages;
    public string Search => _search;
    public string FilterLabel => new[] { "ALL REPLAYS", "FULL MATCHES", "CLIPS", "FAVORITES",
        "RECENT", "RECOVERABLE" }[_filter];
    public string SortLabel => new[] { "NEWEST FIRST", "OLDEST FIRST",
        "NAME A–Z", "LARGEST FIRST" }[_sort];

    public RmlUiReplayEntry? Selected
    {
        get
        {
            foreach (var row in _all)
                if (String.Equals(row.Path, _selected, StringComparison.OrdinalIgnoreCase))
                    return row;
            return null;
        }
    }

    public RmlUiReplayEntry? Entry(int slot)
    {
        if (slot < 0 || slot >= PageSize) return null;
        int index = checked(_page * PageSize + slot);
        return index >= 0 && index < _filtered.Length ? _filtered[index] : null;
    }

    public void Refresh(IEnumerable<RmlUiReplayEntry> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        // Directories can contain many recordings; one native page is capped
        // to a transparent, fixed memory budget rather than unbounded rows.
        _all = rows.Where(x => !String.IsNullOrWhiteSpace(x.Path)
                && x.Title != null && x.Bytes >= 0 && x.LastWriteTicks >= 0)
            .GroupBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .OrderByDescending(x => x.RecordedTicks)
            .Take(8192).ToArray();
        if (Selected == null) _selected = "";
        Rebuild();
    }

    public bool SearchFor(string text)
    {
        if (text == null || text.Length > 512) return false;
        _search = text.Trim();
        _page = 0;
        Rebuild();
        return true;
    }

    public bool NextFilter()
    {
        _filter = (_filter + 1) % 6;
        _page = 0;
        Rebuild();
        return true;
    }

    public bool NextSort()
    {
        _sort = (_sort + 1) % 4;
        _page = 0;
        Rebuild();
        return true;
    }

    public bool Turn(int direction)
    {
        if (direction != -1 && direction != 1) return false;
        if (direction == -1 ? !CanPrevious : !CanNext) return false;
        _page += direction;
        return true;
    }

    public bool Select(int slot)
    {
        RmlUiReplayEntry? row = Entry(slot);
        if (!row.HasValue || Launched) return false;
        _selected = row.Value.Path;
        return true;
    }

    public void MarkLaunched() => Launched = true;

    private void Rebuild()
    {
        IEnumerable<RmlUiReplayEntry> rows = _all;
        if (_search.Length > 0)
        {
            string term = _search;
            rows = rows.Where(x => x.Title.Contains(term, StringComparison.OrdinalIgnoreCase)
                || x.Room.Contains(term, StringComparison.OrdinalIgnoreCase)
                || x.Detail.Contains(term, StringComparison.OrdinalIgnoreCase));
        }
        long recentTicks = DateTime.Now.AddDays(-7).Ticks;
        rows = _filter switch
        {
            1 => rows.Where(x => !x.Clip),
            2 => rows.Where(x => x.Clip),
            3 => rows.Where(x => x.Favorite),
            4 => rows.Where(x => x.RecordedTicks >= recentTicks),
            5 => rows.Where(x => x.Recoverable),
            _ => rows
        };
        rows = _sort switch
        {
            1 => rows.OrderBy(x => x.RecordedTicks),
            2 => rows.OrderBy(x => x.Title, StringComparer.OrdinalIgnoreCase),
            3 => rows.OrderByDescending(x => x.Bytes),
            _ => rows.OrderByDescending(x => x.RecordedTicks)
        };
        _filtered = rows.ToArray();
        _page = Math.Clamp(_page, 0, Pages - 1);
    }

    public bool SelectedUnchanged(RmlUiReplayEntry expected)
        => Selected is { } row && row.Path == expected.Path
            && row.Bytes == expected.Bytes
            && row.LastWriteTicks == expected.LastWriteTicks
            && !Launched;
}
