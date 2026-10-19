using System;
using System.Diagnostics;
using System.Net;
using System.Threading;
using MphRead.Formats;

namespace MphRead.Mods.Network;

// Explicit campaign harness UI for a real custom-map lobby. This owns no
// transport, rules, map preparation, load acknowledgement or gameplay state.
internal static class NcxFixtureLobbyJoin
{
    internal const double StableSeconds = 1;
    internal enum InventoryState { Waiting, Ready, Invalid }
    internal readonly record struct Peer(byte Slot, ushort Generation, byte Hunter,
        bool Bot, bool Spectating, bool Ready, MapAvailabilityState Map);

    internal static bool Eligible(bool fixtureActive, bool campaign, string host,
        Hunter hunter, HitRig.RigMode mode, string? playerCount, out int players, out string reason)
    {
        players = 0;
        reason = "Explicit NCX fixture, campaign evidence, loopback, Samus and two/three declared players are required.";
        bool loopback = string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
            || IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address);
        if (!fixtureActive || !campaign || !loopback || hunter != Hunter.Samus
            || !int.TryParse(playerCount, out players) || players is not (2 or 3)
            || !(mode is HitRig.RigMode.NcxStatic or HitRig.RigMode.NcxLateral
                or HitRig.RigMode.NcxJump or HitRig.RigMode.NcxObserve)
            || mode == HitRig.RigMode.NcxObserve && players != 3) return false;
        reason = "";
        return true;
    }

    internal static bool DeclaredSession(SessionStatePacket session, HitRig.RigMode mode, int players)
    {
        string? requested = HitRig.NcxFixtureRoom(mode);
        bool room = requested != null ? session.Match.RoomKey == requested
            : mode == HitRig.RigMode.NcxObserve && session.Match.RoomKey is
                "NCX EMULATED STATIC" or "NCX EMULATED LATERAL" or "NCX EMULATED JUMP";
        return players is 2 or 3 && room && session.Policy == ServerSessionPolicy.Lobby
            && session.Phase == SessionPhase.Lobby && session.MatchId != 0 && session.AuthorityEpoch != 0
            && session.MaxPlayers == players && session.Match.MapIdentity.IsCustom && session.Match.MapIdentity.IsValid;
    }

    internal static bool SameIdentity(SessionStatePacket initial, SessionStatePacket current)
        => current.AuthorityEpoch == initial.AuthorityEpoch && current.MapGeneration == initial.MapGeneration
            && current.Match == initial.Match && current.MaxPlayers == initial.MaxPlayers
            && current.Policy == initial.Policy && current.RuleFlags == initial.RuleFlags
            && current.WorldProfile == initial.WorldProfile
            && (current.Phase == SessionPhase.Lobby
                ? current.MatchId == initial.MatchId && current.StartGeneration == initial.StartGeneration
                : current.Phase is SessionPhase.Starting or SessionPhase.InMatch
                    && current.MatchId == NetLifecycleTracker.Next(initial.MatchId)
                    && current.StartGeneration == NextStartGeneration(initial.StartGeneration));

    private static uint NextStartGeneration(uint generation)
        => generation == uint.MaxValue ? 1 : generation + 1;

    internal static InventoryState Inventory(Peer[] peers, int players, int localSlot,
        HitRig.RigMode mode, out int nextReadySlot)
    {
        nextReadySlot = -1;
        if (players is not (2 or 3) || localSlot < 0 || localSlot >= players
            || (mode == HitRig.RigMode.NcxObserve ? localSlot != 2 || players != 3 : localSlot > 1)
            || peers.Length > players) return InventoryState.Invalid;
        int slots = 0;
        bool prepared = true;
        foreach (var peer in peers)
        {
            if (peer.Slot >= players || peer.Generation == 0 || (slots & 1 << peer.Slot) != 0
                || peer.Bot || peer.Spectating || peer.Hunter != (byte)Hunter.Samus
                || peer.Map == MapAvailabilityState.Failed) return InventoryState.Invalid;
            slots |= 1 << peer.Slot;
            prepared &= peer.Map == MapAvailabilityState.Ready;
            if (!peer.Ready && (nextReadySlot < 0 || peer.Slot < nextReadySlot)) nextReadySlot = peer.Slot;
        }
        if (slots != (1 << players) - 1 || !prepared) return InventoryState.Waiting;
        return InventoryState.Ready;
    }

    internal sealed class StableRevision
    {
        private ushort _session;
        private double _since;
        private bool _tracking;
        internal bool Observe(ushort session, bool eligible, double now)
        {
            if (!eligible) { _tracking = false; return false; }
            // Roster packet Revision increases on every broadcast; only its
            // SessionRevision acknowledges the lobby configuration revision.
            if (!_tracking || session != _session)
            { _session = session; _since = now; _tracking = true; }
            return now - _since >= StableSeconds;
        }
    }

    internal static bool Join(string host, int port, string name, Hunter hunter, int color = -1)
    {
        bool Fail(string reason)
        {
            Console.Error.WriteLine("[ncx-fixture] lobby join failed: " + reason);
            NetSession.Stop();
            return false;
        }
        if (!Eligible(HitRig.NcxFixtureActive, NetCampaignEvidence.Enabled, host, hunter,
            HitRig.Mode, Environment.GetEnvironmentVariable("PRIME_NCX_FIXTURE_PLAYERS"), out int players, out string error))
            return Fail(error);
        // A literal loopback avoids any hostname re-resolution between the gate
        // and the ordinary Connect call. localhost is explicitly local here.
        string address = host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ? "127.0.0.1" : host;
        if (!NetLaunch.Connect(address, port, name, hunter, timeoutMs: 30000, color: color))
            return Fail(NetLaunch.LastJoinError);
        if (NetSession.ServerSession is not { } initial || !DeclaredSession(initial, HitRig.Mode, players))
            return Fail("Actual lobby room, packaged map identity, capacity or policy differs from the declared NCX fixture.");
        var clock = Stopwatch.StartNew();
        var stable = new StableRevision();
        var generations = new ushort[players];
        LobbyCommandType? outstanding = null;
        bool readyAcknowledged = false;
        while (clock.Elapsed.TotalSeconds < 90)
        {
            NetSession.Pump();
            if (!NetSession.Active || NetSession.Refused || NetSession.SessionTimedOut)
                return Fail("Connection closed, refused or timed out during ordinary lobby preparation.");
            if (NetSession.MapPreparation is { State: MapAvailabilityState.Failed })
                return Fail("Ordinary map preparation failed: " + NetSession.MapPreparationMessage);
            if (NetSession.ServerSession is not { } session || !SameIdentity(initial, session))
                return Fail("The frozen stream, map, configuration or start identity changed unexpectedly.");
            if (outstanding.HasValue && !NetSession.LobbyCommandPending && NetSession.LobbyMessage.Length != 0)
                return Fail("Ordinary lobby command refused or unacknowledged: " + NetSession.LobbyMessage);
            var roster = NetSession.LobbyRoster();
            var peers = new Peer[roster.Count];
            for (int i = 0; i < peers.Length; i++)
            {
                byte slot = roster.Slots[i];
                peers[i] = new Peer(slot, roster.Generations[i], roster.Hunters[i], roster.IsBot(i),
                    slot < NetSession.SlotSpectating.Length && NetSession.SlotSpectating[slot], roster.LobbyReady[i],
                    session.MapAvailability != null && slot < session.MapAvailability.Length
                        ? session.MapAvailability[slot] : MapAvailabilityState.Unknown);
            }
            var inventory = Inventory(peers, players, NetSession.LocalSlot, HitRig.Mode, out int nextReadySlot);
            if (inventory == InventoryState.Invalid || roster.ContainsBots)
                return Fail("Actual assigned slots must be exactly the declared Samus human roles, without bots or spectators; map failure is fatal.");
            foreach (var peer in peers)
            {
                if (generations[peer.Slot] != 0 && generations[peer.Slot] != peer.Generation)
                    return Fail("A declared participant slot was replaced during lobby preparation.");
                generations[peer.Slot] = peer.Generation;
            }
            bool aligned = roster.SessionRevision == session.Revision && roster.MatchId == session.MatchId
                && roster.AuthorityEpoch == session.AuthorityEpoch;
            bool allPrepared = aligned && inventory == InventoryState.Ready && NetSession.RequiredMapReady
                && session.OwnerSlot < players;
            if (outstanding == LobbyCommandType.SetReady && !NetSession.LobbyCommandPending
                && aligned && NetSession.SlotLobbyReady[NetSession.LocalSlot])
            { readyAcknowledged = true; outstanding = null; }
            if (NetSession.ShouldLoadMatch && allPrepared && nextReadySlot < 0
                && readyAcknowledged && session.ExpectedParticipants == (1 << players) - 1)
            {
                try { NetSession.RequireExactMapForLoad(); }
                catch (Exception exception) { return Fail("Exact map changed at the ordinary load boundary: " + exception.Message); }
                NetLaunch.DisableCheatsForMatch();
                Console.WriteLine("[ncx-fixture] ordinary authenticated lobby start accepted with all declared peers and exact prepared map.");
                return true;
            }
            bool canCommand = session.Phase == SessionPhase.Lobby && allPrepared
                && !NetSession.LobbyCommandPending && outstanding == null;
            if (stable.Observe(session.Revision, canCommand, clock.Elapsed.TotalSeconds))
            {
                // Serialize ready commands by actual assigned slot. No stale-
                // revision bypass or simultaneous startup command race.
                if (nextReadySlot == NetSession.LocalSlot)
                {
                    if (!NetSession.SendLobbyCommand(LobbyCommandType.SetReady, ready: true))
                        return Fail("The ordinary ready command could not be scheduled.");
                    outstanding = LobbyCommandType.SetReady;
                }
                else if (nextReadySlot < 0 && readyAcknowledged && NetSession.LocalIsLobbyOwner)
                {
                    if (!NetSession.SendLobbyCommand(LobbyCommandType.StartMatch))
                        return Fail("The ordinary owner start command could not be scheduled.");
                    outstanding = LobbyCommandType.StartMatch;
                }
            }
            Thread.Sleep(10);
        }
        return Fail("90-second bound elapsed while waiting for exact map preparation, roster/readiness acknowledgement or normal owner start.");
    }
}
