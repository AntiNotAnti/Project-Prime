using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Asset-backed request/admission and presentation tests. Reflection only sets
// fixture inventory/cooldown; these are not a claim of native input coverage.
internal static class BombSceneTests
{
    private static int _checks;
    private static uint _sequence;
    private static void Check(bool value, string label)
    { _checks++; if (!value) throw new InvalidOperationException(label); Console.WriteLine("BOMB SCENE PASS " + label); }
    private static void Set(PlayerEntity player, string name, object value)
        => typeof(PlayerEntity).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(player, value);
    private static BombIntent Request(PlayerEntity player)
    {
        var request = new BombIntent(ShotKey.For(player.SlotIndex, ++_sequence), NetSession.NetFrame, BombAction.Place);
        NetSession.RemoteIntentValid[player.SlotIndex] = true;
        NetSession.RemoteIntents[player.SlotIndex] = new IntentPacket { Frame = request.Frame };
        NetBombs.ReceiveRequest(player.SlotIndex, request); NetBombs.AfterSimulation();
        return request;
    }
    private static IEnumerable<BombEntity> Bombs(Scene scene)
    { foreach (var bomb in scene.GetBombEntities()) yield return bomb; }
    private static BombEntity[] Native(PlayerEntity player) => Bombs(player.OwningScene)
        .Where(b => !b.ModReplica && b.Owner == player && !b.Flags.TestFlag(BombFlags.Exploded)).ToArray();
    private static void NativeTriangle(PlayerEntity owner, PlayerEntity victim)
    {
        // Isolated native physics fixture: explicit initial/vertex geometry and
        // controlled inventory replace navigation, not admission or damage.
        owner.Hunter = Hunter.Sylux; owner.Health = victim.Health = 99;
        owner.ModReleaseSpawnProtection(); victim.ModReleaseSpawnProtection();
        Set(owner, "_spawnInvulnTimer", (ushort)0); Set(victim, "_spawnInvulnTimer", (ushort)0);
        Set(owner, "_damageInvulnTimer", (ushort)0); Set(victim, "_damageInvulnTimer", (ushort)0);
        Check(HitRig.TrySetupLockjawTriangleFixture(owner, victim, out Vector3 center),
            "native collision verifies supported clear triangle fixture geometry");
        Console.WriteLine($"BOMB TRIANGLE INITIAL GEOMETRY center={center} owner={owner.Position}");
        var offsets = new[] { new Vector3(3, 0, 0), new Vector3(-1.5f, 0, 2.598076f), new Vector3(-1.5f, 0, -2.598076f) };
        var keys = new ShotKey[3];
        for (int i = 0; i < 3; i++)
        {
            owner.ModPlaceAt(center - Vector3.UnitY * .25f + offsets[i]); owner.Speed = Vector3.Zero;
            Set(owner, "_bombAmmo", (byte)3); Set(owner, "_bombCooldown", (ushort)0);
            var request = Request(owner); keys[i] = request.Key;
            var bomb = Native(owner).Single(b => b.ModOwnerKey == request.Key);
            Check(bomb.BombType == BombType.Lockjaw && owner.SyluxBombs[i] == bomb && bomb.BombIndex == i,
                $"native admission registers exact triangle vertex {i}");
        }
        var triangle = Native(owner);
        Check(owner.SyluxBombCount == 3 && triangle.Length == 3 && triangle.Single(b => b.BombIndex == 2).ModCaptureState().Link0 == keys[0].ShotId
            && triangle.Single(b => b.BombIndex == 2).ModCaptureState().Link1 == keys[1].ShotId,
            "native admitted triad has both exact links before any damage");
        int health = victim.Health; ushort sequence = NetDamage.Sequence(victim.SlotIndex);
        ulong frontier = owner.OwningScene.Bombs.Frontier;
        triangle.Single(b => b.BombIndex == 0).Process();
        Check(triangle.All(b => b.Damage == 60 && b.EnemyDamage == 60 && b.Countdown > 0 && b.Countdown <= 44),
            "native interior snare selects damage60 and shared countdown44 instead of empty-triad expiry");
        // Native processing owns snare targeting, damage60, countdown44 and
        // terminal publication. The fixture never writes any of those values.
        for (int frame = 0; frame < 100 && Native(owner).Length != 0; frame++)
        {
            typeof(NetSession).GetProperty(nameof(NetSession.NetFrame))!.SetValue(null, NetSession.NetFrame + 1);
            foreach (var bomb in triangle) bomb.Process();
        }
        Check(victim.Health < health && NetDamage.Sequence(victim.SlotIndex) != sequence,
            "actual native three-bomb snare removes victim health with authority damage sequence");
        Check(triangle.All(b => b.ModOwnerKey == keys[b.BombIndex] && b.Flags.TestFlag(BombFlags.Exploded))
            && Native(owner).Length == 0 && owner.OwningScene.Bombs.LiveCount == 0
            && owner.OwningScene.Bombs.Frontier == frontier + 3,
            "native damaging triad publishes one terminal per exact admitted placement");
        owner.ModForceForm(false); owner.Hunter = Hunter.Samus;
    }
    private static void Admission(PlayerEntity player)
    {
        player.Health = 99; player.ModReleaseSpawnProtection(); player.ModForceForm(false);
        Set(player, "_bombAmmo", (byte)3); Set(player, "_bombCooldown", (ushort)0);
        ulong frontier = player.OwningScene.Bombs.Frontier;
        Request(player);
        Check(Native(player).Length == 0 && player.OwningScene.Bombs.Frontier == frontier + 1,
            "biped request produces refusal without native placement");
        player.ModForceForm(true);
        // Lift the fixture from the floor so native placement LOS is clear.
        player.ModPlaceAt(player.Position + Vector3.UnitY);
        var request = Request(player);
        var bomb = Native(player).Single();
        Check(bomb.ModOwnerKey == request.Key && bomb.BombType == BombType.MorphBall,
            "Samus request creates native bomb with exact admitted identity");
        Check((byte)typeof(PlayerEntity).GetField("_bombAmmo", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(player)! == 2,
            "native placement consumes inventory once");
        frontier = player.OwningScene.Bombs.Frontier;
        NetBombs.ReceiveRequest(player.SlotIndex, request); NetBombs.AfterSimulation();
        Check(Native(player).Length == 1 && player.OwningScene.Bombs.Frontier == frontier,
            "duplicate request neither places nor changes terminal frontier");
        Set(player, "_bombCooldown", (ushort)10);
        Request(player);
        Check(Native(player).Length == 1 && player.OwningScene.Bombs.Frontier == frontier + 1,
            "native cooldown rejects second distinct request");
        for (int i = 0; i < 2; i++) { Set(player, "_bombCooldown", (ushort)0); Request(player); }
        Check(Native(player).Length == 3, "three distinct admitted placements occupy native cap");
        Set(player, "_bombAmmo", (byte)3); Set(player, "_bombCooldown", (ushort)0);
        frontier = player.OwningScene.Bombs.Frontier; Request(player);
        Check(Native(player).Length == 3 && player.OwningScene.Bombs.Frontier == frontier + 1,
            "fourth placement is refused without replacing native bomb");
        var stale = new BombIntent(request.Key with { ShotId = ++_sequence, LifeId = (ushort)(request.Key.LifeId + 1) }, NetSession.NetFrame, BombAction.Place);
        frontier = player.OwningScene.Bombs.Frontier;
        NetBombs.ReceiveRequest(player.SlotIndex, stale); NetBombs.AfterSimulation();
        Check(player.OwningScene.Bombs.Frontier == frontier && Native(player).Length == 3,
            "future-life request cannot enter current native owner");
    }
    private static void Presentation(PlayerEntity player)
    {
        var scene = player.OwningScene; var world = new NetBombWorld(scene);
        world.Begin(NetSession.CurrentMatchId, NetSession.AuthorityEpoch);
        var key = ShotKey.For(player.SlotIndex, 1000);
        var state = new BombState(key, BombType.Lockjaw, NetSession.NetFrame, player.Position,
            Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, 1, 0, 1001, 1002, 0);
        var receiver = new BombStateReceiver();
        foreach (var page in BombStatePage.PackSnapshot(key.MatchId, key.AuthorityEpoch, NetSession.NetFrame, 5, new[] { state }))
            Check(receiver.Accept(page), "complete late baseline page accepted");
        world.ApplyBaseline(receiver);
        var ghost = Bombs(scene).Single(b => b.ModReplica && b.ModOwnerKey == key);
        Check(ghost.ModCaptureState() == state, "late baseline preserves position rotation countdown and exact links");
        int health = player.Health, countdown = ghost.Countdown; Vector3 position = ghost.Position;
        ghost.Process(); ghost.Process();
        Check(player.Health == health && ghost.Countdown == countdown && ghost.Position == position,
            "canonical ghost has no contact damage countdown or native movement");
        var birth = new BombEvent(6, NetSession.NetFrame, BombEventKind.Placed, 0, state);
        var terminal = new BombEvent(7, NetSession.NetFrame, BombEventKind.Removed, 0, state);
        Check(world.ReceiveEvent(terminal) && world.PendingEvents == 1,
            "out-of-order terminal waits for missing exact predecessor");
        Check(world.ReceiveEvent(birth) && world.Frontier == 7 && world.PendingEvents == 0,
            "same-frame reordered birth and terminal drain in canonical sequence");
        Check(!Bombs(scene).Any(b => b.ModReplica && b.ModOwnerKey == key),
            "terminal removes exact ghost");
        Check(!world.ReceiveEvent(birth), "duplicate birth cannot resurrect terminal identity");
        foreach (var page in BombStatePage.PackSnapshot(key.MatchId, key.AuthorityEpoch, NetSession.NetFrame + 1, 7, new[] { state }))
            world.ReceiveState(page);
        Check(!Bombs(scene).Any(b => b.ModReplica && b.ModOwnerKey == key),
            "later stale live snapshot cannot resurrect terminal identity");
    }
    private static void NativeEntityLifecycle(PlayerEntity player)
    {
        // Direct native entity fixture: ownership/collision gates, not input admission.
        foreach (var hunter in new[] { Hunter.Kanden, Hunter.Sylux })
        {
            player.Hunter = hunter;
            var bomb = BombEntity.Spawn(player, Matrix4.CreateTranslation(player.Position), player.OwningScene);
            Check(bomb != null && bomb.BombType == (hunter == Hunter.Kanden ? BombType.Stinglarva : BombType.Lockjaw),
                $"direct native entity fixture selects {hunter} bomb type");
            var key = ShotKey.For(player.SlotIndex, ++_sequence);
            bomb!.ModOwnerKey = key;
            Check(NetBombs.CanSimulate(bomb), $"direct native {hunter} current owner may simulate");
            bomb.ModOwnerKey = key with { Generation = (ushort)(key.Generation + 1) };
            Check(!NetBombs.CanSimulate(bomb), $"direct native {hunter} replacement generation cannot simulate");
            bomb.ModOwnerKey = key with { AuthorityEpoch = key.AuthorityEpoch + 1 };
            Check(!NetBombs.CanSimulate(bomb), $"direct native {hunter} other authority cannot simulate");
            bomb.ModOwnerKey = key;
            ushort life = NetPlayerLifecycle.Get(player.SlotIndex);
            // Captured old-life ownership survives an actual native respawn.
            player.Spawn(player.Position, Vector3.UnitZ, Vector3.UnitY, player.NodeRef, respawn: true);
            Check(NetPlayerLifecycle.Get(player.SlotIndex) != life && bomb.ModOwnerKey == key && NetBombs.CanSimulate(bomb) == (hunter != Hunter.Sylux),
                $"direct native {hunter} old-life bomb retains exact ownership after respawn");
            if (hunter == Hunter.Sylux)
            {
                player.ModForceForm(true);
                Set(player, "_bombAmmo", (byte)3); Set(player, "_bombCooldown", (ushort)0);
                bomb.Countdown = 20;
                NetBombs.Admitting = true;
                try
                {
                    var reason = player.ModTryAdmitBomb(BombAction.Detonate, out _);
                    Check(reason == BombAdmissionReason.NoBombs && bomb.Countdown == 20,
                        "new-life Lockjaw detonate cannot operate previous-life bomb");
                }
                finally { NetBombs.Admitting = false; }
            }
        }
    }
    private static void DetonationGates(PlayerEntity player)
    {
        // Direct registered native entity fixture, not controls or placement admission.
        player.Hunter = Hunter.Sylux; player.Health = 99; player.ModForceForm(true);
        Set(player, "_bombAmmo", (byte)3); Set(player, "_bombCooldown", (ushort)0);
        var bombs = new BombEntity[3];
        for (int i = 0; i < 3; i++)
        {
            bombs[i] = BombEntity.Spawn(player, Matrix4.CreateTranslation(player.Position), player.OwningScene)!;
            bombs[i].ModOwnerKey = ShotKey.For(player.SlotIndex, ++_sequence);
            bombs[i].Countdown = 30 + i; player.SyluxBombs[i] = bombs[i];
        }
        void Refused(string label, BombAdmissionReason expected)
        {
            var reason = player.ModTryAdmitBomb(BombAction.Detonate, out _);
            Check(reason == expected && bombs.Select((b, i) => b.Countdown == 30 + i).All(v => v), label);
        }
        player.SyluxBombCount = 1; Refused("one Lockjaw cannot detonate and preserves all timers", BombAdmissionReason.NoBombs);
        player.SyluxBombCount = 2; Refused("two Lockjaws cannot detonate and preserves all timers", BombAdmissionReason.NoBombs);
        player.SyluxBombCount = 3; Set(player, "_bombCooldown", (ushort)1);
        Refused("full Lockjaw set during cooldown preserves all timers", BombAdmissionReason.Inventory);
        Set(player, "_bombCooldown", (ushort)0); Set(player, "_bombAmmo", (byte)0);
        Refused("full Lockjaw set without inventory preserves all timers", BombAdmissionReason.Inventory);
        Set(player, "_bombAmmo", (byte)3); var key = bombs[2].ModOwnerKey;
        bombs[2].ModOwnerKey = key with { LifeId = (ushort)(key.LifeId + 1) };
        Refused("one mismatched Lockjaw life preserves entire set", BombAdmissionReason.NoBombs);
        bombs[2].ModOwnerKey = key;
        Check(player.ModTryAdmitBomb(BombAction.Detonate, out _) == BombAdmissionReason.None
            && bombs.All(b => b.Countdown == 0), "eligible full current-life Lockjaw set detonates all three");
    }
    private static void DelayedOwnerJump(PlayerEntity player)
    {
        var scene = player.OwningScene;
        var start = player.Position + Vector3.UnitY * 3;
        player.Hunter = Hunter.Sylux; player.Health = 99; player.ModForceForm(true);
        player.ModPlaceAt(start); player.Speed = new Vector3(.3f, 0, .1f);
        var bomb = BombEntity.Spawn(player, Matrix4.CreateTranslation(player.Volume.SpherePosition), scene)!;
        bomb.ModOwnerKey = ShotKey.For(player.SlotIndex, ++_sequence);
        scene.Bombs.Placed(bomb);
        BombEvent terminal = default;
        var sink = NetSession.BombEventSink;
        NetSession.BombEventSink = bytes =>
        {
            if (BombEvent.TryRead(bytes, out var fact) && fact.Kind == BombEventKind.Detonated
                && fact.State.Key == bomb.ModOwnerKey) terminal = fact;
        };
        try { bomb.Countdown = 0; bomb.Process(); }
        finally { NetSession.BombEventSink = sink; }
        Check(player.Speed.Y == Fixed.ToFloat(player.Values.BombJumpSpeed),
            "moving native Lockjaw receives authored bomb jump");
        Check(terminal.Sequence != 0, "native Lockjaw emits terminal for delayed owner fixture");
        var role = NetSession.Role; bool authority = NetSession.IsAuthority; int local = NetSession.LocalSlot;
        try
        {
            typeof(NetSession).GetProperty(nameof(NetSession.Role))!.SetValue(null, NetRole.Client);
            typeof(NetSession).GetProperty(nameof(NetSession.IsAuthority))!.SetValue(null, false);
            typeof(NetSession).GetProperty(nameof(NetSession.LocalSlot))!.SetValue(null, player.SlotIndex);
            foreach (string variant in new[] { "moving", "stationary", "duplicate", "no-contact", "rising", "dead", "new-life", "new-generation", "other-owner", "unmorphed" })
            {
                player.Health = 99; player.ModForceForm(true);
                player.ModPlaceAt(variant == "stationary" || variant == "no-contact" ? start : start + Vector3.UnitX * 10);
                var speed = new Vector3(.3f, variant == "rising" ? 2 : 0, .1f); player.Speed = speed;
                if (variant == "dead") player.Health = 0;
                if (variant == "unmorphed") player.ModForceForm(false);
                typeof(NetSession).GetProperty(nameof(NetSession.LocalSlot))!.SetValue(null,
                    variant == "other-owner" ? 0 : player.SlotIndex);
                var key = terminal.State.Key;
                if (variant == "new-life") key = key with { LifeId = (ushort)(key.LifeId + 1) };
                if (variant == "new-generation") key = key with { Generation = (ushort)(key.Generation + 1) };
                var fact = terminal with { Sequence = 1, State = terminal.State with { Key = key },
                    Reason = variant == "no-contact" ? (byte)0 : terminal.Reason };
                var world = new NetBombWorld(scene); world.Begin(key.MatchId, key.AuthorityEpoch);
                var receiver = new BombStateReceiver();
                foreach (var page in BombStatePage.PackSnapshot(key.MatchId, key.AuthorityEpoch, fact.Frame, 0, Array.Empty<BombState>())) receiver.Accept(page);
                world.ApplyBaseline(receiver);
                Check(world.ReceiveEvent(fact), $"delayed terminal accepted {variant}");
                bool eligible = variant is "moving" or "stationary" or "duplicate" or "rising";
                Check(player.Speed == speed.WithY(eligible ? Math.Max(speed.Y, Fixed.ToFloat(player.Values.BombJumpSpeed)) : speed.Y),
                    $"delayed bomb jump preserves native impulse and lifecycle {variant}");
                if (variant == "duplicate")
                {
                    player.Speed = speed;
                    Check(!world.ReceiveEvent(fact) && player.Speed == speed, "duplicate terminal cannot reapply owner jump");
                }
            }
        }
        finally
        {
            typeof(NetSession).GetProperty(nameof(NetSession.Role))!.SetValue(null, role);
            typeof(NetSession).GetProperty(nameof(NetSession.IsAuthority))!.SetValue(null, authority);
            typeof(NetSession).GetProperty(nameof(NetSession.LocalSlot))!.SetValue(null, local);
            player.Health = 99; player.ModForceForm(true);
        }
    }
    private static void PendingLifeReplacement(PlayerEntity player)
    {
        NetBombs.Reset(); player.ModForceForm(false); player.Health = 99;
        NetSession.RemoteIntentValid[player.SlotIndex] = false;
        var old = new BombIntent(ShotKey.For(player.SlotIndex, ++_sequence), NetSession.NetFrame, BombAction.Place);
        NetBombs.ReceiveRequest(player.SlotIndex, old); NetBombs.AfterSimulation();
        Check(NetBombs.PendingCount == 1, "request waits for matching source pose");
        player.Spawn(player.Position, Vector3.UnitZ, Vector3.UnitY, player.NodeRef, respawn: true);
        player.ModForceForm(false);
        var current = new BombIntent(ShotKey.For(player.SlotIndex, 1), NetSession.NetFrame, BombAction.Place);
        ulong frontier = player.OwningScene.Bombs.Frontier;
        NetBombs.ReceiveRequest(player.SlotIndex, current);
        Check(NetBombs.PendingCount == 1 && player.OwningScene.Bombs.Frontier == frontier + 1,
            "new-life request replaces stale pending request with explicit stale terminal");
        NetSession.RemoteIntentValid[player.SlotIndex] = true;
        NetSession.RemoteIntents[player.SlotIndex] = new IntentPacket { Frame = current.Frame };
        NetBombs.AfterSimulation();
        Check(NetBombs.PendingCount == 0 && player.OwningScene.Bombs.Frontier == frontier + 2,
            "new-life request independently reaches form refusal rather than being swallowed");
    }
    public static int Run(string profile, string room)
    {
        var sim = new ServerSim();
        if (!sim.Start(room, GameMode.Battle, 2, _ => { }, () => { })) return 1;
        try
        {
            NetSession.ApplyMatchState(new MatchStatePacket { MatchId = 1, AuthorityEpoch = 1, RoomKey = room, Mode = (byte)GameMode.Battle }, false);
            var roster = RosterPacket.Create(); roster.MatchId = 1; roster.AuthorityEpoch = 1; roster.Revision = 1; roster.Count = 2;
            for (byte i = 0; i < 2; i++) { roster.Slots[i] = i; roster.Generations[i] = 1; }
            NetSession.ApplyRoster(roster); for (int i = 0; i < 120; i++) sim.Step();
            NetBombs.Reset(); _sequence = 0;
            var player = PlayerEntity.Players[1]; player.Hunter = Hunter.Samus;
            NativeTriangle(player, PlayerEntity.Players[0]);
            Admission(player); Presentation(player); NativeEntityLifecycle(player); DetonationGates(player); PendingLifeReplacement(player); DelayedOwnerJump(player);
            Check(sim.StepFailures == 0, "asset authority fixture has no simulation failures");
            Console.WriteLine($"PASS: {_checks} bomb scene assertions ({profile})"); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { sim.Stop(); NetSession.Stop(); }
    }
}
