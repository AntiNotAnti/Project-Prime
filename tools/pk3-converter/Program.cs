using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using MphRead.Mods.MapGen;

// Executes within a sandboxed, resource-limited dedicated conversion worker.
// Never accept shell fragments, arbitrary dependency paths or publish tokens.
if (args.Length is not (6 or 8) || args.Length % 2 != 0)
{
    Console.Error.WriteLine(
        "Usage: pk3-converter --source file.pk3 --output file.ppmap --room NAME [--level BSP_NAME]");
    return 2;
}
var values = new Dictionary<string, string>(StringComparer.Ordinal);
for (int i = 0; i < args.Length; i += 2)
{
    if (args[i] is not ("--source" or "--output" or "--room" or "--level")
        || !values.TryAdd(args[i], args[i + 1])
        || args[i + 1].Length == 0 || args[i + 1].Length > 2048)
    {
        Console.Error.WriteLine("Invalid converter argument.");
        return 2;
    }
}
if (!values.TryGetValue("--source", out string? source)
    || !values.TryGetValue("--output", out string? output)
    || !values.TryGetValue("--room", out string? room))
{
    Console.Error.WriteLine("Missing required converter arguments.");
    return 2;
}
using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
try
{
    var result = Q3WebConversion.Convert(
        source, output, room, values.GetValueOrDefault("--level"),
        stop.Token, message => Console.Error.WriteLine(message));
    Console.Out.WriteLine(JsonSerializer.Serialize(result,
        new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    return 0;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Conversion cancelled.");
    return 130;
}
catch (Exception ex) when (ex is IOException or ArgumentException
    or InvalidOperationException or MphRead.ProgramException)
{
    // Do not echo filesystem paths or input data into HTTP API responses.
    Console.Error.WriteLine("Conversion rejected: " + ex.GetType().Name);
    return 1;
}
