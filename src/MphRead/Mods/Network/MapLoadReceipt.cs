using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using MphRead.Entities;

namespace MphRead.Mods.Network;

/// <summary>
/// Pure readiness state for the map-load receipt. A ready time is only
/// accepted after room construction, Scene.OnLoad, a simulation step, an
/// observation that every requested player is live, and the next successful
/// presentation, in that order.
/// </summary>
internal sealed class MapLoadReadiness
{
    private readonly long _requestTimestamp;
    private long? _constructedAt;
    private long? _sceneLoadedAt;
    private long? _firstSimulationAt;
    private long? _readyPresentedAt;
    private bool _allPlayersObservedLive;
    private string? _failure;

    internal MapLoadReadiness(long requestTimestamp) => _requestTimestamp = requestTimestamp;

    internal long? ConstructorCompletedAt => _constructedAt;
    internal long? SceneLoadedAt => _sceneLoadedAt;
    internal long? FirstSimulationAt => _firstSimulationAt;
    internal long? ReadyPresentedAt => _readyPresentedAt;
    internal string? Failure => _failure;
    internal bool IsReady => _readyPresentedAt.HasValue && _failure == null;

    internal bool ConstructorCompleted(long timestamp)
        => Record(ref _constructedAt, timestamp, _requestTimestamp, "constructor");

    internal bool SceneLoadCompleted(long timestamp)
        => Record(ref _sceneLoadedAt, timestamp, _constructedAt, "scene-load");

    internal void BeginSimulationStep() => _allPlayersObservedLive = false;

    internal bool SimulationCompleted(long timestamp)
    {
        if (_firstSimulationAt.HasValue) return true;
        return Record(ref _firstSimulationAt, timestamp, _sceneLoadedAt, "first-simulation");
    }

    internal void ObservePlayers(bool allRequestedPlayersLive)
    {
        if (_firstSimulationAt.HasValue && _failure == null)
            _allPlayersObservedLive = allRequestedPlayersLive;
    }

    internal bool SuccessfulPresent(long timestamp, bool allRequestedPlayersLive)
    {
        if (_readyPresentedAt.HasValue) return true;
        if (_failure != null || !_allPlayersObservedLive || !allRequestedPlayersLive
            || !_firstSimulationAt.HasValue)
            return false;
        if (timestamp < _firstSimulationAt.Value)
            return Reject("presentation timestamp preceded the first simulation");
        _readyPresentedAt = timestamp;
        return true;
    }

    internal void Fail(string reason)
    {
        if (_failure == null) _failure = reason;
        _allPlayersObservedLive = false;
    }

    internal string? NotReadyReason()
    {
        if (_failure != null) return _failure;
        if (!_constructedAt.HasValue) return "window construction did not complete";
        if (!_sceneLoadedAt.HasValue) return "Scene.OnLoad did not complete";
        if (!_firstSimulationAt.HasValue) return "no simulation step completed";
        return "no successful presentation followed an observation with every requested player live";
    }

    private bool Record(ref long? field, long timestamp, long? predecessor, string stage)
    {
        if (_failure != null) return false;
        if (field.HasValue) return true;
        if (!predecessor.HasValue || timestamp < predecessor.Value)
            return Reject($"invalid {stage} stage ordering");
        field = timestamp;
        return true;
    }

    private bool Reject(string reason)
    {
        Fail(reason);
        return false;
    }
}

/// <summary>Opt-in runtime receipt. Hashing and serialization stay outside the timed path.</summary>
internal sealed class MapLoadReceipt : IDisposable
{
    private readonly string _path;
    private readonly string _room;
    private readonly int _requestedPlayers;
    private readonly string _mode;
    private readonly long _requestTimestamp;
    private readonly DateTimeOffset _startedUtc;
    private readonly Process? _process;
    private readonly TimeSpan? _processCpuAtRequest;
    private readonly long _allocatedAtRequest;
    private readonly int[] _gcAtRequest = new int[3];
    private readonly MapLoadReadiness _readiness;
    private readonly StageSample _requestSample;
    private StageSample? _constructorSample;
    private StageSample? _sceneLoadSample;
    private StageSample? _firstSimulationSample;
    private StageSample? _readyPresentSample;
    private StageSample? _runCompleteSample;
    private string? _failure;
    private string? _glVendor;
    private string? _glRenderer;
    private string? _glVersion;
    private string? _loadedRoom;
    private int? _loadedRoomCount;
    private int? _scenePlayerCount;
    private int? _windowWidth;
    private int? _windowHeight;
    private int? _exitCode;
    private long _lastTick;
    private int _disposed;

