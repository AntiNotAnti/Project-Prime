using System;
using System.Linq;
using System.Reflection;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Actual native firing + projectile collision. No fabricated paid witness or hit claim.
// A failure reports a reproduced native obstruction bypass; it does not authorize a fix.
internal static class SyluxMuzzleTests
{
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Field(PlayerEntity player, string name, object value)
        => typeof(PlayerEntity).GetField(name, Private)!.SetValue(player, value);
    private static int _checks, _failures;
    private static void Check(bool ok, string label)
    {
        _checks++;
        Console.WriteLine($"SYLUX {(ok ? "PASS" : "FAIL")} {label}");
        if (!ok) _failures++;
    }

    private static CollisionResult FindWall(PlayerEntity owner)
    {
        var point = owner.Position + Vector3.UnitY * .5f;
        foreach (var dir in new[] { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitZ, -Vector3.UnitZ })
        {
            CollisionResult hit = default;
            if (!CollisionDetection.CheckBetweenPoints(point, point + dir * 100,
                TestFlags.Beams, owner.OwningScene, ref hit)) continue;
            if (hit.Plane.Xyz.LengthSquared < .99f) continue;
            var normal = hit.Plane.Xyz.Normalized();
            var end = hit.Position - normal * .15f;
            CollisionResult beyond = default;
            if (!CollisionDetection.CheckBetweenPoints(end, end - normal * 2,
                TestFlags.Beams, owner.OwningScene, ref beyond)) return hit;
        }
        throw new InvalidOperationException("No real wall with a clear two-unit lane beyond its muzzle plane.");
    }

