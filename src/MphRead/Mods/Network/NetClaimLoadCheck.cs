using System;
using System.Collections.Generic;
using System.Reflection;
using MphRead.Entities;
using OpenTK.Mathematics;
namespace MphRead.Mods.Network;

/// <summary>Legacy ledger-only arbitration with real player damage under a
/// seeded virtual network. It does not test native accepted-attack evidence;
/// ordinary native firing-context checks cover that separate boundary.</summary>
public static class NetClaimLoadCheck
{
    private readonly record struct Delivery(int Shooter, HitClaimPacket Claim);
    public static int Run(string room)
    {
        var sim = new ServerSim();
        if (!sim.Start(room, GameMode.Battle, 8, _ => { }, () => { })) return 1;
        var sink = NetHitClaims.VerdictSink;
        try
        {
            static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
            NetSession.ApplyMatchState(new MatchStatePacket { MatchId = 1, AuthorityEpoch = 1, RoomKey = room, Mode = (byte)GameMode.Battle }, false);
            var roster = RosterPacket.Create(); roster.MatchId = 1; roster.AuthorityEpoch = 1; roster.Revision = 1; roster.Count = 8;
            for (byte slot = 0; slot < 8; slot++) { roster.Slots[slot] = slot; roster.Generations[slot] = 1; roster.Names[slot] = $"LOAD{slot}"; }
            NetSession.ApplyRoster(roster); NetSlotManager.Sync();
            foreach (var player in PlayerEntity.Players) player.ModBootstrapSpawn();
            var scene = PlayerEntity.Players[0].OwningScene;
            var beams = new BeamProjectileEntity[8];
            foreach (var player in PlayerEntity.Players)
            {
                player.ModArmWeapon(BeamType.ShockCoil);
                BeamProjectileEntity.Spawn(player, player.EquipInfo, player.Position, Vector3.UnitY, BeamSpawnFlags.NoMuzzle, player.NodeRef, scene);
                foreach (var beam in player.EquipInfo.Beams) if (beam.Lifespan > 0) { beams[player.SlotIndex] = beam; break; }
                Check(beams[player.SlotIndex] != null, "real Shock Coil projectile exists");
                typeof(PlayerEntity).GetField("_spawnInvulnTimer", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(player, (ushort)0);
            }
            byte[] wire = new byte[1 + HitClaimPacket.Size]; wire[0] = 1;
            const int sourceFrames = 1800, hits = sourceFrames / 2;
            foreach (int rtt in new[] { 0, 50, 150, 250, 320, 400 })
            {
                NetHitClaims.Reset(); NetUnlagged.Reset();
                foreach (var player in PlayerEntity.Players) { player.Health = 20000; NetSession.SlotPing[player.SlotIndex] = rtt; }
                var queue = new NetFaultQueue<Delivery>(8128 + rtt, rtt / 2.0, rtt == 0 ? 0 : 80, rtt == 0 ? 0 : .05, .03, .01);
                var sends = new PriorityQueue<Delivery, double>();
                var physical = new PriorityQueue<Delivery, int>();
                var terminal = new byte[8, hits + 1]; int verdicts = 0, suppressed = 0, tooOld = 0;
                var repliesThisPump = new int[8]; int peakRepliesPerShooterPump = 0;
                NetHitClaims.VerdictSink = (int slot, ReadOnlySpan<(ushort Id, byte Result)> entries) =>
                {
                    Check(entries.Length <= HitVerdictPacket.MaxPerPacket, "every terminal delivery batch stays bounded");
                    repliesThisPump[slot] += entries.Length;
                    foreach (var (id, result) in entries)
                    {
                        // The ordinary bounded outbox plus a 400 ms pump stall
                        // can outlive the configured rewind admission window.
                        // Physical damage still proceeds independently below.
                        Check(result is HitVerdictPacket.ResultApplied or HitVerdictPacket.ResultDuplicate or HitVerdictPacket.ResultTooOld,
                            $"claim {slot}/{id}: {HitVerdictPacket.Describe(result)}");
                        byte stored = (byte)(result + 1);
                        if (terminal[slot, id] == 0) { terminal[slot, id] = stored; verdicts++; if (result == HitVerdictPacket.ResultTooOld) tooOld++; }
                        else Check(terminal[slot, id] == stored, "one immutable terminal verdict");
                    }
                };
                for (int tick = 1; tick <= sourceFrames + 180; tick++)
                {
                    Array.Clear(repliesThisPump);
                    NetSession.Update(NetSession.NetFrame / 60.0);
                    uint frame = NetSession.NetFrame;
                    NetUnlagged.Record(frame);
                    double now = tick * 1000.0 / 60;
                    if (tick <= sourceFrames && tick % 2 == 0)
                        for (int shooter = 0; shooter < 8; shooter++)
                        {
                            int victim = (shooter + 1) % 8;
                            var claim = new HitClaimPacket { MatchId = 1, AuthorityEpoch = 1,
                                ShooterGeneration = 1, ShooterLifeId = NetPlayerLifecycle.Get(shooter),
                                VictimSlot = (byte)victim, VictimGeneration = 1, VictimLifeId = NetPlayerLifecycle.Get(victim),
                                ClaimId = (ushort)(tick / 2), AckFrame = frame, LaunchFrame = frame, ShotId = (uint)(tick / 2),
                                Damage = 1, Beam = (byte)BeamType.ShockCoil, HitPoint = PlayerEntity.Players[victim].Position };
                            var delivery = new Delivery(shooter, claim);
                            // Schedule future sends separately from link delay.
                            // NetFaultQueue preserves FIFO timing, so enqueueing
                            // future retries as extra link hold would block later
                            // first attempts behind a send that has not happened.
                            for (int attempt = 0; attempt < 6; attempt++) sends.Enqueue(delivery, now + attempt * 80);
                            physical.Enqueue(delivery, tick + (tick % 10 == 0 ? 64 : 0));
                        }
                    // Both a client send stall and authority pump stall are modeled
                    // as 400 ms of queued work; source frames and histories continue.
                    bool stalled = tick is >= 500 and < 524 or >= 1000 and < 1024;
                    if (!stalled)
                    {
                        while (sends.TryPeek(out _, out double due) && due <= now)
                            queue.Enqueue(now, sends.Dequeue());
                        // The live receiver reserves eight slot-state cells and
                        // one snapshot within its default realtime pump budget.
                        int receiveBudget = Math.Max(0, NetPumpBudget.Default.Realtime - PlayerEntity.SlotCapacity - 1);
                        while (receiveBudget-- > 0 && queue.TryDequeue(now, out var delivery))
                        {
                            delivery.Claim.Write(wire.AsSpan(1));
                            NetHitClaims.Receive(delivery.Shooter, wire, requireAttackEvidence: false);
                        }
                    }
                    NetHitClaims.Tick();
                    while (physical.TryPeek(out _, out int due) && due <= tick)
                    {
                        var delivery = physical.Dequeue(); var claim = delivery.Claim;
                        var beam = beams[delivery.Shooter]; beam.ModLaunchFrame = claim.LaunchFrame; beam.ModShotId = claim.ShotId;
                        beam.ModLaunchKey = ShotKey.For(delivery.Shooter, claim.ShotId);
                        if (NetHitClaims.AlreadyRescued(delivery.Shooter, claim.VictimSlot, claim.ShotId, beam.ModLaunchKey)) { suppressed++; continue; }
                        var victim = PlayerEntity.Players[claim.VictimSlot]; int before = victim.Health;
                        victim.TakeDamage(1, DamageFlags.IgnoreInvuln | DamageFlags.NoDmgInvuln, null, beam);
                        Check(victim.Health == before - 1, "one physical damage application");
                    }
                    NetHitClaims.Tick();
                    foreach (int replies in repliesThisPump)
                        peakRepliesPerShooterPump = Math.Max(peakRepliesPerShooterPump, replies);
                }
                var missing = new List<string>();
                for (int slot = 0; slot < 8 && missing.Count < 8; slot++)
                    for (int id = 1; id <= hits && missing.Count < 8; id++)
                        if (terminal[slot, id] == 0) missing.Add($"{slot}/{id}");
                Check(verdicts == 8 * hits && NetHitClaims.ClaimsPendingCurrent == 0
                    && sends.Count == 0 && queue.Count == 0 && physical.Count == 0,
                    $"every declared claim terminates and drains: rtt={rtt} verdicts={verdicts}/{8 * hits} "
                    + $"pending={NetHitClaims.ClaimsPendingCurrent} sends={sends.Count} network={queue.Count} "
                    + $"physical={physical.Count} dropped={queue.Dropped} tooOld={tooOld} missing={String.Join(',', missing)}");
                foreach (var player in PlayerEntity.Players) Check(player.Health == 20000 - hits, "physical/rescued hit pays exactly once");
                if (rtt == 0) Check(peakRepliesPerShooterPump > HitVerdictPacket.MaxPerPacket,
                    "ordinary post-stall same-shooter reply burst exceeds one bounded verdict batch and all IDs terminate");
                Check(NetHitClaims.ClaimsCapacityRefused == 0 && NetHitClaims.ResolvedLedgerOverwrittenUnused == 0,
                    "ordinary sustained combat has no capacity loss or unmatched eviction");
                Console.WriteLine($"CLAIM LOAD PASS legacy ledger fixture rtt={rtt} claims={verdicts} tooOld={tooOld} rescued={NetHitClaims.AppliedHere} suppressed={suppressed} pendingHigh={NetHitClaims.ClaimsPendingHighWater} ledgerHigh={NetHitClaims.ResolvedLedgerHighWater} peakRepliesPerShooterPump={peakRepliesPerShooterPump}");
            }
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { NetHitClaims.VerdictSink = sink; sim.Stop(); }
    }
}
