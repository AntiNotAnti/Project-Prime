using System;
using System.Linq;
using System.Reflection;
using MphRead;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.NetTest;

// The same local-body input seam as MovementTourTests. Helper requests enter
// normal keyboard sampling; Roam remains selected and the full native scene steps.
internal static class QuiescentTurretTests
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private static int _checks;
    private static void Check(bool ok, string message)
    { if (!ok) throw new InvalidOperationException(message); _checks++; }
    private static void Frame(uint value) => typeof(NetSession).GetProperty(nameof(NetSession.NetFrame))!.SetValue(null, value);
    private static void Session(string name, object value) => typeof(NetSession).GetProperty(name)!.SetValue(null, value);

    internal static int Run(string room)
    {
        _checks = 0;
        if (!ServerSim.Available(out string reason)) { Console.Error.WriteLine(reason); return 1; }
        bool scripted = NetTestScript.Enabled;
        var sim = new ServerSim();
        var roster = RosterPacket.Create();
        roster.MatchId = 1; roster.AuthorityEpoch = 1; roster.Revision = 1; roster.Count = 1;
        roster.Slots[0] = 0; roster.Generations[0] = 1; roster.Hunters[0] = (byte)Hunter.Weavel; roster.Names[0] = "Quiescent Weavel";
        if (!sim.Start(room, GameMode.Battle, 2, _ => { }, () => { }, roster: roster)) return 1;
        try
        {
            NetTestScript.Enabled = false;
            NetSession.ApplyMatchState(new MatchStatePacket { MatchId = 1, AuthorityEpoch = 1,
                RoomKey = room, Mode = (byte)GameMode.Battle }, false);
            NetSession.ApplyRoster(roster);
            var scene = (Scene)typeof(ServerSim).GetField("_scene", Private)!.GetValue(sim)!;
            scene.Players.MainPlayerIndex = 0; scene.GameState.MatchTime = -1; scene.GameState.PointGoal = 10000;
            NetSlotManager.Sync();
            Session(nameof(NetSession.Role), NetRole.Host); Session(nameof(NetSession.LocalSlot), 0);
            var owner = scene.Players.Items[0];
            Check(owner.Hunter == Hunter.Weavel && !owner.IsBot && scene.CameraMode == CameraMode.Roam, "native Weavel body fixture identity");
            var inputMode = typeof(Scene).GetField("_inputMode", Private)!;
            inputMode.SetValue(scene, Enum.Parse(inputMode.FieldType, "All"));
            foreach (var control in owner.Controls.All) { control.Type = ButtonType.Key; control.Key = Keys.Unknown; }
            owner.Controls.Morph.Key = Keys.C; owner.Controls.MouseAim = false;
            var keyboard = (KeyboardState)typeof(Scene).GetField("_keyboardState", Private)!.GetValue(scene)!;
            var setKey = typeof(KeyboardState).GetMethod("SetKeyState", Private | BindingFlags.Public)!
                .CreateDelegate<Action<KeyboardState, Keys, bool>>();
            bool helperPressed = false, helperReleased = false;
            BeamProjectileEntity? witnessedFlight = null;
            void Step(bool morph = false, bool quiesce = false)
            {
                uint before = NetSession.NetFrame; Frame(unchecked(before + 1));
                if (quiesce)
                {
                    Vector3? flightPosition = witnessedFlight?.Position;
                    float? flightLife = witnessedFlight?.Lifespan;
                    int bodyHealth = owner.Health; bool alt = owner.IsAltForm, unmorph = owner.IsUnmorphing;
                    NetTestScript.ApplyQuiescentControls(owner);
                    Check(owner.Health == bodyHealth && owner.IsAltForm == alt && owner.IsUnmorphing == unmorph,
                        "helper requests controls without directly changing native health or form");
                    if (witnessedFlight != null)
                        Check(witnessedFlight.Position == flightPosition && witnessedFlight.Lifespan == flightLife,
                            "helper does not delete or alter existing native flight before simulation");
                    morph = owner.Controls.Morph.IsDown;
                    helperPressed |= owner.Controls.Morph.IsPressed; helperReleased |= owner.Controls.Morph.IsReleased;
                    Check(owner.Controls.All.Where(control => !ReferenceEquals(control, owner.Controls.Morph)).All(control => !control.IsDown),
                        "quiescent helper requests no other held action");
                }
                setKey(keyboard, Keys.C, morph); sim.Step();
                Check(sim.StepFailures == 0 && NetSession.NetFrame == unchecked(before + 1), "one real native scene step without exception");
            }
            for (int i = 0; i < 120; i++) Step();
            inputMode.SetValue(scene, Enum.Parse(inputMode.FieldType, "All"));
            Check(owner.Health > 0 && owner.ModIsInPlay && !owner.IsAltForm, "native live biped spawned");
            Step(quiesce: true);
            Check(!owner.Controls.Morph.IsDown && !owner.IsMorphing && !owner.IsAltForm, "no-input biped quiescence cannot create a form transition");
            Step(morph: true);
            for (int i = 0; i < 180 && (owner.IsMorphing || !owner.IsAltForm); i++) Step(morph: true);
            Check(owner.IsAltForm && !owner.IsMorphing && owner.Halfturret.Health > 0
                && owner.Flags2.TestFlag(PlayerFlags2.Halfturret), "ordinary native input creates active autonomous turret");
            // Existing flight is an actual native projectile, not a synthetic
            // lifespan/pose witness. Native physics moves it before quiescence.
            Vector3 origin = owner.Halfturret.Position + Vector3.UnitY * .4f, direction = Vector3.Zero;
            NetDynamicGeometryHistory.RecordWorld(NetSession.NetFrame);
            foreach (var ray in new[] { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitZ, -Vector3.UnitZ, Vector3.UnitY })
                if (NetDynamicGeometryHistory.TryTraceDistanceAtFrame(scene, origin, origin + ray * 5,
                    NetSession.NetFrame, out float wall) && wall == 1) { direction = ray; break; }
            Check(direction.LengthSquared == 1, "asset geometry supplies a clear existing-flight lane");
            Check(BeamProjectileEntity.Spawn(owner.Halfturret, owner.Halfturret.EquipInfo, origin, direction,
                BeamSpawnFlags.NoMuzzle, owner.NodeRef, scene) != BeamResultFlags.NoSpawn, "real native turret projectile exists before quiescence");
            var flight = owner.EquipInfo.Beams.Single(beam => beam.Lifespan > 0); witnessedFlight = flight;
            Vector3 launch = flight.Position;
            for (int i = 0; i < 8 && (flight.Position - origin).LengthSquared < 1; i++) Step(morph: true);
            Check(flight.Lifespan > 0 && flight.Position != launch && (flight.Position - origin).LengthSquared >= 1,
                "existing turret flight advances native physics outside its owner body");
            while (unchecked(NetSession.NetFrame + 1) % 12 != 0) Step(morph: true);
            Check(owner.Controls.Morph.IsDown && owner.IsAltForm, "quiescence begins with previously held morph control");
            helperPressed = helperReleased = false;
            Vector3 beforeQuiesce = flight.Position;
            Step(quiesce: true);
            Check(!helperPressed && owner.IsAltForm, "first held pulse does not invent a new morph press");
            Check(flight.Position != beforeQuiesce || flight.Lifespan <= 0,
                "quiescence retains ordinary existing-projectile processing");
            bool unmorphing = false;
            for (int i = 0; i < 180 && (owner.IsAltForm || owner.IsUnmorphing); i++)
            { Step(quiesce: true); unmorphing |= owner.IsUnmorphing; }
            Check(helperReleased && helperPressed && unmorphing, "native release then later pulse enters unmorph");
            Check(!owner.IsAltForm && !owner.IsMorphing && !owner.IsUnmorphing
                && !owner.Flags2.TestFlag(PlayerFlags2.Halfturret), "native unmorph finishes in biped with autonomous turret inactive");
            for (int i = 0; i < 24; i++) Step(quiesce: true);
            Check(!owner.IsAltForm && !owner.IsMorphing && !owner.Controls.Morph.IsDown, "settled biped does not remorph during continued drain");
            owner.TakeDamage(0, DamageFlags.Death | DamageFlags.IgnoreInvuln, null, owner);
            Check(owner.Health == 0, "native death endpoint creates dead quiescence control");
            NetTestScript.ApplyQuiescentControls(owner);
            Check(owner.Health == 0 && owner.Controls.All.All(control => !control.IsDown), "dead quiescence cannot request respawn or morph");
            Console.WriteLine($"QUIESCENT TURRET: {_checks} checks PASS; native input/body/flight, Roam camera, no HUD or network delivery claim");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("QUIESCENT TURRET FAIL: " + ex); return 1; }
        finally { NetTestScript.Enabled = scripted; sim.Stop(); }
    }
}