    internal MapLoadReceipt(string path, string room, int requestedPlayers, string mode)
    {
        _requestTimestamp = Stopwatch.GetTimestamp();
        _lastTick = _requestTimestamp;
        _startedUtc = DateTimeOffset.UtcNow;
        _path = Path.GetFullPath(path);
        _room = room;
        _requestedPlayers = requestedPlayers;
        _mode = mode;
        _readiness = new MapLoadReadiness(_requestTimestamp);
        _allocatedAtRequest = GC.GetTotalAllocatedBytes(precise: false);
        for (int i = 0; i < _gcAtRequest.Length; i++) _gcAtRequest[i] = GC.CollectionCount(i);
        try
        {
            _process = Process.GetCurrentProcess();
            _processCpuAtRequest = _process.TotalProcessorTime;
        }
        catch (Exception ex) when (ex is InvalidOperationException
            or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            _process?.Dispose();
            _process = null;
            _processCpuAtRequest = null;
        }
        _requestSample = Capture(_requestTimestamp);
    }

    internal void ConstructorCompleted()
    {
        long now = Stopwatch.GetTimestamp();
        if (_readiness.ConstructorCompleted(now)) _constructorSample = Capture(now);
        _lastTick = Math.Max(_lastTick, now);
    }

    internal void SceneLoadCompleted()
    {
        long now = Stopwatch.GetTimestamp();
        if (_readiness.SceneLoadCompleted(now)) _sceneLoadSample = Capture(now);
        _lastTick = Math.Max(_lastTick, now);
    }

    internal void BeginSimulationStep() => _readiness.BeginSimulationStep();

    internal void SimulationCompleted()
    {
        long now = Stopwatch.GetTimestamp();
        bool first = !_readiness.FirstSimulationAt.HasValue;
        if (_readiness.SimulationCompleted(now) && first) _firstSimulationSample = Capture(now);
        _lastTick = Math.Max(_lastTick, now);
    }

    internal void ObservePlayers(bool allRequestedPlayersLive) => _readiness.ObservePlayers(allRequestedPlayersLive);

    internal void SuccessfulPresent(bool allRequestedPlayersLive)
    {
        if (_readiness.ReadyPresentedAt.HasValue) return;
        long now = Stopwatch.GetTimestamp();
        if (_readiness.SuccessfulPresent(now, allRequestedPlayersLive)) _readyPresentSample = Capture(now);
        _lastTick = Math.Max(_lastTick, now);
    }

    internal void SetLoadedScene(string? roomName, int roomCount, int playerCount)
    {
        _loadedRoom = roomName;
        _loadedRoomCount = roomCount;
        _scenePlayerCount = playerCount;
    }

    internal void SetGraphicsContext(string? vendor, string? renderer, string? version,
        int width, int height)
    {
        _glVendor = vendor;
        _glRenderer = renderer;
        _glVersion = version;
        _windowWidth = width;
        _windowHeight = height;
    }

    internal void Complete(int exitCode)
    {
        _exitCode = exitCode;
        long now = Stopwatch.GetTimestamp();
        _runCompleteSample = Capture(now);
        _lastTick = Math.Max(_lastTick, now);
    }

    internal void RecordFailure(Exception error)
    {
        _failure ??= error.GetType().Name + ": " + error.Message;
        _readiness.Fail(_failure);
    }

