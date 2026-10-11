using System;
using System.IO;
using System.Reflection;
using MphRead;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: mechanicscheck <assets directory containing paths.txt> [room]");
    return 2;
}
Directory.SetCurrentDirectory(Path.GetFullPath(args[0]));
Paths.UpdatePaths();
Paths.ChooseMphPath();
string room = args.Length > 1 ? args[1] : "MP1 SANCTORUS";
const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
int checks = 0;
void Check(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException(label);
    checks++;
    Console.WriteLine("MECHANICS PASS " + label);
}
void Set(PlayerEntity player, string name, object value)
    => typeof(PlayerEntity).GetField(name, Private)!.SetValue(player, value);
void Flags(PlayerEntity player, PlayerFlags1 value)
    => typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.Flags1))!.SetValue(player, value);
var equip = typeof(PlayerEntity).GetMethod("TryEquipWeapon", Private)!;
var select = typeof(PlayerEntity).GetMethod("ProcessTouchInput", Private)!;
var releaseProtection = typeof(PlayerEntity).GetMethod("ModReleaseSpawnProtection", Private)!;
var place = typeof(PlayerEntity).GetMethod("ModPlaceAt", Private)!;
try
{
    foreach (Hunter hunter in new[] { Hunter.Samus, Hunter.Kanden, Hunter.Spire,
        Hunter.Noxus, Hunter.Trace, Hunter.Sylux, Hunter.Weavel })
    {
        var roster = RosterPacket.Create();
        roster.MatchId = 1; roster.AuthorityEpoch = 1; roster.Revision = 1;
        roster.Count = 1;
        roster.Slots[0] = 0; roster.Generations[0] = 1;
        roster.Hunters[0] = (byte)hunter;
        var sim = new ServerSim();
        Check(sim.Start(room, GameMode.Battle, 2, _ => { }, () => { }, roster: roster), hunter + " asset scene starts");
        try
        {
            NetSession.ApplyMatchState(new MatchStatePacket { MatchId = 1, AuthorityEpoch = 1,
                RoomKey = room, Mode = (byte)GameMode.Battle }, false);
            NetSession.ApplyRoster(roster);
            for (int frame = 0; frame < 120; frame++) sim.Step();
            var player = PlayerEntity.Players[0];
            var scene = (Scene)typeof(ServerSim).GetField("_scene", Private)!.GetValue(sim)!;
            Check(player.Hunter == hunter && player.Health > 0, hunter + " native live spawn");
            var spawn = player.Position;
            var facing = player.FacingVector;
            var node = player.NodeRef;
            CollisionResult floor = default;
            Check(CollisionDetection.CheckBetweenPoints(spawn + Vector3.UnitY * .3f,
                spawn - Vector3.UnitY * 4, TestFlags.Players, scene, ref floor)
                && floor.Field0 == 0 && floor.Plane.Y > .99f, hunter + " authored floor contact");
            foreach (string rules in new[] { "Vanilla", "Balanced", "Enhanced" })
            {
                scene.GameState.BalancedMode = rules == "Balanced";
                scene.GameState.EnhancedHunters = rules == "Enhanced";
                string label = hunter + "/" + rules;
                for (int life = 0; life < 2; life++)
                {
                    equip.Invoke(player, new object[] { BeamType.Missile, true, false });
                    player.Spawn(spawn, facing, Vector3.UnitY, node, respawn: true);
                    Check(player.CurrentWeapon == BeamType.PowerBeam && player.WeaponSelection == BeamType.PowerBeam,
                        label + " respawn resets previous Missile selection " + life);
                    player.Controls.ClearAll();
                    player.Controls.Missile.IsPressed = true;
                    select.Invoke(player, null);
                    Check(player.CurrentWeapon == BeamType.Missile && player.WeaponSelection == BeamType.Missile,
                        label + " common direct-select equips Missile after native respawn " + life);
                }
                var neutral = player.Flags1 & ~(PlayerFlags1.AltForm | PlayerFlags1.Morphing
                    | PlayerFlags1.Unmorphing | PlayerFlags1.Standing | PlayerFlags1.StandingPrevious);
                float threshold = Fixed.ToFloat(player.Values.FallDamageSpeed);
                Console.WriteLine($"MECHANICS DATA {label}: fall threshold={threshold}, raw damage scale={player.Values.FallDamageMax}");
                Check(threshold == .75f && player.Values.FallDamageMax == 70,
                    label + " native fixture fall threshold and raw damage scale");
                int Land(float speed, PlayerFlags1 flags, ushort padAge = 11, float horizontal = 0, int health = 199)
                {
                    player.Health = health;
                    releaseProtection.Invoke(player, null);
                    Set(player, "_spawnInvulnTimer", (ushort)0);
                    Set(player, "_damageInvulnTimer", (ushort)0);
                    Set(player, "_timeSinceJumpPad", padAge);
                    Flags(player, flags);
                    // Use the authored floor plane and real native hunter dimensions.
                    float floorY = floor.Plane.W / floor.Plane.Y;
                    place.Invoke(player, new object[] { new Vector3(spawn.X,
                        floorY - Fixed.ToFloat(player.Values.MinPickupHeight) - .05f, spawn.Z) });
                    player.Speed = new Vector3(horizontal, speed, 0);
                    player.PrevSpeed = player.Speed;
                    player.HandleCollision(floor);
                    return health - player.Health;
                }
                Check(Land(-threshold + .001f, neutral) == 0, label + " below fall threshold preserves health");
                Check(Land(-threshold, neutral) == 1, label + " threshold landing debits minimum one HP");
                int debit = Land(-threshold - .25f, neutral);
                Check(debit == 21, label + " damaging landing debits exact 21 HP, actual=" + debit);
                Check(Land(-threshold - .25f, neutral) == debit, label + " exact repeat preserves landing debit");
                Check(Land(-threshold - .25f, neutral, horizontal: .5f) == debit,
                    label + " horizontal knockback preserves vertical landing debit");
                Check(Land(-threshold - 4f, neutral | PlayerFlags1.Standing) == 0, label + " standing excludes fall debit");
                Check(Land(-threshold - 4f, neutral | PlayerFlags1.AltForm) == 0, label + " alt form excludes fall debit");
                Check(Land(-threshold - 4f, neutral | PlayerFlags1.Morphing) == 0, label + " morphing excludes fall debit");
                Check(Land(-threshold - 4f, neutral | PlayerFlags1.Unmorphing) == 0, label + " unmorphing excludes fall debit");
                Check(Land(-threshold - 4f, neutral, padAge: 10) == 0, label + " jump-pad grace excludes fall debit");
                Check(Land(-threshold - 4f, neutral, health: 500) == 350,
                    label + " deep contact preserves raw scalar without an added damage cap");
                var state = new PlayerState();
                ushort sequence = NetDamage.Sequence(player.SlotIndex);
                Check(Land(-threshold - .25f, neutral, health: 1) == 1 && player.Health == 0,
                    label + " lethal landing resolves exact native health");
                NetDamage.Write(player.SlotIndex, ref state);
                Check(state.DamageEventId != sequence && state.Damage3.EventId == state.DamageEventId
                    && state.Damage3.Damage == debit && state.Damage3.AttackerGeneration == 0
                    && state.AttackerSlot == byte.MaxValue && state.DamageBeam == byte.MaxValue
                    && state.Damage3.AttackerSlot == byte.MaxValue && state.Damage3.Beam == byte.MaxValue,
                    label + " new lethal fall authority receipt retains exact debit and environmental source");
                player.Spawn(spawn, facing, Vector3.UnitY, node, respawn: true);
            }
            Check(sim.StepFailures == 0, hunter + " no native simulation failures");
        }
        finally { sim.Stop(); NetSession.Stop(); }
    }
    Console.WriteLine($"PASS: {checks} asset mechanics assertions; contact kernel and common selection only, no device routing or flight/replay proof");
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error);
    return 1;
}
