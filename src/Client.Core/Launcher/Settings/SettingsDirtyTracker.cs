using System;
using System.Collections.Generic;

namespace MphRead.Mods.Launcher.Settings;

/// <summary>
/// Compares a current Settings draft with the snapshot captured when the view
/// opened. It deliberately knows nothing about controls or persistence: the
/// owner supplies a small immutable value projection and decides when a
/// programmatic refresh is suppressed.
/// </summary>
internal sealed class SettingsDirtyTracker
{
    private readonly Func<IReadOnlyDictionary<string, string?>> _capture;
    private Dictionary<string, string?> _baseline;
    private int _suppressionDepth;
    private bool _isDirty;
    private int _changedCount;

    internal SettingsDirtyTracker(
        Func<IReadOnlyDictionary<string, string?>> capture)
    {
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _baseline = Copy(_capture());
    }

    /// <summary>Raised when dirty state or the optional changed count changes.</summary>
    internal event EventHandler? Changed;

    internal bool IsDirty => _isDirty;
    internal int ChangedCount => _changedCount;
    internal bool IsSuppressed => _suppressionDepth != 0;

    /// <summary>
    /// Ignore row notifications caused by initial population, capability
    /// refreshes, or a preset's dependent-row synchronization. The scope is
    /// nestable and intentionally does not perform an implicit refresh when it
    /// ends; a caller that made a real edit can call <see cref="Refresh"/>
    /// once after its programmatic cascade completes.
    /// </summary>
    internal IDisposable Suppress()
    {
        _suppressionDepth++;
        return new Suppression(this);
    }

    /// <summary>Compare the current draft with the opening baseline.</summary>
    internal void Refresh()
    {
        if (_suppressionDepth != 0)
        {
            return;
        }

        IReadOnlyDictionary<string, string?> current = _capture();
        var keys = new HashSet<string>(_baseline.Keys, StringComparer.Ordinal);
        keys.UnionWith(current.Keys);

        int changed = 0;
        foreach (string key in keys)
        {
            _baseline.TryGetValue(key, out string? baseline);
            current.TryGetValue(key, out string? value);
            if (!String.Equals(baseline, value, StringComparison.Ordinal))
            {
                changed++;
            }
        }

        SetState(changed != 0, changed);
    }

    /// <summary>
    /// Treat the current draft as saved. This does not persist anything and
    /// does not own or alter any rollback snapshot held by SettingsView.
    /// </summary>
    internal void MarkSaved()
    {
        _baseline = Copy(_capture());
        SetState(false, 0);
    }

    /// <summary>
    /// Clear the presentation state after the owner has restored its original
    /// draft snapshot. The baseline is intentionally retained: the owner is
    /// closing this edit session, and the tracker must not take ownership of
    /// the rollback data.
    /// </summary>
    internal void MarkDiscarded()
    {
        SetState(false, 0);
    }

    private void SetState(bool isDirty, int changedCount)
    {
        if (_isDirty == isDirty && _changedCount == changedCount)
        {
            return;
        }

        _isDirty = isDirty;
        _changedCount = changedCount;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static Dictionary<string, string?> Copy(
        IReadOnlyDictionary<string, string?> values)
    {
        if (values == null)
        {
            throw new ArgumentNullException(nameof(values));
        }

        return new Dictionary<string, string?>(values, StringComparer.Ordinal);
    }

    private sealed class Suppression : IDisposable
    {
        private SettingsDirtyTracker? _owner;

        internal Suppression(SettingsDirtyTracker owner) => _owner = owner;

        public void Dispose()
        {
            SettingsDirtyTracker? owner = _owner;
            if (owner == null)
            {
                return;
            }

            _owner = null;
            if (owner._suppressionDepth > 0)
            {
                owner._suppressionDepth--;
            }
        }
    }
}