    private static void Case(PlayerEntity owner, PlayerEntity victim, CollisionResult wall, bool remote, bool blocked,
        bool lateral = false, BeamType weapon = BeamType.PowerBeam, bool staleTarget = false)
    {
        NetFireEvents.Reset(); NetHitClaims.Reset();
        // Select the native owner-origin hook for the local case; remote playback
        // keeps the dedicated server sentinel and consumes its authored event.
        typeof(NetSession).GetProperty(nameof(NetSession.LocalSlot))!.SetValue(null, remote ? -1 : owner.SlotIndex);
        typeof(NetSession).GetProperty(nameof(NetSession.Role))!.SetValue(null, remote ? NetRole.Server : NetRole.Host);
        var normal = wall.Plane.Xyz.Normalized(); var direction = -normal;
        // A 0.3-unit muzzle interval crossing the actual plane, or an open control
        // entirely in front of it shooting away from the wall.
        var muzzle = blocked ? wall.Position - normal * .15f : wall.Position + normal * .5f;
        if (!blocked) direction = lateral ? -Vector3.Cross(normal, Vector3.UnitY).Normalized() : normal;
        var body = blocked ? wall.Position + normal * .6f : muzzle - direction * .3f;
        foreach (var player in new[] { owner, victim })
        {
            foreach (var beam in player.EquipInfo.Beams) beam.Lifespan = 0;
            player.Health = 999; player.ModForceForm(false); player.ModReleaseSpawnProtection();
            player.TeamIndex = player.SlotIndex;
            player.ModSetFrozen(false); player.Controls.ClearAll();
            Field(player, "_spawnInvulnTimer", (ushort)0);
            Field(player, "_damageInvulnTimer", (ushort)0);
        }
        owner.Hunter = Hunter.Sylux; owner.ModPlaceAt(body - Vector3.UnitY * .5f);
        victim.ModPlaceAt(muzzle + direction * 2 - Vector3.UnitY * .5f);
        owner.ModArmWeapon(weapon); owner.EquipInfo.InfiniteAmmo = true;
        // The real pre-simulation input hook clears the selector result each tick.
        owner.ModContinuousFireTick = 0;
        owner.ModContinuousNetworkTarget = NetTargetIdentity.None;
        owner.EquipInfo.ChargeLevel = 0; owner.TimeSinceShot = 1000;
        typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.GunAnimation))!.SetValue(owner, GunAnimation.Idle);
        Field(owner, "_muzzlePos", muzzle);
        Field(owner, "_gunDrawPos", blocked ? wall.Position + normal * .15f : body);
        Field(owner, "_aimPosition", muzzle + direction * 10); Field(owner, "_gunVec1", direction);
        owner.Controls.Shoot.IsDown = true; owner.Controls.Shoot.IsPressed = true;
        uint frame = NetSession.NetFrame;
        if (staleTarget)
        {
            owner.ModContinuousFireTick = unchecked(frame - 1);
            owner.ModContinuousNetworkTarget = NetTargetIdentity.ForSlot(victim.SlotIndex);
        }
        NetUnlagged.Record(frame); NetDynamicGeometryHistory.RecordWorld(frame);
        CollisionResult interval = default;
        bool obstruction = CollisionDetection.CheckBetweenPoints(body, muzzle,
            TestFlags.Beams, owner.OwningScene, ref interval);
        Check(obstruction == blocked, $"{(remote ? "remote" : "owner")} interval fixture blocked={blocked}");
        int before = victim.Health;
        if (remote)
        {
            byte slot = (byte)owner.SlotIndex;
            bool continuous = weapon == BeamType.ShockCoil;
            var fire = new FireEvent(1, frame, frame, 0,
                continuous ? FireEventKind.ContinuousTick : FireEventKind.PressFire,
                (byte)weapon, 0, continuous ? frame : 0, FireEvent.FlagPose,
                muzzle, direction, direction, direction, SourceLife: NetPlayerLifecycle.Get(slot),
                Target: continuous ? NetTargetIdentity.None : default);
            var intent = new IntentPacket { MatchId = NetSession.CurrentMatchId,
                AuthorityEpoch = NetSession.AuthorityEpoch, SlotGeneration = NetPlayerLifecycle.Generation(slot),
                LifeId = NetPlayerLifecycle.Get(slot), Frame = frame, AckFrame = frame,
                Position = owner.Position, Aim = direction, WeaponSelect = (byte)weapon,
                Buttons = IntentButtons.InPlayState | IntentButtons.Shoot, HasFireEvents = true, FireEventCount = 1 };
            intent.FireEvents[0] = fire;
            Check(NetFireEvents.Accept(slot, intent), "remote native event accepted in Shadow mode");
            NetFireEvents.DrainAccepted(owner);
        }
        else
        {
            bool fired = (bool)typeof(PlayerEntity).GetMethod("TryFireWeapon", Private)!.Invoke(owner, null)!;
            Check(fired, "owner native TryFireWeapon traverses actual spawn path");
            if (weapon == BeamType.ShockCoil)
            {
                var intent = new IntentPacket { Frame = frame, MatchId = NetSession.CurrentMatchId,
                    AuthorityEpoch = NetSession.AuthorityEpoch, SlotGeneration = NetPlayerLifecycle.Generation(owner.SlotIndex),
                    LifeId = NetPlayerLifecycle.Get(owner.SlotIndex) };
                NetFireEvents.Fill(ref intent, owner.SlotIndex);
                Check(intent.FireEventCount == 1 && intent.FireEvents[0].ContinuousPhase == frame,
                    $"native Coil retains its source phase: frame={frame} phase={intent.FireEvents[0].ContinuousPhase}");
                Check(intent.FireEvents[0].Target == (blocked ? NetTargetIdentity.None : NetTargetIdentity.ForSlot(victim.SlotIndex)),
                    blocked ? "blocked Coil advertises explicit no target, including stale selection"
                        : "open Coil retains the target captured for its source frame");
                byte[] bytes = new byte[CompactIntentCodec.Size(intent)];
                int size = CompactIntentCodec.Write(intent, bytes);
                Check(CompactIntentCodec.TryRead(bytes.AsSpan(0, size), out var decoded)
                    && decoded.FireEventCount == 1 && decoded.FireEvents[0].ShotId == intent.FireEvents[0].ShotId,
                    "native Coil carrier survives production compact decoder");
            }
        }
        var beams = owner.EquipInfo.Beams.Where(beam => beam.Lifespan > 0).ToArray();
        Check(blocked || beams.Length > 0, $"{(remote ? "remote" : "owner")} open path supplies production projectile");
        for (int i = 0; i < 60 && victim.Health == before; i++)
            foreach (var beam in beams) if (beam.Lifespan > 0) beam.Process();
        int damage = before - victim.Health;
        Console.WriteLine($"SYLUX NATIVE blocked={blocked} remote={remote} damage={damage} wall={wall.Position} muzzle={muzzle} body={body}");
        Check(blocked ? damage == 0 : damage > 0,
            $"{(remote ? "remote" : "owner")} {(blocked ? "wall-at-muzzle prevents behind-wall damage" : lateral ? "lateral cover native control damages target" : "open native control damages target")}");
    }

    private static void LockjawPlacement(PlayerEntity owner, CollisionResult wall)
    {
        NetBombs.Reset();
        owner.Hunter = Hunter.Sylux; owner.Health = 999; owner.ModForceForm(true);
        owner.ModPlaceAt(wall.Position + wall.Plane.Xyz.Normalized() * .6f);
        owner.SyluxBombCount = 0; Array.Clear(owner.SyluxBombs);
        Field(owner, "_bombAmmo", (byte)3); Field(owner, "_bombCooldown", (ushort)0);
        Field(owner, "_abilities", AbilityFlags.Bombs);
        var request = new BombIntent(ShotKey.For(owner.SlotIndex, 1), NetSession.NetFrame, BombAction.Place);
        NetSession.RemoteIntentValid[owner.SlotIndex] = true;
        NetSession.RemoteIntents[owner.SlotIndex] = new IntentPacket { Frame = request.Frame };
        NetBombs.ReceiveRequest(owner.SlotIndex, request); NetBombs.AfterSimulation();
        BombEntity? native = null;
        foreach (var bomb in owner.OwningScene.GetBombEntities())
            if (bomb.ModOwnerKey == request.Key) native = bomb;
        Check(native != null && native.BombType == BombType.Lockjaw,
            "native Lockjaw request creates a bomb rather than a synthetic alt beam");
        if (native != null)
        {
            CollisionResult collision = default;
            Check(!CollisionDetection.CheckBetweenPoints(owner.Position, native.Position,
                TestFlags.Players, owner.OwningScene, ref collision),
                "native Lockjaw body-to-placement segment remains unobstructed beside wall");
        }
        Console.WriteLine("SYLUX Lockjaw scope: placement uses native downward body offset; beam muzzle guard is inapplicable. Bomb radius damage/snare parity is separate.");
    }

    public static int Run(string room)
    {
        _checks = _failures = 0;
        var mode = NetShotProof.Mode; bool accepted = NetFireEvents.AcceptedPlaybackEnabled;
        var sim = new ServerSim();
        if (!sim.Start(room, GameMode.Battle, 2, _ => { }, () => { })) return 1;
        try
        {
            NetSession.ApplyMatchState(new MatchStatePacket { MatchId = 1, AuthorityEpoch = 1,
                RoomKey = room, Mode = (byte)GameMode.Battle }, false);
            var roster = RosterPacket.Create(); roster.MatchId = 1; roster.AuthorityEpoch = 1;
            roster.Revision = 1; roster.Count = 2;
            for (byte i = 0; i < 2; i++) { roster.Slots[i] = i; roster.Generations[i] = 1; }
            NetSession.ApplyRoster(roster);
            for (int i = 0; i < 120; i++) sim.Step();
            NetFireEvents.AcceptedPlaybackEnabled = true; NetShotProof.Mode = ShotProofMode.Shadow;
            var owner = PlayerEntity.Players[0]; var victim = PlayerEntity.Players[1];
            owner.OwningScene.GameState.EnhancedHunters = false;
            owner.OwningScene.GameState.BalancedMode = false;
            var wall = FindWall(owner);
            Case(owner, victim, wall, false, false); Case(owner, victim, wall, false, true);
            Case(owner, victim, wall, true, false); Case(owner, victim, wall, true, true);
            Case(owner, victim, wall, false, false, lateral: true);
            Case(owner, victim, wall, true, false, lateral: true);
            Case(owner, victim, wall, false, true, weapon: BeamType.ShockCoil);
            Case(owner, victim, wall, false, true, weapon: BeamType.ShockCoil, staleTarget: true);
            Case(owner, victim, wall, false, false, weapon: BeamType.ShockCoil);
            Case(owner, victim, wall, true, true, weapon: BeamType.ShockCoil);
            LockjawPlacement(owner, wall);
            Check(sim.StepFailures == 0, "native scene has no step failures");
            Console.WriteLine($"SYLUX {_checks} assertions, {_failures} failures");
            return _failures == 0 ? 0 : 1;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { sim.Stop(); NetSession.Stop(); NetFireEvents.AcceptedPlaybackEnabled = accepted; NetShotProof.Mode = mode; }
    }
}
