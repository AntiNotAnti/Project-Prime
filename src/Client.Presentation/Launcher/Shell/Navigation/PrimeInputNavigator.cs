using System;
using System.Collections.Generic;
using System.Linq;

namespace MphRead.Mods.Launcher.Gui;

/// <summary>
/// Stateful shell navigation coordinator. Geometry decisions remain in
/// <see cref="PrimeSpatialNavigation"/>; this type owns the small amount of
/// state needed to restore focus across routes, trap modal focus, request
/// scrolling, and hand section changes back to the shell.
/// </summary>
public sealed class PrimeInputNavigator
{
    private readonly PrimeModalFocusTrap _modalTrap;
    private string? _focusedId;
    private PrimeFocusScope _scope = PrimeFocusScope.Global;
    private bool _hasScope;

    public PrimeInputNavigator(int focusMemoryCapacity = 64,
        int modalDepthCapacity = 8)
    {
        FocusMemory = new PrimeFocusMemory(focusMemoryCapacity);
        _modalTrap = new PrimeModalFocusTrap(modalDepthCapacity);
    }

    public PrimeFocusMemory FocusMemory { get; }
    public PrimeModalFocusTrap ModalTrap => _modalTrap;
    public PrimeFocusScope CurrentScope => _scope;
    public string? FocusedId => _focusedId;
    public string? ActiveModalId => _modalTrap.CurrentModalId;

    /// <summary>
    /// Enters a route/subsection and restores its remembered ID when that ID
    /// is still eligible. If not, the highest-priority candidate in the active
    /// layer receives focus.
    /// </summary>
    public PrimeNavigationResult EnterScope(PrimeFocusScope scope,
        IEnumerable<PrimeNavigationCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (_hasScope && _scope != scope) RememberFocused();
        _scope = scope;
        _hasScope = true;

        PrimeNavigationCandidate[] visible = ApplyTrap(candidates).ToArray();
        PrimeNavigationLayer activeLayer = PrimeSpatialNavigation.GetActiveLayer(visible);
        PrimeNavigationCandidate? target = null;
        if (FocusMemory.TryGet(scope, out string remembered))
        {
            target = visible.FirstOrDefault(candidate =>
                string.Equals(candidate.FocusId, remembered,
                    StringComparison.Ordinal) && candidate.IsEligible
                && PrimeSpatialNavigation.IsInActiveSurface(candidate, activeLayer));
            if (target is null) FocusMemory.Forget(scope);
        }
        target ??= PrimeSpatialNavigation.SelectInitial(visible);
        return SetFocused(target, previousFocusId: _focusedId);
    }

    /// <summary>Stores the currently focused stable ID in the active scope.</summary>
    public bool RememberFocused()
    {
        if (!_hasScope || string.IsNullOrWhiteSpace(_focusedId)) return false;
        FocusMemory.Remember(_scope, _focusedId);
        return true;
    }

    /// <summary>
    /// Adopts a focus ID selected by the native pointer/keyboard focus model.
    /// Controller navigation remains the owner of directional decisions, but
    /// the shell must be able to remember a target the player clicked before a
    /// route or modal rebuild occurs.
    /// </summary>
    public bool TrackFocused(string focusId)
    {
        if (string.IsNullOrWhiteSpace(focusId)) return false;
        _focusedId = focusId;
        RememberFocused();
        return true;
    }

    public PrimeNavigationResult Move(PrimeNavigationDirection direction,
        IEnumerable<PrimeNavigationCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        PrimeNavigationCandidate[] visible = ApplyTrap(candidates).ToArray();
        PrimeNavigationResult result = PrimeSpatialNavigation.Navigate(_focusedId,
            visible, direction);
        if (!result.Moved) return result;
        _focusedId = result.FocusId;
        RememberFocused();
        return result;
    }

    /// <summary>
    /// Restores a remembered scope explicitly. This is useful after a view has
    /// rebuilt its controls without changing the route itself.
    /// </summary>
    public PrimeNavigationResult RestoreScope(PrimeFocusScope scope,
        IEnumerable<PrimeNavigationCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (_hasScope && _scope != scope) RememberFocused();
        _scope = scope;
        _hasScope = true;
        PrimeNavigationCandidate[] visible = ApplyTrap(candidates).ToArray();
        PrimeNavigationLayer activeLayer = PrimeSpatialNavigation.GetActiveLayer(visible);
        PrimeNavigationCandidate? target = null;
        if (FocusMemory.TryGet(scope, out string remembered))
        {
            target = visible.FirstOrDefault(candidate =>
                candidate.IsEligible && string.Equals(candidate.FocusId, remembered,
                    StringComparison.Ordinal)
                && PrimeSpatialNavigation.IsInActiveSurface(candidate, activeLayer));
            if (target is null) FocusMemory.Forget(scope);
        }
        target ??= PrimeSpatialNavigation.SelectInitial(visible);
        return SetFocused(target, _focusedId);
    }

