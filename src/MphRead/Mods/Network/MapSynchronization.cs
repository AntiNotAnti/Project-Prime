using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Network;

public sealed class LobbyMapPreparationState
{
    public NetworkMapIdentity RequiredMap { get; init; }
    public string RoomKey { get; init; } = "";
    public volatile MapAvailabilityState State;
    public volatile float Progress;
    public string? Error { get; internal set; }
    internal CancellationTokenSource Cancellation { get; } = new();
    internal Task<PreparedMapInstallation>? Preparation;
    internal Task<bool>? Prewarm;
    internal bool Installed;
    internal Task<MapHash256>? StockVerification;
    public MapHash256 RequiredStockHash { get; init; }
}

public static partial class NetSession
{
    public static LobbyMapPreparationState? MapPreparation { get; private set; }
    public static string MapPreparationMessage => MapPreparation is { } map
        ? $"{map.RoomKey}: {(map.State == MapAvailabilityState.Failed ? map.Error : map.State == MapAvailabilityState.Downloading && map.Progress > 0 ? $"Downloading {map.Progress:P0}" : map.State.ToString())}" : "";
    public static void RetryMapPreparation()
    {
        if (MapPreparation?.State == MapAvailabilityState.Failed) ResetMapPreparation();
    }
    public static bool RequiredMapReady => ServerSession is not { } session || _playback
        || MapPreparation is { State: MapAvailabilityState.Ready } map && map.RequiredMap == session.Match.MapIdentity
            && map.RoomKey == session.Match.RoomKey && (session.Match.MapIdentity.IsCustom
                || !session.StockGameplayHash.IsZero && map.RequiredStockHash == session.StockGameplayHash);
    public static void RequireExactMapForLoad()
    {
        if (_playback || ServerSession is not { } session) return;
        if (!session.Match.MapIdentity.IsCustom)
        {
            if (!Metadata.IsBuiltInRoom(session.Match.RoomKey)) throw new InvalidDataException("The server omitted custom map identity.");
            if (!RequiredMapReady) throw new InvalidDataException("The stock map's gameplay content has not been verified against the server.");
            return;
        }
        if (!RequiredMapReady || !CustomRooms.Installed.HasExact(session.Match.MapIdentity.Content(session.Match.RoomKey)))
            throw new InvalidDataException("The exact map package has not finished preparation or changed on disk.");
    }
    private static double _lastMapReport;
    private static uint _mapReportSequence;

    private static void ResetMapPreparation()
    {
        var old = MapPreparation; MapPreparation = null;
        if (old == null) return;
        old.Cancellation.Cancel();
        // A cancelled waiter may still finish its detached build. Never publish it.
        if (old.Preparation is { } work) _ = work.ContinueWith(t =>
        {
            if (t.Status == TaskStatus.RanToCompletion) t.Result.Dispose();
            _ = t.Exception; old.Cancellation.Dispose();
        }, TaskScheduler.Default);
        else if (old.StockVerification is { } verification) _ = verification.ContinueWith(t =>
        { _ = t.Exception; old.Cancellation.Dispose(); }, TaskScheduler.Default);
        else old.Cancellation.Dispose();
    }

