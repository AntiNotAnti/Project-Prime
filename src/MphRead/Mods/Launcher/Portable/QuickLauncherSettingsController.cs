using System;

namespace MphRead.Mods.Launcher;

/// <summary>
/// Three launcher preferences which have no immediate GPU/audio side effects.
/// Complex MenuSettings, video confirmation and input rebinding deliberately
/// remain in the current validated SettingsView until migrated independently.
/// </summary>
public readonly record struct QuickLauncherPreferences(
    bool ReduceMotion, bool AutoUpdate, bool DebugLogs);

public enum QuickPreference { ReduceMotion, AutoUpdate, DebugLogs }

public interface IQuickLauncherPreferenceStore
{
    QuickLauncherPreferences Capture();
    void Save(QuickLauncherPreferences value);
}

/// <summary>
/// Owner-thread, transactional settings draft. Displaying a native page never
/// changes live preferences. If another settings screen saves meanwhile,
/// stale native writes are refused instead of overwriting those changes.
/// </summary>
public sealed class QuickLauncherSettingsController : IDisposable
{
    private readonly IQuickLauncherPreferenceStore _store;
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private QuickLauncherPreferences _original;
    private QuickLauncherPreferences _draft;
    private bool _disposed;
    private long _revision = 1;
    private string _status = "CHANGE OPTIONS THEN APPLY";

    public QuickLauncherSettingsController(IQuickLauncherPreferenceStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _original = _draft = store.Capture();
    }

    public readonly record struct Snapshot(
        long Revision, QuickLauncherPreferences Options, bool Dirty, string Status);

    public Snapshot Current()
    {
        Check();
        return new(_revision, _draft, _draft != _original, _status);
    }

    public bool Toggle(QuickPreference key)
    {
        Check();
        if (!Enum.IsDefined(key)) return false;
        _draft = key switch
        {
            QuickPreference.ReduceMotion => _draft with { ReduceMotion = !_draft.ReduceMotion },
            QuickPreference.AutoUpdate => _draft with { AutoUpdate = !_draft.AutoUpdate },
            QuickPreference.DebugLogs => _draft with { DebugLogs = !_draft.DebugLogs },
            _ => _draft
        };
        _status = "UNSAVED CHANGES";
        _revision++;
        return true;
    }

    public bool Apply()
    {
        Check();
        if (_draft == _original)
        {
            SetStatus("NO CHANGES TO APPLY");
            return true;
        }
        if (_store.Capture() != _original)
        {
            SetStatus("SETTINGS CHANGED ELSEWHERE. DISCARD AND REOPEN.");
            return false;
        }
        try { _store.Save(_draft); }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        {
            SetStatus("COULD NOT SAVE SETTINGS: " + error.Message);
            return false;
        }
        _original = _draft;
        SetStatus("SETTINGS SAVED");
        return true;
    }

    public bool Discard()
    {
        Check();
        // Re-read in case a different preference surface committed changes.
        _original = _draft = _store.Capture();
        SetStatus("CHANGES DISCARDED");
        return true;
    }

    private void SetStatus(string value)
    {
        _status = value.Length <= 180 ? value : value[..180];
        _revision++;
    }

    private void Check()
    {
        if (Environment.CurrentManagedThreadId != _owner)
            throw new InvalidOperationException("Launcher preferences belong to their UI owner thread.");
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed) return;
        Check();
        _disposed = true;
    }
}
