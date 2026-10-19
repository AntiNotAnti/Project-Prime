using System;
using System.Collections.Generic;
using System.Linq;

namespace MphRead.Mods.Launcher.RmlUi.Core;

// Keep the existing Community command index stable; visual tab order is separate.
public enum RmlUiArenaCategory { Mph = 0, Community = 1, Echoes = 2, Halo = 3, Quake = 4 }
public readonly record struct RmlUiArenaChoice(string Room, RmlUiArenaCategory Category,
    bool Selectable = true, string Hint = "INSTALLED PACKAGE");
public readonly record struct RmlUiArenaPreview(string Image, string Status);

/// <summary>
/// Pure six-card native map picker. Names must originate from the established
/// built-in list or locally installed, verified community packages, never from
/// a directory hint or an unverified remote map name.
/// </summary>
public sealed class RmlUiMapPickerModel
{
    public const int PageSize = 6;
    private string[][] _categories;
    private readonly Dictionary<string, RmlUiArenaChoice> _details = new(StringComparer.OrdinalIgnoreCase);
    private bool _loaded;
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
            Normalize(installedCommunity).Where(x => !existing.Contains(x)).ToArray(),
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()
        };
        foreach (string room in _categories[0]) _details[room] = new(room, RmlUiArenaCategory.Mph, true, "MPH ARENA");
        foreach (string room in _categories[1]) _details[room] = new(room, RmlUiArenaCategory.Community);
    }

    // Initial async publication only fills previously empty custom categories.
    // Never reorder existing actionable cards while a click can be queued.
    public void LoadInstalled(IReadOnlyList<RmlUiArenaChoice> choices)
    {
        if (_loaded) return;
        _loaded = true;
        var builtIn = new HashSet<string>(_categories[0], StringComparer.OrdinalIgnoreCase);
        var valid = choices.Where(c => !string.IsNullOrWhiteSpace(c.Room) && c.Room.Length <= 256
                && !builtIn.Contains(c.Room) && c.Category is >= RmlUiArenaCategory.Community and <= RmlUiArenaCategory.Quake)
            .DistinctBy(c => c.Room, StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var choice in valid) _details[choice.Room] = choice;
        _categories[1] = Normalize(valid.Select(c => c.Room));
        for (int category = 2; category < 5; category++)
            _categories[category] = Normalize(valid.Where(c => (int)c.Category == category).Select(c => c.Room));
        Page = Math.Min(Page, Pages - 1);
    }

    public RmlUiArenaChoice? Detail(int slot)
        => Choice(slot) is { } room && _details.TryGetValue(room, out var choice) ? choice : null;

    /// <summary>Display grouping, never a provenance or installation permission check.
    /// Older packages have no origin field, so retain their familiar arena titles.
    /// Community still contains every installed custom package.</summary>
    public static RmlUiArenaCategory Group(string room, string? displayName, string? description, bool quakeImport)
    {
        string title = room + " " + displayName;
        string notes = description is { Length: > 2048 } ? description[..2048] : description ?? "";
        bool Has(string text, string term)
        {
            int start = 0;
            while ((start = text.IndexOf(term, start, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                int end = start + term.Length;
                if ((start == 0 || !char.IsLetterOrDigit(text[start - 1]))
                    && (end == text.Length || !char.IsLetterOrDigit(text[end]))) return true;
                start = end;
            }
            return false;
        }
        if (Has(title, "Echoes") || Has(notes, "Echoes") || Has(notes, "Metroid Prime 2")
            || new[] { "Sidehopper Station", "Shooting Gallery", "Crossfire Chaos", "Spider Complex", "Pipeline", "Spires" }.Any(n => Has(title, n)))
            return RmlUiArenaCategory.Echoes;
        if (Has(title, "Halo") || Has(notes, "Halo")
            || new[] { "Guardian", "Haven", "Midship", "Lockout", "Blood Gulch", "Bloodgulch", "Valhalla" }.Any(n => Has(title, n)))
            return RmlUiArenaCategory.Halo;
        return quakeImport ? RmlUiArenaCategory.Quake : RmlUiArenaCategory.Community;
    }

    private static string[] Normalize(IEnumerable<string> rooms) =>
        rooms.Where(x => !string.IsNullOrWhiteSpace(x) && x.Length <= 256)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();

    public bool SelectCategory(int category)
    {
        if (category is < 0 or > 4) return false;
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
