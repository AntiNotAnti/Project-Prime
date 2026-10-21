using System;
using System.Buffers.Binary;
using System.Linq;
using System.Reflection;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Formats.Collision;
using OpenTK.Mathematics;
using MphRead.Mods.Multiplayer;

namespace MphRead.Mods.Network
{
    public static partial class NetLobbyTest
    {
        public static int RunAdvancedRules()
        {
            try { AdvancedRulesChecks(); AdvancedRulesScenario(); Console.WriteLine($"[advanced-rules] PASS {_checks} checks"); return 0; }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
            finally { NetSession.Stop(); }
        }
        public static int RunAdvancedRulesScene()
        {
            try
            {
                foreach (var option in Launcher.OfflineLaunch.Modes)
                {
                    if (option.Mode is GameMode.GunGame or GameMode.OneInTheChamber)
                    {
                        Check(!MatchModifierRules.Validate(new MatchDefinition { Mode = option.Mode, InstaGib = true }, out _),
                            option.Label + " rejects a modifier that would replace its loadout");
                        continue;
                    }
                    string room = ThumbnailGenerator.MultiplayerRooms().First(key =>
                        MapModeCapabilities.Supports(key, option.Mode, MatchWorldProfile.Resolve(8), out _));
                    var definition = new MatchDefinition { RoomKey = room, Mode = option.Mode,
                        InstaGib = true, LowTier = true, TimeLimitSeconds = 600, PointGoal = MatchGoalRules.DefaultValue(option.Mode) };
                    var session = new SessionStatePacket { MatchId = 1, AuthorityEpoch = 1, MaxPlayers = 8,
                        Phase = SessionPhase.InMatch, Match = definition, WorldProfile = MatchWorldProfile.Resolve(8) };
                    var roster = RosterPacket.Create(); roster.Count = 8; roster.MatchId = 1; roster.AuthorityEpoch = 1; roster.Revision = 1;
                    for (byte i = 0; i < 8; i++)
                    { roster.Slots[i] = i; roster.Generations[i] = 1; roster.Names[i] = "RULE TEST";
                        roster.Hunters[i] = (byte)HunterRules.RandomAllowed(i, true); roster.Teams[i] = (sbyte)(i % 2); }
                    var sim = new ServerSim();
                    try
                    {
                        Check(sim.Start(room, option.Mode, 8, _ => { }, () => { }, roster, session), option.Label + " loads");
                        NetSlotManager.Sync();
                        var player = PlayerEntity.Players[0];
                        var game = player.OwningScene.GameState;
                        string handler = game.ModeState.Method.Name;
                        Check(game.Mode == option.Mode && game.InstaGib && game.LowTier, option.Label + " keeps base mode and modifiers");
                        Check(handler == "ModeState" + (option.Mode.ToString().Replace("Teams", "")), option.Label + " uses base objective handler");
                        for (int life = 0; life < 3; life++)
                        {
                            player.Spawn(player.Position, Vector3.UnitZ, Vector3.UnitY, player.NodeRef, respawn: life > 0);
                            Check(player.CurrentWeapon == BeamType.Imperialist && player.EquipInfo.InfiniteAmmo
                                && Enumerable.Range(0, 9).All(i => player.AvailableWeapons[(BeamType)i] == (i == (int)BeamType.Imperialist)),
                                option.Label + " Imperialist-only respawn " + life);
                            typeof(PlayerEntity).GetMethod("PickUpWeapon", BindingFlags.Instance | BindingFlags.NonPublic)!
                                .Invoke(player, new object[] { ItemType.ShockCoil });
                            player.ModSetWeapon(BeamType.ShockCoil);
                            Check(!player.AvailableWeapons[BeamType.ShockCoil], option.Label + " rejects weapon pickup " + life);
                            var ammo = (int[])typeof(PlayerEntity).GetField("_ammo", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(player)!;
                            Check(ammo[0] == 99, option.Label + " displays 99 UA " + life);
                        }
                        Check(PlayerEntity.Players.All(p => HunterRules.Allowed(p.Hunter, true)), "eight-player Low Tier construction");
                        Check(sim.StepFailures == 0, "no authority step failures");
                    }
                    finally { sim.Stop(); NetSession.Stop(); }
                }
                CheckNoImpScene();
                CheckBalancedImpScene();
                CheckBalancedRangeScene();
                CheckBalancedHunterScene();
                CheckBalancedAffinityScene();
                Console.WriteLine($"[advanced-rules-scene] PASS {_checks} checks"); return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }

        private static void CheckNoImpScene()
        {
            const string room = "MP1 SANCTORUS";
            var match = new MatchDefinition { RoomKey = room, Mode = GameMode.Battle,
                NoImperialist = true, AffinityWeapons = true, TimeLimitSeconds = 600, PointGoal = 100 };
            var session = new SessionStatePacket { MatchId = 1, AuthorityEpoch = 1, MaxPlayers = 8,
                Phase = SessionPhase.InMatch, Match = match, WorldProfile = MatchWorldProfile.Resolve(8) };
            var roster = RosterPacket.Create(); roster.Count = 1; roster.MatchId = 1; roster.AuthorityEpoch = 1; roster.Revision = 1;
            roster.Slots[0] = 0; roster.Generations[0] = 1; roster.Names[0] = "TRACE"; roster.Hunters[0] = (byte)Hunter.Trace;
            var sim = new ServerSim();
            try
            {
                Check(sim.Start(room, GameMode.Battle, 8, _ => { }, () => { }, roster, session), "No Imp world loads");
                NetSlotManager.Sync();
                var player = PlayerEntity.Players[0]; var scene = player.OwningScene;
                Check(scene.GameState.NoImperialist && player.Hunter == Hunter.Trace, "No Imp allows Trace");
                foreach (var entity in scene.Entities)
                    if (entity is ItemSpawnEntity spawn) Check(spawn.Data.ItemType != ItemType.Imperialist, "No Imp removes Imperialist spawn before construction");
                for (int life = 0; life < 3; life++)
                {
                    player.Spawn(player.Position, Vector3.UnitZ, Vector3.UnitY, player.NodeRef, respawn: life > 0);
                    foreach (var item in new[] { ItemType.Imperialist, ItemType.AffinityWeapon })
                    {
                        typeof(PlayerEntity).GetMethod("PickUpWeapon", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(player, new object[] { item });
                        player.ModSetWeapon(BeamType.Imperialist);
                        Check(!player.AvailableWeapons[BeamType.Imperialist], "Trace cannot acquire Imperialist on life " + life);
                    }
                }
                var drop = new ItemInstanceEntity(new ItemInstanceEntityData(Vector3.Zero, ItemType.Imperialist, 60), default, scene);
                Check(drop.ItemType != ItemType.Imperialist, "scripted and dropped Imperialist use the same restriction");
            }
            finally { sim.Stop(); NetSession.Stop(); }
        }

        private static void CheckBalancedImpScene()
        {
            const string room = "MP1 SANCTORUS";
            var match = new MatchDefinition { RoomKey = room, Mode = GameMode.Battle,
                BalancedMode = true, TimeLimitSeconds = 600, PointGoal = 100 };
            var session = new SessionStatePacket { MatchId = 1, AuthorityEpoch = 1, MaxPlayers = 8,
                Phase = SessionPhase.InMatch, Match = match, WorldProfile = MatchWorldProfile.Resolve(8) };
            var roster = RosterPacket.Create(); roster.Count = 1; roster.MatchId = 1; roster.AuthorityEpoch = 1; roster.Revision = 1;
            roster.Slots[0] = 0; roster.Generations[0] = 1; roster.Names[0] = "TRACE"; roster.Hunters[0] = (byte)Hunter.Trace;
            var sim = new ServerSim();
            try
            {
                Check(sim.Start(room, GameMode.Battle, 8, _ => { }, () => { }, roster, session), "Balanced Mode world loads");
                NetSlotManager.Sync();
                var player = PlayerEntity.Players[0];
                player.Spawn(player.Position, Vector3.UnitZ, Vector3.UnitY, player.NodeRef, respawn: false);
                var pickup = typeof(PlayerEntity).GetMethod("PickUpWeapon", BindingFlags.Instance | BindingFlags.NonPublic)!;
                pickup.Invoke(player, new object[] { ItemType.Imperialist });
                player.ModSetWeapon(BeamType.Imperialist);
                Check(player.ModBalancedImperialistShots == PlayerEntity.BalancedImperialistShotCap,
                    "Balanced Imperialist pickup grants exactly five shots");
                int cost = player.EquipInfo.Weapon.AmmoCost;
                player.EquipInfo.Ammo -= cost;
                Check(player.ModBalancedImperialistShots == 4, "Balanced Imperialist spends its private reserve");
                var shared = (int[])typeof(PlayerEntity).GetField("_ammo", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(player)!;
                int sharedBefore = shared[0];
                shared[0] = Math.Min(player.ModAmmoCap, shared[0] + 100);
                Check(shared[0] >= sharedBefore && player.ModBalancedImperialistShots == 4,
                    "Universal Ammo refill does not refill Balanced Imperialist");
                pickup.Invoke(player, new object[] { ItemType.Imperialist });
                Check(player.ModBalancedImperialistShots == PlayerEntity.BalancedImperialistShotCap,
                    "another Imperialist pickup refills the private reserve");
                player.EquipInfo.Ammo = Int32.MaxValue;
                Check(player.ModBalancedImperialistShots == PlayerEntity.BalancedImperialistShotCap,
                    "Balanced Imperialist reserve is hard capped at five shots");
            }
            finally { sim.Stop(); NetSession.Stop(); }
        }

        private static void CheckBalancedRangeScene()
        {
            const string room = "MP1 SANCTORUS";
            var match = new MatchDefinition { RoomKey = room, Mode = GameMode.Battle,
                BalancedMode = true, TimeLimitSeconds = 600, PointGoal = 100 };
            var session = new SessionStatePacket { MatchId = 1, AuthorityEpoch = 1, MaxPlayers = 8,
                Phase = SessionPhase.InMatch, Match = match, WorldProfile = MatchWorldProfile.Resolve(8) };
            var roster = RosterPacket.Create(); roster.Count = 1; roster.MatchId = 1; roster.AuthorityEpoch = 1; roster.Revision = 1;
            roster.Slots[0] = 0; roster.Generations[0] = 1; roster.Names[0] = "RANGE"; roster.Hunters[0] = (byte)Hunter.Samus;
            var sim = new ServerSim();
            try
            {
                Check(sim.Start(room, GameMode.Battle, 8, _ => { }, () => { }, roster, session),
                    "Balanced range world loads");
                NetSlotManager.Sync();
                var owner = PlayerEntity.Players[0];
                var scene = owner.OwningScene;
                var beam = new BeamProjectileEntity(scene)
                {
                    Owner = owner,
                    Beam = BeamType.VoltDriver,
                    SpawnPosition = Vector3.Zero
                };

                static bool Near(float actual, float expected) => MathF.Abs(actual - expected) < 0.001f;

                Check(Near(BalancedModeRules.RangeDamageMultiplier(BeamType.VoltDriver, 0), 0.80f)
                    && Near(BalancedModeRules.RangeDamageMultiplier(BeamType.VoltDriver, BalancedModeRules.MidRange), 1.00f)
                    && Near(BalancedModeRules.RangeDamageMultiplier(BeamType.VoltDriver, BalancedModeRules.FarRange), 1.20f),
                    "Volt range curve is 80/100/120 percent");
                Check(Near(BalancedModeRules.RangeDamageMultiplier(BeamType.Magmaul, 0), 1.20f)
                    && Near(BalancedModeRules.RangeDamageMultiplier(BeamType.Magmaul, BalancedModeRules.MidRange), 1.00f)
                    && Near(BalancedModeRules.RangeDamageMultiplier(BeamType.Magmaul, BalancedModeRules.FarRange), 0.75f),
                    "Magmaul range curve is 120/100/75 percent");

                float voltClose = beam.ModBalancedRangeDamage(100, Vector3.Zero);
                float voltMid = beam.ModBalancedRangeDamage(100, Vector3.UnitZ * BalancedModeRules.MidRange);
                float voltFar = beam.ModBalancedRangeDamage(100, Vector3.UnitZ * BalancedModeRules.FarRange);
                Check(Near(voltClose, 80) && Near(voltMid, 100) && Near(voltFar, 120)
                    && voltClose < voltMid && voltMid < voltFar,
                    "Volt gains damage smoothly with travel distance");

                beam.Beam = BeamType.Magmaul;
                float magClose = beam.ModBalancedRangeDamage(100, Vector3.Zero);
                float magMid = beam.ModBalancedRangeDamage(100, Vector3.UnitZ * BalancedModeRules.MidRange);
                float magFar = beam.ModBalancedRangeDamage(100, Vector3.UnitZ * BalancedModeRules.FarRange);
                Check(Near(magClose, 120) && Near(magMid, 100) && Near(magFar, 75)
                    && magClose > magMid && magMid > magFar,
                    "Magmaul loses damage smoothly with travel distance");

                Check(Near(BalancedModeRules.ProjectileSpeedMultiplier(BeamType.VoltDriver, charged: false), 1.20f)
                    && Near(BalancedModeRules.ProjectileSpeedMultiplier(BeamType.VoltDriver, charged: true), 10f / 7f)
                    && Near(BalancedModeRules.ProjectileSpeedMultiplier(BeamType.Battlehammer, charged: false), 8192f / 4915f)
                    && Near(BalancedModeRules.ProjectileSpeedMultiplier(BeamType.Judicator, charged: false), 1.25f)
                    && Near(BalancedModeRules.ScaleProjectileSpeed(BeamType.Magmaul, charged: false, 6963), 7680),
                    "Balanced projectile speed table matches the first tuning pass");
                Check(Near(BalancedModeRules.ProjectileSpeedMultiplier(BeamType.ShockCoil, charged: false), 1)
                    && Near(BalancedModeRules.ProjectileSpeedMultiplier(BeamType.Imperialist, charged: false), 1),
                    "Shock Coil and Imperialist retain their original projectile speed");

                float voltBase = scene.WeaponRules[(int)BeamType.VoltDriver].UnchargedSpeed / 4096f / 2;
                float voltChargedBase = scene.WeaponRules[(int)BeamType.VoltDriver].ChargedSpeed / 4096f / 2;
                Check(Near(BeamProjectileEntity.ModBalancedProjectileSpeed(scene, owner,
                        BeamType.VoltDriver, charged: false, voltBase), 24576f / 4096f / 2)
                    && Near(BeamProjectileEntity.ModBalancedProjectileSpeed(scene, owner,
                        BeamType.VoltDriver, charged: true, voltChargedBase), 10240f / 4096f / 2),
                    "Balanced Volt runtime speed reaches 24576 uncharged and 10240 charged");
                float battleBase = scene.WeaponRules[(int)BeamType.Battlehammer].UnchargedSpeed / 4096f / 2;
                float judgeBase = scene.WeaponRules[(int)BeamType.Judicator].UnchargedSpeed / 4096f / 2;
                float magmaBase = scene.WeaponRules[(int)BeamType.Magmaul].UnchargedSpeed / 4096f / 2;
                Check(Near(BeamProjectileEntity.ModBalancedProjectileSpeed(scene, owner,
                        BeamType.Battlehammer, charged: false, battleBase), 8192f / 4096f / 2f)
                    && Near(BeamProjectileEntity.ModBalancedProjectileSpeed(scene, owner,
                        BeamType.Judicator, charged: false, judgeBase), judgeBase * 1.25f)
                    && Near(BeamProjectileEntity.ModBalancedProjectileSpeed(scene, owner,
                        BeamType.Magmaul, charged: false, magmaBase), 7680f / 4096f / 2),
                    "Balanced prediction weapons receive their configured runtime speed increases");

                int direct = 12, headshot = 12, splash = 4; byte splashType = 0;
                BeamProjectileEntity.ModBalancedHitTuning(scene, owner, BeamType.Battlehammer,
                    false, ref direct, ref headshot, ref splash, ref splashType);
                Check(direct == 12 && headshot == 12 && splash == 4 && splashType == 0,
                    "Balanced burst rifle preserves authored bullet damage and linear falloff");
                Check(BalancedModeRules.BattlehammerBurstRounds == 3
                    && BalancedModeRules.BattlehammerBulletSpacingFrames == 3
                    && BalancedModeRules.BattlehammerBurstCooldownFrames == 27
                    && BalancedModeRules.BattlehammerBurstAmmoCost == 12,
                    "Battlehammer burst has three 50-ms rounds, 450-ms cadence and one 12-UA charge");

                Vector3 projectileOrigin = new(1, 2, 3);
                Vector3 impactOrigin = new(4, 5, 6);
                Check(BeamProjectileEntity.ModSplashOrigin(scene, BeamType.Battlehammer,
                        projectileOrigin, impactOrigin) == impactOrigin
                    && BeamProjectileEntity.ModSplashOrigin(scene, BeamType.Magmaul,
                        projectileOrigin, impactOrigin) == projectileOrigin,
                    "Balanced Battlehammer splash uses the collision point without changing other weapons");

                direct = headshot = 3; splash = 4; splashType = 0;
                BeamProjectileEntity.ModBalancedHitTuning(scene, owner, BeamType.Battlehammer,
                    true, ref direct, ref headshot, ref splash, ref splashType);
                Check(direct == 3 && headshot == 3 && splash == 4,
                    "retired Battlehammer cluster flag never amplifies burst damage");

                direct = headshot = 32; splash = 16; splashType = 3;
                BeamProjectileEntity.ModBalancedHitTuning(scene, owner, BeamType.Magmaul,
                    false, ref direct, ref headshot, ref splash, ref splashType);
                Check(direct == 32 && headshot == 32 && splash == 12 && splashType == 0,
                    "Balanced Magmaul keeps direct damage but reduces splash and adds linear falloff");

                direct = 56; headshot = 56; splash = 28; splashType = 3;
                BeamProjectileEntity.ModBalancedHitTuning(scene, owner, BeamType.Magmaul,
                    false, ref direct, ref headshot, ref splash, ref splashType);
                Check(direct == 56 && headshot == 56 && splash == 21 && splashType == 0,
                    "Balanced charged Magmaul splash is 21 max before range falloff");

                direct = 24; headshot = 32; splash = 12; splashType = 0;
                BeamProjectileEntity.ModBalancedHitTuning(scene, owner, BeamType.Judicator,
                    false, ref direct, ref headshot, ref splash, ref splashType);
                Check(direct == 24 && headshot == 32 && splash == 9 && splashType == 0,
                    "Balanced Judicator keeps precision damage and reduces splash to 9");

                scene.GameState.BalancedMode = false;
                direct = 14; headshot = 14; splash = 6; splashType = 3;
                BeamProjectileEntity.ModBalancedHitTuning(scene, owner, BeamType.Battlehammer,
                    false, ref direct, ref headshot, ref splash, ref splashType);
                Check(Near(beam.ModBalancedRangeDamage(100, Vector3.UnitZ * BalancedModeRules.FarRange), 100)
                    && Near(BeamProjectileEntity.ModBalancedProjectileSpeed(scene, owner,
                        BeamType.VoltDriver, charged: false, voltBase), voltBase)
                    && BeamProjectileEntity.ModSplashOrigin(scene, BeamType.Battlehammer,
                        projectileOrigin, impactOrigin) == projectileOrigin
                    && direct == 14 && headshot == 14 && splash == 6 && splashType == 3,
                    "range, projectile speed, splash origin and hit tuning are inert outside Balanced Mode");
            }
            finally { sim.Stop(); NetSession.Stop(); }
        }

        private static void CheckBalancedHunterScene()
        {
            const string room = "MP1 SANCTORUS";
            var match = new MatchDefinition { RoomKey = room, Mode = GameMode.Battle,
                BalancedMode = true, TimeLimitSeconds = 600, PointGoal = 100 };
            var session = new SessionStatePacket { MatchId = 1, AuthorityEpoch = 1, MaxPlayers = 8,
                Phase = SessionPhase.InMatch, Match = match, WorldProfile = MatchWorldProfile.Resolve(8) };
            var roster = RosterPacket.Create(); roster.Count = 5; roster.MatchId = 1; roster.AuthorityEpoch = 1; roster.Revision = 1;
            Hunter[] hunters = { Hunter.Kanden, Hunter.Spire, Hunter.Noxus, Hunter.Weavel, Hunter.Samus };
            for (byte slot = 0; slot < hunters.Length; slot++)
            {
                roster.Slots[slot] = slot;
                roster.Generations[slot] = 1;
                roster.Names[slot] = hunters[slot].ToString();
                roster.Hunters[slot] = (byte)hunters[slot];
                roster.Teams[slot] = -1;
            }

            var sim = new ServerSim();
            try
            {
                Check(sim.Start(room, GameMode.Battle, 8, _ => { }, () => { }, roster, session),
                    "Balanced hunter profile world loads");
                NetSlotManager.Sync();
                var kanden = PlayerEntity.Players[0];
                var scene = kanden.OwningScene;
                var spire = PlayerEntity.Players[1];
                var noxus = PlayerEntity.Players[2];
                var weavel = PlayerEntity.Players[3];
                var samus = PlayerEntity.Players[4];

                foreach (var player in new[] { kanden, spire, noxus, weavel, samus })
                    player.Spawn(player.Position, Vector3.UnitZ, Vector3.UnitY, player.NodeRef, respawn: false);

                Check(kanden.Health == 109 && weavel.Health == 109
                    && spire.Health == 99 && noxus.Health == 99 && samus.Health == 99,
                    "Balanced low-tier spawn health bonuses preserve high-tier baseline");
                Check(new[] { kanden, spire, noxus, weavel, samus }.All(p => p.HealthMax == 199),
                    "Balanced profiles preserve the 199 multiplayer health ceiling");

                static bool Near(float actual, float expected) => MathF.Abs(actual - expected) < 0.001f;
                Check(Near(BalancedModeRules.HunterProfile(Hunter.Kanden).AltTractionMultiplier, 1.12f)
                    && Near(BalancedModeRules.HunterProfile(Hunter.Kanden).AltSpeedCapMultiplier, 1.08f)
                    && Near(BalancedModeRules.HunterProfile(Hunter.Weavel).AltTractionMultiplier, 1.10f)
                    && Near(BalancedModeRules.HunterProfile(Hunter.Weavel).AltSpeedCapMultiplier, 1.08f),
                    "Kanden and Weavel receive their mobility profiles");
                Check(Near(BalancedModeRules.HunterProfile(Hunter.Spire).IncomingDamageMultiplier, 0.90f)
                    && Near(BalancedModeRules.HunterProfile(Hunter.Spire).KnockbackMultiplier, 0.80f)
                    && Near(BalancedModeRules.HunterProfile(Hunter.Noxus).IncomingDamageMultiplier, 0.95f)
                    && Near(BalancedModeRules.HunterProfile(Hunter.Noxus).AltTractionMultiplier, 1.10f),
                    "Spire and Noxus receive tank/control profiles");
                Check(BalancedModeRules.HunterProfile(Hunter.Samus) == BalancedHunterProfile.Baseline
                    && BalancedModeRules.HunterProfile(Hunter.Trace) == BalancedHunterProfile.Baseline
                    && BalancedModeRules.HunterProfile(Hunter.Sylux) == BalancedHunterProfile.Baseline,
                    "Samus Trace and Sylux remain baseline in Balanced Mode");

                scene.GameState.EnhancedHunters = true;
                Check(!Mods.EnhancedHunters.EnhancedHunters.Enabled(kanden)
                    && !Mods.EnhancedHunters.EnhancedHunters.Enabled(spire)
                    && !Mods.EnhancedHunters.EnhancedHunters.Enabled(noxus)
                    && !Mods.EnhancedHunters.EnhancedHunters.Enabled(weavel),
                    "Balanced Mode suppresses Enhanced Hunters so native ability tuning is independent");
                scene.GameState.EnhancedHunters = false;

                DamageFlags combat = DamageFlags.IgnoreInvuln | DamageFlags.NoDmgInvuln;
                spire.Health = noxus.Health = kanden.Health = 150;
                spire.TakeDamage(100, combat, null, samus);
                noxus.TakeDamage(100, combat, null, samus);
                kanden.TakeDamage(100, combat, null, samus);
                Check(spire.Health == 60 && noxus.Health == 55 && kanden.Health == 50,
                    "Balanced non-headshot mitigation is 10 percent Spire, 5 percent Noxus, baseline Kanden");

                spire.Health = 199;
                spire.Speed = Vector3.Zero;
                spire.TakeDamage(1, combat, Vector3.UnitX, samus);
                Check(Near(spire.Speed.X, 0.80f), "Spire receives 20 percent less combat knockback");

                var flagsProperty = typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.Flags1))!;
                flagsProperty.SetValue(weavel, weavel.Flags1 | PlayerFlags1.Morphing);
                weavel.Health = 150;
                weavel.TakeDamage(100, combat, null, samus);
                Check(weavel.Health == 75,
                    "Balanced Weavel takes 25 percent less non-headshot damage while transforming");
                weavel.Health = 150;
                weavel.TakeDamage(100, combat | DamageFlags.Headshot, null, samus);
                Check(weavel.Health == 50,
                    "Weavel transition armor never reduces headshots");
                flagsProperty.SetValue(weavel, weavel.Flags1 & ~PlayerFlags1.Morphing);

                scene.GameState.BalancedMode = false;
                spire.Health = 150;
                spire.TakeDamage(100, combat, null, samus);
                Check(spire.Health == 50, "hunter durability is inert outside Balanced Mode");
                scene.GameState.BalancedMode = true;

                spire.Spawn(spire.Position, Vector3.UnitZ, Vector3.UnitY, spire.NodeRef, respawn: true);
                spire.Health = spire.HealthMax;
                spire.TakeDamage(200, combat | DamageFlags.Headshot, null, samus);
                Check(spire.Health == 0, "headshots bypass Balanced durability and preserve the 200-damage lethal breakpoint");
            }
            finally { sim.Stop(); NetSession.Stop(); }
        }

        private static void PrepareSyntheticHit(BeamProjectileEntity beam)
        {
            // Direct-hit fixtures must carry the identity and captured Hunter assigned
            // by production Spawn before TakeDamage accepts their status effects.
            beam.ModOwnerHunter = ((PlayerEntity)beam.Owner!).Hunter;
            NetPlayerLifecycle.StampProjectile(beam);
            Check(NetPlayerLifecycle.CurrentProjectile(beam), $"synthetic {beam.Beam} hit has a current launch identity");
        }

        private static void CheckBalancedAffinityScene()
        {
            const string room = "MP1 SANCTORUS";
            var match = new MatchDefinition { RoomKey = room, Mode = GameMode.Battle,
                BalancedMode = true, TimeLimitSeconds = 600, PointGoal = 100 };
            var session = new SessionStatePacket { MatchId = 1, AuthorityEpoch = 1, MaxPlayers = 8,
                Phase = SessionPhase.InMatch, Match = match, WorldProfile = MatchWorldProfile.Resolve(8) };
            Hunter[] hunters = { Hunter.Samus, Hunter.Kanden, Hunter.Trace, Hunter.Sylux,
                Hunter.Noxus, Hunter.Spire, Hunter.Weavel };
            var roster = RosterPacket.Create(); roster.Count = (byte)hunters.Length;
            roster.MatchId = 1; roster.AuthorityEpoch = 1; roster.Revision = 1;
            for (byte slot = 0; slot < hunters.Length; slot++)
            {
                roster.Slots[slot] = slot; roster.Generations[slot] = 1;
                roster.Names[slot] = hunters[slot].ToString();
                roster.Hunters[slot] = (byte)hunters[slot]; roster.Teams[slot] = -1;
            }

            var sim = new ServerSim();
            try
            {
                Check(sim.Start(room, GameMode.Battle, 8, _ => { }, () => { }, roster, session),
                    "Balanced affinity world loads");
                NetSlotManager.Sync();
                var samus = PlayerEntity.Players[0];
                var kanden = PlayerEntity.Players[1];
                var trace = PlayerEntity.Players[2];
                var sylux = PlayerEntity.Players[3];
                var noxus = PlayerEntity.Players[4];
                var spire = PlayerEntity.Players[5];
                var weavel = PlayerEntity.Players[6];
                var scene = samus.OwningScene;
                // Observe only this fixture's explicit seven-Hunter spawn/status phase.
                // Room construction and slot activation have already emitted picks.
                BalancedModeTelemetry.Reset();
                foreach (var player in new[] { samus, kanden, trace, sylux, noxus, spire, weavel })
                    player.Spawn(player.Position, Vector3.UnitZ, Vector3.UnitY, player.NodeRef, respawn: false);

                WeaponInfo missile = scene.WeaponRules[(int)BeamType.Missile];
                WeaponInfo samusMissile = scene.WeaponRules[(int)BeamType.Missile + 9];
                Check(missile.UnchargedDamage == samusMissile.UnchargedDamage
                    && missile.ChargedDamage == samusMissile.ChargedDamage
                    && samusMissile.ChargedHoming > missile.ChargedHoming,
                    "Samus affinity keeps missile damage stock and reserves its bonus for charged homing");

                Check(MathF.Abs(BalancedModeRules.ScopeVisualSpeed(Hunter.Trace, BeamType.Imperialist) - 1.20f) < 0.001f,
                    "Trace affinity only accelerates the Imperialist visual scope transition");
                WeaponInfo imp = scene.WeaponRules[(int)BeamType.Imperialist];
                WeaponInfo traceImp = scene.WeaponRules[(int)BeamType.Imperialist + 9];
                Check(imp.UnchargedDamage == traceImp.UnchargedDamage
                    && imp.HeadshotDamage == traceImp.HeadshotDamage
                    && imp.ShotCooldown == traceImp.ShotCooldown,
                    "Trace affinity has no raw Imperialist damage or fire-rate bonus");

                var disrupt = new BeamProjectileEntity(scene)
                {
                    Owner = kanden, Beam = BeamType.VoltDriver, BeamKind = BeamType.VoltDriver,
                    Flags = BeamFlags.Charged, Afflictions = Affliction.Disrupt, EnhancedDirectHit = true
                };
                PrepareSyntheticHit(disrupt);
                trace.Health = 199;
                trace.TakeDamage(1, DamageFlags.IgnoreInvuln | DamageFlags.NoDmgInvuln, null, disrupt);
                ushort disrupted = (ushort)typeof(PlayerEntity).GetField("_disruptedTimer",
                    BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(trace)!;
                Check(disrupted == BalancedModeRules.AffinityControlDurationFrames,
                    "Kanden charged direct hit disrupts for 1.25 seconds");
                typeof(PlayerEntity).GetField("_disruptedTimer", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(trace, (ushort)0);
                trace.TakeDamage(1, DamageFlags.IgnoreInvuln | DamageFlags.NoDmgInvuln, null, disrupt);
                disrupted = (ushort)typeof(PlayerEntity).GetField("_disruptedTimer",
                    BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(trace)!;
                Check(disrupted == 0 && trace.ModBalancedDisruptImmunityTimer > 0,
                    "Kanden disrupt cannot immediately chain through its immunity window");
                trace.ModResetBalancedAffinityState();
                disrupt.EnhancedDirectHit = false;
                trace.TakeDamage(1, DamageFlags.IgnoreInvuln | DamageFlags.NoDmgInvuln, null, disrupt);
                disrupted = (ushort)typeof(PlayerEntity).GetField("_disruptedTimer",
                    BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(trace)!;
                Check(disrupted == 0, "Kanden disrupt does not apply from charged splash");

                Check(sylux.ModBalancedLifeDrainHeal(10) == 4
                    && sylux.ModBalancedLifeDrainHeal(10) == 4
                    && sylux.ModBalancedLifeDrainHeal(10) == 0,
                    "Sylux affinity heals 40 percent of actual damage with an 8 HP per-second cap");

                var freeze = new BeamProjectileEntity(scene)
                {
                    Owner = noxus, Beam = BeamType.Judicator, BeamKind = BeamType.Judicator,
                    Flags = BeamFlags.Charged, Afflictions = Affliction.Freeze, EnhancedDirectHit = true
                };
                PrepareSyntheticHit(freeze);
                samus.Health = 199;
                typeof(PlayerEntity).GetField("_timeSinceFrozen", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(samus, (ushort)255);
                samus.TakeDamage(1, DamageFlags.IgnoreInvuln | DamageFlags.NoDmgInvuln, null, freeze);
                ushort frozen = (ushort)typeof(PlayerEntity).GetField("_frozenTimer",
                    BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(samus)!;
                Check(frozen == BalancedModeRules.AffinityControlDurationFrames,
                    "Noxus affinity freeze lasts 1.25 seconds");
                typeof(PlayerEntity).GetField("_frozenTimer", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(samus, (ushort)0);
                typeof(PlayerEntity).GetField("_timeSinceFrozen", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(samus, (ushort)0);
                samus.TakeDamage(1, DamageFlags.IgnoreInvuln | DamageFlags.NoDmgInvuln, null, freeze);
                frozen = (ushort)typeof(PlayerEntity).GetField("_frozenTimer",
                    BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(samus)!;
                Check(frozen == 0, "Noxus freeze honors the post-thaw immunity window");

                WeaponInfo noxAffinity = scene.WeaponRules[(int)BeamType.Judicator + 9];
                var noxEquip = new EquipInfo(noxAffinity, noxus.EquipInfo.Beams)
                {
                    InfiniteAmmo = true,
                    ChargeLevel = (ushort)(noxAffinity.FullCharge * 2)
                };
                BeamProjectileEntity.Spawn(noxus, noxEquip, noxus.Position.AddY(1), Vector3.UnitZ,
                    BeamSpawnFlags.NoMuzzle, noxus.NodeRef, scene);
                BeamProjectileEntity? freezeBolt = noxEquip.Beams.FirstOrDefault(b =>
                    b.Owner == noxus && b.Beam == BeamType.Judicator && b.Flags.TestFlag(BeamFlags.Charged)
                    && b.Lifespan > 0);
                Check(freezeBolt != null && MathF.Abs(freezeBolt.Speed - 1.25f) < 0.001f
                    && MathF.Abs(freezeBolt.MaxDistance - 12f) < 0.001f
                    && freezeBolt.SplashDamage == 0 && freezeBolt.Afflictions.TestFlag(Affliction.Freeze),
                    "Noxus charged affinity is a direct freeze bolt instead of the instant ice-wave cone");

                var burn = new BeamProjectileEntity(scene)
                {
                    Owner = spire, Beam = BeamType.Magmaul, BeamKind = BeamType.Magmaul,
                    Flags = BeamFlags.Charged, Afflictions = Affliction.Burn, EnhancedDirectHit = true
                };
                PrepareSyntheticHit(burn);
                samus.Health = 199;
                typeof(PlayerEntity).GetField("_burnTimer", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(samus, (ushort)0);
                samus.TakeDamage(1, DamageFlags.IgnoreInvuln | DamageFlags.NoDmgInvuln, null, burn);
                ushort burnTimer = (ushort)typeof(PlayerEntity).GetField("_burnTimer",
                    BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(samus)!;
                Check(burnTimer == BalancedModeRules.SpireBurnDurationFrames
                    && BalancedModeRules.SpireBurnDurationFrames / BalancedModeRules.SpireBurnTickFrames == 6,
                    "Spire charged direct hit applies a three-second six-damage burn");
                typeof(PlayerEntity).GetField("_burnTimer", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(samus, (ushort)0);
                burn.EnhancedDirectHit = false;
                samus.TakeDamage(1, DamageFlags.IgnoreInvuln | DamageFlags.NoDmgInvuln, null, burn);
                burnTimer = (ushort)typeof(PlayerEntity).GetField("_burnTimer",
                    BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(samus)!;
                Check(burnTimer == 0, "Spire burn does not apply from splash");

                var pickup = typeof(PlayerEntity).GetMethod("PickUpWeapon",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                pickup.Invoke(weavel, new object[] { ItemType.Battlehammer });
                weavel.ModSetWeapon(BeamType.Battlehammer);
                Check(ReferenceEquals(weavel.EquipInfo.Weapon, scene.WeaponRules[(int)BeamType.Battlehammer]),
                    "Balanced Weavel uses stock inventory metadata with explicit burst overrides");

                int weavelAmmoBefore = weavel.EquipInfo.Ammo;
                Check(BeamProjectileEntity.Spawn(weavel, weavel.EquipInfo, weavel.Position.AddY(1), Vector3.UnitZ,
                    BeamSpawnFlags.NoMuzzle, weavel.NodeRef, scene) != BeamResultFlags.NoSpawn,
                    "Weavel emits one complete burst");
                var affinityRounds = weavel.EquipInfo.Beams
                    .Where(b => b.Owner == weavel && b.Beam == BeamType.Battlehammer && b.Lifespan > 0)
                    .OrderByDescending(b => b.Age).ToArray();
                Check(affinityRounds.Length == 3
                    && affinityRounds.All(b => b.Damage == 14 && b.HeadshotDamage == 14
                        && b.SplashDamage == 5 && b.SplashDamageType == 0
                        && MathF.Abs(b.SplashRadius - 1.65f) < .001f
                        && MathF.Abs(b.Speed - 1f) < .001f && b.Acceleration == Vector3.Zero)
                    && weavelAmmoBefore - weavel.EquipInfo.Ammo == 12,
                    "Weavel burst spends 12 UA for 3 fast, gravity-free 14/5 rounds");
                Check(affinityRounds.Length == 3
                    && MathF.Abs(affinityRounds[0].Age) < .001f
                    && MathF.Abs(affinityRounds[1].Age + .05f) < .001f
                    && MathF.Abs(affinityRounds[2].Age + .10f) < .001f
                    && MathF.Abs(affinityRounds[0].DamageDirMag - .24f) < .001f
                    && MathF.Abs(affinityRounds[1].DamageDirMag - .24f) < .001f
                    && MathF.Abs(affinityRounds[2].DamageDirMag
                        - BalancedModeRules.BattlehammerThirdRoundConcussion) < .001f,
                    "Weavel's third bullet activates after 100 ms with enhanced concussion knockback");
                Check(!affinityRounds.Any(b => b.BattlehammerClusterChild),
                    "Balanced burst cannot spawn legacy terrain-cluster children");
                // Force an active, vulnerable life so this fixture exercises
                // self-knockback rather than spawn protection rejecting the hit.
                typeof(PlayerEntity).GetField("_spawnInvulnTimer",
                    BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(weavel, (ushort)0);
                weavel.ModSetSpawnProtectionFromAuthority(false);
                int weavelHealthBefore = weavel.Health;
                int weavelTurretBefore = weavel.Halfturret.Health;
                ushort damageSequenceBefore = NetDamage.Sequence(weavel.SlotIndex);
                var splashMethod = typeof(BeamProjectileEntity).GetMethod("CheckSplashDamage",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                Vector3[] candidateOrigins =
                {
                    weavel.Position,
                    weavel.Position.AddY(0.25f),
                    weavel.Position.AddY(0.5f),
                    weavel.Position + Vector3.UnitX * 0.25f + Vector3.UnitY * 0.25f,
                    weavel.Position - Vector3.UnitX * 0.25f + Vector3.UnitY * 0.25f
                };
                Vector3 selfSplashOrigin = default;
                bool clearSelfSplash = false;
                foreach (Vector3 candidate in candidateOrigins)
                {
                    CollisionResult blocked = default;
                    if (!CollisionDetection.CheckBetweenPoints(candidate, weavel.Position,
                        TestFlags.Beams, scene, ref blocked))
                    {
                        selfSplashOrigin = candidate;
                        clearSelfSplash = true;
                        break;
                    }
                }
                Check(clearSelfSplash, "Battlehammer self-knockback test has clear splash line of sight");

                weavel.Speed = Vector3.Zero;
                splashMethod.Invoke(affinityRounds[0], new object?[] { null, selfSplashOrigin });
                Vector3 regularImpulse = affinityRounds[0].GetDamageDirection(selfSplashOrigin, weavel.Position);
                Vector3 regularSpeed = weavel.Speed;
                Check(weavel.Health == weavelHealthBefore
                    && weavel.Halfturret.Health == weavelTurretBefore
                    && NetDamage.Sequence(weavel.SlotIndex) == damageSequenceBefore
                    && (regularSpeed - regularImpulse).Length < 0.0001f
                    && regularSpeed.LengthSquared > 0,
                    "Weavel receives his own first-round blast impulse without HP/turret damage or a damage event");

                weavel.Speed = Vector3.Zero;
                splashMethod.Invoke(affinityRounds[2], new object?[] { null, selfSplashOrigin });
                Vector3 concussionImpulse = affinityRounds[2].GetDamageDirection(selfSplashOrigin, weavel.Position);
                Check(weavel.Health == weavelHealthBefore
                    && weavel.Halfturret.Health == weavelTurretBefore
                    && NetDamage.Sequence(weavel.SlotIndex) == damageSequenceBefore
                    && (weavel.Speed - concussionImpulse).Length < 0.0001f
                    && weavel.Speed.LengthSquared > regularSpeed.LengthSquared,
                    "Weavel's third-round concussion also pushes himself without any health debit");

                weavel.Speed = Vector3.Zero;
                typeof(PlayerEntity).GetField("_spawnInvulnTimer",
                    BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(weavel, (ushort)3);
                splashMethod.Invoke(affinityRounds[0], new object?[] { null, selfSplashOrigin });
                Check(weavel.Speed == Vector3.Zero && weavel.Health == weavelHealthBefore
                    && NetDamage.Sequence(weavel.SlotIndex) == damageSequenceBefore,
                    "normal spawn invulnerability still prevents Battlehammer self-knockback");
                typeof(PlayerEntity).GetField("_spawnInvulnTimer",
                    BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(weavel, (ushort)0);

                pickup.Invoke(samus, new object[] { ItemType.Battlehammer });
                samus.ModSetWeapon(BeamType.Battlehammer);
                int normalAmmoBefore = samus.EquipInfo.Ammo;
                Check(BeamProjectileEntity.Spawn(samus, samus.EquipInfo, samus.Position.AddY(1), Vector3.UnitZ,
                    BeamSpawnFlags.NoMuzzle, samus.NodeRef, scene) != BeamResultFlags.NoSpawn,
                    "non-affinity user emits one complete burst");
                var normalRounds = samus.EquipInfo.Beams
                    .Where(b => b.Owner == samus && b.Beam == BeamType.Battlehammer && b.Lifespan > 0)
                    .OrderByDescending(b => b.Age).ToArray();
                Check(normalRounds.Length == 3
                    && normalRounds.All(b => b.Damage == 12 && b.HeadshotDamage == 12
                        && b.SplashDamage == 4 && MathF.Abs(b.SplashRadius - 1.5f) < .001f
                        && MathF.Abs(b.Speed - 1f) < .001f && b.Acceleration == Vector3.Zero)
                    && normalAmmoBefore - samus.EquipInfo.Ammo == 12,
                    "normal Battlehammer fires three straight 12/4 rounds for 12 UA");
                Check(normalRounds.Length == 3
                    && normalRounds.All(b => MathF.Abs(b.DamageDirMag - .20f) < .001f)
                    && MathF.Abs(normalRounds[1].Age + .05f) < .001f
                    && MathF.Abs(normalRounds[2].Age + .10f) < .001f,
                    "normal Battlehammer has the same burst pacing without Weavel's concussion");

                var balance = BalancedModeTelemetry.Capture();
                Check(balance.Revision == BalancedModeRules.BalanceRevision
                    && balance.HunterPicks.Sum() == 7 && balance.HunterPicks.All(picks => picks == 1)
                    && balance.Battlehammer.Length == BalancedModeTelemetry.BattlehammerMetrics,
                    "Balanced telemetry captures revision and the revised burst diagnostics");
                Check(balance.Affinity[0] >= 1 && balance.Affinity[1] >= 1
                    && balance.Affinity[2] >= 1 && balance.Affinity[3] >= 1
                    && balance.Affinity[4] >= 1,
                    "Balanced telemetry captures affinity applications and rejected chains");

                scene.GameState.BalancedMode = false;
                weavel.ModSetWeapon(BeamType.PowerBeam);
                weavel.ModSetWeapon(BeamType.Battlehammer);
                Check(ReferenceEquals(weavel.EquipInfo.Weapon,
                        scene.WeaponRules[(int)BeamType.Battlehammer + 9]),
                    "non-Balanced Weavel restores the stock affinity Battlehammer metadata");

                pickup.Invoke(samus, new object[] { ItemType.Battlehammer });
                samus.ModSetWeapon(BeamType.Battlehammer);
                foreach (var previous in samus.EquipInfo.Beams) previous.Lifespan = 0;
                BeamProjectileEntity.Spawn(samus, samus.EquipInfo, samus.Position.AddY(1), Vector3.UnitZ,
                    BeamSpawnFlags.NoMuzzle, samus.NodeRef, scene);
                BeamProjectileEntity? stock = samus.EquipInfo.Beams.FirstOrDefault(b =>
                    b.Owner == samus && b.Beam == BeamType.Battlehammer && b.Lifespan > 0);
                Check(stock != null && stock.Damage == 12 && stock.SplashDamage == 8
                    && MathF.Abs(stock.SplashRadius - 1.5f) < 0.001f,
                    "Battlehammer is fully stock outside Balanced Mode");
                Check(stock!.Age == 0 && stock.Acceleration.Y < 0
                    && !stock.BattlehammerClusterChild,
                    "stock Battlehammer retains its single arcing shell outside Balanced Mode");
            }
            finally { sim.Stop(); NetSession.Stop(); }
        }

        private static void AdvancedRulesChecks()
        {
            var balanceAggregate = new Telemetry.NetTelemetryAggregator();
            balanceAggregate.Add(new Telemetry.NetTelemetryEvent(Telemetry.TelemetryEventType.BalancedMode, 1,
                Player: (byte)Hunter.Kanden, Result: (int)BalancedModeMetric.HunterPick,
                A: 2, Id: (uint)BalancedModeRules.BalanceRevision));
            balanceAggregate.Add(new Telemetry.NetTelemetryEvent(Telemetry.TelemetryEventType.BalancedMode, 2,
                Weapon: (byte)BeamType.VoltDriver, Result: (int)BalancedModeMetric.WeaponDamage,
                A: 123, Id: (uint)BalancedModeRules.BalanceRevision));
            var balanceSummary = balanceAggregate.Capture(
                new Telemetry.TelemetryHeader(6, NetConfig.ProtocolVersion, "balanced-test",
                    "test", "test", "test", "Battle", "MP1 SANCTORUS", 2), 1, default);
            Check(balanceSummary.BalancedMode.Revision == BalancedModeRules.BalanceRevision
                && balanceSummary.BalancedMode.HunterPicks[(int)Hunter.Kanden] == 2
                && balanceSummary.BalancedMode.WeaponDamage[(int)BeamType.VoltDriver] == 123
                && balanceSummary.BalancedMode.WeaponDamage.Sum() == 123,
                "telemetry schema v6 routes bounded Balanced Mode counters by revision");

            Check(BalancedModeRules.BalanceRevision == 3,
                "Hunter ability refinement advances Balanced telemetry revision");
            int kandenStockCooldown = unchecked((ushort)(
                Metadata.PlayerValues[(int)Hunter.Kanden].BombCooldown * 2));
            Check(BalancedHunterAbilityRules.KandenAcquireRange == 6.5f
                && MathF.Abs(BalancedHunterAbilityRules.KandenLaunchSpeed - 0.36f) < 0.001f
                && MathF.Abs(BalancedHunterAbilityRules.KandenSteeringFactor - 0.0625f) < 0.001f
                && kandenStockCooldown == 60
                && BalancedHunterAbilityRules.KandenCooldownFrames(kandenStockCooldown) == 51,
                "Kanden native ability profile preserves the stock 60-tick Stinglarva recharge before applying the 15 percent reduction");
            Check(BalancedHunterAbilityRules.SpireLedgeGraceFrames == 9
                && MathF.Abs(BalancedHunterAbilityRules.SpireClimbSpeedMultiplier - 1.20f) < 0.001f
                && MathF.Abs(BalancedHunterAbilityRules.RollTraction(Hunter.Spire, true, false, 1f) - 1.15f) < 0.001f,
                "Spire native ability profile has 150ms grace and 20/15 percent climb/control tuning");
            Check(BalancedHunterAbilityRules.NoxusStartupFrames(120) == 90
                && MathF.Abs(BalancedHunterAbilityRules.RollTraction(Hunter.Noxus, false, true, 1f) - 1.20f) < 0.001f,
                "Noxus native ability profile starts 25 percent faster with 20 percent attack steering");
            Check(MathF.Abs(BalancedHunterAbilityRules.StrafeTraction(Hunter.Weavel, 1f) - 1.15f) < 0.001f
                && BalancedHunterAbilityRules.WeavelTransitionDamage(100, headshot: false, transitioning: true) == 75
                && BalancedHunterAbilityRules.WeavelTransitionDamage(100, headshot: true, transitioning: true) == 100,
                "Weavel native ability profile improves movement and preserves headshot damage through transition armor");
            Check(Enumerable.Range(0, 40).Count(i => BalancedHunterAbilityRules.ExtraSpireAttackAnimationFrame((ulong)i)) == 4
                && Enumerable.Range(0, 40).Count(i => BalancedHunterAbilityRules.ExtraWeavelTransitionAnimationFrame((ulong)i)) == 5,
                "Spire attack and Weavel transform animation pacing is exactly 20 and 25 percent faster");

            var defaults = new MatchDefinition();
            Check(!defaults.ShadowFreeze && !defaults.SpawnProtection && !defaults.InstaGib
                && !defaults.LowTier && !defaults.NoImperialist && !defaults.BalancedMode
                && !defaults.ThirdPersonCamera, "advanced rules and third-person camera default off");
            Check(!new MatchStatePacket().ShadowFreeze && !new MatchStatePacket().SpawnProtection, "zero match flags mean off");
            var settings = new MenuSettings();
            Check(settings.ShadowFreeze == "off" && settings.SpawnProtection == "off"
                && settings.BalancedMode == "off", "new settings opt in to protection, shadow freeze and Balanced Mode");
            var state = new SessionStatePacket { MaxPlayers = 8, MatchId = 1, AuthorityEpoch = 1,
                Match = new MatchDefinition { RoomKey = Rooms()[0], Mode = GameMode.Capture,
                    InstaGib = true, LowTier = true, ShadowFreeze = true, SpawnProtection = true, ThirdPersonCamera = true } };
            byte[] bytes = new byte[SessionStatePacket.Size];
            state.Write(bytes);
            Check(SessionStatePacket.TryRead(bytes, out var read) && read.Match == state.Match, "all combat and camera rules survive session round trip");
            var configuredState = new SceneGameState(new ScenePlayerRegistry());
            state.Match.ApplyModifiers(configuredState);
            Check(configuredState.ThirdPersonCamera
                && configuredState.ForScene(new ScenePlayerRegistry()).ThirdPersonCamera,
                "third-person match configuration survives scene ownership copy");
            state.Match = state.Match with { InstaGib = false, NoImperialist = true, BalancedMode = true };
            state.Write(bytes);
            Check(SessionStatePacket.TryRead(bytes, out read) && read.Match.NoImperialist && read.Match.LowTier
                && read.Match.BalancedMode, "No Imp and Balanced Mode survive session round trip");
            Check(!MatchModifierRules.Validate(new MatchDefinition { Mode = GameMode.Battle, BalancedMode = true, InstaGib = true }, out _)
                && !MatchModifierRules.Validate(new MatchDefinition { Mode = GameMode.Battle, BalancedMode = true, Fiesta = true }, out _)
                && !MatchModifierRules.Validate(new MatchDefinition { Mode = GameMode.Battle, BalancedMode = true, EnhancedHunters = true }, out _)
                && !MatchModifierRules.Validate(new MatchDefinition { Mode = GameMode.OneInTheChamber, BalancedMode = true }, out _)
                && !MatchModifierRules.Validate(new MatchDefinition { Mode = GameMode.GunGame, BalancedMode = true }, out _),
                "Balanced Mode rejects Enhanced Hunters and loadout modes with incompatible simulation ownership");
            state.Match = state.Match with { BalancedMode = false };
            foreach (bool insta in new[] { false, true })
            {
                state.Match = state.Match with { InstaGib = insta, NoImperialist = !insta };
                var decoder = new ReplayReplicaState();
                byte[] matchPacket = new byte[1 + MatchStatePacket.Size]; matchPacket[0] = (byte)PacketType.MatchState;
                new MatchStatePacket { RoomKey = state.Match.RoomKey, Mode = (byte)state.Match.Mode,
                    MatchId = state.MatchId, AuthorityEpoch = state.AuthorityEpoch }.Write(matchPacket.AsSpan(1));
                decoder.Accept(matchPacket, 0);
                byte[] sessionPacket = new byte[1 + SessionStatePacket.Size]; sessionPacket[0] = (byte)PacketType.SessionState;
                state.Write(sessionPacket.AsSpan(1)); decoder.Accept(sessionPacket, 0);
                var restored = new ReplayReplicaState(); restored.RestoreCheckpoint(decoder.CaptureCheckpoint());
                Check(restored.Configuration?.Match == state.Match, "replay checkpoint retains every modifier");
            }
            var legacy = new MatchDefinition { Mode = GameMode.InstaGib, ThirdPersonCamera = true }.NormalizeLegacy();
            Check(legacy.Mode == GameMode.Battle && legacy.InstaGib && legacy.ThirdPersonCamera,
                "legacy mode normalizes while retaining the camera rule");
            foreach (var mode in Launcher.OfflineLaunch.Modes.Select(m => m.Mode))
            {
                var match = new MatchDefinition { RoomKey = Rooms()[0], Mode = mode, InstaGib = true };
                Check(LobbyRules.ValidateDefinition(match, out _) == (mode is GameMode.GunGame or GameMode.OneInTheChamber ? LobbyResultCode.InvalidConfiguration : LobbyResultCode.Ok), $"{mode} validates Insta-Gib compatibility");
                Check(LobbyRules.ValidateDefinition(match with { NoImperialist = true }, out _) == LobbyResultCode.InvalidConfiguration, $"{mode} rejects conflicting weapons");
            }
            foreach (Hunter hunter in Enum.GetValues<Hunter>())
            {
                bool expected = hunter is Hunter.Kanden or Hunter.Spire or Hunter.Noxus or Hunter.Weavel;
                Check(HunterRules.Allowed(hunter, true) == expected, $"Low Tier membership {hunter}");
                Check(HunterRules.Sanitize(hunter, true) == (expected ? hunter : Hunter.Kanden), $"Low Tier fallback {hunter}");
            }
            Check(Enumerable.Range(0, 4096).All(i => HunterRules.Allowed(HunterRules.RandomAllowed((uint)i, true), true)), "4096 random selections obey Low Tier");
            var replacements = Enumerable.Range(0, 256).Select(id => WeaponResourceRules.Replacement("TEST MAP", id)).ToArray();
            Check(replacements.Distinct().Count() == 5, "spawn identities vary across the full replacement pool");
            Check(replacements.All(type => type is ItemType.VoltDriver or ItemType.Battlehammer or ItemType.Judicator or ItemType.Magmaul or ItemType.ShockCoil), "replacement pool excludes Imperialist and special weapons");
            Check(replacements.SequenceEqual(Enumerable.Range(0, 256).Select(id => WeaponResourceRules.Replacement("TEST MAP", id))), "reconstruction preserves replacements");
            Check(WeaponResourceRules.ResolveBeam(BeamType.Imperialist, true, "TEST MAP", 0) != BeamType.Imperialist, "Trace affinity cannot grant Imperialist");
            byte[] item = new byte[72]; BinaryPrimitives.WriteInt32LittleEndian(item.AsSpan(44), (int)ItemType.Imperialist);
            var data = Read.ReadStruct<ItemSpawnEntityData>(item);
            var room = Metadata.RoomMetadata[Rooms()[0]];
            var resolved = MapResourceRules.ResolveData(room, ResourceSpawnProfile.Vanilla, data, true);
            Check(resolved.ItemType == WeaponResourceRules.Replacement(room.Name, data.Header.EntityId), "map rule transforms even vanilla resource profiles");
            Check(MapResourceRules.ResolveData(room, ResourceSpawnProfile.Vanilla, data).ItemType == ItemType.Imperialist, "ordinary maps retain Imperialist");
            var old = new byte[1 + MatchStatePacket.Size]; old[0] = (byte)PacketType.MatchState;
            new MatchStatePacket { Mode = (byte)GameMode.InstaGib }.Write(old.AsSpan(1));
            var adapted = MatchStatePacket.Read(ReplayIdentityCompatibility.Convert(old, 27)[1..]);
            Check(adapted.ShadowFreeze && adapted.SpawnProtection, "legacy replay zero flags retain historical enabled rules");
            Check(old[11] == 0, "replay adaptation never mutates source bytes");
            var oldDecoder = new ReplayReplicaState();
            new MatchStatePacket { RoomKey = "MP1 SANCTORUS", Mode = (byte)GameMode.InstaGib,
                MatchId = 1, AuthorityEpoch = 1 }.Write(old.AsSpan(1));
            oldDecoder.Accept(old, 0);
            byte[] historicalCheckpoint = ReplayCheckpointCheckFixture.Protocol80(oldDecoder.CaptureCheckpoint().Bytes, Check);
            // Build actual protocol-27 bytes; changing only the version label would
            // leave protocol-29 player extensions in the historical fixture.
            var historicalBytes = historicalCheckpoint.ToList();
            historicalBytes.RemoveAt(7); // v12 feature byte is not a historical header field
            // Schemas 5/6 add two empty semantic counts and two uint frontiers;
            // schema 8 adds the bomb-history count; schema 9 adds the seen and
            // pending turret-outcome counts; schema 10 adds the pickup room and
            // three pickup counts. All are absent from schema 4.
            // Check the empty appendices before stripping them so this fixture
            // cannot silently discard populated history or target the wrong bytes.
            const int grantAppendixBytes = 87; // length prefix + empty v11 pure presentation state
            const int historicalAppendixBytes = 8 * sizeof(int) + 3 * sizeof(uint) + grantAppendixBytes;
            Check(historicalBytes.Skip(historicalBytes.Count - historicalAppendixBytes)
                .Select((value, index) => value == (index == historicalAppendixBytes - grantAppendixBytes ? 83 : 0)).All(value => value),
                "protocol-27 fixture strips only empty later-schema appendices");
            historicalBytes.RemoveRange(historicalBytes.Count - historicalAppendixBytes, historicalAppendixBytes);
            historicalBytes[4] = 4; historicalBytes[5] = 0;
            const int matchStart = 46;
            int rosterStart = matchStart + MatchStatePacket.Size + 1;
            int firstPlayer = rosterStart + RosterPacket.Size;
            // The projected v12/protocol80 checkpoint has actual173-byte
            // player rows and1296-byte native-touch intents, not v14 rows.
            int stride = 7 + PlayerState.Protocol80Size + 1 + IntentPacket.Protocol80TouchSize + 4;
            for (int slot = PlayerEntity.SlotCapacity - 1; slot >= 0; slot--)
            {
                int intentStart = firstPlayer + slot * stride + 8 + PlayerState.Protocol80Size;
                // Protocol 27 predates authored fire-event blocks entirely.
                historicalBytes.RemoveRange(intentStart + IntentPacket.LegacyFullSize,
                    IntentPacket.Protocol80TouchSize - IntentPacket.LegacyFullSize);
                historicalBytes.RemoveRange(firstPlayer + slot * stride + 7 + PlayerState.LegacySize,
                    PlayerState.Protocol80Size - PlayerState.LegacySize);
            }
            // Protocol 33 added one handicap byte to every fixed roster entry.
            // A protocol-27 checkpoint genuinely predates those bytes, so remove
            // them rather than only relabelling a current-width roster.
            for (int slot = RosterPacket.MaxSlots - 1; slot >= 0; slot--)
                historicalBytes.RemoveAt(rosterStart + RosterPacket.HeaderSize
                    + slot * RosterPacket.EntrySize + RosterPacket.EntrySize - 1);
            // Protocol 27 has neither the old ushort nor the protocol-60 uint rule tail.
            historicalBytes.RemoveRange(matchStart + 103, MatchStatePacket.Size - 103);
            historicalCheckpoint = historicalBytes.ToArray();
            historicalCheckpoint[6] = 27;
            var legacyDecoder = new ReplayReplicaState(); legacyDecoder.RestoreCheckpoint(new(historicalCheckpoint));
            Check(legacyDecoder.Match is { ShadowFreeze: true, SpawnProtection: true }, "historical decoder checkpoints adapt negative flags");
        }
        private static void AdvancedRulesScenario()
        {
            using var rig = new Rig();
            Client owner = rig.Add(880), other = rig.Add(881);
            var config = owner.State!.Value;
            config.Match = config.Match with { LowTier = true, NoImperialist = true, ShadowFreeze = false, SpawnProtection = false };
            rig.Expect(owner, owner.Command(LobbyCommandType.UpdateMatch, config: config), LobbyResultCode.Ok);
            Check(Enumerable.Range(0, owner.Roster.Count).All(i => owner.Roster.Hunters[i] == (byte)Hunter.Kanden), "enabling Low Tier remaps existing roster");
            other.Identify((byte)Hunter.Weavel);
            rig.Wait(() => owner.Roster.Hunters[Array.IndexOf(owner.Roster.Slots, (byte)other.Slot)] == (byte)Hunter.Weavel, "allowed Identify is accepted");
            other.Identify((byte)Hunter.Trace);
            rig.Wait(() => owner.Roster.Hunters[Array.IndexOf(owner.Roster.Slots, (byte)other.Slot)] == (byte)Hunter.Kanden, "direct Identify is sanitized");
            rig.Expect(owner, owner.Command(LobbyCommandType.AddBot, hunter: (byte)Hunter.Sylux), LobbyResultCode.Ok);
            Check(Enumerable.Range(0, owner.Roster.Count).All(i => HunterRules.Allowed((Hunter)owner.Roster.Hunters[i], true)), "server bots obey Low Tier");
            var conflict = owner.State!.Value;
            conflict.Match = conflict.Match with { InstaGib = true };
            rig.Expect(owner, owner.Command(LobbyCommandType.UpdateMatch, config: conflict), LobbyResultCode.InvalidConfiguration);
            rig.ReadyAll();
            rig.Expect(owner, owner.Command(LobbyCommandType.StartMatch), LobbyResultCode.Ok);
            owner.Loaded(); other.Loaded();
            rig.Wait(() => owner.State!.Value.Phase == SessionPhase.InMatch, "modified match starts");
            Client joiner = rig.Add(882);
            Check(joiner.State!.Value.Match.LowTier && joiner.State.Value.Match.NoImperialist
                && !joiner.State.Value.Match.ShadowFreeze && !joiner.State.Value.Match.SpawnProtection,
                "late join receives exact authoritative modifiers before play");
        }
    }
}
