using System;
using System.Linq;
using Avalonia;

namespace MphRead.Mods.Launcher.Gui;

/// <summary>
/// Directions understood by shell spatial navigation. These are deliberately
/// separate from the signed adjustment values used by <see
/// cref="IControllerNavigable"/> controls.
/// </summary>
public enum PrimeNavigationDirection
{
    Up,
    Right,
    Down,
    Left
}

/// <summary>
/// Focus layers used by the shell. A higher layer owns navigation whenever it
/// is exclusive (modal or an open editor), which keeps those controls from
/// leaking into the page or shell chrome. With no exclusive layer, page and
/// chrome candidates intentionally share one normal spatial surface.
/// </summary>
public enum PrimeNavigationLayer
{
    ShellChrome = 0,
    ActivePage = 1,
    OpenEditor = 2,
    /// <summary>Alias useful to callers that name the concrete control.</summary>
    OpenCombo = OpenEditor,
    Modal = 3
}

public static class PrimeNavigationLayerPriority
{
    public const int ShellChrome = 0;
    public const int ActivePage = 100;
    public const int OpenEditor = 200;
    public const int Modal = 300;

    public static int For(PrimeNavigationLayer layer) => layer switch
    {
        PrimeNavigationLayer.ShellChrome => ShellChrome,
        PrimeNavigationLayer.ActivePage => ActivePage,
        PrimeNavigationLayer.OpenEditor => OpenEditor,
        PrimeNavigationLayer.Modal => Modal,
        _ => throw new ArgumentOutOfRangeException(nameof(layer), layer, null)
    };
}

/// <summary>LB/RB section movement is intentionally not directional focus travel.</summary>
public enum PrimeNavigationSectionDirection
{
    Previous,
    Next
}

/// <summary>
/// Stable key used for per-route and per-subsection focus memory. Modal keys
/// are part of the scope so closing one modal cannot restore a different
/// overlay's focus.
/// </summary>
public readonly record struct PrimeFocusScope
{
    public PrimeFocusScope(string route, string? subsection = null, string? modal = null)
    {
        if (string.IsNullOrWhiteSpace(route))
            throw new ArgumentException("A focus scope needs a stable route key.", nameof(route));
        if (subsection is not null && string.IsNullOrWhiteSpace(subsection))
            throw new ArgumentException("A subsection key cannot be empty.", nameof(subsection));
        if (modal is not null && string.IsNullOrWhiteSpace(modal))
            throw new ArgumentException("A modal key cannot be empty.", nameof(modal));

        Route = route;
        Subsection = subsection;
        Modal = modal;
    }

    public PrimeFocusScope(PrimeRoute route, string? subsection = null, string? modal = null)
        : this(route.ToString(), subsection, modal)
    {
    }

    public string Route { get; }
    public string? Subsection { get; }
    public string? Modal { get; }

    public bool IsModal => Modal is not null;

    public PrimeFocusScope WithSubsection(string? subsection)
        => new(Route, subsection, Modal);

    public PrimeFocusScope WithModal(string? modal)
        => new(Route, Subsection, modal);

    public static PrimeFocusScope Global { get; } = new("global");
}