    private static void PumpMapPreparation(double now)
    {
        if (_playback || ServerSession is not { } session || _hostEndPoint == null) return;
        var required = session.Match.MapIdentity;
        if (!required.IsCustom)
        {
            if (MapPreparation == null || MapPreparation.RequiredMap.IsCustom || MapPreparation.RoomKey != session.Match.RoomKey
                || MapPreparation.RequiredStockHash != session.StockGameplayHash)
            {
                ResetMapPreparation();
                var stock = new LobbyMapPreparationState { RoomKey = session.Match.RoomKey,
                    RequiredStockHash = session.StockGameplayHash, State = MapAvailabilityState.Verifying };
                MapPreparation = stock;
                stock.StockVerification = Task.Run(() =>
                {
                    if (stock.RequiredStockHash.IsZero) throw new InvalidDataException("The server did not advertise stock gameplay content identity.");
                    stock.Cancellation.Token.ThrowIfCancellationRequested();
                    var local = NetworkMapIdentity.StockGameplayHash(stock.RoomKey);
                    if (local != stock.RequiredStockHash) throw new InvalidDataException("Stock map gameplay content differs from the server. Repair game-file extraction.");
                    return local;
                });
            }
            var stockState = MapPreparation;
            if (stockState.StockVerification is { IsCompleted: true } verification && stockState.State == MapAvailabilityState.Verifying)
            {
                try { _ = verification.GetAwaiter().GetResult(); stockState.State = MapAvailabilityState.Ready; }
                catch (Exception ex) { stockState.State = MapAvailabilityState.Failed; stockState.Error = ex.GetBaseException().Message; }
            }
            return;
        }
        if (MapPreparation == null || MapPreparation.RequiredMap != required || MapPreparation.RoomKey != session.Match.RoomKey)
        {
            ResetMapPreparation();
            var state = new LobbyMapPreparationState { RequiredMap = required, RoomKey = session.Match.RoomKey, State = MapAvailabilityState.Verifying };
            MapPreparation = state;
            bool localHost = System.Net.IPAddress.IsLoopback(_hostEndPoint.Address);
            state.Preparation = Task.Run(async () =>
            {
                var content = required.Content(state.RoomKey);
                if (Metadata.IsBuiltInRoom(state.RoomKey)) throw new InvalidDataException("A custom map cannot use a built-in room name.");
                if (CustomRooms.Installed.HasExact(content) && CustomRooms.Installed.TryGet(content.MapId, out var installed))
                {
                    state.State = MapAvailabilityState.Building;
                    return await MapPackageInstaller.PrepareAsync(installed.PackagePath, content, state.Cancellation.Token).ConfigureAwait(false);
                }
                string address = string.IsNullOrWhiteSpace(session.MapDownloadSource) ? NetworkMapIdentity.ConfiguredDownloadSource() : session.MapDownloadSource;
                var sourceUri = new Uri(address, UriKind.Absolute);
                if (sourceUri.IsLoopback && !localHost)
                    throw new InvalidDataException("A remote server cannot direct map downloads to this computer's loopback interface.");
                using var client = new MapCommunityClient(address);
                return await client.PrepareExactAsync(content, state.Cancellation.Token,
                    stage => { if (Enum.TryParse<MapAvailabilityState>(stage, out var value)) state.State = value; },
                    progress => state.Progress = progress).ConfigureAwait(false);
            }, state.Cancellation.Token);
        }
        var map = MapPreparation;
        if (map.Preparation is { IsCompleted: true } preparation && !map.Installed && map.State != MapAvailabilityState.Failed)
        {
            try
            {
                var prepared = preparation.GetAwaiter().GetResult();
                // Publication runs from the session pump, after the owning UI has ended the old scene.
                if (MapRuntimeUsage.IsInUse(map.RoomKey)) return;
                map.State = MapAvailabilityState.Installing;
                var definition = prepared.Commit(CustomRooms.UserMapDirectory, initialJoin: _loadedStart == null);
                Metadata.RegisterDownloadedMap(definition);
                prepared.Dispose(); map.Installed = true;
                map.State = MapAvailabilityState.Prewarming;
                Mods.RoomPrewarm.Begin(map.RoomKey);
                map.Prewarm = Task.Run(() => Mods.RoomPrewarm.JoinForLoad(map.RoomKey));
            }
            catch (Exception ex) { map.State = MapAvailabilityState.Failed; map.Error = ex.GetBaseException().Message; }
        }
        if (map.Prewarm is { IsCompleted: true } warmed && map.State == MapAvailabilityState.Prewarming)
        {
            if (warmed.Status == TaskStatus.RanToCompletion && warmed.Result) { map.State = MapAvailabilityState.Ready; _lastMapReport = 0; }
            else { map.State = MapAvailabilityState.Failed; map.Error = "The installed map could not be prewarmed."; }
        }
        if (now - _lastMapReport >= .5)
        {
            _lastMapReport = now;
            uint sequence = ++_mapReportSequence; if(sequence==0) sequence=++_mapReportSequence;
            new MapAvailabilityPacket(session.MatchId, session.AuthorityEpoch, required, map.State,session.MapGeneration,sequence).Write(_scratch);
            _transport?.Send(_hostEndPoint, PacketType.MapAvailability, _scratch.AsSpan(0, MapAvailabilityPacket.Size));
        }
    }
}
