using System;
using System.Collections.Generic;
using System.Linq;
using MphRead.Mods.Input;

namespace MphRead.Mods.Launcher.Gui;

/// <summary>
/// Cyclic LB/RB navigation over visible sections. Section movement has its
/// own ordering and never falls back to spatial geometry; this prevents a
/// bumper press from unexpectedly moving a row focus.
/// </summary>
public static class PrimeSectionNavigation
{
    /// <summary>
    /// Maps normalized LB/RB presses to section movement. Directional buttons
    /// are intentionally not accepted here; callers must route those through
    /// <see cref="PrimeSpatialNavigation"/> instead.
    /// </summary>
    public static bool TryGetDirection(GamepadButtons button,
        out PrimeNavigationSectionDirection direction)
    {
        if (button == GamepadButtons.LeftBumper)
        {
            direction = PrimeNavigationSectionDirection.Previous;
            return true;
        }
        if (button == GamepadButtons.RightBumper)
        {
            direction = PrimeNavigationSectionDirection.Next;
            return true;
        }
        direction = default;
        return false;
    }

    public static PrimeNavigationSection? Select(string? currentSectionId,
        IEnumerable<PrimeNavigationSection> sections,
        PrimeNavigationSectionDirection direction)
    {
        ArgumentNullException.ThrowIfNull(sections);
        PrimeNavigationSection[] eligible = sections
            .Where(section => section is not null && section.IsEligible)
            .OrderBy(section => section.Order)
            .ThenBy(section => section.SectionId, StringComparer.Ordinal)
            .ToArray();
        if (eligible.Length == 0) return null;

        int current = Array.FindIndex(eligible, section =>
            string.Equals(section.SectionId, currentSectionId,
                StringComparison.Ordinal));
        if (current < 0)
            return direction == PrimeNavigationSectionDirection.Next
                ? eligible[0] : eligible[^1];

        int step = direction == PrimeNavigationSectionDirection.Next ? 1 : -1;
        return eligible[(current + step + eligible.Length) % eligible.Length];
    }

    public static PrimeSectionNavigationResult Move(string? currentSectionId,
        IEnumerable<PrimeNavigationSection> sections,
        PrimeNavigationSectionDirection direction)
    {
        PrimeNavigationSection? target = Select(currentSectionId, sections, direction);
        if (target is null || string.Equals(target.SectionId, currentSectionId,
                StringComparison.Ordinal))
            return PrimeSectionNavigationResult.None(currentSectionId);
        return PrimeSectionNavigationResult.Moved(currentSectionId, target,
            target.DefaultFocusId);
    }
}
