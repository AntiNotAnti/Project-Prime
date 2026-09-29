using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead.Entities;
using MphRead.Mods.Input;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.Mods.Physics;

public static class FpsScenarioRunner
{
    private static TraceVector Trace(Vector3 value) => new(value.X, value.Y, value.Z);
    public static int Run(string path, string output, bool nativeKernel = false, bool cadence60 = false, int inputPhase = 0)
    {
        if (inputPhase is < 0 or > 1 || inputPhase != 0 && !cadence60)
            throw new ArgumentException("Input phase 1 is supported only by the 60 Hz native-cadence experiment.");
        string full = Path.GetFullPath(path);
        var scenario = FpsScenario.Load(full);
        if (scenario.PlacementRequired != null)
        {
            Console.Error.WriteLine("[fpsscenario] Unverified placement: " + scenario.PlacementRequired);
            return 2;
        }
        if (Network.NetSession.Active) throw new InvalidOperationException("Run offline scenarios in a fresh process.");
        nativeKernel |= cadence60;
        bool combat = scenario.Inputs.Any(input => input.Fire) || scenario.InitialFreezeNativeTicks > 0;
        if (combat && nativeKernel && !cadence60)
            throw new InvalidOperationException("Combat requires the real 60 Hz scene clock.");
        int frameStride = nativeKernel && !cadence60 ? 2 : 1;
        int traceStride = nativeKernel ? 2 : 1;
        Headless.Enter();
        Paths.UpdatePaths(); Paths.ChooseMphPath();
        GamepadContexts.Current = GamepadContext.Gameplay;
        GamepadContexts.Focused = true;
        var keyboard = SyntheticInput.CreateKeyboard();
        var mouse = SyntheticInput.CreateMouse();
        var setKey = typeof(KeyboardState).GetMethod("SetKeyState", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!
            .CreateDelegate<Action<KeyboardState, Keys, bool>>();
        var scene = new Scene(new Vector2i(256, 192), keyboard, mouse, _ => { }, () => { });
        scene.NativeMovementMode = cadence60 ? NativeMovementMode.DiagnosticCadence60
            : nativeKernel ? NativeMovementMode.Diagnostic30 : NativeMovementMode.Legacy60;
        try
        {
            scene.Random.SetRng1(scenario.Seed1); scene.Random.SetRng2(scenario.Seed2);
            scene.AddPlayer(Hunter.Samus, position: new Vector3(scenario.Spawn[0], scenario.Spawn[1], scenario.Spawn[2]));
            scene.AddRoom(scenario.Room, GameMode.Battle, playerCount: 2);
            scene.OnLoad();
            var player = scene.Players.Items[0];
            player.IsBot = false;
            foreach (var bind in player.Controls.All) { bind.Type = ButtonType.Key; bind.Key = Keys.Unknown; }
            void Bind(Keybind bind, Keys key) { bind.Type = ButtonType.Key; bind.Key = key; }
            Bind(player.Controls.MoveUp, Keys.W); Bind(player.Controls.MoveDown, Keys.S);
            Bind(player.Controls.MoveLeft, Keys.A); Bind(player.Controls.MoveRight, Keys.D); Bind(player.Controls.Jump, Keys.Space); Bind(player.Controls.Shoot, Keys.F);
            player.Controls.MouseAim = false; player.Controls.KeyboardAim = false;
            scene.GameState.MatchState = MatchState.InProgress;
            scene.GameState.MatchTime = 3600;
            // Establish the input snapshot and spawn before resetting the scenario clock.
            scene.OnSimulationFrame();
            player.Spawn(new(scenario.Spawn[0], scenario.Spawn[1] - 1, scenario.Spawn[2]),
                new(scenario.Facing[0], scenario.Facing[1], scenario.Facing[2]), Vector3.UnitY, player.NodeRef, respawn: true);
            player.Speed = Vector3.Zero;
            scene.Random.SetRng1(scenario.Seed1); scene.Random.SetRng2(scenario.Seed2);
            for (int frame = 0; frame < scenario.SettleNativeTicks * 2; frame += frameStride) scene.OnSimulationFrame();
            if (!player.ModIsInPlay || player.IsAltForm) throw new InvalidOperationException("Scenario did not establish an active biped.");
            if (scenario.InitialImpulse is { } impulse)
                player.ModSetDiagnosticImpulse(new(impulse.Velocity[0], impulse.Velocity[1], impulse.Velocity[2]),
                    new(impulse.Acceleration[0], impulse.Acceleration[1], impulse.Acceleration[2]), impulse.NativeTicks);
            if (scenario.InitialFreezeNativeTicks > 0) player.ModSetDiagnosticFreeze(scenario.InitialFreezeNativeTicks);
            // The movement fixture is already in play; do not retain the room's
            // intro sequence, which blocks real first-person draw preparation.
            scene.CameraSequences.Current = null;
            scene.SetFreeCamera(false);
            player.CameraInfo.Update();
            player.CameraInfo.ModResetDrawState();
            player.ModResetFirstPersonDrawState();
            scene.ResetFrameCount();
            player.NativeCadenceJumpPending = false;
            player.NativeCadenceFirePending = false;
            var captureHealth = player.Health;
            var cadenceFrames = new List<object>();
            var combatFrames = new List<object>();
            int projectileDrawSamples = 0;
            PhysicsTrace.Start(output);
            for (int frame = 0; frame < scenario.NativeTicks * 2; frame += frameStride)
            {
                // Move the same native command to either half of its 60 Hz pair.
                // The late phase exercises a one-frame jump press without a buffered early edge.
                int commandFrame = frame - inputPhase;
                var input = commandFrame < 0 ? new ScenarioInput { Tick = 0 }
                    : scenario.AtPrimeTick(commandFrame);
                setKey(keyboard, Keys.W, input.MoveY > 0); setKey(keyboard, Keys.S, input.MoveY < 0);
                setKey(keyboard, Keys.D, input.MoveX > 0); setKey(keyboard, Keys.A, input.MoveX < 0);
                setKey(keyboard, Keys.Space, input.Jump);
                setKey(keyboard, Keys.F, input.Fire);
                Vector3 before = player.Position;
                scene.OnSimulationFrame();
                if (combat && player.Controls.Shoot.IsDown != input.Fire)
                    throw new InvalidDataException("Fire input was not consumed as scripted.");
                if (combat && cadence60)
                {
                    foreach (var beam in player.EquipInfo.Beams.Where(beam => beam.Lifespan > 0))
                    {
                        Vector3 position = beam.Position, velocity = beam.Velocity;
                        float age = beam.Age, lifespan = beam.Lifespan;
                        foreach (double alpha in new[] { 0d, 0.25, 0.5, 0.75, 1d })
                        {
                            Vector3 presented = beam.ModNativePowerDrawPosition(alpha);
                            Vector3 expected = beam.Flags.TestFlag(BeamFlags.Collided) || beam.Age == 0 ? position
                                : Vector3.Lerp(beam.BackPosition, position, (float)((frame % 2 == 0 ? 0.5 : 0) + alpha * 0.5));
                            if ((presented - expected).LengthSquared > 1e-10f || beam.Position != position
                                || beam.Velocity != velocity || beam.Age != age || beam.Lifespan != lifespan)
                                throw new InvalidDataException("Projectile presentation differs or modified simulation state.");
                            projectileDrawSamples++;
                        }
                    }
                }
                if (combat)
                    combatFrames.Add(new { frame = frame + 1, fire = player.Controls.Shoot.IsDown,
                        shot = player.TimeSinceShot == 0, timeSinceShot = player.TimeSinceShot,
                        freezeTimer = player.ModDiagnosticFreezeTimer,
                        chargeLevel = player.EquipInfo.ChargeLevel, ammo = player.EquipInfo.Ammo,
                        weapon = (int)player.CurrentWeapon,
                        projectiles = player.EquipInfo.Beams.Select((beam, slot) => new { beam, slot })
                            .Where(entry => entry.beam.Lifespan > 0).Select(entry => new
                            {
                                entry.slot, weapon = (int)entry.beam.Beam, flags = (int)entry.beam.Flags,
                                position = new TraceVector(entry.beam.Position.X, entry.beam.Position.Y, entry.beam.Position.Z),
                                spawnPosition = new TraceVector(entry.beam.SpawnPosition.X, entry.beam.SpawnPosition.Y, entry.beam.SpawnPosition.Z),
                                velocity = new TraceVector(entry.beam.Velocity.X * 2, entry.beam.Velocity.Y * 2, entry.beam.Velocity.Z * 2),
                                age = entry.beam.Age, lifespan = entry.beam.Lifespan,
                                presentationPosition = Trace(entry.beam.ModNativePowerDrawPosition(1))
                            }).ToArray() });
                if (cadence60)
                {
                    bool boundary = (frame & 1) != 0;
                    if (!boundary && player.Position != before)
                        throw new InvalidDataException($"Unexpected movement between native boundaries at frame {frame + 1}.");
                    Vector3 presented = player.ReplayDrawTransform.Row3.Xyz;
                    Vector3 expectedPresentation = boundary ? (before + player.Position) * 0.5f : player.Position;
                    if ((presented - expectedPresentation).LengthSquared > 1e-10f)
                        throw new InvalidDataException($"Native cadence draw pose differs at frame {frame + 1}.");
                    Vector3 expectedCamera = player.CameraInfo.Position + expectedPresentation - player.Position;
                    if ((player.CameraInfo.ModGetDrawPosition(1) - expectedCamera).LengthSquared > 1e-10f)
                        throw new InvalidDataException($"Native cadence camera pose differs at frame {frame + 1}.");
                    Vector3 simulationPosition = player.Position, simulationSpeed = player.Speed;
                    Vector3 simulationFacing = player.FacingVector;
                    bool pendingJump = player.NativeCadenceJumpPending, pendingFire = player.NativeCadenceFirePending;
                    foreach (double alpha in new[] { 0d, 0.25, 0.5, 0.75, 1d })
                    {
                        bool prepared = player.ModPrepareFirstPersonRenderPose(alpha, 0, 0, 0, 0, out _, out Vector3 cameraPosition, out _);
                        if (!prepared || (cameraPosition - player.CameraInfo.ModGetDrawPosition(alpha)).LengthSquared > 1e-10f)
                            throw new InvalidDataException($"Native first-person pose differs at frame {frame + 1}, alpha {alpha}: prepared={prepared}, camera={player.CameraType}, actual={cameraPosition}, expected={player.CameraInfo.ModGetDrawPosition(alpha)}.");
                        if (player.Position != simulationPosition || player.Speed != simulationSpeed
                            || player.FacingVector != simulationFacing || player.NativeCadenceJumpPending != pendingJump || player.NativeCadenceFirePending != pendingFire)
                            throw new InvalidDataException("Drawing changed native movement state.");
                    }
                    cadenceFrames.Add(new
                    {
                        frame = frame + 1, nativeBoundary = boundary,
                        requestedJump = input.Jump,
                        presentationPosition = new TraceVector(presented.X, presented.Y, presented.Z),
                        positionBefore = new TraceVector(before.X, before.Y, before.Z),
                        positionActual = new TraceVector(player.Position.X, player.Position.Y, player.Position.Z),
                        velocityActual = new TraceVector(player.Speed.X, player.Speed.Y, player.Speed.Z)
                    });
                }
                if (player.Health != captureHealth)
                    throw new InvalidOperationException($"Unexpected damage contaminated the scenario at tick {frame}.");
                if (scene.FrameCount != (ulong)(frame / frameStride) + 1 || !player.ModIsInPlay)
                    throw new InvalidOperationException($"Scenario stopped advancing or player left play at tick {frame}.");
            }
            PhysicsTrace.Stop();
            var rows = File.ReadLines(output).Select(line => JsonSerializer.Deserialize<PhysicsSample>(line,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })!).ToArray();
            if (rows.Length != scenario.NativeTicks * 2 / traceStride) throw new InvalidDataException("Incomplete scenario trace.");
            for (int i = 0; i < rows.Length; i++)
            {
                var requested = scenario.AtPrimeTick(i * traceStride);
                if (rows[i].Frame < (ulong)scenario.InitialFreezeNativeTicks * 2)
                    requested = requested with { MoveX = 0, MoveY = 0 };
                var consumed = rows[i].Stages ?? throw new InvalidDataException("Missing input stages.");
                if (rows[i].Frame != (ulong)((i + 1) * traceStride) || consumed.MoveX != requested.MoveX || consumed.MoveY != requested.MoveY
                    || consumed.JumpPressed != requested.Jump)
                    throw new InvalidDataException($"Input was not consumed as scripted at frame {i + 1}.");
            }
            if (combat)
                File.WriteAllLines(output + ".combat.jsonl", combatFrames.Select(row => JsonSerializer.Serialize(row,
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })));
            if (cadence60)
                File.WriteAllLines(output + ".cadence.jsonl", cadenceFrames.Select(row => JsonSerializer.Serialize(row,
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })));
            File.WriteAllText(output + ".manifest.json", JsonSerializer.Serialize(new
            {
                scenario.Name, scenario.Room, scenario.NativeTicks, scenario.SettleNativeTicks,
                scenarioSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(full))),
                projectileDrawSamples,
                sceneClockHz = 60, movementOperationHz = nativeKernel ? 30 : 60, diagnosticNativeKernel = nativeKernel,
                diagnosticCadence60 = cadence60, inputPhase, presentationDelaySceneFrames = cadence60 ? 1 : 0,
                intermediateAuthority = cadence60 ? "Held; draw-only midpoint then endpoint" : "Not applicable",
                sceneFramesPerNativeTick = 2 / frameStride, nativeInputHz = 30, schemaVersion = 2,
                inputPath = "Synthetic keyboard through normal local-player input", nativeParity = "Unverified"
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"[fpsscenario] {scenario.Name}: {scenario.NativeTicks * 2 / frameStride} real simulation ticks captured (native kernel diagnostic: {nativeKernel}).");
            return 0;
        }
        finally { PhysicsTrace.Stop(); scene.DoCleanup(); scene.UnloadGl(); }
    }
}
