using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using ProjectPrime.Server.Shared;
using MphRead.Mods.Launcher.Theme;
using AvaloniaButton = Avalonia.Controls.Button;

namespace MphRead.Mods.Launcher.Gui;

/// <summary>
/// Bridges the live Avalonia shell tree to the pure controller-navigation
/// core. The adapter owns no focus state: it measures one shell coordinate
/// root, assigns stable names, and applies the core's focus/scroll result to
/// the current controls.
/// </summary>
internal sealed class PrimeShellNavigationAdapter
{
    internal const string CoordinateRoot = "shell";

    private static readonly HashSet<string> SettingsSectionIds = new(
        new[] { "Gameplay", "Controls", "Graphics", "Audio", "System",
            "Network", "Accessibility", "About" },
        StringComparer.OrdinalIgnoreCase);

    private readonly Control _root;

    public PrimeShellNavigationAdapter(Control root)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
    }

    /// <summary>
    /// Measures the currently visible shell surface. Modal content owns the
    /// snapshot while it is visible; otherwise the active page and shell
    /// chrome are measured together so focus can cross between them only when
    /// their geometry and layer permit it.
    /// </summary>
    public IReadOnlyList<PrimeShellNavigationTarget> Capture(
        Control pageRoot, IReadOnlyList<Control> chromeRoots,
        Control overlayRoot, bool overlayVisible, PrimeRoute route,
        string? subsection = null, string? modalId = null,
        Control? openEditor = null)
    {
        ArgumentNullException.ThrowIfNull(pageRoot);
        ArgumentNullException.ThrowIfNull(chromeRoots);
        ArgumentNullException.ThrowIfNull(overlayRoot);

        var raw = new List<RawTarget>();
        if (overlayVisible)
        {
            AddRoot(raw, overlayRoot, PrimeNavigationLayer.Modal, route,
                subsection, modalId ?? "shell-overlay", openEditor);
        }
        else
        {
            foreach (Control chromeRoot in chromeRoots)
            {
                if (chromeRoot != null)
                    AddRoot(raw, chromeRoot, PrimeNavigationLayer.ShellChrome,
                        route, subsection, modalId, openEditor);
            }
            AddRoot(raw, pageRoot, PrimeNavigationLayer.ActivePage, route,
                subsection, modalId, openEditor);
        }

        // A semantic name is preferred, but duplicate labels are common in
        // overlays (for example several "Close" actions). Assign duplicate
        // ordinals in structural order, not visual-tree enumeration order.
        var grouped = raw.GroupBy(target => target.SemanticKey,
            StringComparer.Ordinal);
        var ids = new Dictionary<RawTarget, string>();
        foreach (IGrouping<string, RawTarget> group in grouped)
        {
            RawTarget[] ordered = group
                .OrderBy(target => target.StructuralKey, StringComparer.Ordinal)
                .ToArray();
            for (int i = 0; i < ordered.Length; i++)
            {
                string suffix = ordered.Length == 1 ? "" : $"#{i + 1}";
                ids[ordered[i]] = $"{ordered[i].SemanticKey}{suffix}";
            }
        }

        var result = new List<PrimeShellNavigationTarget>(raw.Count);
        foreach (RawTarget target in raw)
        {
            if (!ids.TryGetValue(target, out string? focusId))
                continue;
            PrimeNavigationCandidate candidate = new(focusId, target.Bounds,
                target.Layer, target.Priority, target.Control.IsEffectivelyEnabled,
                target.Control.IsEffectivelyVisible, target.Control.Focusable,
                target.IsOffscreen, target.ScrollHost is not null,
                target.ScrollHostId, target.SectionId, target.ModalId,
                target.NavigationGroup);
            result.Add(new PrimeShellNavigationTarget(target.Control, candidate));
        }
        return result;
    }

    /// <summary>Returns the stable ID corresponding to a live focus object.</summary>
    public static string? FindFocusedId(
        IReadOnlyList<PrimeShellNavigationTarget> targets, object? focused)
    {
        if (focused is not Control control)
            return null;
        return targets.FirstOrDefault(target =>
            ReferenceEquals(target.Control, control))?.FocusId;
    }

    /// <summary>
    /// Applies a navigation result and always asks the owning scroll host to
    /// reveal the target before focus. Avalonia's BringIntoView is the native
    /// scroll path; no pointer or synthetic input is generated.
    /// </summary>
    public static bool Apply(
        IReadOnlyList<PrimeShellNavigationTarget> targets,
        PrimeNavigationResult result)
    {
        if (!result.Moved || result.FocusId is not { } focusId)
            return false;
        PrimeShellNavigationTarget? target = targets.FirstOrDefault(candidate =>
            String.Equals(candidate.FocusId, focusId, StringComparison.Ordinal));
        if (target is null)
            return false;

        if (result.ShouldScrollIntoView || target.Candidate.CanScrollIntoView)
            target.Control.BringIntoView();
        return target.Control.Focus(NavigationMethod.Directional);
    }

    public static bool FocusById(
        IReadOnlyList<PrimeShellNavigationTarget> targets, string focusId)
    {
        if (String.IsNullOrWhiteSpace(focusId))
            return false;
        PrimeShellNavigationTarget? target = targets.FirstOrDefault(candidate =>
            String.Equals(candidate.FocusId, focusId, StringComparison.Ordinal));
        if (target is null)
            return false;
        if (target.Candidate.CanScrollIntoView)
            target.Control.BringIntoView();
        return target.Control.Focus(NavigationMethod.Directional);
    }

    /// <summary>Finds the currently selected settings category from its rail.</summary>
    public static string? SelectedSettingsSection(
        IEnumerable<PrimeShellNavigationTarget> targets)
        => targets
            .Where(target => target.Candidate.SectionId is not null
                && target.Control is MenuEntry menu && menu.Selected)
            .Select(target => target.Candidate.SectionId)
            .FirstOrDefault(section => section is not null);

    /// <summary>Whether LB/RB can move major sections on this route.</summary>
    public static IReadOnlyList<PrimeNavigationSection> SectionsFor(
        PrimeRoute route, IEnumerable<PrimeShellNavigationTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        string[] ids = targets
            .Select(target => target.Candidate.SectionId)
            .Where(section => section is not null)
            .Select(section => section!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(section => SectionOrder(route, section))
            .ThenBy(section => section, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return ids.Select((id, index) => new PrimeNavigationSection(id,
            SectionOrder(route, id), DefaultSectionFocus(id, targets))).ToArray();
    }

    /// <summary>
    /// Applies the conservative, platform-neutral safe-area fallback to shell
    /// chrome. Android can provide real insets later; this does not infer a
    /// device cutout or platform-specific pixel value.
    /// </summary>
    public static void ApplySafeArea(Border header, Border mobileFooter,
        bool mobile)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(mobileFooter);
        // No platform inset is inferred here. Android can pass measured
        // insets through a future adapter; zero means the documented
        // conservative fallback. Desktop explicitly resets mobile padding.
        header.Padding = PrimeLayoutMetrics.ResolveHeaderPadding(mobile, default);
        mobileFooter.Padding = mobile
            ? PrimeLayoutMetrics.ResolveMobileFooterPadding(default)
            : new Thickness(8, 4);
    }

    /// <summary>Every shell chrome action gets the shared 44-DIP hit target.</summary>
    public static void ApplyTouchTarget(Control control, bool primary = false)
    {
        ArgumentNullException.ThrowIfNull(control);
        double minimum = primary
            ? PrimeLayoutMetrics.PreferredPrimaryActionMinimumDip
            : PrimeLayoutMetrics.MinimumTouchTargetDip;
        control.MinHeight = Math.Max(control.MinHeight, minimum);
    }

    /// <summary>
    /// Identity exit is destructive whenever a Node snapshot still contains a
    /// lobby. The snapshot may be retained while disconnected, so checking
    /// only the live transport would silently abandon recoverable membership.
    /// </summary>
    public static bool RequiresIdentityExitConfirmation(LobbySnapshot? lobby)
        => lobby is not null;

    public static bool RequiresIdentityExitConfirmation(LobbySnapshot? lobby,
        NodeMatchHandoff? handoff)
        => lobby is not null || handoff is not null;

    private void AddRoot(List<RawTarget> targets, Control root,
        PrimeNavigationLayer layer, PrimeRoute route, string? subsection,
        string? modalId, Control? openEditor)
    {
        foreach (Control control in root.GetVisualDescendants().OfType<Control>())
        {
            if (!IsFocusableLeaf(control))
                continue;
            PrimeNavigationBounds? bounds = Measure(control,
                out ScrollViewer? scrollHost, out bool offscreen);
            if (bounds is null)
                continue;

            PrimeNavigationLayer targetLayer = layer;
            if (openEditor is not null && ReferenceEquals(control, openEditor))
                targetLayer = PrimeNavigationLayer.OpenEditor;

            string? section = SectionFor(route, control);
            string semantic = SemanticKey(control, route);
            string structural = StructuralKey(control);
            targets.Add(new RawTarget(control, bounds.Value, targetLayer,
                PriorityFor(control), offscreen, scrollHost,
                scrollHost is null ? null : StructuralKey(scrollHost), section,
                modalId, targetLayer == PrimeNavigationLayer.Modal
                    ? "modal" : route.ToString(), semantic, structural));
        }
    }

    private static bool IsFocusableLeaf(Control control)
    {
        if (!control.Focusable || !control.IsEffectivelyVisible
            || !control.IsEffectivelyEnabled)
            return false;

        // UserControl/ContentControl shells frequently set Focusable so they
        // can receive keyboard focus when empty. If they contain a focusable
        // child, the child is the actionable target and the shell itself would
        // otherwise become an oversized duplicate candidate.
        if (control is UserControl or ContentControl
            && control.GetVisualDescendants().OfType<Control>().Any(child =>
                child.Focusable && child.IsEffectivelyVisible
                && child.IsEffectivelyEnabled))
            return false;
        return control.Bounds.Width > 0 && control.Bounds.Height > 0;
    }

    private PrimeNavigationBounds? Measure(Control control,
        out ScrollViewer? scrollHost, out bool offscreen)
    {
        scrollHost = FindScrollHost(control);
        offscreen = false;
        Point? origin = control.TranslatePoint(default, _root);
        if (origin is null)
            return null;

        var rootBounds = new Rect(origin.Value, control.Bounds.Size);
        if (scrollHost is not null)
        {
            Point? viewportOrigin = scrollHost.TranslatePoint(default, _root);
            if (viewportOrigin is not null)
            {
                Rect viewport = new(viewportOrigin.Value, scrollHost.Bounds.Size);
                offscreen = rootBounds.Right <= viewport.Left
                    || rootBounds.Left >= viewport.Right
                    || rootBounds.Bottom <= viewport.Top
                    || rootBounds.Top >= viewport.Bottom;
            }
        }
        return PrimeNavigationBounds.FromRoot(rootBounds, CoordinateRoot);
    }

    private static ScrollViewer? FindScrollHost(Control control)
    {
        Visual? visual = control.GetVisualParent();
        while (visual is not null)
        {
            if (visual is ScrollViewer scrollViewer)
                return scrollViewer;
            visual = visual.GetVisualParent();
        }
        return null;
    }

    private static string SemanticKey(Control control, PrimeRoute route)
    {
        string? name = AutomationProperties.GetName(control);
        if (!String.IsNullOrWhiteSpace(name))
            return $"a11y:{Normalize(name)}";
        if (!String.IsNullOrWhiteSpace(control.Name))
            return $"name:{Normalize(control.Name)}";
        if (control is MenuEntry menu && !String.IsNullOrWhiteSpace(menu.Title))
            return $"menu:{Normalize(menu.Title)}";
        if (control is AvaloniaButton button && button.Content is string content
            && !String.IsNullOrWhiteSpace(content))
            return $"button:{Normalize(content)}";
        if (control is ComboBox combo && combo.Tag is ControllerComboSelector selector)
            return $"combo:{selector}";
        return $"control:{route}:{control.GetType().Name}";
    }

    private static string? SectionFor(PrimeRoute route, Control control)
    {
        string? text = control switch
        {
            MenuEntry menu => menu.Title,
            AvaloniaButton button when button.Content is string content => content,
            _ => null
        };
        if (String.IsNullOrWhiteSpace(text))
            return null;
        if (route == PrimeRoute.Hunter
            && Enum.TryParse<HunterSection>(text, ignoreCase: true,
                out HunterSection hunterSection))
            return hunterSection.ToString();
        return route == PrimeRoute.Settings && SettingsSectionIds.Contains(text)
            ? text.Trim() : null;
    }

    private static int SectionOrder(PrimeRoute route, string section)
    {
        if (route == PrimeRoute.Hunter
            && Enum.TryParse<HunterSection>(section, true,
                out HunterSection hunterSection))
            return (int)hunterSection;
        if (route == PrimeRoute.Settings)
        {
            string[] order = ["Gameplay", "Controls", "Graphics", "Audio",
                "System", "Network", "Accessibility", "About"];
            int index = Array.FindIndex(order, candidate =>
                String.Equals(candidate, section, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) return index;
        }
        return Int32.MaxValue;
    }

    private static string? DefaultSectionFocus(string section,
        IEnumerable<PrimeShellNavigationTarget> targets)
        => targets
            .Where(target => String.Equals(target.Candidate.SectionId, section,
                StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(target => target.Candidate.Priority)
            .ThenBy(target => target.FocusId, StringComparer.Ordinal)
            .Select(target => target.FocusId)
            .FirstOrDefault();

    private static int PriorityFor(Control control)
        => control switch
        {
            PrimeButton => 20,
            MenuEntry menu when menu.Selected => 10,
            AvaloniaButton => 5,
            _ => 0
        };

    private static string StructuralKey(Visual visual)
    {
        var segments = new List<string>();
        Visual? current = visual;
        while (current is not null)
        {
            Visual? parent = current.GetVisualParent();
            int index = 0;
            if (parent is not null)
            {
                foreach (Visual sibling in parent.GetVisualChildren())
                {
                    if (ReferenceEquals(sibling, current))
                        break;
                    index++;
                }
            }
            string name = current is Control control
                && !String.IsNullOrWhiteSpace(control.Name)
                ? control.Name! : current.GetType().Name;
            segments.Add($"{name}[{index}]");
            current = parent;
        }
        segments.Reverse();
        return String.Join('/', segments);
    }

    private static string Normalize(string value)
        => String.Join('-', value.Trim().Split((char[]?)null,
            StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

    private sealed record RawTarget(Control Control,
        PrimeNavigationBounds Bounds, PrimeNavigationLayer Layer, int Priority,
        bool IsOffscreen, ScrollViewer? ScrollHost, string? ScrollHostId,
        string? SectionId, string? ModalId, string NavigationGroup,
        string SemanticKey, string StructuralKey);
}

internal sealed record PrimeShellNavigationTarget(Control Control,
    PrimeNavigationCandidate Candidate)
{
    public string FocusId => Candidate.FocusId;
}
