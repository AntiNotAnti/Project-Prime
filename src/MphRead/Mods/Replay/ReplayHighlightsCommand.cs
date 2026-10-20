using System;
using System.Globalization;
using System.IO;
using System.Threading;
using MphRead.Mods.Network;

namespace MphRead.Mods.Replay;

/// <summary>Headless annotation analysis; never opens a scene or writes replay/reel files.</summary>
internal static class ReplayHighlightsCommand
{
    internal const int MaxEvents = ReplayStudio.MaximumAnalysisEvents;

    public static int Run(string[] args)
    {
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            string? source = null;
            string? output = null;
            int combat = 100, objective = 100, finish = 100;
            for (int i = 0; i < args.Length; i++)
            {
                string key = args[i].TrimStart('-').ToLowerInvariant();
                if (key == "replayhighlights")
                    source = Next(args, ref i);
                else if (key == "highlightsout") output = Next(args, ref i);
                else if (key == "combatweight") combat = Weight(Next(args, ref i));
                else if (key == "objectiveweight") objective = Weight(Next(args, ref i));
                else if (key == "finishweight") finish = Weight(Next(args, ref i));
            }
            if (String.IsNullOrWhiteSpace(source))
                throw new ArgumentException("Usage: -replayhighlights <replay.ppdemo> [-highlightsout <new.json>] [-combatweight 100] [-objectiveweight 100] [-finishweight 100]");
            cancellation.Token.ThrowIfCancellationRequested();
            using var reader = DemoReader.Open(source, out var status, metadataOnly: true);
            if (reader == null) throw new InvalidDataException($"Replay could not be opened: {status}.");
            if (reader.Metadata == null) throw new InvalidDataException("Legacy replay has no annotation metadata; no events can be verified.");
            string json = ReplayContextAnalysis.Json(source, reader.Metadata.Events, reader.DurationFrames,
                new ReplayHighlightWeights(combat, objective, finish), cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (output == null) Console.WriteLine(json);
            else
            {
                // CreateNew protects both replay bytes and any previous analysis/manual file.
                using var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write);
                using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false));
                writer.Write(json);
                writer.Write('\n');
            }
            return 0;
        }
        catch (OperationCanceledException) { Console.Error.WriteLine("Replay highlight analysis cancelled."); return 130; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { Console.Error.WriteLine("Replay highlight analysis failed: " + ex.Message); return 1; }
        finally { Console.CancelKeyPress -= cancel; }
    }

    internal static string Analyze(System.Collections.Generic.IReadOnlyList<ReplayEvent> events,
        uint duration, ReplayHighlightWeights weights, CancellationToken cancellationToken)
    {
        if (events.Count > MaxEvents) throw new InvalidDataException($"Highlight analysis limit is {MaxEvents} annotations.");
        var snapshot = new ReplayEvent[events.Count];
        for (int i = 0; i < snapshot.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            snapshot[i] = events[i];
        }
        cancellationToken.ThrowIfCancellationRequested();
        string result = ReplayStudio.HighlightsJson(snapshot, duration, weights, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    private static string Next(string[] args, ref int index)
        => ++index < args.Length ? args[index] : throw new ArgumentException("Missing highlight argument value.");

    private static int Weight(string value)
        => Int32.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int weight)
            && weight is >= 0 and <= 1000 ? weight
            : throw new ArgumentException("Highlight weights must be integer percentages from 0 to 1000.");
}
