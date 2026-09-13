using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;

namespace MphRead.Mods.Launcher.Gui;

/// <summary>
/// Pure geometry-based selection for shell focus. It accepts immutable
/// candidates and never touches an Avalonia visual or synthesizes pointer
/// input, so the same decision can be tested without a window or controller.
/// </summary>
public static class PrimeSpatialNavigation
{
    // Perpendicular travel should be visibly more expensive than forward
    // travel. The projection-gap term makes a candidate in the same row or
    // column win over a nearer diagonal candidate.
    private const double PerpendicularWeight = 2.5;
    private const double AlignmentGapWeight = 4;
    private const double DirectionEpsilon = 0.0000001;

    /// <summary>Returns the highest eligible layer in a candidate snapshot.</summary>
    public static PrimeNavigationLayer GetActiveLayer(
        IEnumerable<PrimeNavigationCandidate> candidates,
        string? coordinateRoot = null)
        => PrimeNavigationSnapshot.Create(candidates, coordinateRoot).ActiveLayer;

    /// <summary>
    /// Determines whether a candidate belongs to the currently navigable
    /// surface. Modal and open-editor layers are exclusive. With neither
    /// present, the page and shell chrome form one spatial surface so a player
    /// can move between a page action and the persistent header/footer.
    /// </summary>
    public static bool IsInActiveSurface(PrimeNavigationCandidate candidate,
        PrimeNavigationLayer activeLayer)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return activeLayer == PrimeNavigationLayer.ActivePage
            ? candidate.Layer is PrimeNavigationLayer.ShellChrome
                or PrimeNavigationLayer.ActivePage
            : candidate.Layer == activeLayer;
    }

    public static PrimeNavigationCandidate? Select(string? currentFocusId,
        IEnumerable<PrimeNavigationCandidate> candidates,
        PrimeNavigationDirection direction, string? coordinateRoot = null)
    {
        PrimeNavigationSnapshot snapshot = PrimeNavigationSnapshot.Create(
            candidates, coordinateRoot);
        if (snapshot.Eligible.Length == 0) return null;

        PrimeNavigationLayer activeLayer = snapshot.ActiveLayer;
        PrimeNavigationCandidate? current = snapshot.Eligible.FirstOrDefault(candidate =>
            string.Equals(candidate.FocusId, currentFocusId, StringComparison.Ordinal)
            && IsInActiveSurface(candidate, activeLayer));

        // A newly opened modal/editor or a route whose previous control was
        // removed starts at its highest-priority deterministic candidate.
        if (current is null)
            return SelectInitial(snapshot.Eligible
                .Where(candidate => IsInActiveSurface(candidate, activeLayer))
                .ToArray(), activeLayer);

        PrimeNavigationCandidate[] scored = snapshot.Eligible
            .Where(candidate => IsInActiveSurface(candidate, activeLayer)
                && !string.Equals(candidate.FocusId, current.FocusId,
                    StringComparison.Ordinal))
            .Select(candidate => Score(current, candidate, direction))
            .Where(result => result is not null)
            .Select(result => result!.Value)
            .OrderBy(result => result.Score)
            .ThenBy(result => result.AlignmentGap)
            .ThenBy(result => result.PrimaryDistance)
            .ThenBy(result => result.PerpendicularDistance)
            .ThenByDescending(result => result.Candidate.Priority)
            .ThenBy(result => result.Candidate.FocusId, StringComparer.Ordinal)
            .ThenBy(result => result.Candidate.RootBounds.X)
            .ThenBy(result => result.Candidate.RootBounds.Y)
            .ThenBy(result => result.Candidate.RootBounds.Width)
            .ThenBy(result => result.Candidate.RootBounds.Height)
            .Select(result => result.Candidate)
            .ToArray();

        return scored.Length == 0 ? null : scored[0];
    }

    public static PrimeNavigationResult Navigate(string? currentFocusId,
        IEnumerable<PrimeNavigationCandidate> candidates,
        PrimeNavigationDirection direction, string? coordinateRoot = null)
    {
        PrimeNavigationSnapshot snapshot = PrimeNavigationSnapshot.Create(
            candidates, coordinateRoot);
        if (snapshot.Eligible.Length == 0)
            return PrimeNavigationResult.NoMove(currentFocusId);

        PrimeNavigationCandidate? target = Select(currentFocusId, snapshot.Eligible,
            direction, snapshot.CoordinateRoot);
        return target is null
            ? PrimeNavigationResult.NoMove(currentFocusId, snapshot.ActiveLayer)
            : PrimeNavigationResult.MovedTo(currentFocusId, target);
    }

    /// <summary>
    /// Picks the first/highest-priority target for a new page or modal. Ordering
    /// is independent of the visual-tree enumeration order.
    /// </summary>
    public static PrimeNavigationCandidate? SelectInitial(
        IEnumerable<PrimeNavigationCandidate> candidates,
        PrimeNavigationLayer? layer = null, string? modalId = null,
        string? coordinateRoot = null)
    {
        PrimeNavigationSnapshot snapshot = PrimeNavigationSnapshot.Create(
            candidates, coordinateRoot);
        PrimeNavigationLayer selectedLayer = layer ?? snapshot.ActiveLayer;
        return SelectInitial(snapshot.Eligible
            .Where(candidate => IsInActiveSurface(candidate, selectedLayer)
                && (modalId is null || string.Equals(candidate.ModalId, modalId,
                    StringComparison.Ordinal)))
            .ToArray(), selectedLayer);
    }

    private static PrimeNavigationCandidate? SelectInitial(
        IReadOnlyList<PrimeNavigationCandidate> candidates,
        PrimeNavigationLayer layer)
    {
        _ = layer;
        return candidates
            .OrderByDescending(candidate => candidate.Priority)
            .ThenBy(candidate => candidate.RootBounds.Y)
            .ThenBy(candidate => candidate.RootBounds.X)
            .ThenBy(candidate => candidate.FocusId, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.RootBounds.Width)
            .ThenBy(candidate => candidate.RootBounds.Height)
            .FirstOrDefault();
    }

    private static ScoredCandidate? Score(PrimeNavigationCandidate current,
        PrimeNavigationCandidate candidate, PrimeNavigationDirection direction)
    {
        Rect currentBounds = current.RootBounds;
        Rect candidateBounds = candidate.RootBounds;
        Point currentCenter = currentBounds.Center;
        Point candidateCenter = candidateBounds.Center;
        double primaryDelta;
        double perpendicularDistance;
        double primaryDistance;
        double alignmentGap;

        switch (direction)
        {
            case PrimeNavigationDirection.Up:
                primaryDelta = currentCenter.Y - candidateCenter.Y;
                if (primaryDelta <= DirectionEpsilon) return null;
                perpendicularDistance = Math.Abs(candidateCenter.X - currentCenter.X);
                primaryDistance = Math.Max(0, currentBounds.Top - candidateBounds.Bottom);
                alignmentGap = ProjectionGap(currentBounds.Left, currentBounds.Right,
                    candidateBounds.Left, candidateBounds.Right);
                break;
            case PrimeNavigationDirection.Right:
                primaryDelta = candidateCenter.X - currentCenter.X;
                if (primaryDelta <= DirectionEpsilon) return null;
                perpendicularDistance = Math.Abs(candidateCenter.Y - currentCenter.Y);
                primaryDistance = Math.Max(0, candidateBounds.Left - currentBounds.Right);
                alignmentGap = ProjectionGap(currentBounds.Top, currentBounds.Bottom,
                    candidateBounds.Top, candidateBounds.Bottom);
                break;
            case PrimeNavigationDirection.Down:
                primaryDelta = candidateCenter.Y - currentCenter.Y;
                if (primaryDelta <= DirectionEpsilon) return null;
                perpendicularDistance = Math.Abs(candidateCenter.X - currentCenter.X);
                primaryDistance = Math.Max(0, candidateBounds.Top - currentBounds.Bottom);
                alignmentGap = ProjectionGap(currentBounds.Left, currentBounds.Right,
                    candidateBounds.Left, candidateBounds.Right);
                break;
            case PrimeNavigationDirection.Left:
                primaryDelta = currentCenter.X - candidateCenter.X;
                if (primaryDelta <= DirectionEpsilon) return null;
                perpendicularDistance = Math.Abs(candidateCenter.Y - currentCenter.Y);
                primaryDistance = Math.Max(0, currentBounds.Left - candidateBounds.Right);
                alignmentGap = ProjectionGap(currentBounds.Top, currentBounds.Bottom,
                    candidateBounds.Top, candidateBounds.Bottom);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(direction), direction, null);
        }

        // Include a small fraction of center-forward distance. Edge distance
        // is preferred for adjoining controls, while this term differentiates
        // overlapping or very wide targets without changing row/column bias.
        double score = primaryDistance + primaryDelta * 0.25
            + perpendicularDistance * PerpendicularWeight
            + alignmentGap * AlignmentGapWeight;
        return new ScoredCandidate(candidate, score, primaryDistance,
            perpendicularDistance, alignmentGap);
    }

    private static double ProjectionGap(double firstStart, double firstEnd,
        double secondStart, double secondEnd)
    {
        if (firstEnd < secondStart) return secondStart - firstEnd;
        if (secondEnd < firstStart) return firstStart - secondEnd;
        return 0;
    }

    private readonly record struct ScoredCandidate(
        PrimeNavigationCandidate Candidate, double Score,
        double PrimaryDistance, double PerpendicularDistance, double AlignmentGap);
}

