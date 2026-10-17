using System;
using System.Collections.Generic;
using System.Linq;

namespace MphRead.Mods.Launcher.RmlUi.Core;

/// <summary>
/// Pure six-card native map picker. Names must originate from the established
/// built-in list or locally installed, verified community packages, never from
/// a directory hint or an unverified remote map name.
/// </summary>
public sealed class RmlUiMapPickerModel
{
    public const int PageSize = 6;
    private readonly string[][] _categories;
    public int Category { get; private set; }
    public int Page { get; private set; }
    public int Count => _categories[Category].Length;
    public int Pages => Math.Max(1, (Count + PageSize - 1) / PageSize);
    public bool HasPrevious => Page > 0;
    public bool HasNext => Page + 1 < Pages;
    public IReadOnlyList<string> Current => _categories[Category];

    public RmlUiMapPickerModel(IEnumerable<string> builtIn, IEnumerable<string> installedCommunity)
    {
        ArgumentNullException.ThrowIfNull(builtIn);
        ArgumentNullException.ThrowIfNull(installedCommunity);
        string[] vanilla = Normalize(builtIn);
        var existing = new HashSet<string>(vanilla, StringComparer.OrdinalIgnoreCase);
        _categories = new[]
        {
            vanilla,
            Normalize(installedCommunity).Where(x => !existing.Contains(x)).ToArray()
        };
    }

    private static string[] Normalize(IEnumerable<string> rooms) =>
        rooms.Where(x => !string.IsNullOrWhiteSpace(x) && x.Length <= 256)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();

    public bool SelectCategory(int category)
    {
        if (category is < 0 or > 1) return false;
        Category = category;
        Page = 0;
        return true;
    }

    public bool NextPage()
    {
        if (!HasNext) return false;
        Page++;
        return true;
    }

    public bool PreviousPage()
    {
        if (!HasPrevious) return false;
        Page--;
        return true;
    }

    public string? Choice(int slot)
    {
        if (slot is < 0 or >= PageSize) return null;
        int index = checked(Page * PageSize + slot);
        return index < Count ? Current[index] : null;
    }
}
