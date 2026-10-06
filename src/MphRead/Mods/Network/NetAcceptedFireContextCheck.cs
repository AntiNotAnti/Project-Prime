using System;
using System.Reflection;
using MphRead.Entities;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

/// <summary>Benign native weapon fixtures for original firing-context parity.
/// An ordinary successful native shot supplies each repeated event. The fixture
/// then checks normal/recovered authority launch mechanics; it is not a network
/// end-to-end prediction test or a damage/attack fabrication harness.</summary>
public static class NetAcceptedFireContextCheck
{
    private static int _checks;
    private static void Check(bool ok, string name)
    {
        if (!ok) throw new InvalidOperationException(name);
        _checks++; Console.WriteLine($"FIRE CONTEXT PASS {name}");
    }

    public static int Run(string room)
    {
        Headless.Enter(); _checks = 0;
        int ceiling = NetUnlagged.MaxRewindFrames;
        bool previousHalf = Features.HalfDamageUnscoped;
        var sim = new ServerSim();
        if (!sim.Start(room, GameMode.Battle, 2, _ => { }, () => { })) return 1;
        try
        {
            NetSession.ApplyMatchState(new MatchStatePacket { MatchId = 1, AuthorityEpoch = 1,
                RoomKey = room, Mode = (byte)GameMode.Battle, Flags = MatchStatePacket.FlagInProgress }, false);
            var roster = RosterPacket.Create(); roster.MatchId = 1; roster.AuthorityEpoch = 1;
            roster.Revision = 1; roster.Count = 2;
            for (byte slot = 0; slot < 2; slot++)
            { roster.Slots[slot] = slot; roster.Generations[slot] = 1; roster.Hunters[slot] = (byte)Hunter.Samus; roster.Names[slot] = "Context"; }
            NetSession.ApplyRoster(roster); NetSlotManager.Sync();
            for (int i = 0; i < 120; i++) sim.Step();
            PlayerEntity player = PlayerEntity.Players[0];
            Check(player.ModIsInPlay && sim.StepFailures == 0, "native fixture actor spawned");
            var fireMethod = typeof(PlayerEntity).GetMethod("TryFireWeapon", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var aimMethod = typeof(PlayerEntity).GetMethod("UpdateAimVecs", BindingFlags.Instance | BindingFlags.NonPublic)!;
            void Frame(uint frame) => typeof(NetSession).GetProperty(nameof(NetSession.NetFrame))!.SetValue(null, frame);
            void RetireBeams()
            {
                foreach (var beam in player.EquipInfo.Beams)
                    if (beam != null) { beam.Lifespan = 0; beam.Flags |= BeamFlags.Collided; }
            }
            void Ready()
            {
                typeof(PlayerEntity).GetField("_timeSinceShot", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(player, (ushort)1000);
                player.Controls.Shoot.IsDown = player.Controls.Shoot.IsPressed = true;
                player.Controls.Shoot.IsReleased = false;
                player.ModSetAim(Vector3.UnitZ); aimMethod.Invoke(player, null);
            }
            IntentPacket Carrier(uint frame, FireEvent fire) => new()
            {
                MatchId = 1, AuthorityEpoch = 1, SlotGeneration = NetPlayerLifecycle.Generation(0), LifeId = NetPlayerLifecycle.Get(0),
                Frame = frame, AckFrame = frame, Position = fire.SourcePosition - Vector3.UnitZ * .1f,
                Aim = Vector3.UnitX, WeaponSelect = fire.Weapon, HasFireEvents = true, FireEventCount = 1,
                HasState = true, ChargeLevel = 0, Buttons = IntentButtons.Shoot,
                HasContinuousFireTick = fire.Kind == FireEventKind.ContinuousTick,
                ContinuousFireTick = fire.ContinuousPhase + 1
            };
            BeamProjectileEntity? Beam(uint id)
            { foreach (var beam in player.EquipInfo.Beams) if (beam.ModShotId == id && beam.Lifespan > 0) return beam; return null; }
            byte[] encoded = new byte[FireEvent.Size];
            foreach (BeamType weapon in new[] { BeamType.PowerBeam, BeamType.Missile, BeamType.Imperialist, BeamType.ShockCoil })
            {
                foreach (var variant in new[] { (Charged: false, Delayed: false), (Charged: false, Delayed: true),
                    (Charged: true, Delayed: false), (Charged: true, Delayed: true) })
                {
                    bool charged = variant.Charged, delayed = variant.Delayed;
                    if (charged && weapon is not (BeamType.PowerBeam or BeamType.Missile)) continue;
                    RetireBeams(); NetFireEvents.Reset(); NetSession.RemoteIntentValid[0] = false;
                    player.Health = 99; player.ModArmWeapon(weapon); Ready();
                    byte charge = charged ? (byte)(player.EquipInfo.Weapon.FullCharge * 2) : (byte)0;
                    player.EquipInfo.ChargeLevel = charge;
                    Features.HalfDamageUnscoped = true; player.ModSetZoom(weapon == BeamType.Imperialist);
                    uint source = NetSession.NetFrame + 200; Frame(source); NetUnlagged.Record(source);
                    IntentPacket authored = new() { Frame = source };
                    // The same fixture world supplies both roles sequentially.
                    // Owner role is essential for continuous targeting to stamp
                    // its source phase; an unowned server puppet has no decision.
                    NetRole sourceRole = NetSession.Role; int sourceSlot = NetSession.LocalSlot;
                    try
                    {
                        typeof(NetSession).GetProperty(nameof(NetSession.Role))!.SetValue(null, NetRole.Host);
                        typeof(NetSession).GetProperty(nameof(NetSession.LocalSlot))!.SetValue(null, 0);
                        Check((bool)fireMethod.Invoke(player, null)!, $"ordinary source native spawn/{weapon}/{charged}/{delayed}");
                        NetFireEvents.Fill(ref authored, 0);
                    }
                    finally
                    {
                        typeof(NetSession).GetProperty(nameof(NetSession.Role))!.SetValue(null, sourceRole);
                        typeof(NetSession).GetProperty(nameof(NetSession.LocalSlot))!.SetValue(null, sourceSlot);
                    }
                    Check(authored.FireEventCount == 1, $"successful native source emits one event/{weapon}/{delayed}");
                    FireEvent original = authored.FireEvents[0];
                    BeamProjectileEntity ownerBeam = Beam(original.ShotId)!;
                    float originalDamage = ownerBeam.Damage, originalHead = ownerBeam.HeadshotDamage;
                    Check(original.HasSourcePose && player.ModSupportsFireSource(original, out _),
                        $"native mid-step body supports native muzzle/{weapon}/{delayed}");
                    original.Write(encoded);
                    FireEvent decoded = FireEvent.Read(encoded);
                    Check(decoded.SourcePosition == original.SourcePosition && (decoded.SourceUp - original.SourceUp).Length < .0002f,
                        $"compact source body/up round trip/{weapon}/{delayed}");
                    Check(!FireEvent.Read(encoded.AsSpan(0, FireEvent.Protocol41Size)).HasSourcePose,
                        $"protocol41 fire retains old pose without synthetic source body/{weapon}/{delayed}");
                    RetireBeams(); NetFireEvents.Reset();
                    if (charged)
                    {
                        Frame(source - charge);
                        var held = Carrier(source - charge, decoded); held.FireEventCount = 0;
                        NetSession.AcceptSlotIntent(0, held);
                    }
                    uint delivery = source + (delayed ? 2u : 0u); Frame(delivery); NetUnlagged.Record(delivery);
                    var carrier = Carrier(delivery, decoded); carrier.FireEvents[0] = decoded;
                    NetSession.AcceptSlotIntent(0, carrier);
                    Check(NetAcceptedAttacks.Authorized(0, decoded.ShotId), $"ordinary repeated fire admitted/{weapon}/{delayed}");
                    player.ModSetZoom(false); player.ModSetAim(Vector3.UnitX); aimMethod.Invoke(player, null);
                    NetFireEvents.Prepare(player, carrier);
                    // A later input step may advance/reset its presentation hold
                    // clock; native launch must restore the admitted charge.
                    player.EquipInfo.ChargeLevel = 0;
                    Check(NetHooks.RemoteShotOrigin(player, player.ModMuzzlePos) == decoded.Origin
                        && (NetHooks.RemoteShotDirection(player, Vector3.UnitX).Normalized() - decoded.Direction).Length < .0001f,
                        $"carrier movement/aim cannot change admitted firing pose/{weapon}/{delayed}");
                    // The owner fires after movement, whereas its carrier body is
                    // sampled before movement. This intentional difference is valid.
                    Check(carrier.Position != decoded.SourcePosition, $"pre-step carrier and firing body may differ/{weapon}/{delayed}");
                    typeof(PlayerEntity).GetField("_timeSinceShot", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(player, (ushort)1000);
                    if (!delayed) Check((bool)fireMethod.Invoke(player, null)!, $"normal admitted native launch/{weapon}");
                    else NetAcceptedAttacks.EmitPending();
                    BeamProjectileEntity? authority = Beam(decoded.ShotId);
                    Check(authority != null && authority.SpawnPosition == decoded.Origin
                        && authority.Damage == originalDamage && authority.HeadshotDamage == originalHead,
                        $"normal/recovered native pose/damage/scope parity/{weapon}/{delayed}");
                    NetFireEvents.Prepare(player, carrier);
                    Check(!NetFireEvents.CanFire(player), $"repeated carrier cannot launch twice/{weapon}/{delayed}");
                }
            }
            // An ordinary native landing-camera phase can be absent from the
            // authority's later camera. Calculate its camera and muzzle using
            // native methods, then emit the source through TryFireWeapon. This
            // is a camera-phase fixture, not a falling-physics/network test.
            RetireBeams(); NetFireEvents.Reset(); NetSession.RemoteIntentValid[0] = false;
            var landingField = typeof(PlayerEntity).GetField("_field44C", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var standingField = typeof(PlayerEntity).GetField("_timeStanding", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var gunBobField = typeof(PlayerEntity).GetField("_gunViewBob", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var walkBobField = typeof(PlayerEntity).GetField("_walkViewBob", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var switchField = typeof(PlayerEntity).GetField("_camSwitchTimer", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var cameraMethod = typeof(PlayerEntity).GetMethod("UpdateCameraFirst", BindingFlags.Instance | BindingFlags.NonPublic)!;
            object oldLanding = landingField.GetValue(player)!, oldStanding = standingField.GetValue(player)!;
            object oldGunBob = gunBobField.GetValue(player)!, oldWalkBob = walkBobField.GetValue(player)!, oldSwitch = switchField.GetValue(player)!;
            NetRole landingRole = NetSession.Role; int landingSlot = NetSession.LocalSlot;
            try
            {
                player.ModArmWeapon(BeamType.PowerBeam); Ready(); player.EquipInfo.ChargeLevel = 0;
                landingField.SetValue(player, Fixed.ToFloat(800)); standingField.SetValue(player, (ushort)9);
                gunBobField.SetValue(player, 270f); walkBobField.SetValue(player, 0f);
                switchField.SetValue(player, (ushort)(player.Values.CamSwitchTime * 2));
                cameraMethod.Invoke(player, null); player.CameraInfo.Update(); aimMethod.Invoke(player, null);
                Vector3 eye = player.Position.AddY(Fixed.ToFloat(player.Values.AimYOffset));
                Check(MathF.Abs(player.CameraInfo.Position.Y - eye.Y + 2 * Fixed.ToFloat(800)) < .00001f,
                    "native capped landing camera reaches its source displacement");
                uint source = NetSession.NetFrame + 200; Frame(source); NetUnlagged.Record(source);
                typeof(NetSession).GetProperty(nameof(NetSession.Role))!.SetValue(null, NetRole.Host);
                typeof(NetSession).GetProperty(nameof(NetSession.LocalSlot))!.SetValue(null, 0);
                Check((bool)fireMethod.Invoke(player, null)!, "ordinary native landing-camera source spawn");
                var authored = new IntentPacket { Frame = source }; NetFireEvents.Fill(ref authored, 0);
                Check(authored.FireEventCount == 1 && (authored.FireEvents[0].SourceFlags & FireEvent.FlagSourceTransition) == 0,
                    "landing camera produces one non-transition native body event");
                FireEvent original = authored.FireEvents[0];
                float currentZeroRadius = new Vector3(Fixed.ToFloat(player.Values.FieldB0),
                    Fixed.ToFloat(player.Values.FieldB4), Fixed.ToFloat(player.Values.FieldB8)).Length
                    + MathF.Abs(Fixed.ToFloat(player.Values.MuzzleOffset)) + Fixed.ToFloat(20)
                    + MathF.Abs(Fixed.ToFloat(player.Values.WalkBobMax));
                Check((original.Origin - eye).Length > currentZeroRadius,
                    "ordinary landing muzzle exceeds a zero-current-bob allowance");
                RetireBeams(); NetFireEvents.Reset(); landingField.SetValue(player, 0f);
                typeof(NetSession).GetProperty(nameof(NetSession.Role))!.SetValue(null, landingRole);
                typeof(NetSession).GetProperty(nameof(NetSession.LocalSlot))!.SetValue(null, landingSlot);
                Frame(source + 2); NetUnlagged.Record(source + 2);
                Check(player.ModSupportsFireSource(original, out _),
                    "native source landing muzzle remains valid after current camera bob resets");
                var carrier = Carrier(source + 2, original); carrier.FireEvents[0] = original;
                NetSession.AcceptSlotIntent(0, carrier);
                Check(NetAcceptedAttacks.Authorized(0, original.ShotId), "delayed native landing source admitted with its original body");
                NetAcceptedAttacks.EmitPending();
                Check(Beam(original.ShotId)?.SpawnPosition == original.Origin,
                    "delayed native landing launch preserves original muzzle");
            }
            finally
            {
                landingField.SetValue(player, oldLanding); standingField.SetValue(player, oldStanding);
                gunBobField.SetValue(player, oldGunBob); walkBobField.SetValue(player, oldWalkBob); switchField.SetValue(player, oldSwitch);
                typeof(NetSession).GetProperty(nameof(NetSession.Role))!.SetValue(null, landingRole);
                typeof(NetSession).GetProperty(nameof(NetSession.LocalSlot))!.SetValue(null, landingSlot);
            }
            // Native turret relocation between source and delivery retains the
            // exact historical turret body and its native +0.4 muzzle offset.
            RetireBeams(); NetFireEvents.Reset(); NetSession.RemoteIntentValid[0] = false;
            Hunter previousHunter = player.Hunter; var previousValues = player.Values;
            PlayerFlags2 previousFlags = player.Flags2;
            var turretField = typeof(PlayerEntity).GetField("_halfturret", BindingFlags.Instance | BindingFlags.NonPublic)!;
            object? previousTurret = turretField.GetValue(player);
            try
            {
                player.Hunter = Hunter.Weavel;
                typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.Values))!.SetValue(player, Metadata.PlayerValues[(int)Hunter.Weavel]);
                typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.Flags2))!.SetValue(player, previousFlags | PlayerFlags2.Halfturret);
                var turret = new HalfturretEntity(player, player.OwningScene) { Health = 40 };
                turretField.SetValue(player, turret); turret.Create(); turret.Initialize();
                uint source = NetSession.NetFrame + 200; Frame(source); NetUnlagged.Record(source);
                var result = BeamProjectileEntity.Spawn(turret, turret.EquipInfo, turret.Position.AddY(.4f), Vector3.UnitZ,
                    BeamSpawnFlags.NoMuzzle, turret.NodeRef, player.OwningScene);
                Check(result != BeamResultFlags.NoSpawn, "ordinary native turret source spawn");
                var authored = new IntentPacket { Frame = source }; NetFireEvents.Fill(ref authored, 0);
                Check(authored.FireEventCount == 1 && authored.FireEvents[0].Kind == FireEventKind.TurretFire,
                    "native turret produces original body event");
                FireEvent original = authored.FireEvents[0]; RetireBeams(); NetFireEvents.Reset();
                turret.Reposition(Vector3.UnitX * .2f, turret.NodeRef);
                Frame(source + 2); NetUnlagged.Record(source + 2);
                var carrier = Carrier(source + 2, original); carrier.Position = player.Position; carrier.FireEvents[0] = original;
                Check(player.ModSupportsFireSource(original, out _) && original.SourcePosition != turret.Position,
                    "delayed native turret binds to retained ACK body after relocation");
                NetSession.AcceptSlotIntent(0, carrier); NetAcceptedAttacks.EmitPending();
                BeamProjectileEntity? restored = Beam(original.ShotId);
                Check(restored != null && restored.Owner == turret && restored.SpawnPosition == original.SourcePosition.AddY(.4f),
                    "delayed native turret keeps original body/muzzle relationship");
            }
            finally
            {
                player.Hunter = previousHunter; typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.Values))!.SetValue(player, previousValues);
                typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.Flags2))!.SetValue(player, previousFlags); turretField.SetValue(player, previousTurret);
            }
            // Resume cancels native transients created by ordinary owner
            // actions without manufacturing an impact, a life, or a new ID.
            RetireBeams(); NetFireEvents.Reset(); NetSession.RemoteIntentValid[0] = false;
            NetRole pauseRole = NetSession.Role; int pauseSlot = NetSession.LocalSlot;
            bool pauseAuthority = NetSession.IsAuthority;
            try
            {
                typeof(NetSession).GetProperty(nameof(NetSession.Role))!.SetValue(null, NetRole.Host);
                typeof(NetSession).GetProperty(nameof(NetSession.LocalSlot))!.SetValue(null, 0);
                player.ModArmWeapon(BeamType.PowerBeam); Ready(); player.EquipInfo.ChargeLevel = 0;
                Frame(NetSession.NetFrame + 200);
                Check((bool)fireMethod.Invoke(player, null)!, "ordinary native pre-pause beam spawn");
                var authored = new IntentPacket { Frame = NetSession.NetFrame }; NetFireEvents.Fill(ref authored, 0);
                uint beforeId = authored.FireEvents[0].ShotId;
                BeamProjectileEntity staleBeam = Beam(beforeId)!;
                Check(beforeId != 0 && staleBeam != null, "pre-pause native beam retains authored identity");
                typeof(NetSession).GetProperty(nameof(NetSession.Role))!.SetValue(null, NetRole.Client);
                typeof(NetSession).GetProperty(nameof(NetSession.IsAuthority))!.SetValue(null, false);
                Check(player.ModSpawnAcceptedBomb(player.Position, false, 0), "ordinary native pre-pause bomb spawn");
                BombEntity? staleBomb = null;
                foreach (EntityBase entity in player.OwningScene.Entities)
                    if (entity is BombEntity bomb && bomb.Owner == player) { staleBomb = bomb; break; }
                Check(staleBomb != null, "pre-pause bomb is in native scene pool");
                ShotKey fence = ShotKey.For(0, 0); Hunter hunter = player.Hunter;
                int health = player.Health, ammo = player.EquipInfo.Ammo;
                int bombHits = NetDamage.BombHits, pending = NetHitClaims.ClaimsPendingCurrent;
                ushort damageSequence = NetDamage.Sequence(0);
                player.EquipInfo.ChargeLevel = (ushort)(player.EquipInfo.Weapon.MinCharge * 2);
                typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.Flags2))!.SetValue(player,
                    player.Flags2 | PlayerFlags2.Shooting);
                player.ModContinuousFireTick = NetSession.NetFrame;
                player.Controls.ClearAll(); player.ModCancelClientCharge();
                Check(player.EquipInfo.ChargeLevel == 0 && !player.Flags2.TestFlag(PlayerFlags2.Shooting)
                    && player.ModContinuousFireTick == 0 && !player.Controls.Shoot.IsDown,
                    "pause cancels held charge and continuous shooting without release");
                NetFireEvents.DiscardLocalPending(0); NetFireEvents.RetireClientAttacks(player.OwningScene);
                bool retained = false;
                foreach (EntityBase entity in player.OwningScene.Entities)
                    retained |= entity is BeamProjectileEntity or BombEntity;
                Check(!retained && staleBeam.Lifespan == 0 && staleBeam.Owner == null && staleBomb!.Owner == null,
                    "resume silently retires native beams and bombs");
                Check(ShotKey.For(0, 0) == fence && player.Hunter == hunter && player.Health == health
                    && player.EquipInfo.Ammo == ammo, "resume attack cleanup preserves life hunter health and ammunition");
                Check(NetDamage.BombHits == bombHits && NetDamage.Sequence(0) == damageSequence
                    && NetHitClaims.ClaimsPendingCurrent == pending, "resume cleanup creates no native hit damage or claim");
                authored.FireEventCount = 0; NetFireEvents.Fill(ref authored, 0);
                Check(authored.FireEventCount == 0, "pause discards only queued outgoing fire history");
                typeof(NetSession).GetProperty(nameof(NetSession.Role))!.SetValue(null, NetRole.Host);
                typeof(NetSession).GetProperty(nameof(NetSession.IsAuthority))!.SetValue(null, pauseAuthority);
                Frame(NetSession.NetFrame + 1); Ready();
                Check((bool)fireMethod.Invoke(player, null)!, "ordinary native fresh shot after pause cleanup");
                authored.Frame = NetSession.NetFrame; NetFireEvents.Fill(ref authored, 0);
                Check(authored.FireEventCount == 1 && NetLifecycleTracker.Newer(authored.FireEvents[0].ShotId, beforeId),
                    "same-life next authored fire ID stays monotonic after pause");
            }
            finally
            {
                typeof(NetSession).GetProperty(nameof(NetSession.Role))!.SetValue(null, pauseRole);
                typeof(NetSession).GetProperty(nameof(NetSession.LocalSlot))!.SetValue(null, pauseSlot);
                typeof(NetSession).GetProperty(nameof(NetSession.IsAuthority))!.SetValue(null, pauseAuthority);
            }
            NetUnlagged.MaxRewindFrames = 45;
            Check(LagCompensationPolicy.TryAdmitTime(100, 55, 0, out double at) && at == 55, "45-frame boundary admitted exactly");
            Check(!LagCompensationPolicy.TryAdmitTime(100, 54, 255, out _), "one fractional tick outside 45 frames refused");
            Check(LagCompensationPolicy.TryAdmitTime(100, 55, 1, out _), "fractional point inside 45 frames admitted");
            Check(!LagCompensationPolicy.TryAdmitTime(100, 100, 1, out _), "fractional future ACK refused");
            Check(!LagCompensationPolicy.TryAdmitTime(100, 0, 0, out _), "absent exact ACK refused");
            NetUnlagged.MaxRewindFrames = 24;
            Check(LagCompensationPolicy.TryAdmitTime(100, 76, 0, out _) && !LagCompensationPolicy.TryAdmitTime(100, 75, 255, out _),
                "configured launch/contact/claim ceiling honored fractionally");
            Check(sim.StepFailures == 0, "native fixture has no scene step failures");
            Console.WriteLine($"FIRE CONTEXT PASS {_checks} assertions (native fixture; no live network prediction assertion)");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine($"FIRE CONTEXT FAIL {ex}"); return 1; }
        finally { NetUnlagged.MaxRewindFrames = ceiling; Features.HalfDamageUnscoped = previousHalf; sim.Stop(); }
    }
}
