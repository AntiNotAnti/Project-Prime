using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

/// <summary>
/// Deterministic presentation-path measurement. This is not rendered WAN
/// evidence: it delays authoritative Shot facts in a synthetic schedule and
/// exercises the bounded client registry only.
/// </summary>
internal static class ProjectilePresentationMeasurementCheck
{
    private static readonly CombatActor Actor = new(1, 0xA11CEUL, 3);
    private const int ShotCount = 120;
    private const int ShotIntervalFrames = 12;

    public static int Run(string[] args)
    {
        if (args.Length > 2)
        {
            Console.Error.WriteLine("Usage: nettest --projectile-presentation [OUTPUT_JSON]");
            return 2;
        }

        var cases = new List<object>();
        bool passed = true;
        foreach (int delayMs in new[] { 100, 150, 200 })
        {
            MeasurementCase result = RunCase(delayMs);
            cases.Add(result);
            passed &= result.Passed;
        }

        var report = new
        {
            Mode = "presentation-path-simulation",
            RenderedWanProof = false,
            Description = "Synthetic delayed authoritative Shot echoes; no renderer, socket, or WAN is involved.",
            WindowFrames = ProjectilePresentationMeasurement.DefaultWindowFrames,
            ShotCount,
            Cases = cases,
            Passed = passed
        };
        string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        if (args.Length == 2)
            File.WriteAllText(args[1], json);
        Console.WriteLine($"PROJECTILE_PRESENTATION mode=presentation-path-simulation renderedWanProof=false "
            + $"cases={cases.Count} passed={passed}"
            + (args.Length == 2 ? $" output={args[1]}" : string.Empty));
        return passed ? 0 : 1;
    }

    private static MeasurementCase RunCase(int delayMs)
    {
        int delayFrames = (int)Math.Ceiling(delayMs * 60 / 1000d);
        var measurement = new ProjectilePresentationMeasurement();
        measurement.SetContext(0x5001, Actor);
        var echoes = new List<DelayedEcho>(ShotCount);
        uint nextEventId = 1;

        int duration = ShotCount * ShotIntervalFrames + delayFrames;
        for (int frame = 0; frame < duration; frame++)
        {
            measurement.Advance();
            for (int i = echoes.Count - 1; i >= 0; i--)
            {
                if (echoes[i].ArrivalFrame > frame)
                    continue;
                DelayedEcho echo = echoes[i];
                echoes.RemoveAt(i);
                measurement.RecordAuthoritativeShot(echo.Event);
                // The live local path suppresses this echoed visual. A true
                // value is covered by the focused unit test for duplicate
                // accounting, but is intentionally not the baseline here.
                measurement.ObserveAuthoritativeVisual(echo.Event, visualWillSpawn: false);
            }

            if (frame % ShotIntervalFrames != 0 || frame / ShotIntervalFrames >= ShotCount)
                continue;
            uint command = (uint)(frame / ShotIntervalFrames);
            Vector3 predictedPosition = Position(frame);
            Vector3 predictedDirection = Direction(frame);
            Assert(measurement.RecordPredictedShot(Actor, command, weapon: 1,
                predictedPosition, predictedDirection, visualCount: command % 3 == 0 ? 3 : 1),
                "predicted observation was not accepted");
            Vector3 authoritativePosition = predictedPosition + new Vector3(.25f, -.125f, .05f);
            Vector3 authoritativeDirection = (predictedDirection + new Vector3(.02f, .015f, -.01f)).Normalized();
            CombatEvent value = new(nextEventId++, (uint)frame + (uint)delayFrames, command,
                CombatEventKind.Shot, 1, CombatEventFlags.None, Actor, CombatActor.None,
                0, 0, authoritativePosition, authoritativeDirection, 0, 0, 0);
            echoes.Add(new DelayedEcho(frame + delayFrames, value));
        }

        for (int frame = duration; frame < duration + delayFrames + measurement.WindowFrames + 1; frame++)
        {
            measurement.Advance();
            for (int i = echoes.Count - 1; i >= 0; i--)
            {
                if (echoes[i].ArrivalFrame > frame)
                    continue;
                DelayedEcho echo = echoes[i];
                echoes.RemoveAt(i);
                measurement.RecordAuthoritativeShot(echo.Event);
                measurement.ObserveAuthoritativeVisual(echo.Event, visualWillSpawn: false);
            }
        }

        ProjectilePresentationMeasurementSnapshot metrics = measurement.Metrics;
        bool casePassed = metrics.PredictedShotCreated == ShotCount
            && metrics.AuthoritativeShotMatched == ShotCount
            && metrics.AdoptionCandidateMiss == 0
            && metrics.VisualDuplicate == 0
            && metrics.CorrectionSamples == ShotCount
            && metrics.CorrectionAngleSamples == ShotCount;
        return new MeasurementCase(delayMs, delayFrames, metrics.PredictedShotCreated,
            metrics.AuthoritativeShotMatched, metrics.MatchRate, metrics.VisualDuplicate,
            metrics.AdoptionCandidateMiss, metrics.VisualCorrectionDistance,
            metrics.MeanCorrectionDistance, metrics.VisualCorrectionAngle,
            metrics.MeanCorrectionAngle, casePassed);
    }

    private static Vector3 Position(int frame)
        => new(frame * .125f, MathF.Sin(frame * .07f), MathF.Cos(frame * .031f) * .5f);

    private static Vector3 Direction(int frame)
        => new Vector3(1, MathF.Sin(frame * .043f) * .25f, MathF.Cos(frame * .037f) * .15f).Normalized();

    private static void Assert(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private readonly record struct DelayedEcho(int ArrivalFrame, CombatEvent Event);

    private readonly record struct MeasurementCase(
        int DelayMs,
        int DelayFrames,
        long PredictedShotCreated,
        long AuthoritativeShotMatched,
        double MatchRate,
        long VisualDuplicate,
        long AdoptionCandidateMiss,
        double VisualCorrectionDistance,
        double MeanCorrectionDistance,
        double VisualCorrectionAngle,
        double MeanCorrectionAngle,
        bool Passed);
}
