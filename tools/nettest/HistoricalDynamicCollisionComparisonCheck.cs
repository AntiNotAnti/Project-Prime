using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MphRead.Formats;
using MphRead.Formats.Collision;
using MphRead.Mods.Network;
using MphRead.Runtime.HistoricalCollision;
using OpenTK.Mathematics;

namespace MphRead.NetTest
{
    /// <summary>
    /// Deterministic QZ1-E comparison of current geometry with the bounded
    /// historical collision contracts. This intentionally does not start a
    /// worker or pretend to be rendered WAN evidence.
    /// </summary>
    internal static class HistoricalDynamicCollisionComparisonCheck
    {
        private const uint ShotTick = 600;
        private static readonly int[] DelayMilliseconds = [0, 50, 100, 150, 200];
        private static readonly JsonSerializerOptions Json = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        private enum CompensationMode
        {
            Off,
            HistoricalPlayersOnly,
            HistoricalPlayersAndDynamicGeometry
        }

        private readonly record struct DynamicScenario(
            string Name,
            HistoricalCollisionQuery Query,
            HistoricalCollisionState HistoricalState,
            HistoricalCollisionState CurrentState);

        private sealed record ScenarioObservation(
            string Scenario,
            bool HistoricalHit,
            bool CurrentHit,
            bool SelectedHit,
            HistoricalColliderKind SelectedColliderKind,
            HistoricalColliderId SelectedColliderId,
            bool GeometryChanged,
            bool FalseWallBlock,
            bool ShotThroughClosedDoor,
            bool ForceFieldContradiction);

        private sealed record ModeObservation(
            string Mode,
            int FalseWallBlock,
            int ShotThroughClosedDoor,
            int ForceFieldContradiction,
            int HistoricalGeometryChangedOutcome,
            int HistoricalDynamicQueries,
            int HistoricalDynamicMissing,
            ScenarioObservation[] Scenarios,
            bool Passed);

        private sealed record DelayObservation(
            int DelayMilliseconds,
            int DelayTicks,
            uint CurrentTick,
            uint RequestedQueryTick,
            uint ResolvedQueryTick,
            uint RewindTicks,
            bool RewindClamped,
            ModeObservation[] Modes);

        private sealed record ComparisonReport(
            int Schema,
            string Evidence,
            string Transport,
            bool RenderedWanProof,
            bool HistoricalDynamicCollisionEnabledByDefault,
            string[] Contracts,
            DelayObservation[] Cases,
            bool Passed);

        public static int Run(string[] args)
        {
            if (args.Length > 2 || (args.Length > 0 && args[0] != "--dynamic-lagcomp-comparison"))
            {
                Console.Error.WriteLine("--dynamic-lagcomp-comparison [OUTPUT_JSON]");
                return 2;
            }

            try
            {
                ComparisonReport report = BuildReport();
                if (args.Length == 2)
                    File.WriteAllText(args[1], JsonSerializer.Serialize(report, Json));

                Print(report);
                return report.Passed ? 0 : 1;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                return 1;
            }
        }

        private static ComparisonReport BuildReport()
        {
            var cases = new List<DelayObservation>(DelayMilliseconds.Length);
            foreach (int delayMs in DelayMilliseconds)
            {
                int delayTicks = (int)Math.Ceiling(delayMs * 60d / 1000d);
                uint currentTick = ShotTick + (uint)delayTicks;
                LagCompensationTime resolved = LagCompensationPolicy.ResolveTick(
                    currentTick, ShotTick, delayMs);
                DynamicScenario[] scenarios = CreateScenarios(delayTicks > 0);
                var modes = new List<ModeObservation>(3);
                foreach (CompensationMode mode in Enum.GetValues<CompensationMode>())
                    modes.Add(ObserveMode(mode, scenarios, resolved.Tick));

                cases.Add(new DelayObservation(delayMs, delayTicks, currentTick, ShotTick,
                    resolved.Tick, resolved.RewindTicks, resolved.Clamped, modes.ToArray()));
            }

            bool passed = cases.All(c => c.Modes.All(m => m.Passed));
            return new ComparisonReport(
                Schema: 1,
                Evidence: "bounded historical-collision contract simulation with deterministic authoritative facts",
                Transport: "none; no loopback or WAN impairment",
                RenderedWanProof: false,
                HistoricalDynamicCollisionEnabledByDefault: false,
                Contracts:
                [
                    "HistoricalCollisionState.ForDoor / ForForceField",
                    "HistoricalCollisionQuery",
                    "HistoricalCollisionResult",
                    "HistoricalColliderId(EntityId, Generation)",
                    "LagCompensationPolicy.ResolveTick"
                ],
                Cases: cases.ToArray(),
                Passed: passed);
        }

