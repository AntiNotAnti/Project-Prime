using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Asset-free production grant publication, bootstrap and retained-delivery policy.
// These fixtures do not manufacture a live pickup or multiplayer acceptance run.
internal static class GrantNetworkTests
{
    private static int _checks;
    private const BindingFlags Hidden = BindingFlags.Static | BindingFlags.NonPublic;
    private static void Check(bool value, string label)
    { _checks++; if (!value) throw new InvalidOperationException("Grant network: " + label); }
    private static void Session(string property, object? value)
        => typeof(NetSession).GetProperty(property)!.SetValue(null, value);
    private static ShotKey Life(int slot) => new(11, 7, slot, 1, 1, 0);
    private static TimedGrantRecord Grant(int slot = 0, TimedPowerUp type = TimedPowerUp.DoubleDamage)
        => new((ulong)(slot * 3 + (int)type + 1), new(11, 7, 3, slot * 3 + (int)type + 10, 1),
            Life(slot), type, 100, 100, 1900);
    private static void Authority(uint frame = 100)
    {
        NetGrantNetwork.Reset(); Session(nameof(NetSession.Role), NetRole.Server);
        Session(nameof(NetSession.IsAuthority), true); Session(nameof(NetSession.LocalSlot), -1);
        Session(nameof(NetSession.NetFrame), frame);
    }
    private static List<TimedGrantFact> Decode(ReadOnlySpan<byte> payload)
    {
        Check(NetFactBatch.Validate(PacketType.TimedGrant, payload), "production grant batch validates before individual rows");
        var rows = new List<TimedGrantFact>();
        for (int at = 0; at < payload.Length; at += TimedGrantFact.Size)
        {
            Check(TimedGrantFact.TryRead(payload.Slice(at, TimedGrantFact.Size), out var fact), "published row decodes exactly");
            rows.Add(fact);
        }
        return rows;
    }
    private static void ImmutableTransition()
    {
        Authority(150); var peer = NetGrantNetwork.Join(); var grant = Grant();
        grant.EndFrame = 150; grant.EndReason = TimedGrantEnd.Death;
        NetGrantNetwork.Changed(grant);
        grant.EndFrame = 160; grant.EndReason = TimedGrantEnd.Retired;
        var observed = new List<TimedGrantFact>();
        Check(NetGrantNetwork.Pump(peer, (type, payload) =>
        { Check(type == PacketType.TimedGrant, "transition uses the grant reliable lane"); observed.AddRange(Decode(payload)); return true; }),
            "closed grant transition admits successfully");
        Check(observed.Count == 1 && observed[0].Reason == TimedGrantEnd.Death && observed[0].EndFrame == 150
            && observed[0].Sequence == grant.Sequence && observed[0].Life == grant.Life,
            "published closed-record bytes are immutable after the mutable ledger record changes");
        Session(nameof(NetSession.NetFrame), 200u); var late = NetGrantNetwork.Join();
        var baseline = Decode(late.Baseline.Peek());
        Check(baseline.Single().Reason == TimedGrantEnd.Death && baseline[0].EndFrame == 150 && baseline[0].Resume,
            "late bootstrap retains the published closed outcome rather than re-reading the mutated record");
    }
    private static void DenseLateJoin()
    {
        Authority();
        var state = new PlayerState { SlotIndex = 0, SlotGeneration = 1, LifeId = 1, Health = 99 };
        var intent = new IntentPacket { AuthorityEpoch = 11, MatchId = 7, SlotGeneration = 1, LifeId = 1,
            Frame = 100, Position = new Vector3(1, 2, 3), Aim = Vector3.UnitZ, MoveX = 17, MoveY = -33 };
        byte[] playerBefore = new byte[PlayerState.Size], intentBefore = new byte[IntentPacket.FullSize];
        state.Write(playerBefore); intent.Write(intentBefore);
        var ledger = new NetTimedGrants(11, 7, 3, NetGrantNetwork.Changed);
        var sequences = new Dictionary<(int, TimedPowerUp), ulong>();
        for (int slot = 0; slot < 8; slot++)
        {
            ledger.BeginLife(Life(slot), 100); NetGrantNetwork.BeginLife(Life(slot), 3, 100);
            foreach (var type in Enum.GetValues<TimedPowerUp>())
            {
                Check(ledger.RegisterSpawn(slot * 3 + (int)type + 10, type, out var spawn), "native ledger spawn fixture registered");
                Check(ledger.TryGrant(spawn, Life(slot), 100, 100, out var record) == TimedGrantResult.Granted,
                    "authority ledger assigns each of eight lives each of three timed grants");
                sequences.Add((slot, type), record!.Sequence);
            }
        }
        Session(nameof(NetSession.NetFrame), 700u); var late = NetGrantNetwork.Join();
        Check(late.Baseline.Count == 2 && late.Baseline.Peek().Length == 22 * 53,
            "24 current slot/type facts bootstrap in exactly two packets with at most 22 records");
        var facts = new List<TimedGrantFact>();
        foreach (var packet in late.Baseline)
        {
            Check(packet.Length + NetHeader.Size + 4 <= 1200, "complete grant baseline reliable datagram respects 1200-byte budget");
            facts.AddRange(Decode(packet));
        }
        Check(22 * 53 + NetHeader.Size + 4 == 1194 && facts.Count == 24
            && facts.Select(fact => (fact.Life.ShooterSlot, fact.Type)).Distinct().Count() == 24,
            "all 24 unique life/type identities survive maximum 1194-byte baseline envelope");
        var presentation = new TimedGrantPresentation();
        foreach (var fact in facts)
        {
            Check(fact.Resume && fact.ContactFrame == 100 && fact.EndFrame == 1900 && fact.AuthorityFrame == 700
                && fact.Sequence == sequences[(fact.Life.ShooterSlot, fact.Type)],
                "late join refreshes authority clock/revision without renewing contact, expiry or grant sequence");
            Check(presentation.Accept(fact, 50), "late baseline accepted by production presentation");
            var view = presentation.View(fact.Life, fact.Type, 50);
            Check(view.Remaining == 1200 && view.ConfirmedRemaining == 1200 && view.Resume,
                "late join resumes only the authority's remaining 1200 ticks");
            Check(presentation.View(fact.Life, fact.Type, 60).Remaining == 1190,
                "late join remaining duration continues to decrease rather than restarting");
        }
        ulong before = late.Cursor.Next;
        Check(NetGrantNetwork.Pump(late, (_, _) => false) && late.Baseline.Count == 2 && late.Cursor.Next == before,
            "blocked bootstrap keeps baseline and retained cursor untouched");
        Check(NetGrantNetwork.Pump(late, (_, payload) => { Decode(payload); return true; })
            && late.Baseline.Count == 0 && NetGrantNetwork.Pending(late) == 0, "retry admits complete bootstrap exactly once");
        var stalledBaseline = NetGrantNetwork.Join();
        stalledBaseline.Since = NetPickupNetwork.Now - 16;
        int baselineCalls = 0;
        Check(!NetGrantNetwork.Pump(stalledBaseline, (_, _) => { baselineCalls++; return true; })
            && baselineCalls == 0 && stalledBaseline.Baseline.Count == 2,
            "15-second deadline rejects a stalled bootstrap before admitting any baseline prefix");
        byte[] playerAfter = new byte[PlayerState.Size], intentAfter = new byte[IntentPacket.FullSize];
        state.Write(playerAfter); intent.Write(intentAfter);
        Check(playerBefore.AsSpan().SequenceEqual(playerAfter) && intentBefore.AsSpan().SequenceEqual(intentAfter),
            "grant publication and bootstrap do not alter serialized player or intent fast-lane bytes");
    }
    private static void IndependentCursorsAndFailure()
    {
        Authority(); var first = NetGrantNetwork.Join(); var second = NetGrantNetwork.Join();
        NetGrantNetwork.Changed(Grant()); int firstRows = 0, secondRows = 0;
        Check(NetGrantNetwork.Pump(first, (_, payload) => { firstRows += Decode(payload).Count; return true; })
            && firstRows == 1 && NetGrantNetwork.Pending(first) == 0 && NetGrantNetwork.Pending(second) == 1,
            "one peer's admission advances only its own retained cursor");
        ulong before = second.Cursor.Next;
        Check(NetGrantNetwork.Pump(second, (_, _) => false) && second.Cursor.Next == before,
            "failed ordinary reliable admission remains retryable for the other peer");
        Check(NetGrantNetwork.Pump(second, (_, payload) => { secondRows += Decode(payload).Count; return true; })
            && secondRows == 1 && NetGrantNetwork.Pending(second) == 0, "second peer retries exactly its own pending row");
        NetGrantNetwork.Reset();
        Check(!NetGrantNetwork.Pump(first, (_, _) => true) && !NetGrantNetwork.Pump(second, (_, _) => true),
            "reset invalidates cursors from the previous retained owner");
        Authority(); var stalled = NetGrantNetwork.Join(); NetGrantNetwork.Changed(Grant());
        Check(NetGrantNetwork.Pump(stalled, (_, _) => false), "initial backpressure is retained rather than dropped");
        stalled.Since = NetPickupNetwork.Now - 16;
        Check(!NetGrantNetwork.Pump(stalled, (_, _) => false), "15-second pending peer deadline fails explicitly");
        Authority(); var overrun = NetGrantNetwork.Join();
        for (int i = 0; i < 2049; i++) NetGrantNetwork.Changed(Grant());
        int callbacks = 0;
        Check(!NetGrantNetwork.Pump(overrun, (_, _) => { callbacks++; return true; }) && callbacks == 0,
            "2048-row retained history overrun fails without silently skipping transitions");
    }
    private static void ClientScope()
    {
        NetGrantNetwork.Reset(); Session(nameof(NetSession.Role), NetRole.Client);
        Session(nameof(NetSession.IsAuthority), false); Session(nameof(NetSession.LocalSlot), 0);
        Session(nameof(NetSession.NetFrame), 700u);
        var fact = new TimedGrantFact(Life(0), 3, 1, TimedPowerUp.DoubleDamage, 1, 10, 1,
            100, 1900, TimedGrantEnd.Active, 700) { Resume = true };
        Check(!NetGrantNetwork.Receive(fact with { Life = fact.Life with { AuthorityEpoch = 12 } })
            && !NetGrantNetwork.Receive(fact with { Life = fact.Life with { MatchId = 8 } }),
            "client ignores facts outside the exact authority epoch/match scope");
        Check(!NetGrantNetwork.Presentation.View(Life(0), TimedPowerUp.DoubleDamage, 700).Covered,
            "foreign scope cannot start local grant presentation");
        Check(NetGrantNetwork.Receive(fact) && !NetGrantNetwork.Receive(fact), "matching client bootstrap is accepted once");
        Check(!NetGrantNetwork.Receive(fact with { Room = 2, Revision = 2 }), "late prior-room grant cannot cross the accepted room fence");
        Check(NetGrantNetwork.Presentation.View(Life(0), TimedPowerUp.DoubleDamage, 700).Remaining == 1200,
            "matching client receive projects remaining time without renewing the grant");
    }
    private static void DiscoveryRoomFence()
    {
        NetPickupNetwork.Reset(); Session(nameof(NetSession.Role), NetRole.Client);
        Session(nameof(NetSession.IsAuthority), false); Session(nameof(NetSession.LocalSlot), 0);
        Session(nameof(NetSession.NetFrame), 700u);
        var old = new TimedGrantFact(Life(0), 3, 1, TimedPowerUp.DoubleDamage, 1, 10, 1,
            100, 1900, TimedGrantEnd.Active, 700);
        var oldPrediction = new PickupClaim(new(11, 7, 0, 1, 1, 20), new(11, 7, 3, 11, 1),
            TimedPowerUp.Cloak, 700, Vector3.Zero);
        Check(NetGrantNetwork.Receive(old) && NetGrantNetwork.Presentation.Predict(oldPrediction, 700),
            "live room fixture begins with confirmed grant and independent pending overlay");
        var item = new PickupItemFact(new(11, 7, 4, 12, 1), TimedPowerUp.DoubleDamage, 700, true, Vector3.Zero);
        Check(!NetPickupNetwork.ReceiveItem(item with { Spawn = item.Spawn with { Epoch = 12, Room = 5 } })
            && NetGrantNetwork.Presentation.View(Life(0), old.Type, 700).ConfirmedRemaining == 1200,
            "foreign discovery cannot fence matching life presentation");
        Check(NetPickupNetwork.ReceiveItem(item) && NetGrantNetwork.Presentation.RoomIdentity == 4
            && !NetGrantNetwork.Presentation.HasCoverage && !NetGrantNetwork.Presentation.Facts.Any()
            && NetGrantNetwork.Presentation.PendingCount == 0
            && !NetGrantNetwork.Presentation.View(Life(0), old.Type, 700).Active,
            "new-room item clears prior confirmation and prediction before any new-room grant without fake absence");
        Check(!NetGrantNetwork.Receive(old with { Revision = 99, AuthorityFrame = 701 })
            && !NetGrantNetwork.Presentation.Predict(oldPrediction, 701)
            && !NetPickupNetwork.ReceiveItem(item with { Spawn = item.Spawn with { Room = 3 } }),
            "reordered older room grant prediction and discovery cannot resurrect old presentation");
        var prediction = new PickupClaim(new(11, 7, 0, 1, 1, 21), item.Spawn, item.Type, 700, Vector3.Zero);
        Check(NetGrantNetwork.Presentation.Predict(prediction, 700), "matching new-room owner prediction remains eligible");
        var independent = old with { Life = Life(1), Room = 4, Type = TimedPowerUp.Cloak,
            Sequence = 2, Revision = 2, SpawnId = 13, ContactFrame = 600, EndFrame = 2400 };
        Check(NetGrantNetwork.Receive(independent) && NetGrantNetwork.Presentation.PendingCount == 1
            && NetGrantNetwork.Presentation.View(Life(1), independent.Type, 700).ConfirmedRemaining == 1700,
            "independent new-room grant preserves owner prediction and exact other life");
        var confirmed = old with { Room = 4, Sequence = 3, Revision = 3, SpawnId = 12,
            ContactFrame = 600, EndFrame = 2400 };
        Check(NetGrantNetwork.Receive(confirmed) && NetGrantNetwork.Presentation.PendingCount == 0
            && NetGrantNetwork.Presentation.View(Life(0), confirmed.Type, 700).ConfirmedRemaining == 1700,
            "matching new-room delta confirms and retires only matching prediction");
        Check(NetPickupNetwork.ReceiveItem(item with { AuthorityFrame = 701, Available = false })
            && NetGrantNetwork.Presentation.View(Life(0), confirmed.Type, 700).ConfirmedRemaining == 1700
            && NetGrantNetwork.Presentation.View(Life(1), independent.Type, 700).ConfirmedRemaining == 1700,
            "same-room discovery update preserves both new-room confirmations");
        NetPickupNetwork.Reset();
    }
    internal static int Run()
    {
        var role = NetSession.Role; var authority = NetSession.IsAuthority; var slot = NetSession.LocalSlot;
        var match = NetSession.ServerMatch; var frame = NetSession.NetFrame;
        var trackers = (NetLifecycleTracker[])typeof(NetPlayerLifecycle).GetField("_slots", Hidden)!.GetValue(null)!;
        var lives = trackers.Select(tracker => tracker.Capture()).ToArray();
        long crossMatch = NetPlayerLifecycle.CrossMatch, crossAuthority = NetPlayerLifecycle.CrossAuthority;
        try
        {
            _checks = 0; Session(nameof(NetSession.ServerMatch), new MatchStatePacket { MatchId = 7, AuthorityEpoch = 11 });
            for (int i = 0; i < 8; i++) trackers[i].Restore(new(1, 1, NetworkPlayerState.Alive, false));
            Check(NetReliableChannel.IsReliable(PacketType.TimedGrant) && !NetReliableChannel.IsCritical(PacketType.TimedGrant),
                "grant transitions use ordinary reliable delivery");
            ImmutableTransition(); DenseLateJoin(); IndependentCursorsAndFailure(); ClientScope(); DiscoveryRoomFence();
            Console.WriteLine($"PASS: grant network {_checks} assertions; immutable transitions, dense bootstrap, remaining time and bounded peer delivery.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally
        {
            NetGrantNetwork.Reset();
            for (int i = 0; i < 8; i++) trackers[i].Restore(lives[i]);
            NetPlayerLifecycle.CrossMatch = crossMatch; NetPlayerLifecycle.CrossAuthority = crossAuthority;
            Session(nameof(NetSession.Role), role); Session(nameof(NetSession.IsAuthority), authority);
            Session(nameof(NetSession.LocalSlot), slot); Session(nameof(NetSession.ServerMatch), match); Session(nameof(NetSession.NetFrame), frame);
        }
    }
}
