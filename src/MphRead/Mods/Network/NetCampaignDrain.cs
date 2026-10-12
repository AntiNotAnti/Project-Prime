using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using MphRead.Entities;

namespace MphRead.Mods.Network;

/// <summary>Opt-in network test shutdown barrier. Never participates in normal play.</summary>
internal static class NetCampaignDrain
{
    private static readonly string? StatusPath = Environment.GetEnvironmentVariable("PRIME_NET2_DRAIN_STATUS");
    private static readonly string? ReleasePath = Environment.GetEnvironmentVariable("PRIME_NET2_DRAIN_RELEASE");
    private static readonly bool Server = Environment.GetEnvironmentVariable("PRIME_NET2_DRAIN_ROLE") == "server";
    internal static readonly double Seconds = Double.TryParse(Environment.GetEnvironmentVariable("PRIME_NET2_DRAIN_SECONDS"),
        NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) ? seconds : 0;
    internal static bool Enabled => Seconds > 0 && StatusPath != null && ReleasePath != null;
    internal static bool Quiescing { get; private set; }
    private static long _quiescedAt, _lastPublish;
    private static NetTransport? _transport;
    private static ulong _epoch;
    private static ushort _match;
    private static uint _semantic, _award;
    private static int _projectiles, _bombs, _pendingRespawns, _turrets;
    private static string? _error;
    private static Release? _release;
    private static QuiesceRequest? _quiesceRequest;
    internal sealed record QuiesceRequest(ulong Epoch, ushort Match);
    private static object? _atRelease;
    private static readonly HashSet<string> Offered = new(), Accepted = new();
    internal sealed record Release(string Status, ulong Epoch, ushort Match, uint Semantic, uint Award, ulong Bomb = 0);

    internal static void Begin()
    {
        if (!Enabled || Quiescing) return;
        Quiescing = true;
        _quiescedAt = Stopwatch.GetTimestamp();
        Console.WriteLine("[net2-drain] scripted actions quiescing; simulation continues");
    }

    internal static void Shot(ShotKey key, bool offered)
    {
        if (!Enabled) return;
        string value = $"{key.AuthorityEpoch}:{key.MatchId}:{key.ShooterSlot}:{key.Generation}:{key.LifeId}:{key.ShotId}";
        var keys = offered ? Offered : Accepted;
        if (keys.Contains(value)) return;
        if (keys.Count >= 16384) { _error = "bounded drain shot-key evidence overflow"; return; }
        keys.Add(value);
    }

    internal static void Fact(ulong epoch, ushort match, uint id, bool award, bool baseline)
    {
        if (!Enabled || !Server || baseline) return;
        if (_epoch != epoch || _match != match) { _epoch = epoch; _match = match; _semantic = _award = 0; }
        if (award) _award = Math.Max(_award, id); else _semantic = Math.Max(_semantic, id);
    }

    internal static void Transport(NetTransport transport) { if (Enabled) _transport = transport; }

    internal static void Tick(Scene scene)
    {
        if (!Enabled) return;
        long now = Stopwatch.GetTimestamp();
        if (_lastPublish != 0 && Stopwatch.GetElapsedTime(_lastPublish, now).TotalSeconds < .2) return;
        _lastPublish = now;
        _projectiles = _bombs = _pendingRespawns = _turrets = 0;
        foreach (var player in scene.Players.Items)
            if (player.LoadFlags.TestFlag(LoadFlags.Active) && player.Health > 0
                && player.Hunter == Hunter.Weavel
                && player.ModCaptureForm().TurretActive && player.Halfturret.Health > 0) _turrets++;
        if (Server && scene.GameState.MatchState == MatchState.InProgress)
        {
            foreach (var player in scene.Players.Items)
            {
                int slot = player.SlotIndex;
                bool eliminated = (scene.GameState.Mode is GameMode.Survival or GameMode.SurvivalTeams or GameMode.OneInTheChamber)
                    && scene.GameState.TeamDeaths[slot] > scene.GameState.PointGoal;
                // Death-to-spawn native timers can outlive the claim grace.
                // Keep simulating until their next lifecycle fact has converged.
                if (player.LoadFlags.TestFlag(LoadFlags.Active) && player.Health == 0
                    && NetPlayerLifecycle.Get(slot) != 0 && !NetHooks.SuppressSpawn(player) && !eliminated)
                    _pendingRespawns++;
            }
        }
        foreach (var beam in scene.GetBeamProjectileEntities()) if (beam.Lifespan > 0) _projectiles++;
        foreach (var bomb in scene.GetBombEntities()) if (bomb.Countdown > 0) _bombs++;
        if (Server)
        {
            // The bus includes generated events even before retained fanout admits them.
            _semantic = Math.Max(_semantic, scene.MatchEvents.Bus.LastEvent.EventId);
        }
        ReadQuiesceRequest();
        ReadRelease();
        Publish();
    }

