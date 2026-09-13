using System;
using System.Collections.Generic;

namespace MphRead.Mods.Launcher.Gui;

/// <summary>Entry in the bounded modal focus stack.</summary>
public sealed record PrimeModalFocusEntry(
    string ModalId, PrimeFocusScope ParentScope, string? PreviousFocusId);

/// <summary>
/// Models modal focus ownership without knowing anything about Avalonia. The
/// top entry is the only modal allowed to receive focus; nested modals restore
/// the exact focus and scope that were active when they opened.
/// </summary>
public sealed class PrimeModalFocusTrap
{
    private readonly int _capacity;
    private readonly List<PrimeModalFocusEntry> _stack = new();

    public PrimeModalFocusTrap(int capacity = 8)
    {
        if (capacity is < 1 or > 32)
            throw new ArgumentOutOfRangeException(nameof(capacity),
                "Modal focus depth must remain bounded between 1 and 32.");
        _capacity = capacity;
    }

    public int Capacity => _capacity;
    public int Depth => _stack.Count;
    public bool IsOpen => _stack.Count != 0;
    public PrimeModalFocusEntry? Current => _stack.Count == 0 ? null : _stack[^1];
    public string? CurrentModalId => Current?.ModalId;

    public PrimeModalFocusEntry Open(string modalId, PrimeFocusScope parentScope,
        string? previousFocusId)
    {
        if (string.IsNullOrWhiteSpace(modalId))
            throw new ArgumentException("A modal needs a stable ID.", nameof(modalId));
        if (_stack.Count == _capacity)
            throw new InvalidOperationException("The modal focus stack is full.");
        var entry = new PrimeModalFocusEntry(modalId, parentScope, previousFocusId);
        _stack.Add(entry);
        return entry;
    }

    public bool TryClose(out PrimeModalFocusEntry? closed)
        => TryClose(CurrentModalId, out closed);

    public bool TryClose(string? modalId, out PrimeModalFocusEntry? closed)
    {
        closed = null;
        if (_stack.Count == 0 || modalId is null
            || !string.Equals(_stack[^1].ModalId, modalId,
                StringComparison.Ordinal))
            return false;

        int index = _stack.Count - 1;
        closed = _stack[index];
        _stack.RemoveAt(index);
        return true;
    }

    /// <summary>
    /// A modal trap allows only candidates belonging to the current modal. A
    /// candidate without a modal ID is page/chrome content and is rejected
    /// while any modal is open.
    /// </summary>
    public bool Allows(PrimeNavigationCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return !IsOpen || (candidate.Layer == PrimeNavigationLayer.Modal
            && string.Equals(candidate.ModalId, CurrentModalId,
                StringComparison.Ordinal));
    }

    public IReadOnlyList<PrimeModalFocusEntry> Snapshot() => _stack.ToArray();
}
