#if (MPHREAD_RMLUI_POC || MPHREAD_RMLUI) && !MPHREAD_SERVER
using System;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Update;

namespace MphRead.Mods.Launcher.RmlUi.Setup;

internal sealed record NativeUpdateCheckResult(UpdateInfo? Available, string Reason);

/// <summary>Existing updater discovery, polled by the native launcher owner. It never manipulates documents or installs a package.</summary>
internal sealed class NativeUpdateMonitor : IDisposable
{
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly Func<CancellationToken, NativeUpdateCheckResult> _check;
    private readonly Func<bool> _enabled;
    private readonly Func<double> _clock;
    private CancellationTokenSource? _requestLifetime;
    private Task<NativeUpdateCheckResult>? _request;
    private UpdateInfo? _pending;
    private double _lastCheck = double.NegativeInfinity;
    private string _promptedTag = "";
    private bool _disposed, _mayPrompt;
    internal string Status { get; private set; } = "";
    internal NativeUpdateMonitor(Func<CancellationToken, NativeUpdateCheckResult>? check = null,
        Func<bool>? enabled = null, Func<double>? clock = null)
    {
        _check = check ?? (cancel => { var update = Updater.Check(cancel); return new(update, UpdateCheck.LastReason ?? ""); });
        _enabled = enabled ?? (() => LauncherPrefs.AutoUpdate && !Updater.Disabled);
        _clock = clock ?? (() => Environment.TickCount64 / 1000d);
    }
    private void Owner()
    { if (Environment.CurrentManagedThreadId != _owner) throw new InvalidOperationException("Native update monitor must be polled on its owner thread."); }
    /// <param name="mayPrompt">True only when routing is safe: no active match, busy operation, modal, or unsaved settings draft.</param>
    internal void Tick(bool mayPrompt)
    {
        Owner(); if (_disposed) return; _mayPrompt = mayPrompt;
        if (!_enabled())
        {
            _requestLifetime?.Cancel(); _pending = null; _mayPrompt = false;
            // A cancelled HTTP operation remains owned until its completion is observed.
            if (_request?.IsCompleted == true)
            { try { _request.GetAwaiter().GetResult(); } catch (Exception) { } _request = null; _requestLifetime?.Dispose(); _requestLifetime = null; }
            return;
        }
        if (_request?.IsCompleted == true)
        {
            var request = _request; _request = null;
            bool cancelled = _requestLifetime?.IsCancellationRequested == true;
            try
            {
                NativeUpdateCheckResult result = request.GetAwaiter().GetResult(); Status = result.Reason;
                if (!cancelled && result.Available is { } available && available.Tag != _promptedTag) _pending = available;
            }
            catch (Exception ex) { Status = ex.GetBaseException().Message; }
            _requestLifetime?.Dispose(); _requestLifetime = null;
        }
        double now = _clock();
        if (_request == null && now - _lastCheck >= 5 * 60)
        {
            _lastCheck = now; _requestLifetime = new(); var cancellation = _requestLifetime.Token;
            _request = Task.Run(() => _check(cancellation));
        }
    }
    internal bool TryTakeAvailable(out UpdateInfo update)
    {
        Owner(); update = default;
        if (_disposed || !_mayPrompt || !_enabled() || _pending == null) return false;
        update = _pending.Value; _pending = null; _promptedTag = update.Tag; return true;
    }
    public void Dispose()
    {
        Owner(); if (_disposed) return; _disposed = true; _pending = null;
        _requestLifetime?.Cancel(); _requestLifetime?.Dispose(); _requestLifetime = null;
    }
}
#endif