    private static void ReadQuiesceRequest()
    {
        if (!Server || !Enabled || Quiescing || _quiesceRequest != null) return;
        try
        {
            string path = ReleasePath + ".quiesce";
            if (!File.Exists(path)) return;
            var request = JsonSerializer.Deserialize<QuiesceRequest>(File.ReadAllText(path));
            if (request == null || request.Epoch == 0 || request.Match == 0
                || request.Epoch != NetSession.AuthorityEpoch || request.Match != NetSession.CurrentMatchId)
            { _error = "server quiesce request has wrong context"; return; }
            _quiesceRequest = request;
            Begin();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        { _error = "server quiesce request unreadable: " + ex.Message; }
    }

    private static void ReadRelease()
    {
        if (_release != null || !Enabled) return;
        try
        {
            if (File.Exists(ReleasePath))
            {
                _release = JsonSerializer.Deserialize<Release>(File.ReadAllText(ReleasePath!));
                var receiver = NetSession.SemanticReceived;
                _atRelease = new { Epoch = NetSession.AuthorityEpoch, Match = NetSession.CurrentMatchId,
                    SemanticLatest = Server ? _semantic : receiver.LatestEvent,
                    SemanticPresented = Server ? _semantic : receiver.PresentedEvent,
                    AwardLatest = Server ? _award : receiver.LatestAward,
                    AwardPresented = Server ? _award : receiver.PresentedAward,
                    BombFrontier = NetBombs.LiveScene?.Bombs.Frontier ?? 0,
                    BombPendingRequests = NetBombs.PendingCount,
                    TurretPendingOutcomes = NetSession.PendingTurretOutcomes,
                    PickupPendingWork = NetSession.PendingPickupWork,
                    BombPendingEvents = (NetBombs.LiveScene?.Bombs.PendingEvents ?? 0) + NetSession.PendingBombPackets,
                    BombFaulted = NetBombs.LiveScene?.Bombs.Faulted ?? false };
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        { _error = "release token unreadable: " + ex.Message; }
    }

    internal static bool Completed
    {
        get
        {
            if (!Enabled || !Quiescing) return false;
            if (_release != null) return true;
            // The runner normally releases a failed barrier first, allowing evidence export.
            if (Stopwatch.GetElapsedTime(_quiescedAt).TotalSeconds <= Seconds + 5) return false;
            _error = "bounded drain timeout without release token";
            return true;
        }
    }

    internal static bool Succeeded => !Enabled || _error == null && _release is { Status: "PASS" }
        && _release.Epoch == NetSession.AuthorityEpoch && _release.Match == NetSession.CurrentMatchId
        && NetSession.SemanticReceived.PresentedEvent >= _release.Semantic
        && NetSession.SemanticReceived.PresentedAward >= _release.Award
        && (NetBombs.LiveScene?.Bombs.Frontier ?? 0) == _release.Bomb;

    internal static object Capture()
    {
        ReadRelease();
        var receiver = NetSession.SemanticReceived;
        var transport = (_transport ?? NetSession.EvidenceTransport)?.CaptureBudget();
        return new { Enabled, Role = Server ? "server" : "client", Quiescing,
            Epoch = NetSession.AuthorityEpoch, Match = NetSession.CurrentMatchId, Frame = NetSession.NetFrame,
            SemanticLatest = Server ? _semantic : receiver.LatestEvent,
            SemanticPresented = Server ? _semantic : receiver.PresentedEvent,
            AwardLatest = Server ? _award : receiver.LatestAward,
            AwardPresented = Server ? _award : receiver.PresentedAward,
            BombFrontier = NetBombs.LiveScene?.Bombs.Frontier ?? 0,
            BombPendingRequests = NetBombs.PendingCount,
                    TurretPendingOutcomes = NetSession.PendingTurretOutcomes,
                    PickupPendingWork = NetSession.PendingPickupWork,
            BombPendingEvents = (NetBombs.LiveScene?.Bombs.PendingEvents ?? 0) + NetSession.PendingBombPackets,
            BombFaulted = NetBombs.LiveScene?.Bombs.Faulted ?? false,
            PendingRespawns = _pendingRespawns,
            ActiveProjectiles = _projectiles, ActiveBombs = _bombs, ActiveTurrets = _turrets, ClaimsPending = Server ? NetHitClaims.ClaimsPendingCurrent : NetHitClaims.OutgoingReceipts,
            ClaimGraceSeconds = 2.0 * NetHitClaims.MaxGraceFrames / 60,
            OfferedShotKeys = Offered.OrderBy(key => key).ToArray(), AcceptedShotKeys = Accepted.OrderBy(key => key).ToArray(),
            Reliable = transport?.Endpoints.Select(peer => new { peer.Endpoint, peer.Connection.ConnectionId,
                peer.Reliable.Pending, peer.Reliable.Failed }).ToArray(),
            ConnectionLost = NetSession.ConnectionLost, Error = _error, QuiesceRequest = _quiesceRequest,
            Release = _release, AtRelease = _atRelease };
    }

    private static void Publish()
    {
        try
        {
            string temp = StatusPath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(Capture()));
            File.Move(temp, StatusPath!, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { _error = "status publication failed: " + ex.Message; }
    }
}