        private static ModeObservation ObserveMode(CompensationMode mode,
            DynamicScenario[] scenarios, uint queryTick)
        {
            var observations = new List<ScenarioObservation>(scenarios.Length);
            foreach (DynamicScenario scenario in scenarios)
            {
                var historical = new ContractQuery(scenario.HistoricalState);
                var current = new ContractQuery(scenario.CurrentState);
                bool historicalHit = historical.TryQuery(scenario.Query, queryTick,
                    out HistoricalCollisionResult historicalResult);
                bool currentHit = current.TryQuery(scenario.Query, queryTick,
                    out HistoricalCollisionResult currentResult);
                bool useHistoricalGeometry = mode == CompensationMode.HistoricalPlayersAndDynamicGeometry;
                bool selectedHit = useHistoricalGeometry ? historicalHit : currentHit;
                HistoricalCollisionResult selected = useHistoricalGeometry ? historicalResult : currentResult;
                bool geometryChanged = historicalHit != currentHit
                    || historicalHit && (historicalResult.ColliderKind != currentResult.ColliderKind
                        || historicalResult.ColliderId != currentResult.ColliderId);
                bool falseWallBlock = scenario.Name == "FalseWallBlock"
                    && geometryChanged && currentHit && !historicalHit && selectedHit;
                bool shotThroughClosedDoor = scenario.Name == "ShotThroughClosedDoor"
                    && geometryChanged && !currentHit && historicalHit && !selectedHit;
                bool forceFieldContradiction = scenario.Name == "ForceFieldContradiction"
                    && geometryChanged && !currentHit && historicalHit && !selectedHit;
                observations.Add(new ScenarioObservation(scenario.Name, historicalHit, currentHit,
                    selectedHit, selected.ColliderKind, selected.ColliderId, geometryChanged, falseWallBlock,
                    shotThroughClosedDoor, forceFieldContradiction));
            }

            int falseWall = observations.Count(o => o.FalseWallBlock);
            int throughDoor = observations.Count(o => o.ShotThroughClosedDoor);
            int fieldContradiction = observations.Count(o => o.ForceFieldContradiction);
            bool usesDynamic = mode == CompensationMode.HistoricalPlayersAndDynamicGeometry;
            int changed = usesDynamic ? observations.Count(o => o.GeometryChanged) : 0;
            int queries = usesDynamic ? scenarios.Length : 0;
            bool transitioned = scenarios.Any(s => s.HistoricalState != s.CurrentState);
            int expectedBaseline = transitioned ? 1 : 0;
            bool expected = usesDynamic
                ? falseWall == 0 && throughDoor == 0 && fieldContradiction == 0
                    && changed == (transitioned ? scenarios.Length : 0)
                : falseWall == expectedBaseline && throughDoor == expectedBaseline
                    && fieldContradiction == expectedBaseline && changed == 0;
            return new ModeObservation(mode.ToString(), falseWall, throughDoor, fieldContradiction,
                changed, queries, 0, observations.ToArray(), expected);
        }

        private static DynamicScenario[] CreateScenarios(bool transitioned)
        {
            HistoricalCollisionState historicalDoor = HistoricalCollisionState.ForDoor(
                new HistoricalColliderId(101, 1), blocking: false, Vector3.UnitX,
                Vector3.Zero, radiusSquared: 4f);
            HistoricalCollisionState currentDoor = HistoricalCollisionState.ForDoor(
                new HistoricalColliderId(101, 1), blocking: transitioned, Vector3.UnitX,
                Vector3.Zero, radiusSquared: 4f);

            HistoricalCollisionState historicalClosedDoor = HistoricalCollisionState.ForDoor(
                new HistoricalColliderId(102, 1), blocking: true, Vector3.UnitX,
                Vector3.Zero, radiusSquared: 4f);
            HistoricalCollisionState currentOpenDoor = HistoricalCollisionState.ForDoor(
                new HistoricalColliderId(102, 1), blocking: !transitioned, Vector3.UnitX,
                Vector3.Zero, radiusSquared: 4f);

            Vector4 plane = new(Vector3.UnitX, 0f);
            HistoricalCollisionState historicalField = HistoricalCollisionState.ForForceField(
                new HistoricalColliderId(103, 1), active: true, plane, Vector3.Zero,
                Vector3.UnitY, Vector3.UnitZ, width: 2f, height: 2f);
            HistoricalCollisionState currentField = HistoricalCollisionState.ForForceField(
                new HistoricalColliderId(103, 1), active: !transitioned, plane, Vector3.Zero,
                Vector3.UnitY, Vector3.UnitZ, width: 2f, height: 2f);

            return
            [
                new DynamicScenario("FalseWallBlock", BeamQuery(),
                    transitioned ? historicalDoor : currentDoor, currentDoor),
                new DynamicScenario("ShotThroughClosedDoor", BeamQuery(),
                    historicalClosedDoor, transitioned ? currentOpenDoor : historicalClosedDoor),
                new DynamicScenario("ForceFieldContradiction", BeamQuery(),
                    transitioned ? historicalField : currentField, currentField)
            ];
        }