    /// <summary>Moves through major sections for LB/RB, not rows for D-pad.</summary>
    public PrimeSectionNavigationResult MoveSection(
        string? currentSectionId, IEnumerable<PrimeNavigationSection> sections,
        PrimeNavigationSectionDirection direction,
        IEnumerable<PrimeNavigationCandidate>? candidates = null)
    {
        PrimeSectionNavigationResult result = PrimeSectionNavigation.Move(
            currentSectionId, sections, direction);
        if (!result.Changed || result.Section is null) return result;

        // Preserve the outgoing subsection before changing the memory key.
        // Without this, leaving a page via LB/RB would overwrite the prior
        // subsection's focus with whichever control the next section selects.
        RememberFocused();
        PrimeFocusScope sectionScope = _scope.WithSubsection(result.Section.SectionId);
        _scope = sectionScope;
        _hasScope = true;
        if (candidates is null)
        {
            _focusedId = result.RestoredFocusId;
            RememberFocused();
        }
        else
        {
            PrimeNavigationCandidate[] visible = ApplyTrap(candidates)
                .Where(candidate => candidate.SectionId is null
                    || string.Equals(candidate.SectionId, result.Section.SectionId,
                        StringComparison.Ordinal))
                .ToArray();
            PrimeNavigationLayer activeLayer = PrimeSpatialNavigation.GetActiveLayer(visible);
            PrimeNavigationCandidate? target = null;
            if (FocusMemory.TryGet(sectionScope, out string remembered))
                target = visible.FirstOrDefault(candidate => candidate.IsEligible
                    && string.Equals(candidate.FocusId, remembered,
                        StringComparison.Ordinal)
                    && PrimeSpatialNavigation.IsInActiveSurface(candidate, activeLayer));
            target ??= result.RestoredFocusId is null ? null
                : visible.FirstOrDefault(candidate => candidate.IsEligible
                    && string.Equals(candidate.FocusId, result.RestoredFocusId,
                        StringComparison.Ordinal)
                    && PrimeSpatialNavigation.IsInActiveSurface(candidate, activeLayer));
            target ??= PrimeSpatialNavigation.SelectInitial(visible);
            if (target is not null)
            {
                _focusedId = target.FocusId;
                RememberFocused();
            }
        }
        return result with { RestoredFocusId = _focusedId ?? result.RestoredFocusId };
    }

    /// <summary>
    /// Opens a modal, remembers the underlying focus, and selects the
    /// highest-priority modal action. Candidate.ModalId is required for the
    /// trap to admit a candidate.
    /// </summary>
    public PrimeNavigationResult OpenModal(string modalId,
        IEnumerable<PrimeNavigationCandidate> candidates,
        PrimeFocusScope? modalScope = null)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        string? previousFocus = _focusedId;
        RememberFocused();
        PrimeFocusScope parentScope = _scope;
        PrimeFocusScope targetScope = modalScope ?? _scope.WithModal(modalId);
        _modalTrap.Open(modalId, parentScope, previousFocus);
        _scope = targetScope;
        _hasScope = true;

        PrimeNavigationCandidate[] modalCandidates = candidates
            .Where(candidate => _modalTrap.Allows(candidate)).ToArray();
        PrimeNavigationCandidate? target = PrimeSpatialNavigation.SelectInitial(
            modalCandidates, PrimeNavigationLayer.Modal, modalId);
        return SetFocused(target, previousFocus);
    }

    /// <summary>Closes only the top modal and restores its parent focus ID.</summary>
    public PrimeNavigationResult CloseModal(string modalId,
        IEnumerable<PrimeNavigationCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (!_modalTrap.TryClose(modalId, out PrimeModalFocusEntry? closed)
            || closed is null)
            return PrimeNavigationResult.NoMove(_focusedId);

        string? previousFocus = _focusedId;
        _scope = closed.ParentScope;
        _hasScope = true;
        PrimeNavigationCandidate[] visible = ApplyTrap(candidates).ToArray();
        PrimeNavigationLayer activeLayer = PrimeSpatialNavigation.GetActiveLayer(visible);
        PrimeNavigationCandidate? target = null;
        if (!string.IsNullOrWhiteSpace(closed.PreviousFocusId))
            target = visible.FirstOrDefault(candidate => candidate.IsEligible
                && string.Equals(candidate.FocusId, closed.PreviousFocusId,
                    StringComparison.Ordinal)
                && PrimeSpatialNavigation.IsInActiveSurface(candidate, activeLayer));
        if (target is null && FocusMemory.TryGet(_scope, out string remembered))
            target = visible.FirstOrDefault(candidate => candidate.IsEligible
                && string.Equals(candidate.FocusId, remembered,
                    StringComparison.Ordinal)
                && PrimeSpatialNavigation.IsInActiveSurface(candidate, activeLayer));
        target ??= PrimeSpatialNavigation.SelectInitial(visible);
        return SetFocused(target, previousFocus);
    }

    public PrimeNavigationResult CloseModal(
        IEnumerable<PrimeNavigationCandidate> candidates)
    {
        string? modalId = _modalTrap.CurrentModalId;
        return modalId is null
            ? PrimeNavigationResult.NoMove(_focusedId)
            : CloseModal(modalId, candidates);
    }

    /// <summary>Returns the current focus without changing focus state.</summary>
    public bool IsFocused(string focusId)
        => string.Equals(_focusedId, focusId, StringComparison.Ordinal);

    public void ClearFocus()
    {
        RememberFocused();
        _focusedId = null;
    }

    private PrimeNavigationResult SetFocused(PrimeNavigationCandidate? target,
        string? previousFocusId)
    {
        if (target is null)
        {
            _focusedId = null;
            return PrimeNavigationResult.NoMove(previousFocusId);
        }
        _focusedId = target.FocusId;
        RememberFocused();
        return PrimeNavigationResult.MovedTo(previousFocusId, target);
    }

    private IEnumerable<PrimeNavigationCandidate> ApplyTrap(
        IEnumerable<PrimeNavigationCandidate> candidates)
    {
        if (_modalTrap.IsOpen)
            return candidates.Where(candidate => _modalTrap.Allows(candidate));
        // Closed modal trees should not win merely because their stale
        // candidates were included in a visual snapshot.
        return candidates.Where(candidate => candidate.ModalId is null);
    }
}