/// <summary>Immutable eligibility/layer snapshot used by the pure algorithm.</summary>
internal sealed class PrimeNavigationSnapshot
{
    private PrimeNavigationSnapshot(PrimeNavigationCandidate[] eligible,
        PrimeNavigationLayer activeLayer, string coordinateRoot)
    {
        Eligible = eligible;
        ActiveLayer = activeLayer;
        CoordinateRoot = coordinateRoot;
    }

    public PrimeNavigationCandidate[] Eligible { get; }
    public PrimeNavigationLayer ActiveLayer { get; }
    public string CoordinateRoot { get; }

    public static PrimeNavigationSnapshot Create(
        IEnumerable<PrimeNavigationCandidate> candidates, string? coordinateRoot)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        PrimeNavigationCandidate[] all = candidates
            .Where(candidate => candidate is not null)
            .ToArray();
        string[] roots = all.Select(candidate => candidate.Bounds.CoordinateRoot)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(root => root, StringComparer.Ordinal)
            .ToArray();
        if (coordinateRoot is not null && !roots.Contains(coordinateRoot,
                StringComparer.Ordinal))
            return new(Array.Empty<PrimeNavigationCandidate>(),
                PrimeNavigationLayer.ShellChrome, coordinateRoot);
        if (roots.Length > 1 && coordinateRoot is null)
            throw new ArgumentException(
                "All navigation candidates must use one coordinate root.", nameof(candidates));

        string root = coordinateRoot ?? (roots.Length == 0 ? "shell" : roots[0]);
        PrimeNavigationCandidate[] eligible = all
            .Where(candidate => candidate.IsEligible
                && string.Equals(candidate.Bounds.CoordinateRoot, root,
                    StringComparison.Ordinal))
            .ToArray();
        PrimeNavigationLayer activeLayer = eligible.Length == 0
            ? PrimeNavigationLayer.ShellChrome
            : eligible.OrderByDescending(candidate => candidate.LayerPriority)
                .ThenByDescending(candidate => candidate.Priority)
                .ThenBy(candidate => candidate.FocusId, StringComparer.Ordinal)
                .First().Layer;
        return new(eligible, activeLayer, root);
    }
}