        private static HistoricalCollisionQuery BeamQuery()
            => new(new Vector3(-2f, 0f, 0f), new Vector3(2f, 0f, 0f), TestFlags.Beams);

        private static void Print(ComparisonReport report)
        {
            Console.WriteLine($"QZ1-E dynamic lag-comp comparison: passed={report.Passed} "
                + $"transport={report.Transport}; renderedWanProof={report.RenderedWanProof}");
            foreach (DelayObservation value in report.Cases)
            {
                Console.WriteLine($"  {value.DelayMilliseconds,3} ms (rewind={value.RewindTicks,2}, "
                    + $"query={value.ResolvedQueryTick}):");
                foreach (ModeObservation mode in value.Modes)
                {
                    Console.WriteLine($"    {mode.Mode,-40} "
                        + $"false-wall={mode.FalseWallBlock} through-door={mode.ShotThroughClosedDoor} "
                        + $"force-field={mode.ForceFieldContradiction} "
                        + $"changed={mode.HistoricalGeometryChangedOutcome} passed={mode.Passed}");
                }
            }
        }

        private sealed class ContractQuery(HistoricalCollisionState state) : IHistoricalCollisionQuery
        {
            public bool TryQuery(in HistoricalCollisionQuery query, uint tick,
                out HistoricalCollisionResult result)
            {
                _ = tick;
                result = Evaluate(query, state);
                return result.Hit;
            }
        }

        private static HistoricalCollisionResult Evaluate(in HistoricalCollisionQuery query,
            in HistoricalCollisionState state)
        {
            if (!state.Active || !state.Blocking) return HistoricalCollisionResult.None;

            Vector3 normal;
            float planeOffset;
            if (state.Kind == HistoricalColliderKind.Door)
            {
                normal = state.Facing.LengthSquared > 0 ? state.Facing.Normalized() : Vector3.UnitX;
                planeOffset = -Vector3.Dot(normal, state.Position);
            }
            else if (state.Kind == HistoricalColliderKind.ForceField)
            {
                normal = new Vector3(state.Plane.X, state.Plane.Y, state.Plane.Z);
                if (normal.LengthSquared <= 0) return HistoricalCollisionResult.None;
                normal = normal.Normalized();
                planeOffset = state.Plane.W;
            }
            else return HistoricalCollisionResult.None;

            Vector3 delta = query.End - query.Start;
            float denominator = Vector3.Dot(normal, delta);
            if (MathF.Abs(denominator) < 0.0001f) return HistoricalCollisionResult.None;
            float fraction = -(Vector3.Dot(normal, query.Start) + planeOffset) / denominator;
            if (!Single.IsFinite(fraction) || fraction < 0 || fraction > 1) return HistoricalCollisionResult.None;

            Vector3 position = query.Start + delta * fraction;
            bool inside = state.Kind == HistoricalColliderKind.Door
                ? (position - state.Position).LengthSquared <= state.RadiusSquared
                : MathF.Abs(Vector3.Dot(position - state.Position, state.Right)) <= state.Width * 0.5f
                    && MathF.Abs(Vector3.Dot(position - state.Position, state.Up)) <= state.Height * 0.5f;
            if (!inside) return HistoricalCollisionResult.None;
            return new HistoricalCollisionResult
            {
                Hit = true,
                ColliderKind = state.Kind,
                ColliderId = state.Identity,
                Distance = delta.Length * fraction,
                Position = position,
                Plane = state.Plane,
                Flags = state.CollisionFlags
            };
        }
    }
}
