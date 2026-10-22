using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Launcher;

public interface INativeCommunityPreparedImport : IDisposable
{
    string Name { get; }
    string Version { get; }
    string PackageHash { get; }
    bool IsCurrent { get; }
    void Commit();
}
public interface INativeCommunityImportAuthority
{
    bool PickerAvailable { get; }
    object? CaptureContext();
    bool IsCurrentContext(object context);
    Task<INativeCommunityPreparedImport?> PrepareAsync(string path, bool usePicker, object context, CancellationToken token);
}

/// <summary>Review a privately prepared package; only explicit owner-thread confirmation publishes it.</summary>
public sealed class NativeCommunityImportController : IDisposable
{
    private readonly record struct Completion(long Epoch, INativeCommunityPreparedImport? Prepared, bool Failed);
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly INativeCommunityImportAuthority _authority;
    private readonly object _gate = new();
    private readonly Queue<Completion> _done = new();
    private INativeCommunityPreparedImport? _prepared;
    private object? _context;
    private CancellationTokenSource? _work;
    private bool _disposed, _busy, _imported;
    private long _epoch, _revision;
    private string _status = "CHOOSE A .PPMAP PACKAGE OR ENTER ITS PATH";
    public NativeCommunityImportController(INativeCommunityImportAuthority authority)
        => _authority = authority ?? throw new ArgumentNullException(nameof(authority));
    public sealed record View(long Revision, bool Busy, bool CanConfirm, bool Imported,
        bool PickerAvailable, string Name, string Version, string PackageHash, string Status);

    public bool Prepare(string path, bool usePicker)
    {
        Owner();
        if (_busy || usePicker && !_authority.PickerAvailable || !usePicker && (string.IsNullOrWhiteSpace(path) || path.Length > 4096)) return false;
        Cancel(); _imported = false;
        try { _context = _authority.CaptureContext(); }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException) { _context = null; }
        if (_context == null) { _status = "IMPORT UNAVAILABLE // VERIFY GAME FILES AND CLOSE ACTIVE GAMEPLAY"; _revision++; return false; }
        _busy = true; _status = usePicker ? "CHOOSE A LOCAL PACKAGE" : "VERIFYING AND PREPARING LOCAL PACKAGE"; _revision++;
        object context = _context; long epoch = _epoch; var cancel = _work = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            INativeCommunityPreparedImport? prepared = null; bool failed = false;
            try { prepared = await _authority.PrepareAsync(path, usePicker, context, cancel.Token).ConfigureAwait(false); }
            catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException) { failed = true; }
            lock (_gate)
            {
                if (_disposed || cancel.IsCancellationRequested) prepared?.Dispose();
                else _done.Enqueue(new(epoch, prepared, failed));
            }
            cancel.Dispose();
        });
        return true;
    }

    public bool Confirm()
    {
        Owner(); var view = Tick(); if (!view.CanConfirm || _prepared == null) return false;
        var prepared = _prepared; _prepared = null;
        try
        {
            // No await or route transition between the final source check and publication.
            if (_context == null || !_authority.IsCurrentContext(_context) || !prepared.IsCurrent)
                throw new InvalidOperationException("Import context changed.");
            prepared.Commit(); _imported = true; _status = "PACKAGE IMPORTED // AVAILABLE IN INSTALLED AND THE MAP PICKER";
            return true;
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        { _status = "IMPORT COULD NOT BE COMMITTED // VERIFY GAME FILES AND PREPARE AGAIN"; return false; }
        finally { prepared.Dispose(); _context = null; _revision++; }
    }

    public View Tick()
    {
        Owner();
        if (_context != null && !_authority.IsCurrentContext(_context))
        { Cancel(); _status = "GAME FILES CHANGED // PREPARE THE PACKAGE AGAIN"; _revision++; }
        if (_prepared != null && !_prepared.IsCurrent)
        { _prepared.Dispose(); _prepared = null; _status = "PACKAGE CHANGED // PREPARE AGAIN"; _revision++; }
        lock (_gate)
        {
            while (_done.TryDequeue(out var result))
            {
                if (result.Epoch != _epoch) { result.Prepared?.Dispose(); continue; }
                _busy = false; _work = null; _prepared?.Dispose(); _prepared = null;
                if (result.Failed) _status = "PACKAGE UNAVAILABLE OR INVALID // CHOOSE A MODERN .PPMAP PACKAGE AND RETRY";
                else if (result.Prepared == null) _status = "FILE SELECTION CANCELED // CHOOSE A PACKAGE WHEN READY";
                else if (_context == null || !_authority.IsCurrentContext(_context) || !result.Prepared.IsCurrent
                    || !ValidHash(result.Prepared.PackageHash))
                { result.Prepared.Dispose(); _status = "PACKAGE OR GAME FILES CHANGED // PREPARE AGAIN"; }
                else
                { _prepared = result.Prepared; _status = "PACKAGE VERIFIED // REVIEW THE EXACT HASH, THEN CONFIRM IMPORT"; }
                _revision++;
            }
        }
        return new(_revision, _busy, !_busy && _prepared != null && !_imported, _imported,
            _authority.PickerAvailable, _prepared?.Name ?? "", _prepared?.Version ?? "", _prepared?.PackageHash ?? "", _status);
    }
    private static bool ValidHash(string hash) => hash is { Length: 64 }
        && System.Linq.Enumerable.All(hash, c => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');
    public void Cancel()
    {
        Owner(); _epoch++;
        try { _work?.Cancel(); } catch (ObjectDisposedException) { }
        _work = null; _busy = false; _imported = false; _context = null; _prepared?.Dispose(); _prepared = null;
        _status = "CHOOSE A .PPMAP PACKAGE OR ENTER ITS PATH";
        lock (_gate) { while (_done.TryDequeue(out var result)) result.Prepared?.Dispose(); }
        _revision++;
    }
    private void Owner()
    {
        if (Environment.CurrentManagedThreadId != _owner) throw new InvalidOperationException("Local imports require the launcher owner thread.");
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
    public void Dispose()
    {
        if (_disposed) return; Cancel();
        lock (_gate) { _disposed = true; while (_done.TryDequeue(out var result)) result.Prepared?.Dispose(); }
    }
}
