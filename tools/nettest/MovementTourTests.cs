// Scope: asset-backed local native production movement in a ServerSim-owned scene.
// Host/local0 is a fixture input seam; this is not dedicated native input authority.
using System;
using System.Reflection;
using System.Text;
using System.Text.Json;
using MphRead;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.NetTest;
internal static class MovementTourTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Hunter[] Hunters = { Hunter.Samus, Hunter.Kanden, Hunter.Spire,
        Hunter.Noxus, Hunter.Trace, Hunter.Sylux, Hunter.Weavel };
    private static void Session(string name, object value) =>
        typeof(NetSession).GetProperty(name)!.SetValue(null, value);
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    internal static int Run(string room)
    {
        Require(ServerSim.Available(out string reason), reason);
        bool enabled = NetMovementValidationShadow.Enabled;
        try
        {
            foreach (Hunter hunter in Hunters)
            {
                try { RunHunter(room, hunter, sample => NetMovementValidationShadow.Observe(sample)); }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"MOVEMENT TOUR FAIL {hunter}: {ex}");
                    return 1; // Activation, asset/geometry and shadow failures are never waived.
                }
            }
            Console.WriteLine("MOVEMENT TOUR: local native production, seven hunters; no dedicated kernel proof.");
            Console.WriteLine("UNSUPPORTED/UNEXERCISED: pad, moving platform, teleport, external knockback, grapple, Spire wall climb.");
            Console.WriteLine("Stable alt intervals are measured as Unsupported; advisory WouldRefuse candidates are not hard rejections.");
            return 0;
        }
        finally { NetMovementValidationShadow.Enabled = enabled; }
    }

    private static void RunHunter(string room, Hunter hunter, Action<MovementValidationSample> observe)
    {
        // Fresh runtime per hunter avoids previous alt entities, cooldowns and life fences.
        // Initial spawn is native and seeded. No per-frame position/speed assignment exists.
        Rng.SetRng1(8128); Rng.SetRng2(0x8128a5a5);
        var roster = RosterPacket.Create();
        roster.MatchId = 1; roster.AuthorityEpoch = 1; roster.Revision = 1; roster.Count = 1;
        roster.Slots[0] = 0; roster.Generations[0] = 1; roster.Hunters[0] = (byte)hunter;
        roster.Names[0] = "N12 native " + hunter;
        var sim = new ServerSim();
        Require(sim.Start(room, GameMode.Battle, 2, _ => { }, () => { }, roster: roster), "room start");
        try
        {
            NetSession.ApplyMatchState(new MatchStatePacket { MatchId = 1, AuthorityEpoch = 1,
                RoomKey = room, Mode = (byte)GameMode.Battle }, false);
            NetSession.ApplyRoster(roster);
            NetMovementValidationShadow.Reset();
            NetMovementValidationShadow.Enabled = true;
            Session(nameof(NetSession.Role), NetRole.Host);
            Session(nameof(NetSession.LocalSlot), 0);
            var scene = (Scene)typeof(ServerSim).GetField("_scene", Private)!.GetValue(sim)!;
            scene.Players.MainPlayerIndex = 0;
            // Dedicated ServerSim deliberately suppresses local input and uses
            // Roam. Select the actual local-player input/camera path for this
            // isolated tour; native keyboard sampling still owns all controls.
            var inputMode = typeof(Scene).GetField("_inputMode", Private)!;
            inputMode.SetValue(scene, Enum.Parse(inputMode.FieldType, "All"));
            scene.GameState.MatchTime = -1; scene.GameState.PointGoal = 10000;
            NetSlotManager.Sync();
            var player = scene.Players.Items[0];
            Require(!player.IsBot && player.Hunter == hunter, "native local hunter identity");
            var keyboard = (KeyboardState)typeof(Scene).GetField("_keyboardState", Private)!.GetValue(scene)!;
            var setKey = typeof(KeyboardState).GetMethod("SetKeyState", Private | BindingFlags.Public)!
                .CreateDelegate<Action<KeyboardState, Keys, bool>>();
            var bindings = new (Keybind Control, Keys Key)[] {
                (player.Controls.MoveUp, Keys.W), (player.Controls.MoveDown, Keys.S),
                (player.Controls.MoveLeft, Keys.A), (player.Controls.MoveRight, Keys.D),
                (player.Controls.RollUp, Keys.I), (player.Controls.RollDown, Keys.K),
                (player.Controls.RolltLeft, Keys.J), (player.Controls.RollRight, Keys.L),
                (player.Controls.Jump, Keys.Space), (player.Controls.Morph, Keys.C),
                (player.Controls.AltAttack, Keys.F), (player.Controls.Boost, Keys.B) };
            foreach (Keybind control in player.Controls.All) { control.Type = ButtonType.Key; control.Key = Keys.Unknown; }
            foreach (var binding in bindings) binding.Control.Key = binding.Key;
            player.Controls.MouseAim = false;
            bool testedDisabled = false;
            long samples = 0;
            bool trackWalk = false; Vector3 walkStart = Vector3.Zero;
            float maximumWalkDistance = 0; int nativeMovementInputFrames = 0;
            // Empty device snapshots baseline through the real input path first.
            void Step(params Keys[] down)
            {
                foreach (var binding in bindings) setKey(keyboard, binding.Key, Array.IndexOf(down, binding.Key) >= 0);
                uint priorFrame = NetSession.NetFrame;
                Session(nameof(NetSession.NetFrame), unchecked(priorFrame + 1));
                sim.Step();
                Require(NetSession.NetFrame == unchecked(priorFrame + 1), "exactly one network frame per actual native step");
                Require(sim.StepFailures == 0, "native simulation exception");
                if (player.ModIsInPlay && player.Health > 0)
                {
                    if (trackWalk)
                    {
                        maximumWalkDistance = Math.Max(maximumWalkDistance, (player.Position - walkStart).Length);
                        if (player.Controls.MoveUp.IsDown || player.Controls.MoveDown.IsDown
                            || player.Controls.MoveLeft.IsDown || player.Controls.MoveRight.IsDown) nativeMovementInputFrames++;
                    }
                    IntentPacket intent = NetPlayerBridge.CaptureIntent(player);
                    var sample = new MovementValidationSample(NetSession.AuthorityEpoch, NetSession.CurrentMatchId,
                        player.SlotIndex, NetPlayerLifecycle.Generation(player.SlotIndex), NetPlayerLifecycle.Get(player.SlotIndex),
                        player.Hunter, NetSession.NetFrame, player.Position, intent.Buttons,
                        player.ModMovementValidationExemptions, intent.MoveX, intent.MoveY);
                    Require(sample.Epoch != 0 && sample.Match != 0 && sample.Generation != 0 && sample.Life != 0,
                        "native identity is available for every movement sample");
                    string before = Fingerprint(scene);
                    if (!testedDisabled)
                    {
                        string counts = JsonSerializer.Serialize(NetMovementValidationShadow.Capture().Hunters);
                        NetMovementValidationShadow.Enabled = false;
                        observe(sample);
                        Require(JsonSerializer.Serialize(NetMovementValidationShadow.Capture().Hunters) == counts,
                            "disabled observer produces no counts");
                        Require(Fingerprint(scene) == before, "disabled observer mutated native state");
                        NetMovementValidationShadow.Enabled = true;
                        testedDisabled = true;
                    }
                    observe(sample); // Actual post-step Position, never a generated pose.
                    Require(Fingerprint(scene) == before, "enabled observer mutated native simulation state");
                    samples++;
                }
                Session(nameof(NetSession.Role), NetRole.Server);
                try { NetSession.BroadcastSnapshot(); }
                finally { Session(nameof(NetSession.Role), NetRole.Host); }
            }
            void Steps(int count, params Keys[] down) { for (int i = 0; i < count; i++) Step(down); }
            Steps(120);
            Require(player.ModIsInPlay && player.Health > 0, "native spawned live body");
            // Initialize/spawn in Roam so the headless fixture never initializes
            // GPU HUD resources; then select the real local simulation branches.
            typeof(Scene).GetField("_cameraMode", Private)!.SetValue(scene, CameraMode.Player);
            Require(player.IsMainPlayer, "fixture selects actual native local-player path");
            walkStart = player.Position; trackWalk = true;
            Steps(24, Keys.W); Steps(24, Keys.D); Steps(24, Keys.S); Steps(24, Keys.A); Steps(20);
            trackWalk = false;
            Require(nativeMovementInputFrames > 0 && maximumWalkDistance > 0,
                "native keyboard controls produce actual biped displacement during tour");
            bool airborne = false, nativeUpwardImpulse = false;
            Step(Keys.Space);
            for (int i = 0; i < 100; i++)
            {
                airborne |= !player.Flags1.TestFlag(PlayerFlags1.Grounded);
                nativeUpwardImpulse |= player.Speed.Y > 0;
                Step();
            }
            Require(airborne && nativeUpwardImpulse, "native jump impulse and airborne interval");
            Step(Keys.C); Step();
            for (int i = 0; i < 180 && (!player.IsAltForm || player.IsMorphing); i++) Step();
            Require(player.IsAltForm && !player.IsMorphing, "native completed alt entry");
            Steps(32, Keys.I); Steps(32, Keys.L);
            if (hunter == Hunter.Samus)
            {
                bool boosted = false;
                Steps(player.Values.BoostChargeMax * 2 + 1, Keys.B);
                for (int i = 0; i < 32; i++) { Step(); boosted |= player.Flags1.TestFlag(PlayerFlags1.Boosting); }
                Require(boosted, "native Samus hold/release boost");
            }
            else
            {
                // Noxus must hold through startup; other native attacks are rising-edge actions.
                long bombsBefore = NetDamage.BombSpawnMade;
                bool attack = false;
                Step(Keys.F);
                attack |= player.Flags2.TestFlag(PlayerFlags2.AltAttack);
                for (int i = 0; i < 100; i++)
                {
                    Step(hunter == Hunter.Noxus ? new[] { Keys.F } : Array.Empty<Keys>());
                    attack |= player.Flags2.TestFlag(PlayerFlags2.AltAttack);
                }
                if (hunter is Hunter.Kanden or Hunter.Sylux)
                    Require(NetDamage.BombSpawnMade > bombsBefore, "native bomb/tail action");
                else Require(attack, "native alt action activation");
            }
            Step(); Step(Keys.C); Step();
            for (int i = 0; i < 180 && (player.IsAltForm || player.IsUnmorphing); i++) Step();
            Require(!player.IsAltForm && !player.IsMorphing && !player.IsUnmorphing, "native completed biped return");
            MovementValidationReport report = NetMovementValidationShadow.Capture();
            MovementValidationCounts biped = report.Hunters[(int)hunter * 2];
            MovementValidationCounts alt = report.Hunters[(int)hunter * 2 + 1];
            Require(report.HardRejections == 0, "no native hard rejection");
            Require(report.Invalid == 0 && report.Overflow == 0, "bounded valid movement evidence");
            Require(biped.Evaluated > 0 && alt.Unsupported > 0 && samples > 0 && testedDisabled,
                "nonzero biped and deliberately unsupported stable-alt coverage");
            Console.WriteLine($"MOVEMENT TOUR {hunter}: native activation PASS, frames={sim.Frames}, samples={samples}, "
                + $"hardRejections={report.HardRejections}, wouldRefuseCandidates={biped.WouldRefuse + alt.WouldRefuse}, "
                + $"evaluatedBiped={biped.Evaluated}, unsupportedAlt={alt.Unsupported}, "
                + $"exempt={biped.Exempt + alt.Exempt}, inputAbsentWhileMoving={biped.InputAbsentWhileMoving}");
            // Primitive row/count fields only; avoids serializing Vector3 swizzle properties.
            Console.WriteLine("MOVEMENT REPORT " + JsonSerializer.Serialize(report));
        }
        finally { sim.Stop(); }
    }

    private static string Fingerprint(Scene scene)
    {
        var text = new StringBuilder();
        void V(Vector3 v) { text.Append(BitConverter.SingleToInt32Bits(v.X)).Append(',')
            .Append(BitConverter.SingleToInt32Bits(v.Y)).Append(',').Append(BitConverter.SingleToInt32Bits(v.Z)).Append(';'); }
        text.Append(scene.FrameCount).Append('/').Append(NetSession.NetFrame).Append('/')
            .Append(scene.Random.Rng1).Append('/').Append(scene.Random.Rng2).Append('/')
            .Append(scene.GameState.MatchState).Append('/').Append(scene.GameState.MatchTime).Append('/');
        foreach (EntityBase entity in scene.Entities)
        {
            text.Append(entity.GetType().Name).Append(':').Append(entity.Id).Append(':'); V(entity.Position);
            if (entity is PlayerEntity player)
            {
                V(player.Speed); text.Append(player.Health).Append('/').Append(player.Flags1).Append('/')
                    .Append(player.Flags2).Append('/').Append(player.LoadFlags).Append('/').Append(player.CurrentWeapon)
                    .Append('/').Append(player.EquipInfo.ChargeLevel).Append('/').Append(player.EquipInfo.Zoomed)
                    .Append('/').Append(player.BipedModel2.AnimInfo.Frame[0]).Append('/');
            }
        }
        return text.ToString();
    }
}
