#if !MPHREAD_SERVER
using System;

namespace MphRead.Mods.Launcher.Core;

/// <summary>Detached values from the ending receipt, never a mutable campaign save.</summary>
public readonly record struct NativeCampaignEndingReport(byte Slot, bool Complete, bool SaveCommitted,
    int? CompletionPercent, int Octoliths, uint Deaths, uint HunterKills, uint EnemyKills);

public readonly record struct NativeCampaignEndingSnapshot(long Revision, NativeCampaignEndingReport Report, bool Closed);

/// <summary>One ending receipt can request its return once, including after the command is consumed.</summary>
public sealed class NativeCampaignEndingController : IDisposable
{
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly NativeCampaignEndingReport _report;
    private bool _closed, _pending, _disposed;

    public NativeCampaignEndingController(NativeCampaignEndingReport report) => _report = report;

    public NativeCampaignEndingSnapshot Snapshot()
    {
        VerifyOwner();
        return new(_closed ? 2 : 1, _report, _closed);
    }

    public bool Continue() => RequestReturn();
    public bool Back() => RequestReturn();

    private bool RequestReturn()
    {
        VerifyOwner();
        if (_disposed || _closed) return false;
        _closed = _pending = true;
        return true;
    }

    public bool TryTakeReturn(out NativeCampaignEndingReport receipt)
    {
        VerifyOwner();
        receipt = default;
        if (_disposed || !_pending) return false;
        _pending = false;
        receipt = _report;
        return true;
    }

    public void Dispose()
    {
        VerifyOwner();
        _disposed = true;
        _pending = false;
    }

    private void VerifyOwner()
    {
        if (_owner != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Campaign ending belongs to the UI owner thread.");
    }
}
#endif
