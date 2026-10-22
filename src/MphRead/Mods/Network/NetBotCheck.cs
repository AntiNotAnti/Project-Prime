using System;
using MphRead.Entities;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

/// <summary>Real asset-backed authority simulation, including mid-round lifecycle changes.</summary>
public static class NetBotCheck
{
    public static int Run(string room, int bots = 3)
    {
        var sim = new ServerSim();
        int assertions = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            assertions++; Console.WriteLine("[botcheck] PASS " + message);
        }
        try
        {
            Check(sim.Start(room, GameMode.Battle, 8, _ => { }, () => { }), "authority room loads");
            NetSession.ApplyMatchState(new MatchStatePacket { MatchId = 1, AuthorityEpoch = 1, RoomKey = room, Mode = (byte)GameMode.Battle }, false);
            var roster = RosterPacket.Create(); roster.MatchId = 1; roster.AuthorityEpoch = 1; roster.Revision = 1;
            roster.Count = (byte)(bots + 1); roster.ContainsBots = true;
            for (byte slot = 0; slot < roster.Count; slot++)
            {
                roster.Slots[slot] = slot; roster.Generations[slot] = 1; roster.Teams[slot] = -1;
                roster.Hunters[slot] = (byte)(slot % 7); roster.Names[slot] = slot == 0 ? "HUMAN" : "BOT";
                roster.Flags[slot] = slot == 0 ? (byte)0 : (byte)1;
                roster.BotLevels[slot] = (byte)(slot % 4);
            }
            NetSession.ApplyRoster(roster); NetSlotManager.Sync();
            int intents = 0, shots = 0, fireEvents = 0; IntentPacket old = default;
            // Gameplay-derived input quality metrics. They are diagnostics,
            // not visual or skill proof, and are never used as game authority.
            int analogMovementSamples = 0, instantAxisReversals = 0, measuredTurns = 0;
            int altFormSamples = 0, altAnalogSamples = 0, formTransitions = 0;
            double totalAimDegrees = 0;
            float largestAimStep = 0;
            var lastAxisX = new sbyte[PlayerEntity.SlotCapacity];
            var lastAxisY = new sbyte[PlayerEntity.SlotCapacity];
            var lastAim = new Vector3[PlayerEntity.SlotCapacity];
            var lastLife = new ushort[PlayerEntity.SlotCapacity];
            var lastAltForm = new bool[PlayerEntity.SlotCapacity];
            var hasLast = new bool[PlayerEntity.SlotCapacity];
            NetBotInput.Sink = (slot, intent) =>
            {
                intents++;
                if ((intent.Buttons & IntentButtons.Shoot) != 0) shots++;
                fireEvents += intent.FireEventCount;
                if (slot == bots) old = intent;
                if (intent.AckFrame != 0 || intent.SlotGeneration != NetPlayerLifecycle.Generation(slot))
                    throw new InvalidOperationException("bot intent identity/rewind");
                bool altForm = PlayerEntity.Players[slot].IsAltForm;
                if (intent.HasAnalogMove && (intent.MoveX != 0 || intent.MoveY != 0))
                    analogMovementSamples++;
                if (altForm)
                {
                    altFormSamples++;
                    if (intent.HasAnalogMove && (intent.MoveX != 0 || intent.MoveY != 0))
                        altAnalogSamples++;
                }
                if (hasLast[slot] && lastLife[slot] == intent.LifeId)
                {
                    if (lastAltForm[slot] != altForm) formTransitions++;
                    if (Math.Sign(intent.MoveX) * Math.Sign(lastAxisX[slot]) < 0
                        && Math.Abs((int)intent.MoveX) > 16 && Math.Abs((int)lastAxisX[slot]) > 16
                        || Math.Sign(intent.MoveY) * Math.Sign(lastAxisY[slot]) < 0
                        && Math.Abs((int)intent.MoveY) > 16 && Math.Abs((int)lastAxisY[slot]) > 16)
                        instantAxisReversals++;
                    if (intent.Aim.LengthSquared > 0.01f && lastAim[slot].LengthSquared > 0.01f)
                    {
                        float dot = Math.Clamp(Vector3.Dot(
                            intent.Aim.Normalized(), lastAim[slot].Normalized()), -1, 1);
                        float degrees = MathF.Acos(dot) * (180f / MathF.PI);
                        totalAimDegrees += degrees;
                        largestAimStep = Math.Max(largestAimStep, degrees);
                        measuredTurns++;
                    }
                }
                hasLast[slot] = true;
                lastLife[slot] = intent.LifeId;
                lastAltForm[slot] = altForm;
                lastAxisX[slot] = intent.MoveX;
                lastAxisY[slot] = intent.MoveY;
                lastAim[slot] = intent.Aim;
            };
            GameState.PointGoal = 10000; GameState.MatchTime = 3600;
            for (int i = 0; i < 300; i++) sim.Step();
            var positions = new Vector3[bots + 1];
            for (int slot = 1; slot <= bots; slot++)
            {
                var player = PlayerEntity.Players[slot];
                Check(player.IsBot && player.BotLevel == slot % 4 && NetPlayerLifecycle.Get(slot) > 0, $"slot {slot} AI difficulty and normal spawn");
                positions[slot] = player.Position;
            }
            for (int i = 0; i < 900; i++) sim.Step();
            int moved = 0;
            for (int slot = 1; slot <= bots; slot++) if ((PlayerEntity.Players[slot].Position - positions[slot]).Length > 0.1f) moved++;
            Check(moved == bots, "every bot moves");
            Check(analogMovementSamples > 0,
                "authoritative bot intents include shaped analog movement");
            Check(intents > 100 && shots > 0, "bot intent stream includes firing");
            Check(fireEvents > 0, "bot intent stream repeats authoritative fire events for client visuals");
            long damage = 0;
            for (int slot = 0; slot <= bots; slot++) damage += GameState.MatchDamageDealt[slot];
            Check(damage > 0, "bots deal authoritative combat damage");
            Check(!PlayerEntity.Players[0].IsBot, "human authority slot has no AI");
            roster.Revision++; roster.Count--;
            NetSession.ApplyRoster(roster); sim.Step();
            Check(!PlayerEntity.Players[bots].LoadFlags.TestFlag(LoadFlags.Active) && !PlayerEntity.Players[bots].IsBot, "removal deactivates AI");
            Check(NetSession.MatchContainsBots, "practice latch survives removal");
            roster.Revision++; roster.Count++; roster.Generations[bots] = 3; roster.Flags[bots] = 0; roster.BotLevels[bots] = 0;
            NetSession.ApplyRoster(roster); sim.Step();
            NetSession.AcceptSlotIntent(bots, old);
            Check(!NetSession.RemoteIntentValid[bots] && !PlayerEntity.Players[bots].IsBot, "old bot input rejected for replacement human");
            roster.Revision++; roster.Generations[bots] = 5; roster.Flags[bots] = 1; roster.BotLevels[bots] = 3;
            NetSession.ApplyRoster(roster);
            for (int i = 0; i < 180; i++) sim.Step();
            Check(PlayerEntity.Players[bots].IsBot && NetPlayerLifecycle.Get(bots) > 0, "mid-match bot activates and spawns");
            Check(sim.StepFailures == 0, "no simulation exceptions");
            Console.WriteLine($"[botcheck] {assertions} checks; bots={bots} mean={sim.StepSeconds / sim.Frames * 1000:0.00}ms worst={sim.WorstStepSeconds * 1000:0.00}ms overruns={sim.OverrunSteps} allocations/frame={sim.StepAllocatedBytes / sim.Frames} intents={intents}");
            Console.WriteLine($"[botcheck] motor analogSamples={analogMovementSamples}"
                + $" sameLifeDirectAxisReversals={instantAxisReversals}"
                + $" aimSamples={measuredTurns} meanAimStep={(measuredTurns == 0 ? 0 : totalAimDegrees / measuredTurns):0.00}deg"
                + $" peakAimStep={largestAimStep:0.00}deg"
                + $" altFormFrames={altFormSamples} altAnalogFrames={altAnalogSamples}"
                + $" formTransitions={formTransitions}");
            typeof(NetSession).GetProperty(nameof(NetSession.Role))!.SetValue(null, NetRole.Client);
            typeof(NetSession).GetProperty(nameof(NetSession.IsAuthority))!.SetValue(null, false);
            typeof(NetSession).GetProperty(nameof(NetSession.LocalSlot))!.SetValue(null, 0);
            NetPlayerSetup.Reset(); NetPlayerSetup.ApplyOnce();
            for (int slot = 0; slot <= bots; slot++) Check(!PlayerEntity.Players[slot].IsBot, $"client slot {slot} never runs AI");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { sim.Stop(); }
    }
}
