using System;
using MphRead.Formats;
using MphRead.Mods.MapGen;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Network;

namespace MphRead.NetTest;

// Pure eligibility/readiness guards. These do not fabricate a successful UDP
// join, map preparation, loaded acknowledgement or rendered measurement.
internal static class NcxFixtureLobbyJoinTests
{
    internal static int Run()
    {
        try
        {
            int checks = 0;
            void Check(bool value, string label)
            { if (!value) throw new InvalidOperationException("NCX fixture lobby: " + label); checks++; }
            bool Gate(bool active = true, bool campaign = true, string host = "127.0.0.1",
                Hunter hunter = Hunter.Samus, HitRig.RigMode mode = HitRig.RigMode.NcxStatic, string? count = "2")
                => NcxFixtureLobbyJoin.Eligible(active, campaign, host, hunter, mode, count, out _, out _);
            Check(Gate() && Gate(host: "::1") && Gate(host: "localhost"), "explicit local fixture allowed");
            Check(!Gate(active: false) && !Gate(campaign: false), "fixture and evidence gates both required");
            Check(!Gate(host: "192.0.2.1") && !Gate(host: "example.test") && !Gate(host: "localhost.example.test"),
                "remote and unresolved endpoints excluded");
            Check(!Gate(hunter: Hunter.Kanden) && !Gate(mode: HitRig.RigMode.Off), "Samus and exact fixture modes required");
            foreach (string? count in new[] { null, "", "1", "4", "8", "two" })
                Check(!Gate(count: count), "explicit two/three player declaration required");
            Check(Gate(count: "3") && Gate(count: "3", mode: HitRig.RigMode.NcxObserve)
                && !Gate(mode: HitRig.RigMode.NcxObserve), "observer requires three declared players");

            var map = new NetworkMapIdentity(Guid.NewGuid(), new MapHash256(1, 2, 3, 4),
                new MapHash256(5, 6, 7, 8), NetworkMapFlags.Custom);
            var initial = new SessionStatePacket { MatchId = ushort.MaxValue, AuthorityEpoch = 7, MapGeneration = 2,
                StartGeneration = uint.MaxValue, Phase = SessionPhase.Lobby, Policy = ServerSessionPolicy.Lobby,
                MaxPlayers = 2, OwnerSlot = 1, WorldProfile = MatchWorldProfile.Resolve(2),
                Match = new MatchDefinition { RoomKey = "NCX EMULATED STATIC", MapIdentity = map } };
            Check(NcxFixtureLobbyJoin.DeclaredSession(initial, HitRig.RigMode.NcxStatic, 2), "declared packaged room accepted");
            var wrong = initial; wrong.Match = initial.Match with { RoomKey = "NCX EMULATED LATERAL" };
            Check(!NcxFixtureLobbyJoin.DeclaredSession(wrong, HitRig.RigMode.NcxStatic, 2), "participant fixture must match exact declared room");
            wrong = initial; wrong.MaxPlayers = 3;
            Check(!NcxFixtureLobbyJoin.DeclaredSession(wrong, HitRig.RigMode.NcxStatic, 2), "capacity must match declaration");
            wrong = initial; wrong.Match = initial.Match with { MapIdentity = default };
            Check(!NcxFixtureLobbyJoin.DeclaredSession(wrong, HitRig.RigMode.NcxStatic, 2), "loose or omitted package identity rejected");
            wrong = initial; wrong.Phase = SessionPhase.InMatch;
            Check(!NcxFixtureLobbyJoin.DeclaredSession(wrong, HitRig.RigMode.NcxStatic, 2), "joining an already running fixture rejected");
            var start = initial; start.Phase = SessionPhase.Starting; start.MatchId = 1; start.StartGeneration = 1;
            Check(NcxFixtureLobbyJoin.SameIdentity(initial, start), "ordinary wrapped match/start generation advance accepted");
            wrong = start; wrong.AuthorityEpoch++;
            Check(!NcxFixtureLobbyJoin.SameIdentity(initial, wrong), "authority stream cannot change");
            wrong = start; wrong.MapGeneration++;
            Check(!NcxFixtureLobbyJoin.SameIdentity(initial, wrong), "map generation cannot change");
            wrong = start; wrong.Match = wrong.Match with { MapIdentity = map with { PackageHash = new MapHash256(9, 0, 0, 0) } };
            Check(!NcxFixtureLobbyJoin.SameIdentity(initial, wrong), "exact archive cannot change");
            wrong = start; wrong.Match = wrong.Match with { DisablePowerups = true };
            Check(!NcxFixtureLobbyJoin.SameIdentity(initial, wrong), "helper cannot edit match rules");
            wrong = start; wrong.MatchId = 2;
            Check(!NcxFixtureLobbyJoin.SameIdentity(initial, wrong), "unexpected stream advance rejected");

            var p0 = new NcxFixtureLobbyJoin.Peer(0, 4, (byte)Hunter.Samus, false, false, false, MapAvailabilityState.Ready);
            var p1 = p0 with { Slot = 1, Generation = 9 };
            NcxFixtureLobbyJoin.InventoryState Inspect(NcxFixtureLobbyJoin.Peer[] peers, out int next,
                int local = 0, int count = 2, HitRig.RigMode mode = HitRig.RigMode.NcxStatic)
                => NcxFixtureLobbyJoin.Inventory(peers, count, local, mode, out next);
            Check(Inspect(new[] { p0 }, out _) == NcxFixtureLobbyJoin.InventoryState.Waiting,
                "missing peer waits instead of starting a solo match");
            Check(Inspect(new[] { p1, p0 }, out int next) == NcxFixtureLobbyJoin.InventoryState.Ready && next == 0,
                "ready order follows actual slot rather than packet order or owner assumption");
            Check(Inspect(new[] { p0 with { Ready = true }, p1 }, out next) == NcxFixtureLobbyJoin.InventoryState.Ready && next == 1,
                "next ready command waits for preceding slot acknowledgement");
            Check(Inspect(new[] { p0 with { Ready = true }, p1 with { Ready = true } }, out next)
                == NcxFixtureLobbyJoin.InventoryState.Ready && next == -1, "all actual ready acknowledgements required before start");
            Check(Inspect(new[] { p0, p1 with { Map = MapAvailabilityState.Building } }, out _)
                == NcxFixtureLobbyJoin.InventoryState.Waiting, "native map preparation must finish for every participant");
            foreach (var invalid in new[] { p1 with { Bot = true }, p1 with { Spectating = true },
                p1 with { Hunter = (byte)Hunter.Kanden }, p1 with { Generation = 0 }, p1 with { Slot = 0 },
                p1 with { Slot = 2 }, p1 with { Map = MapAvailabilityState.Failed } })
                Check(Inspect(new[] { p0, invalid }, out _) == NcxFixtureLobbyJoin.InventoryState.Invalid,
                    "bot, spectator, wrong identity/slot and failed preparation cannot start fixture");
            var p2 = p1 with { Slot = 2 };
            Check(Inspect(new[] { p0, p1, p2 }, out _, 2, 3, HitRig.RigMode.NcxObserve)
                == NcxFixtureLobbyJoin.InventoryState.Ready, "observer occupies actual third human slot");
            Check(Inspect(new[] { p0, p1, p2 }, out _, 1, 3, HitRig.RigMode.NcxObserve)
                == NcxFixtureLobbyJoin.InventoryState.Invalid, "observer role cannot use a shooter slot");
            Check(Inspect(new[] { p0, p1, p2 }, out _, 2, 3)
                == NcxFixtureLobbyJoin.InventoryState.Invalid, "shooter role cannot use observer slot");

            var stable = new NcxFixtureLobbyJoin.StableRevision();
            Check(!stable.Observe(65535, true, 0) && !stable.Observe(65535, true, .99)
                && stable.Observe(65535, true, 1), "stable acknowledged revision interval required");
            Check(!stable.Observe(0, true, 1.1) && stable.Observe(0, true, 2.1), "revision wrap resets stable interval");
            Check(!stable.Observe(0, false, 2.2) && !stable.Observe(0, true, 3.5)
                && stable.Observe(0, true, 4.5), "pending commands or unaligned roster reset stability");
            Console.WriteLine($"PASS: NCX fixture lobby eligibility/readiness ({checks} checks); pure guards, no fabricated network/load success.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
