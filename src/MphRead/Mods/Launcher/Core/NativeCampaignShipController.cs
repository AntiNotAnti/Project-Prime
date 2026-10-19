#if !MPHREAD_SERVER
using System;
using System.Collections.Generic;

namespace MphRead.Mods.Launcher.Core;

/// <summary>Local navigation over detached campaign values; no StorySave, scene or save mutation.</summary>
public sealed class NativeCampaignShipController<TLaunch> : IDisposable
{
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly INativeCampaignShipBackend<TLaunch> _backend;
    private readonly NativeCampaignShipDock _dock;
    private readonly int _hunter;
    private IReadOnlyList<NativeCampaignShipDestination> _destinations = Array.Empty<NativeCampaignShipDestination>();
    private string _fingerprint = "", _error = "";
    private int _selected;
    private long _revision;
    private bool _starMap, _closed, _disposed;
    private NativeCampaignShipEffect<TLaunch>? _effect;

    public NativeCampaignShipController(INativeCampaignShipBackend<TLaunch> backend, NativeCampaignShipDock dock, int hunter)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        if (dock.Slot is < 1 or > 3 || dock.Planet is < 0 or > 4) throw new ArgumentOutOfRangeException(nameof(dock));
        if (hunter is < 0 or > 6) throw new ArgumentOutOfRangeException(nameof(hunter));
        _dock = dock; _hunter = hunter; _selected = dock.Planet;
        Refresh();
    }

    public NativeCampaignShipSnapshot Snapshot()
    {
        VerifyOwner();
        return new(_revision, _dock, _hunter, _starMap, _selected, _destinations,
            _fingerprint.Length != 0, _closed, _error);
    }

    public bool Refresh()
    {
        VerifyOwner();
        if (_disposed || _closed) return false;
        NativeCampaignShipRead result = _backend.Read(_dock);
        ClearProjection();
        if (result is null || !String.IsNullOrEmpty(result.Error))
            _error = result?.Error ?? "SHIP DATA IS UNAVAILABLE. REFRESH TO TRY AGAIN.";
        else if (!ValidFingerprint(result.Fingerprint) || !TryCopyDestinations(result.Destinations, out var rows)
            || !rows[_dock.Planet].Unlocked)
            _error = "SHIP DATA COULD NOT BE VERIFIED. REFRESH TO TRY AGAIN.";
        else { _fingerprint = result.Fingerprint; _destinations = rows; }
        _revision++;
        return true;
    }

    public bool OpenMap()
    {
        VerifyOwner();
        if (_disposed || _closed || _starMap || _fingerprint.Length == 0) return false;
        _starMap = true; _revision++; return true;
    }

    public bool Cockpit()
    {
        VerifyOwner();
        if (_disposed || _closed || !_starMap) return false;
        _starMap = false; _revision++; return true;
    }

    public bool Select(int planet)
    {
        VerifyOwner();
        if (_disposed || _closed || !_starMap || planet is < 0 or > 4 || _fingerprint.Length == 0) return false;
        if (_selected != planet) { _selected = planet; _revision++; }
        return true;
    }

    public bool Launch(bool exitShipToSurface)
    {
        VerifyOwner();
        if (_disposed || _closed || _fingerprint.Length == 0 || exitShipToSurface == _starMap) return false;
        int destination = exitShipToSurface ? _dock.Planet : _selected;
        if (!_destinations[destination].Unlocked) return false;
        var request = new NativeCampaignShipRequest(new(_dock, _fingerprint, _hunter), destination, exitShipToSurface);
        if (!_backend.TryCreateLaunch(request, out var plan, out string error))
        {
            ClearProjection();
            _error = String.IsNullOrWhiteSpace(error) ? "DEPARTURE COULD NOT BE VERIFIED. REFRESH TO TRY AGAIN." : error;
            _revision++;
            return false;
        }
        _effect = new(true, plan, _dock) { Fingerprint = _fingerprint }; _closed = true; _revision++;
        return true;
    }

    /// <summary>Back from the map returns to the cockpit; Back there returns to file select.</summary>
    public bool Back()
    {
        VerifyOwner();
        if (_disposed || _closed) return false;
        if (_starMap) return Cockpit();
        return ReturnToSaves();
    }

    public bool ReturnToSaves()
    {
        VerifyOwner();
        if (_disposed || _closed) return false;
        _effect = new(false, default!, _dock); _closed = true; _revision++;
        return true;
    }

    public bool TryTakeEffect(out NativeCampaignShipEffect<TLaunch> effect)
    {
        VerifyOwner(); effect = default;
        if (_disposed || _effect is not { } pending) return false;
        effect = pending; _effect = null; return true;
    }

    public void Dispose() { VerifyOwner(); _disposed = true; _effect = null; ClearProjection(); }
    private void ClearProjection() { _fingerprint = ""; _destinations = Array.Empty<NativeCampaignShipDestination>(); _error = ""; }
    public static bool ValidFingerprint(string? value)
    {
        if (value?.Length != 64) return false;
        foreach (char c in value) if (c is not (>= '0' and <= '9' or >= 'A' and <= 'F' or >= 'a' and <= 'f')) return false;
        return true;
    }
    private static bool TryCopyDestinations(NativeCampaignShipDestination[]? source, out IReadOnlyList<NativeCampaignShipDestination> rows)
    {
        rows = Array.Empty<NativeCampaignShipDestination>();
        if (source?.Length != 5) return false;
        var copy = new NativeCampaignShipDestination[5]; var seen = new bool[5];
        foreach (var row in source)
        {
            if (row.Planet is < 0 or > 4 || seen[row.Planet] || String.IsNullOrWhiteSpace(row.Name)
                || row.Name.Length > 128 || row.Octoliths < 0 || row.OctolithTotal < row.Octoliths
                || row.Artifacts < 0 || row.ArtifactTotal < row.Artifacts) return false;
            seen[row.Planet] = true; copy[row.Planet] = row;
        }
        rows = Array.AsReadOnly(copy); return true;
    }
    private void VerifyOwner()
    {
        if (_owner != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Campaign ship belongs to the UI owner thread.");
    }
}
#endif
