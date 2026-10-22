using System;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

internal static class ConfirmedImpactTests
{
    private static int _checks;
    private static void Check(bool ok, string message)
    { _checks++; if (!ok) throw new InvalidOperationException(message); }
    public static int Run()
    {
        bool enabled = NetConfirmedImpacts.Enabled;
        try
        {
            _checks = 0; Codec(); ConnectionIsolation(); Budgets(); FaultIsolation(); Manager(); ImpactTransportTests.Run();
            Console.WriteLine($"PASS: {_checks} confirmed-impact assertions"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { NetConfirmedImpacts.Reset(); NetConfirmedImpacts.Enabled = enabled; NetSession.Stop(); }
    }
    private static ConfirmedImpact Fact(uint id = uint.MaxValue) => new(
        new ShotKey(4, 51, 0, 9, 2, id), uint.MaxValue, uint.MaxValue,
        1, 10, 7, ushort.MaxValue, uint.MaxValue, 0,
        ReplayShotFactFlags.Continuous | ReplayShotFactFlags.Direct,
        ImpactProvenance.NativePoint, new Vector3(-1.125f, 2.25f, 99999));
    private static void Codec()
    {
        var fact = Fact(); var bytes = new byte[ConfirmedImpact.Size]; fact.Write(bytes);
        Check(bytes.Length == 53 && ConfirmedImpact.TryRead(bytes, out var decoded) && decoded == fact,
            "53-byte full identity, uint wrap, component and endpoint round trip");
        for (int n = 0; n < bytes.Length; n++)
            Check(!ConfirmedImpact.TryRead(bytes.AsSpan(0, n), out _), "every truncation refused");
        Check(!ConfirmedImpact.TryRead(new byte[54], out _), "trailing bytes refused");
        var anchor = fact with { Key = fact.Key with { ShotId = 0 }, Component = 0, Phase = 0,
            Flags = ReplayShotFactFlags.None, Weapon = 255, Provenance = ImpactProvenance.ConfirmedBodyAnchor };
        anchor.Write(bytes);
        Check(ConfirmedImpact.TryRead(bytes, out decoded) && decoded == anchor, "unknown projectile body anchor preserves explicit zero association");
        var variants = new[] {
            fact with { Key = fact.Key with { AuthorityEpoch = 0 } },
            fact with { Key = fact.Key with { MatchId = 0 } },
            fact with { Key = fact.Key with { ShooterSlot = 8 } },
            fact with { Key = fact.Key with { Generation = 0 } },
            fact with { Key = fact.Key with { LifeId = 0 } },
            fact with { Victim = 8 }, fact with { VictimGeneration = 0 }, fact with { VictimLife = 0 },
            fact with { DamageEvent = 0 }, fact with { Weapon = 254 },
            fact with { Provenance = (ImpactProvenance)0 }, fact with { Provenance = (ImpactProvenance)4 },
            fact with { Flags = ReplayShotFactFlags.None },
            fact with { Point = new Vector3(float.NaN, 0, 0) },
            fact with { Point = new Vector3(0, float.PositiveInfinity, 0) },
            fact with { Point = new Vector3(0, 0, -100000) },
            anchor with { Component = 1 }, anchor with { Provenance = ImpactProvenance.NativePoint }
        };
        foreach (var invalid in variants) { invalid.Write(bytes); Check(!ConfirmedImpact.TryRead(bytes, out _), "invalid impact refused by decoder"); }
        bool threw = false; try { fact.Write(new byte[52]); } catch (ArgumentException) { threw = true; }
        Check(threw, "short encoder destination refused");
    }
    private static NetConnection Connection(ulong id = 7) => new(new IPEndPoint(IPAddress.Loopback, 1234), id, initialSequence: uint.MaxValue);
    private static void ConnectionIsolation()
    {
        var a = Connection(); var b = Connection();
        Check(a.Reliable.TryQueue(PacketType.Roster, new byte[] { 1 }, 0, out uint eventId), "reliable pending fixture");
        var game = a.Send(PacketType.Intent, 0, NetHeaderFlags.Reliable, eventId);
        Check(b.Receive(game, 10) == SequenceResult.New, "gameplay receive fixture");
        a.AckPending = true; var before = a.Capture(); var reliable = a.Reliable.Capture(10);
        for (int i = 0; i < 10000; i++)
        {
            var cue = a.Send(PacketType.ConfirmedImpact, 10, NetHeaderFlags.Reliable, eventId);
            Check(cue.Flags == NetHeaderFlags.None && cue.Ack == 0 && cue.AckBits == 0, "cosmetics have no reliable or ACK envelope");
            // A malicious cosmetic ACK must never acknowledge gameplay attempts.
            a.Receive(cue with { Flags = NetHeaderFlags.AckValid, Ack = game.Sequence, AckBits = uint.MaxValue }, 20);
            b.Receive(cue, 20);
        }
        Check(a.Capture() == before && a.AckPending && a.Reliable.Capture(10) == reliable,
            "10000 cosmetics leave gameplay RTT, counters, ACK pending and reliable queue unchanged");
        Check(b.Receive(game, 30) == SequenceResult.Duplicate, "cosmetics cannot evict gameplay receive window");
        var response = b.Send(PacketType.Snapshot, 30);
        Check(response.Ack == game.Sequence && (response.Flags & NetHeaderFlags.AckValid) != 0,
            "gameplay ACK frontier remains exact");
        a.Receive(response, 40);
        Check(a.Capture().Acknowledged == 1 && a.Capture().RttMilliseconds == 40
            && a.Reliable.Capture(40).Pending == 0, "normal ACK still completes reliable event");
        Check(a.Send(PacketType.Intent, 41).Sequence == 0, "cosmetics do not advance wrapped gameplay sequence");
    }
    private static void Budgets()
    {
        int size = NetHeader.Size + ConfirmedImpact.Size;
        var a = Connection(); var peer = Connection(8);
        Check(!a.Allow(PacketType.ConfirmedImpact, 0, size), "untrusted peer cannot receive authority cosmetic lane");
        a.ReceivesAuthorityFacts = peer.ReceivesAuthorityFacts = true;
        Check(!a.Allow(PacketType.ConfirmedImpact, 0, size - 1), "wrong cosmetic datagram size refused");
        for (int i = 0; i < NetConnection.CosmeticBurst; i++)
            Check(a.Allow(PacketType.ConfirmedImpact, 0, size), "bounded cosmetic receive burst");
        Check(!a.Allow(PacketType.ConfirmedImpact, 0, size), "cosmetic receive burst exhaustion");
        Check(peer.Allow(PacketType.ConfirmedImpact, 0, size), "per-peer budget independence");
        Check(a.Allow(PacketType.Intent, 0, 100) && a.Allow(PacketType.Roster, 0, 100)
            && a.Allow(PacketType.BombEvent, 0, 100), "cosmetic budget cannot starve gameplay classes");
        Check(a.AllowCosmeticSend(0, NetConnection.CosmeticByteBurst) && !a.AllowCosmeticSend(0, 1),
            "independent cosmetic byte budget exhausted before packet budget");
        Check(peer.AllowCosmeticSend(0, 1) && a.Allow(PacketType.ConfirmedImpact, 1000, size),
            "send/receive peer separation and receive refill");
        var packets = Connection(9);
        for (int i = 0; i < NetConnection.CosmeticBurst; i++) Check(packets.AllowCosmeticSend(0, 1), "send packet burst");
        Check(!packets.AllowCosmeticSend(0, 1) && packets.AllowCosmeticSend(1000, 1), "send packet budget independent of bytes and refills");
    }
    private static void FaultIsolation()
    {
        int seed = NetLag.Seed, rtt = NetLag.RoundTripMs, jitter = NetLag.JitterMs;
        double loss = NetLag.LossPercent, reorder = NetLag.ReorderRate, duplicate = NetLag.DuplicateRate;
        try
        {
            NetLag.ConfigureSeed("7721"); NetLag.Configure("200:40"); NetLag.ConfigureLoss("5%");
            NetLag.ConfigureReorder("20%"); NetLag.ConfigureDuplicate("10%");
            foreach (bool outbound in new[] { false, true })
            {
                var control = NetLag.CreateQueue<int>(outbound); var game = NetLag.CreateQueue<int>(outbound);
                var cosmetic = NetLag.CreateQueue<int>(outbound, cosmetic: true);
                var left = new List<(int, int)>(); var right = new List<(int, int)>();
                for (int time = 0; time < 2000; time++)
                {
                    if (time < 500) { control.Enqueue(time, time); game.Enqueue(time, time);
                        for (int j = 0; j < 20; j++) cosmetic.Enqueue(time, j); }
                    while (control.TryDequeue(time, out int x)) left.Add((time, x));
                    while (game.TryDequeue(time, out int x)) right.Add((time, x));
                    while (cosmetic.TryDequeue(time, out _)) { }
                }
                Check(left.Count > 0 && left.Count == right.Count, "gameplay queue nonempty same size under cosmetic flood");
                for (int i = 0; i < left.Count; i++) Check(left[i] == right[i], "seeded gameplay timing/order unaffected by cosmetic RNG");
                Check(control.Dropped == game.Dropped && control.Duplicated == game.Duplicated
                    && control.Reordered == game.Reordered && control.Count == game.Count, "fault queue accounting isolation");
            }
        }
        finally
        {
            NetLag.ConfigureSeed(seed.ToString()); NetLag.Configure($"{rtt}:{jitter}");
            NetLag.ConfigureLoss((loss / 100).ToString(System.Globalization.CultureInfo.InvariantCulture));
            NetLag.ConfigureReorder(reorder.ToString(System.Globalization.CultureInfo.InvariantCulture));
            NetLag.ConfigureDuplicate(duplicate.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }
    private static void Frame(uint value) => typeof(NetSession).GetProperty(nameof(NetSession.NetFrame))!.SetValue(null, value);
    private static void AuthorityFrame(uint value) => typeof(NetSession).GetField("_lastSnapshotFrame", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, value);
    private static void Manager()
    {
        HealthShotTests.Session();
        typeof(NetSession).GetProperty(nameof(NetSession.Role))!.SetValue(null, NetRole.Client);
        NetConfirmedImpacts.Enabled = true; NetConfirmedImpacts.Reset(); Frame(48); AuthorityFrame(428);
        Check(NetConfirmedImpacts.Accept(Fact(17) with { Frame = 428 }), "late join uses authority frontier428 rather than local clock48");
        var entries = (Array)typeof(NetConfirmedImpacts).GetField("Entries", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var drawable = typeof(NetConfirmedImpacts).GetMethod("Drawable", BindingFlags.Static | BindingFlags.NonPublic)!;
        Frame(58);
        Check((bool)drawable.Invoke(null, new[] { entries.GetValue(0) })!, "draw retention uses local arrival clock through10frames");
        Frame(59);
        Check(!(bool)drawable.Invoke(null, new[] { entries.GetValue(0) })!, "draw cue expires after10localframes independent of authority frontier");
        NetConfirmedImpacts.Reset(); AuthorityFrame(0);
        Check(!NetConfirmedImpacts.Accept(Fact(18) with { Frame = 0 }), "no authority snapshot means no freshness proof");
        AuthorityFrame(1);
        var fact = Fact() with { Frame = uint.MaxValue };
        Check(NetConfirmedImpacts.Accept(fact), "wrap-age retained client impact admitted");
        Check(!NetConfirmedImpacts.Accept(fact), "full-key duplicate refused");
        Check(NetConfirmedImpacts.Accept(fact with { Component = 4 }), "different exact component is independent");
        Check(NetConfirmedImpacts.Accept(fact with { Phase = 4 }), "different continuous phase independent");
        Check(NetConfirmedImpacts.Accept(fact with { DamageEvent = 1 }), "wrapped damage sequence independent");
        Check(!NetConfirmedImpacts.Accept(fact with { Key = fact.Key with { AuthorityEpoch = 5 } }), "cross authority refused");
        Check(!NetConfirmedImpacts.Accept(fact with { Key = fact.Key with { MatchId = 52 } }), "cross match refused");
        Check(!NetConfirmedImpacts.Accept(fact with { Key = fact.Key with { Generation = 10 } }), "wrong shooter occupant refused");
        Check(!NetConfirmedImpacts.Accept(fact with { VictimGeneration = 11 }), "wrong victim occupant refused");
        Check(!NetConfirmedImpacts.Accept(fact with { VictimLife = 8 }), "future victim life refused");
        Check(!NetConfirmedImpacts.Accept(fact with { Frame = unchecked(1u - 121) }), "expired authority frame refused across wrap");
        Check(!NetConfirmedImpacts.Accept(fact with { Frame = 62 }), "far future frame refused");
        Check(NetConfirmedImpacts.Accept(fact with { Frame = unchecked(1u - 120), Component = 9 }), "retention boundary and reordered frame admitted");
        NetConfirmedImpacts.Reset(); Frame(70); AuthorityFrame(1000);
        for (uint i = 0; i < NetConfirmedImpacts.Capacity; i++)
            Check(NetConfirmedImpacts.Accept(Fact(i + 1) with { Frame = 1000 }), "bounded table admits unique full keys");
        Check(!NetConfirmedImpacts.Accept(Fact(9999) with { Frame = 1000 }), "capacity overflow expendable fact refused");
        Frame(71); AuthorityFrame(1121);
        Check(NetConfirmedImpacts.Accept(Fact(9999) with { Frame = 1121 }), "expired entries release capacity");
        NetConfirmedImpacts.Enabled = false;
        Check(!NetConfirmedImpacts.Accept(Fact(9998) with { Frame = 1121 }), "disabled feature accepts no cosmetic fact");
    }
}
