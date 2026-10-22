#if !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ProjectPrime.Studio.IPC;
using ProjectPrime.Studio.Protocol;

namespace MphRead.Mods.StudioIntegration;

/// <summary>Authenticated process coordination. Only Pump dispatches document requests to the UI owner.</summary>
public sealed class StudioApplicationInstance : IDisposable
{
    private const int MaximumPending = 32;
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly object _gate = new();
    private readonly Queue<StudioOpenRequest> _pending = new();
    private StudioInstanceGuard? _guard;
    private bool _disposed;
    public bool IsPrimary => _guard?.IsPrimary == true;
    public bool Forwarded => _guard is { IsPrimary: false, ForwardResult.Accepted: true };
    public string? Error { get; private set; }

    public static StudioOpenRequest CreateRequest(string? documentPath = null, bool recover = false)
    {
        var kind = documentPath == null ? StudioOpenKind.Home
            : Path.GetExtension(documentPath).Equals(".ppdemo", StringComparison.OrdinalIgnoreCase)
                ? StudioOpenKind.Replay
                : Path.GetExtension(documentPath).Equals(".ppclip", StringComparison.OrdinalIgnoreCase)
                    ? StudioOpenKind.Clip
                    : Path.GetExtension(documentPath).Equals(".ppweaponproject", StringComparison.OrdinalIgnoreCase)
                        || Path.GetExtension(documentPath).Equals(".ppweapon", StringComparison.OrdinalIgnoreCase)
                            ? StudioOpenKind.Weapon : StudioOpenKind.Map;
        return new(Guid.NewGuid(), kind, documentPath, recover);
    }

    public static StudioApplicationInstance AcquireStudioInstance(string[] args,
        string? installationDirectory = null, string? userDataDirectory = null)
    {
        string? path = null;
        StudioOpenKind kind = StudioOpenKind.Home;
        bool recover = false, safe = false, workspace = false, studio = false;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--studio":
                    if (studio) return new() { Error = "Duplicate Studio startup switch." };
                    studio = true; break;
                case "--recover":
                    if (recover) return new() { Error = "Duplicate Studio recovery switch." };
                    recover = true; break;
                case "--safe-mode":
                    if (safe) return new() { Error = "Duplicate Studio safe-mode switch." };
                    safe = true; break;
                case "--replay-library":
                    if (workspace) return new() { Error = "Specify one Studio workspace." };
                    workspace = true; kind = StudioOpenKind.ReplayLibrary; break;
                case "--weapon":
                    if (workspace) return new() { Error = "Specify one Studio workspace." };
                    workspace = true; kind = StudioOpenKind.Weapon;
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)) path = args[++i];
                    break;
                case "--replay": case "--clip": case "--map":
                    if (workspace || i + 1 >= args.Length)
                        return new() { Error = "Specify one absolute Studio document path." };
                    workspace = true;
                    kind = args[i] == "--replay" ? StudioOpenKind.Replay
                        : args[i] == "--clip" ? StudioOpenKind.Clip : StudioOpenKind.Map;
                    path = args[++i]; break;
                default: return new() { Error = "Unsupported Studio startup argument." };
            }
        }
        return AcquireStudioInstance(new StudioOpenRequest(Guid.NewGuid(), kind, path, recover, safe),
            installationDirectory, userDataDirectory);
    }

    public static StudioApplicationInstance AcquireStudioInstance(StudioOpenRequest request,
        string? installationDirectory = null, string? userDataDirectory = null)
    {
        var instance = new StudioApplicationInstance();
        if (request.Validate() is { } invalid) { instance.Error = invalid; return instance; }
        try
        {
            // Avoid taking the UI synchronization context through startup or forwarding waits.
            instance._guard = Task.Run(() => StudioInstanceGuard.TryAcquireAsync(
                installationDirectory ?? AppContext.BaseDirectory,
                userDataDirectory ?? LocalIpcEndpointStore.DefaultUserDataDirectory,
                request, instance.Enqueue)).GetAwaiter().GetResult();
            if (instance.IsPrimary) instance.Enqueue(request, default).GetAwaiter().GetResult();
            else if (!instance.Forwarded) instance.Error = instance._guard.ForwardResult.Error;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or PlatformNotSupportedException or System.Security.SecurityException)
        { instance.Error = "Studio instance coordination is unavailable."; }
        return instance;
    }

    private Task<StudioRequestResult> Enqueue(StudioOpenRequest request, CancellationToken token)
    {
        if (request.Validate() is { } invalid) return Task.FromResult(StudioRequestResult.Rejected(invalid));
        lock (_gate)
        {
            if (_disposed || token.IsCancellationRequested)
                return Task.FromResult(StudioRequestResult.Rejected("Studio is closing."));
            if (_pending.Count >= MaximumPending)
                return Task.FromResult(StudioRequestResult.Rejected("Studio's document queue is full. Retry shortly."));
            _pending.Enqueue(request);
        }
        return Task.FromResult(StudioRequestResult.Success);
    }

    public int Pump(Action<StudioOpenRequest> open, int maximum = 8)
    {
        ArgumentNullException.ThrowIfNull(open);
        if (_owner != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Studio requests require the startup owner thread.");
        int handled = 0;
        while (handled < Math.Clamp(maximum, 0, MaximumPending))
        {
            StudioOpenRequest request;
            lock (_gate)
            {
                if (_disposed || _pending.Count == 0) break;
                request = _pending.Dequeue();
            }
            open(request);
            handled++;
        }
        return handled;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _pending.Clear();
        }
        _guard?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _guard = null;
    }
}
#endif
