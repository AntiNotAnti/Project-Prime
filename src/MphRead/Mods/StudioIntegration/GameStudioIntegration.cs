#if !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.MapGen;
using MphRead.Mods.Network;
using ProjectPrime.Studio.Protocol;

namespace MphRead.Mods.StudioIntegration;

/// <summary>Authenticated local endpoint and a bounded owner queue. No gameplay timing or network protocol changes.</summary>
public static class GameStudioIntegration
{
    private static readonly ConcurrentQueue<GameStudioOwnerWork> Queue = new();
    private static readonly Dictionary<Guid, StudioGameResult> Playtests = new();
    private static readonly object PlaytestGate = new();
    private static CancellationTokenSource? _lifetime;
    private static LocalIpcServer? _server;
    private static FileStream? _lease;
    private static IDisposable? _installationLease;
    private static string? _descriptorPath;
    private static RenderWindow? _window;
    private static Guid _currentPlaytest;
    private static string? _currentRoom;
    private static int _pendingCount;

    public static void Start()
    {
        if (_server != null) return;
        string data = LocalIpcEndpointStore.DefaultUserDataDirectory;
        string directory = LocalIpcEndpointStore.GetDirectory(AppContext.BaseDirectory, data);
        try
        {
            _installationLease = Update.InstallationLifetime.AcquireApplication(AppContext.BaseDirectory);
            LocalIpcEndpointStore.EnsurePrivateDirectory(Path.GetDirectoryName(directory)!);
            LocalIpcEndpointStore.EnsurePrivateDirectory(directory);
            _lease = LocalIpcEndpointStore.OpenLock(directory, StudioEndpointRole.Game);
            string? inherited = Environment.GetEnvironmentVariable("PROJECT_PRIME_STUDIO_LAUNCH_SECRET");
            Environment.SetEnvironmentVariable("PROJECT_PRIME_STUDIO_LAUNCH_SECRET", null);
            string secret = inherited ?? StudioIpcAuthentication.NewSecret();
            _ = StudioIpcAuthentication.CreateProof(secret, StudioIpcAuthentication.NewNonce(), "client", StudioProtocol.StudioIpcVersion);
            var endpoint = new StudioEndpointDescriptor(StudioProtocol.StudioIpcVersion,
                "ProjectPrime.Game." + Guid.NewGuid().ToString("N")[..16], secret, Environment.ProcessId, StudioEndpointRole.Game);
            _lifetime = new();
            CancellationToken gameLifetime = _lifetime.Token;
            var broker = new GameStudioBroker(DispatchAsync, CheckPublication, Launch, Playtest,
                (refresh, token) => refresh ? HunterLicenseClient.RefreshCommunityMapTicketAsync(token) : HunterLicenseClient.GetCommunityMapTicketAsync(token));
            _server = new(endpoint, async (envelope, token) =>
            {
                if (envelope.Type != "Game" || envelope.GameRequest == null)
                    return new(StudioProtocol.StudioIpcVersion, "Error", envelope.RequestId, Error: "The game broker command is unsupported.");
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, gameLifetime);
                StudioGameResult result = await broker.HandleAsync(envelope.GameRequest, linked.Token).ConfigureAwait(false);
                return new(StudioProtocol.StudioIpcVersion, "Result", envelope.RequestId, GameResult: result);
            });
            LocalIpcEndpointStore.Write(directory, endpoint);
            _descriptorPath = LocalIpcEndpointStore.GetDescriptorPath(AppContext.BaseDirectory, data, StudioEndpointRole.Game);
            _server.Start();
            DebugLog.Line("studio", "authenticated local Studio game broker ready; IPC version 1");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or PlatformNotSupportedException)
        {
            DebugLog.Line("studio", "Studio game broker unavailable: " + ex.Message);
            Stop();
        }
    }

    public static void Stop() => StopAsync().GetAwaiter().GetResult();
    public static async Task StopAsync()
    {
        _lifetime?.Cancel();
        while (Queue.TryDequeue(out var work)) { Interlocked.Decrement(ref _pendingCount); work.CancelPendingAndRelease(); }
        LocalIpcServer? server = Interlocked.Exchange(ref _server, null);
        if (server != null) await server.DisposeAsync().ConfigureAwait(false);
        if (_descriptorPath != null)
        {
            try { File.Delete(_descriptorPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            _descriptorPath = null;
        }
        _lease?.Dispose(); _lease = null;
        _installationLease?.Dispose(); _installationLease = null;
        _lifetime?.Dispose(); _lifetime = null;
        _window = null;
        lock (PlaytestGate)
        {
            if (_currentPlaytest != Guid.Empty && Playtests.TryGetValue(_currentPlaytest, out var current))
                Playtests[_currentPlaytest] = current with { PlaytestState = StudioPlaytestState.Ended, Error = "The game closed." };
            _currentPlaytest = Guid.Empty; _currentRoom = null;
        }
    }

    public static void PumpOwnerThread(RenderWindow window)
    {
        _window = window;
        for (int index = 0; index < 8 && Queue.TryDequeue(out var work); index++)
        {
            Interlocked.Decrement(ref _pendingCount);
            work.Execute();
        }
        lock (PlaytestGate)
        {
            if (_currentPlaytest == Guid.Empty || !Playtests.TryGetValue(_currentPlaytest, out var current)) return;
            if (Shell.IsExternalStudioPlaytestRunning(_currentRoom!))
                Playtests[_currentPlaytest] = current with { PlaytestState = StudioPlaytestState.Started };
            else if (!Shell.ExternalStudioLaunchPending)
            {
                if (current.PlaytestState == StudioPlaytestState.Started)
                    Playtests[_currentPlaytest] = current with { PlaytestState = StudioPlaytestState.Ended };
                else if (current.PlaytestState == StudioPlaytestState.Accepted)
                    Playtests[_currentPlaytest] = current with { Accepted = false, PlaytestState = StudioPlaytestState.Rejected,
                        Error = MatchStart.LastError ?? "The game could not start the playtest." };
                _currentPlaytest = Guid.Empty; _currentRoom = null;
            }
        }
    }

    private static Task<StudioGameResult> DispatchAsync(Func<StudioGameResult> action, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (Interlocked.Increment(ref _pendingCount) > 64)
        {
            Interlocked.Decrement(ref _pendingCount);
            return Task.FromResult(StudioGameResult.Rejected("The game owner queue is full. Retry later.", deferred: true));
        }
        var work = new GameStudioOwnerWork(action, token);
        Queue.Enqueue(work);
        // Pending cancellation completes promptly. Once execution begins, await the real action
        // so a caller cannot dispose staged publication files while the owner still uses them.
        return work.Completion;
    }

    private static string? CheckPublication()
    {
        if (!Shell.Active || _window == null) return "The game shell is still starting.";
        if (!GameFiles.Ready) return "Set up game files in Project Prime before installing or playtesting maps.";
        if (_window.HasScene || Shell.ExternalStudioLaunchPending || NetSession.Active)
            return "Return to the front screen and disconnect before installing or playtesting maps.";
        return null;
    }

    private static StudioGameResult Launch(MapDefinition definition, StudioPlaytestOptions options, Guid id, bool host)
    {
        if (host)
        {
            if (!Shell.OpenExternalStudioHosting(definition.Name, options.CommunityAddress)) return StudioGameResult.Rejected("The game hosting screen is unavailable.");
            return new(true);
        }
        if (!Shell.QueueExternalStudioPlaytest(definition.Name, options)) return StudioGameResult.Rejected("The game is busy.", deferred: true);
        var result = new StudioGameResult(true, PlaytestId: id, PlaytestState: StudioPlaytestState.Accepted);
        lock (PlaytestGate)
        {
            if (Playtests.Count >= 64)
            {
                Guid oldest = System.Linq.Enumerable.First(Playtests.Keys);
                Playtests.Remove(oldest);
            }
            Playtests[id] = result; _currentPlaytest = id; _currentRoom = definition.Name;
        }
        return result;
    }

    private static StudioGameResult Playtest(Guid id, bool stop)
    {
        lock (PlaytestGate)
        {
            if (!Playtests.TryGetValue(id, out var current)) return StudioGameResult.Rejected("The playtest response is stale or unknown.");
            if (stop)
            {
                if (_currentPlaytest != id) return StudioGameResult.Rejected("The playtest response is stale; no current match was changed.");
                Shell.StopExternalStudioPlaytest(_currentRoom!);
                current = current with { PlaytestState = StudioPlaytestState.Ended };
                Playtests[id] = current; _currentPlaytest = Guid.Empty; _currentRoom = null;
            }
            return current;
        }
    }
}
#endif
