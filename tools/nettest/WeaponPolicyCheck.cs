using System;
using MphRead.Entities;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest
{
    /// <summary>Retail content and actual Spawn resolution; no renderer or UDP sockets.</summary>
    internal static class WeaponPolicyCheck
    {
        // Independent reviewed multiplayer expectations. Do not derive these from GetMode.
        private readonly record struct Expected(float Speed, float Homing, int Pellets = 1,
            bool Continuous = false, bool Area = false, bool Bounce = false, bool Child = false);

        public static int Run(string[] args)
        {
            if (args.Length is < 2 or > 3)
            {
                Console.Error.WriteLine("Usage: nettest --weapon-policy DATA_DIRECTORY [VERSION]");
                return 2;
            }
            try
            {
                ServerContent.Open(args[1], args.Length > 2 ? args[2] : "AMHE1");
                using var simulation = new ServerSimulation(new RotationEntry { RoomKey = "MP1 SANCTORUS", Mode = GameMode.Battle });
                simulation.Scene.Match.Phase = MatchPhase.Playing;
                PlayerEntity owner = simulation.Scene.Players[0];
                owner.ServerActivate(100, Hunter.Samus, 0);
                simulation.Scene.Players.ActiveCount = 1;
                int cases = 0;
                simulation.Combat.BeginTick(100);
                simulation.Combat.SetCommand(0, new(1, 100, 100, 0, 0, Vector3.UnitZ, InputCommand.NoWeapon));
                ushort[] full = [60, 120, 90, 120, 180, 120, 120, 90, 600];
                ushort[] partial = [48, 75, 60, 75, 100, 75, 75, 60, 315];
                Expected[] uncharged = [new(1.5f, 0), new(2.5f, 0), new(.125f, 0),
                    new(4915 / 8192f, 0), new(100, 0), new(1, 0, Child: true),
                    new(6963 / 8192f, 0, Bounce: true), new(3.5f, 409 / 8192f, Continuous: true), new(.25f, 0)];
                Expected[] charged = [new(.75f, 0), new(.875f, 0), new(.125f, 0),
                    uncharged[3], uncharged[4], new(1, 0, 3, Child: true),
                    new(6963 / 8192f, 0), uncharged[7], uncharged[8]];
                for (int affinity = 0; affinity < 2; affinity++)
                {
                    for (int beam = 0; beam < 9; beam++)
                    {
                        Expected un = uncharged[beam], mid = beam == 0 ? charged[0] : un, max = charged[beam];
                        if (affinity != 0)
                        {
                            if (beam == 0) { mid = mid with { Homing = 40.5f / 8192 }; max = max with { Homing = 81 / 8192f }; }
                            if (beam == 1) max = max with { Homing = 40 / 8192f };
                            if (beam == 2) max = max with { Homing = 81 / 8192f };
                            if (beam == 5) max = new(.5f, 0, Area: true);
                            if (beam == 8) un = mid = max = new(.5f, 0);
                        }
                        Check(beam + affinity * 9, 0, un);
                        Check(beam + affinity * 9, partial[beam], mid);
                        Check(beam + affinity * 9, full[beam], max);
                    }
                }
                // Exact charge boundaries catch raw charged metadata and epsilon thresholds.
                Check(0, 36, new(1.5f, 0));
                Check(0, 37, new(.75f, 0));
                Check(9, 36, new(1.5f, 0));
                Check(9, 37, new(.75f, 81f / 24 / 8192));
                Check(10, 119, new(2.5f, 0));
                Check(11, 89, new(.125f, 0));
                Check(14, 119, new(1, 0, Child: true));
                Console.WriteLine($"WEAPONPOLICY PASS cases={cases} variants=18 source=actual-Spawn");
                return 0;

                void Check(int index, ushort level, Expected expected)
                {
                    BeamProjectileEntity[] pool = new BeamProjectileEntity[8];
                    for (int i = 0; i < pool.Length; i++) pool[i] = new(simulation.Scene);
                    var equip = new EquipInfo(Weapons.WeaponsMP[index], pool) { ChargeLevel = level, InfiniteAmmo = true };
                    BeamProjectileEntity.Spawn(owner, equip, owner.Position.AddY(1), Vector3.UnitZ,
                        BeamSpawnFlags.NoMuzzle, owner.NodeRef, simulation.Scene, spreadSeed: 123);
                    BeamProjectileEntity first = pool[0];
                    var mechanics = first.Mechanics;
                    string label = $"{equip.Weapon.Description} level={level}";
                    Require(mechanics.Beam == (BeamType)(index % 9) && mechanics.BeamKind == mechanics.Beam, label + " identity");
                    Require(MathF.Abs(mechanics.Speed - expected.Speed) < .000001f, label + " speed");
                    Require(MathF.Abs(mechanics.Homing - expected.Homing) < .00000001f, label + " homing");
                    Require(mechanics.Continuous == expected.Continuous && mechanics.InstantArea == expected.Area, label + " path");
                    if (!expected.Area)
                    {
                        int count = 0;
                        foreach (var beam in pool) if (beam.Owner == owner) count++;
                        Require(count == expected.Pellets, label + " pellet count");
                        Require(first.Flags.HasFlag(BeamFlags.Ricochet) == expected.Bounce, label + " bounce");
                        Require((first.RicochetWeapon != null) == expected.Child, label + " child");
                    }
                    else Require(first.Lifespan == 0 && first.Owner == null, label + " angular area must not spawn projectile");
                    LagCompensationMode mode = ExpectedMode(expected, index % 9);
                    Require(LagCompensationPolicy.GetMode(mechanics) == mode, label + " lag policy");
                    Require(first.TimingMode == mode && first.CombatShot.Mode == mode, label + " Spawn policy");
                    Require(first.CombatShot.IsValid && first.CombatShot.ActionServerTick == 100
                        && first.CombatShot.RewindTicks == 0 && !first.CatchUpPending, label + " current-tick shot");
                    foreach (var beam in pool) { simulation.Scene.RemoveEntity(beam); beam.Destroy(); }
                    cases++;
                }
            }
            catch (Exception error) { Console.Error.WriteLine("WEAPONPOLICY FAIL " + error); return 1; }
        }

        private static LagCompensationMode ExpectedMode(Expected expected, int beam)
            => expected.Continuous || expected.Area
                ? LagCompensationMode.None : expected.Homing > 0
                ? LagCompensationMode.HomingProjectileCatchUp : beam == (int)BeamType.Imperialist
                ? LagCompensationMode.HistoricalTrace : LagCompensationMode.ProjectileCatchUp;

        private static void Require(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException(label);
        }
    }
}