/// <summary>
/// Bounds supplied to navigation in one common coordinate root. UI controls
/// can be measured locally with <see cref="FromLocal"/> and the navigator only
/// ever compares <see cref="RootBounds"/>. The root name is diagnostic and
/// catches accidental mixing of page-local and shell coordinates.
/// </summary>
public readonly record struct PrimeNavigationBounds
{
    public PrimeNavigationBounds(Rect localBounds, Point rootOffset,
        string coordinateRoot = "shell")
    {
        if (string.IsNullOrWhiteSpace(coordinateRoot))
            throw new ArgumentException("A coordinate root needs a stable key.",
                nameof(coordinateRoot));
        if (!IsFinite(localBounds.X) || !IsFinite(localBounds.Y)
            || !IsFinite(localBounds.Width) || !IsFinite(localBounds.Height)
            || localBounds.Width < 0 || localBounds.Height < 0
            || !IsFinite(rootOffset.X) || !IsFinite(rootOffset.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(localBounds),
                "Navigation bounds must be finite and non-negative.");
        }

        LocalBounds = localBounds;
        RootOffset = rootOffset;
        CoordinateRoot = coordinateRoot;
    }

    public PrimeNavigationBounds(Rect rootBounds)
        : this(rootBounds, default, "shell")
    {
    }

    public Rect LocalBounds { get; }
    public Point RootOffset { get; }
    public string CoordinateRoot { get; }

    public Rect RootBounds => new(
        LocalBounds.X + RootOffset.X,
        LocalBounds.Y + RootOffset.Y,
        LocalBounds.Width,
        LocalBounds.Height);

    public static PrimeNavigationBounds FromRoot(Rect rootBounds,
        string coordinateRoot = "shell")
        => new(rootBounds, default, coordinateRoot);

    public static PrimeNavigationBounds FromLocal(Rect localBounds, Point rootOffset,
        string coordinateRoot = "shell")
        => new(localBounds, rootOffset, coordinateRoot);

    /// <summary>
    /// Converts all four corners through a caller-owned local-to-root mapping
    /// and stores the resulting axis-aligned root bounds. This supports the
    /// scale/transform cases that a simple origin offset cannot represent.
    /// </summary>
    public static PrimeNavigationBounds FromLocal(Rect localBounds,
        Func<Point, Point> toRoot, string coordinateRoot = "shell")
    {
        ArgumentNullException.ThrowIfNull(toRoot);
        Point[] corners =
        [
            toRoot(localBounds.TopLeft),
            toRoot(localBounds.TopRight),
            toRoot(localBounds.BottomLeft),
            toRoot(localBounds.BottomRight)
        ];
        double minX = corners.Min(point => point.X);
        double minY = corners.Min(point => point.Y);
        double maxX = corners.Max(point => point.X);
        double maxY = corners.Max(point => point.Y);
        return FromRoot(new Rect(minX, minY, maxX - minX, maxY - minY),
            coordinateRoot);
    }

    private static bool IsFinite(double value) => !double.IsNaN(value)
        && !double.IsInfinity(value);
}

/// <summary>
/// A focusable shell target. The record is immutable so a freshly measured
/// visual tree can be handed to the pure navigator without shared UI state.
/// </summary>
public sealed record PrimeNavigationCandidate
{
    public PrimeNavigationCandidate(string focusId, Rect rootBounds,
        PrimeNavigationLayer layer = PrimeNavigationLayer.ActivePage,
        int priority = 0, bool isEnabled = true, bool isVisible = true,
        bool isFocusable = true, bool isOffscreen = false,
        bool canScrollIntoView = false, string? scrollHostId = null,
        string? sectionId = null, string? modalId = null,
        string? navigationGroup = null)
        : this(focusId, PrimeNavigationBounds.FromRoot(rootBounds), layer, priority,
            isEnabled, isVisible, isFocusable, isOffscreen, canScrollIntoView,
            scrollHostId, sectionId, modalId, navigationGroup)
    {
    }

    public PrimeNavigationCandidate(string focusId, PrimeNavigationBounds bounds,
        PrimeNavigationLayer layer = PrimeNavigationLayer.ActivePage,
        int priority = 0, bool isEnabled = true, bool isVisible = true,
        bool isFocusable = true, bool isOffscreen = false,
        bool canScrollIntoView = false, string? scrollHostId = null,
        string? sectionId = null, string? modalId = null,
        string? navigationGroup = null)
    {
        if (string.IsNullOrWhiteSpace(focusId))
            throw new ArgumentException("A focus candidate needs a stable ID.", nameof(focusId));
        _ = PrimeNavigationLayerPriority.For(layer);

        FocusId = focusId;
        Bounds = bounds;
        Layer = layer;
        Priority = priority;
        IsEnabled = isEnabled;
        IsVisible = isVisible;
        IsFocusable = isFocusable;
        IsOffscreen = isOffscreen;
        // Supplying a scroll host is enough to prove that an off-screen item
        // can be revealed. Callers may also use the explicit flag when the
        // host is represented by an external adapter.
        CanScrollIntoView = canScrollIntoView || scrollHostId is not null;
        ScrollHostId = scrollHostId;
        SectionId = sectionId;
        ModalId = modalId;
        NavigationGroup = navigationGroup;
    }

