using System;
using MphRead.Mods.Multiplayer;

namespace MphRead.Mods.Launcher;

public readonly record struct LauncherHunterChoice(Hunter Hunter, int Suit,
    LocalBodyAppearance Body = LocalBodyAppearance.Native, LocalDroneAppearance Drone = LocalDroneAppearance.Native);
public interface ILauncherHunterChoiceStore
{
    LauncherHunterChoice Capture();
    void Save(LauncherHunterChoice choice);
}

/// <summary>
/// Standalone Hunter/Suit draft for the native Home view. Session-owned Hunter
/// changes continue to use the authoritative lobby service and Identify.
/// </summary>
public sealed class LauncherHunterChoiceController : IDisposable
{
    public static readonly Hunter[] Playable =
    {
        Hunter.Samus, Hunter.Kanden, Hunter.Trace, Hunter.Sylux,
        Hunter.Noxus, Hunter.Spire, Hunter.Weavel
    };

    private readonly ILauncherHunterChoiceStore _store;
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private LauncherHunterChoice _original, _draft;
    private bool _disposed;
    private string _status = "SELECT HUNTER AND SUIT // APPLY TO SAVE";
    private long _revision = 1;

    public LauncherHunterChoiceController(ILauncherHunterChoiceStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        var captured = store.Capture();
        _original = captured;
        _draft = (Array.IndexOf(Playable, captured.Hunter) >= 0 && captured.Suit is >= 0 and <= 3)
            ? captured with { Body = LocalBodyAppearanceRules.CanonicalForHunter(captured.Body, captured.Hunter),
                Drone = LocalDroneAppearanceRules.CanonicalForHunter(captured.Drone, captured.Hunter) }
            : new(Hunter.Samus, Math.Clamp(captured.Suit, 0, 3));
    }

    public readonly record struct Snapshot(long Revision, LauncherHunterChoice Choice,
        bool Dirty, string Status);

    public Snapshot Current()
    {
        Owner();
        return new(_revision, _draft, _original != _draft, _status);
    }

    public bool SelectHunter(int index)
    {
        Owner();
        if (index < 0 || index >= Playable.Length) return false;
        _draft = _draft with { Hunter = Playable[index], Body = LocalBodyAppearanceRules.CanonicalForHunter(_draft.Body, Playable[index]),
            Drone = LocalDroneAppearanceRules.CanonicalForHunter(_draft.Drone, Playable[index]) };
        Note("HUNTER CHOICE UNSAVED");
        return true;
    }

    public bool SelectSuit(int index)
    {
        Owner();
        if (index is < 0 or > 3) return false;
        _draft = _draft with { Suit = index };
        Note("SUIT CHOICE UNSAVED");
        return true;
    }

    public bool SelectBody(int index)
    {
        Owner();
        if (index is < 0 or > 6 || !LocalBodyAppearanceRules.MatchesHunter((LocalBodyAppearance)index, _draft.Hunter)
            || index != 0 && !LocalBodyAppearanceRules.Available) return false;
        _draft = _draft with { Body = (LocalBodyAppearance)index };
        Note("LOCAL OFFLINE BODY UNSAVED");
        return true;
    }

    public bool SelectDrone(int index)
    {
        Owner();
        if (index is < 0 or > 2 || !LocalDroneAppearanceRules.MatchesHunter((LocalDroneAppearance)index, _draft.Hunter)
            || index != 0 && !LocalDroneAppearanceRules.Available) return false;
        _draft = _draft with { Drone = (LocalDroneAppearance)index };
        Note("PRACTICE DRONE APPEARANCE UNSAVED");
        return true;
    }

    /// <summary>Validate without mutating status, revision or persisted identity.</summary>
    public bool CanApply(out string reason)
    {
        Owner();
        reason = "";
        if (!HunterRules.Allowed(_draft.Hunter, lowTier: false) || _draft.Suit is < 0 or > 3
            || LocalBodyAppearanceRules.Canonical(_draft.Body) != _draft.Body
            || !LocalBodyAppearanceRules.MatchesHunter(_draft.Body, _draft.Hunter)
            || LocalDroneAppearanceRules.Canonical(_draft.Drone) != _draft.Drone
            || !LocalDroneAppearanceRules.MatchesHunter(_draft.Drone, _draft.Hunter))
            reason = "INVALID HUNTER OR SUIT";
        else if (_draft != _original && _store.Capture() != _original)
            reason = "PROFILE CHANGED ELSEWHERE. DISCARD AND REOPEN.";
        return reason.Length == 0;
    }

    public bool Apply()
    {
        Owner();
        if (!CanApply(out string reason)) { Note(reason); return false; }
        if (_draft == _original)
        {
            Note("HUNTER ALREADY EQUIPPED");
            return true;
        }
        try { _store.Save(_draft); }
        catch (Exception e) when (e is not OutOfMemoryException and not StackOverflowException)
        {
            Note("COULD NOT EQUIP: " + e.Message);
            return false;
        }
        _original = _draft;
        Note("HUNTER AND SUIT EQUIPPED");
        return true;
    }

    public void Discard()
    {
        Owner();
        _original = _store.Capture();
        _draft = Array.IndexOf(Playable, _original.Hunter) >= 0
            ? _original with { Suit = Math.Clamp(_original.Suit, 0, 3),
                Body = LocalBodyAppearanceRules.CanonicalForHunter(_original.Body, _original.Hunter),
                Drone = LocalDroneAppearanceRules.CanonicalForHunter(_original.Drone, _original.Hunter) }
            : new(Hunter.Samus, Math.Clamp(_original.Suit, 0, 3));
        Note("HUNTER SELECTION DISCARDED");
    }

    private void Note(string message)
    {
        _status = message;
        _revision++;
    }

    private void Owner()
    {
        if (Environment.CurrentManagedThreadId != _owner)
            throw new InvalidOperationException("Hunter profile edits belong to their launcher owner thread.");
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed) return;
        Owner();
        _disposed = true;
    }
}