    internal void Write()
    {
        if (System.Threading.Interlocked.Exchange(ref _disposed, 1) != 0) return;
        string? temporaryPath = null;
        try
        {
            long now = Stopwatch.GetTimestamp();
            StageSample finished = Capture(Math.Max(_lastTick, now));
            var document = new
            {
                schema = 1,
                kind = "map-load-receipt",
                scope = "map-audit request immediately before window construction; excludes process startup",
                startedUtc = _startedUtc,
                finishedUtc = DateTimeOffset.UtcNow,
                room = _room,
                mode = _mode,
                requestedPlayers = _requestedPlayers,
                loadedRoom = _loadedRoom,
                loadedRoomCount = _loadedRoomCount,
                scenePlayerCount = _scenePlayerCount,
                window = new { width = _windowWidth, height = _windowHeight },
                process = new
                {
                    operatingSystem = RuntimeInformation.OSDescription,
                    architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                    logicalProcessors = Environment.ProcessorCount
                },
                graphics = new
                {
                    vendor = _glVendor,
                    renderer = _glRenderer,
                    version = _glVersion
                },
                ready = _readiness.IsReady && _failure == null,
                readyElapsedMilliseconds = _readiness.ReadyPresentedAt is long readyAt
                    ? ElapsedMilliseconds(readyAt) : (double?)null,
                notReadyReason = _readiness.IsReady && _failure == null
                    ? null : _failure ?? _readiness.NotReadyReason(),
                outcome = _failure != null ? "exception"
                    : _exitCode is null ? "incomplete"
                    : _exitCode == 0 ? "passed" : "audit-failed",
                exitCode = _exitCode,
                failure = _failure,
                stages = new
                {
                    request = _requestSample,
                    constructorCompleted = _constructorSample,
                    sceneOnLoadCompleted = _sceneLoadSample,
                    firstSimulationCompleted = _firstSimulationSample,
                    firstSuccessfulPresentWithAllPlayersLive = _readyPresentSample,
                    runCompleted = _runCompleteSample,
                    receiptFinalized = finished
                }
            };
            string json = JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true });
            string? directory = Path.GetDirectoryName(_path);
            if (!String.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            temporaryPath = _path + ".pending-" + Guid.NewGuid().ToString("N");
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, _path, overwrite: true);
            temporaryPath = null;
            Console.WriteLine($"[mapload] receipt {_path} ready={_readiness.IsReady && _failure == null}"
                + $" requestToReadyMs={(_readiness.ReadyPresentedAt is long tick ? ElapsedMilliseconds(tick).ToString("F3") : "unavailable")}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException or System.Security.SecurityException or System.Text.Json.JsonException)
        {
            Console.Error.WriteLine($"[mapload] receipt write failed: {ex.Message}");
        }
        finally
        {
            if (temporaryPath != null)
            {
                try { File.Delete(temporaryPath); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            _process?.Dispose();
        }
    }

    public void Dispose() => Write();

    private StageSample Capture(long timestamp)
    {
        double? processCpu = null;
        long? workingSet = null;
        long? peakWorkingSet = null;
        if (_process != null)
        {
            try
            {
                _process.Refresh();
                if (_processCpuAtRequest is TimeSpan baseline)
                    processCpu = Math.Max(0, (_process.TotalProcessorTime - baseline).TotalMilliseconds);
                workingSet = _process.WorkingSet64;
                peakWorkingSet = _process.PeakWorkingSet64;
            }
            catch (Exception ex) when (ex is InvalidOperationException
                or System.ComponentModel.Win32Exception or NotSupportedException)
            { }
        }
        var collections = new int[3];
        for (int i = 0; i < collections.Length; i++)
            collections[i] = Math.Max(0, GC.CollectionCount(i) - _gcAtRequest[i]);
        return new StageSample(
            Math.Max(0, timestamp - _requestTimestamp),
            ElapsedMilliseconds(timestamp),
            processCpu,
            workingSet,
            peakWorkingSet,
            Math.Max(0, GC.GetTotalAllocatedBytes(precise: false) - _allocatedAtRequest),
            collections);
    }

    private double ElapsedMilliseconds(long timestamp)
        => Math.Max(0, timestamp - _requestTimestamp) * 1000.0 / Stopwatch.Frequency;

    private sealed record StageSample(long elapsedStopwatchTicks, double elapsedMilliseconds,
        double? processCpuMilliseconds, long? workingSetBytes, long? peakWorkingSetBytes,
        long allocatedBytes, int[] gcCollections);
}

internal static class MapLoadReceiptChecks
{
    internal static int Run()
    {
        int failures = 0;
        void Check(bool passed, string name)
        {
            Console.WriteLine($"[maploadreceiptcheck] {(passed ? "PASS" : "FAIL")}: {name}");
            if (!passed) failures++;
        }

        var valid = new MapLoadReadiness(100);
        Check(valid.ConstructorCompleted(110)
            && valid.SceneLoadCompleted(130)
            && valid.SimulationCompleted(140), "lifecycle stages accept monotonic order");
        valid.BeginSimulationStep();
        valid.ObservePlayers(allRequestedPlayersLive: false);
        Check(!valid.SuccessfulPresent(150, allRequestedPlayersLive: true)
            && !valid.IsReady, "present cannot pass without an all-player live observation");
        valid.BeginSimulationStep();
        valid.ObservePlayers(allRequestedPlayersLive: true);
        Check(valid.SuccessfulPresent(170, allRequestedPlayersLive: true)
            && valid.IsReady && valid.ReadyPresentedAt == 170,
            "ready is timestamped only after all-player observation and presentation");

        var missing = new MapLoadReadiness(200);
        missing.ConstructorCompleted(210);
        missing.SceneLoadCompleted(220);
        missing.SimulationCompleted(230);
        missing.BeginSimulationStep();
        missing.ObservePlayers(allRequestedPlayersLive: false);
        Check(!missing.SuccessfulPresent(240, allRequestedPlayersLive: false)
            && !missing.IsReady, "missing live players cannot claim ready");
        Check(missing.NotReadyReason()?.Contains("every requested player live", StringComparison.Ordinal) == true,
            "missing readiness has an explicit reason");

        var resetObservation = new MapLoadReadiness(245);
        resetObservation.ConstructorCompleted(246);
        resetObservation.SceneLoadCompleted(247);
        resetObservation.SimulationCompleted(248);
        resetObservation.BeginSimulationStep();
        resetObservation.ObservePlayers(allRequestedPlayersLive: true);
        resetObservation.BeginSimulationStep();
        Check(!resetObservation.SuccessfulPresent(249, allRequestedPlayersLive: true)
            && !resetObservation.IsReady,
            "a new simulation step clears the previous live-player observation");

        var outOfOrder = new MapLoadReadiness(250);
        outOfOrder.ConstructorCompleted(260);
        outOfOrder.SceneLoadCompleted(270);
        outOfOrder.SimulationCompleted(280);
        outOfOrder.BeginSimulationStep();
        outOfOrder.ObservePlayers(allRequestedPlayersLive: true);
        Check(!outOfOrder.SuccessfulPresent(275, allRequestedPlayersLive: true)
            && !outOfOrder.IsReady, "out-of-order presentation cannot claim ready");

        var failed = new MapLoadReadiness(300);
        failed.ConstructorCompleted(310);
        failed.SceneLoadCompleted(320);
        failed.SimulationCompleted(330);
        failed.BeginSimulationStep();
        failed.ObservePlayers(allRequestedPlayersLive: true);
        failed.Fail("synthetic scene-load failure");
        Check(!failed.SuccessfulPresent(340, allRequestedPlayersLive: true)
            && !failed.IsReady && failed.Failure == "synthetic scene-load failure",
            "a failed receipt cannot claim ready");

        var failedAfterReady = new MapLoadReadiness(350);
        failedAfterReady.ConstructorCompleted(360);
        failedAfterReady.SceneLoadCompleted(370);
        failedAfterReady.SimulationCompleted(380);
        failedAfterReady.BeginSimulationStep();
        failedAfterReady.ObservePlayers(allRequestedPlayersLive: true);
        Check(failedAfterReady.SuccessfulPresent(390, allRequestedPlayersLive: true)
            && failedAfterReady.IsReady, "ready can be recorded before a later run failure");
        failedAfterReady.Fail("synthetic post-ready exception");
        Check(!failedAfterReady.IsReady && failedAfterReady.ReadyPresentedAt == 390
            && failedAfterReady.Failure == "synthetic post-ready exception",
            "a failure after ready revokes the ready claim but preserves its timestamp");

        var wrongOrder = new MapLoadReadiness(400);
        Check(!wrongOrder.SceneLoadCompleted(410)
            && !wrongOrder.ConstructorCompleted(420)
            && !wrongOrder.IsReady, "out-of-order lifecycle events are rejected");

        Console.WriteLine($"[maploadreceiptcheck] {(failures == 0 ? "PASS" : "FAIL")}: {failures} failure(s)");
        return failures;
    }
}

public sealed partial class MapAudit
{
    private MapLoadReceipt? _loadReceipt;

    private void PresentLoadFrame()
    {
#if !ANDROID && !MPHREAD_SERVER
        Render.DesktopGraphicsSession.Present(this);
#else
        SwapBuffers();
#endif
        if (_loadReceipt != null)
            _loadReceipt.SuccessfulPresent(AllRequestedPlayersLive());
    }

    private bool AllRequestedPlayersLive()
    {
        if (_loadReceipt == null) return false;
        for (int i = 0; i < _players; i++)
        {
            if (i >= PlayerEntity.Players.Count) return false;
            PlayerEntity player = PlayerEntity.Players[i];
            if (!player.LoadFlags.TestFlag(LoadFlags.Active)
                || !player.LoadFlags.TestFlag(LoadFlags.Spawned)
                || player.Health <= 0)
                return false;
        }
        return true;
    }
}