    public string FocusId { get; }
    public PrimeNavigationBounds Bounds { get; }
    public Rect RootBounds => Bounds.RootBounds;
    public PrimeNavigationLayer Layer { get; }
    public int Priority { get; }
    public bool IsEnabled { get; }
    public bool IsVisible { get; }
    public bool IsFocusable { get; }
    public bool IsOffscreen { get; }
    public bool CanScrollIntoView { get; }
    public string? ScrollHostId { get; }
    public string? SectionId { get; }
    public string? ModalId { get; }
    public string? NavigationGroup { get; }

    public int LayerPriority => PrimeNavigationLayerPriority.For(Layer);
    public bool IsEligible => IsEnabled && IsVisible && IsFocusable
        && (!IsOffscreen || CanScrollIntoView);
}

public readonly record struct PrimeScrollIntoViewRequest(
    string FocusId, Rect Bounds, string? ScrollHostId);

/// <summary>Result of a directional focus request.</summary>
public sealed record PrimeNavigationResult
{
    private PrimeNavigationResult(bool moved, string? previousFocusId,
        PrimeNavigationCandidate? candidate, PrimeNavigationLayer? activeLayer,
        PrimeScrollIntoViewRequest? scrollRequest)
    {
        Moved = moved;
        PreviousFocusId = previousFocusId;
        Candidate = candidate;
        ActiveLayer = activeLayer;
        ScrollRequest = scrollRequest;
    }

    public bool Moved { get; }
    public bool DidMove => Moved;
    public string? PreviousFocusId { get; }
    public PrimeNavigationCandidate? Candidate { get; }
    public PrimeNavigationCandidate? Target => Candidate;
    public string? FocusId => Candidate?.FocusId;
    public string? TargetFocusId => FocusId;
    public PrimeNavigationLayer? ActiveLayer { get; }
    public PrimeScrollIntoViewRequest? ScrollRequest { get; }
    public bool ShouldScrollIntoView => ScrollRequest is not null;

    public static PrimeNavigationResult NoMove(string? currentFocusId = null,
        PrimeNavigationLayer? activeLayer = null)
        => new(false, currentFocusId, null, activeLayer, null);

    internal static PrimeNavigationResult MovedTo(string? previousFocusId,
        PrimeNavigationCandidate candidate)
    {
        PrimeScrollIntoViewRequest? scroll = candidate.ScrollHostId is not null
            || candidate.IsOffscreen || candidate.CanScrollIntoView
            ? new(candidate.FocusId, candidate.RootBounds, candidate.ScrollHostId)
            : null;
        return new(true, previousFocusId, candidate, candidate.Layer, scroll);
    }
}

/// <summary>One major tab/section used by LB/RB navigation.</summary>
public sealed record PrimeNavigationSection
{
    public PrimeNavigationSection(string sectionId, int order,
        string? defaultFocusId = null, bool isEnabled = true, bool isVisible = true)
    {
        if (string.IsNullOrWhiteSpace(sectionId))
            throw new ArgumentException("A section needs a stable ID.", nameof(sectionId));
        SectionId = sectionId;
        Order = order;
        DefaultFocusId = defaultFocusId;
        IsEnabled = isEnabled;
        IsVisible = isVisible;
    }

    public string SectionId { get; }
    public int Order { get; }
    public string? DefaultFocusId { get; }
    public bool IsEnabled { get; }
    public bool IsVisible { get; }
    public bool IsEligible => IsEnabled && IsVisible;
}

public sealed record PrimeSectionNavigationResult
{
    private PrimeSectionNavigationResult(bool changed, string? previousSectionId,
        PrimeNavigationSection? section, string? restoredFocusId)
    {
        Changed = changed;
        PreviousSectionId = previousSectionId;
        Section = section;
        RestoredFocusId = restoredFocusId;
    }

    public bool Changed { get; }
    public string? PreviousSectionId { get; }
    public PrimeNavigationSection? Section { get; }
    public string? SectionId => Section?.SectionId;
    public string? RestoredFocusId { get; init; }

    internal static PrimeSectionNavigationResult None(string? currentSectionId)
        => new(false, currentSectionId, null, null);

    internal static PrimeSectionNavigationResult Moved(string? previousSectionId,
        PrimeNavigationSection section, string? restoredFocusId)
        => new(true, previousSectionId, section, restoredFocusId);
}
