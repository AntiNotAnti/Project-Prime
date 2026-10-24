using System;
using System.IO;
using System.Text.Json;
using MphRead.Mods.Network;

namespace MphRead.NetTest;

internal static class ReplayParityReportTests
{
    internal static int Run()
    {
        string directory = Path.Combine(Path.GetTempPath(), "prime-parity-report-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string missing = Path.Combine(directory, "missing.ppdemo"), report = Path.Combine(directory, "report.json");
            if (ReplayDeterminism.Run(missing, reportOutput: report) == 0)
                throw new InvalidDataException("Missing real source cannot pass parity acceptance.");
            using var json = JsonDocument.Parse(File.ReadAllBytes(report));
            var root = json.RootElement;
            if (root.GetProperty("Status").GetString() != "FAIL"
                || root.GetProperty("SourceSha256").ValueKind != JsonValueKind.Null
                || root.GetProperty("ImageParity").GetString() != "NOT_RUN"
                || root.GetProperty("AudioParity").GetString() != "NOT_RUN"
                || root.GetProperty("PhysicalDisplayPacing").GetString() != "NOT_RUN"
                || root.GetProperty("SeekUpdates").GetProperty("TotalSamples").GetInt64() != 0
                || File.Exists(missing)) throw new InvalidDataException("Unexecuted acceptance must be explicit and source must remain absent.");
            string protectedSource = Path.Combine(directory, "source.json");
            byte[] bytes = [1, 2, 3, 4]; File.WriteAllBytes(protectedSource, bytes);
            if (ReplayDeterminism.Run(protectedSource, reportOutput: protectedSource) == 0
                || !File.ReadAllBytes(protectedSource).AsSpan().SequenceEqual(bytes))
                throw new InvalidDataException("Parity report cannot replace source, even when named .json.");
            Console.WriteLine("PASS production parity report marks absent evidence FAIL/NOT_RUN, refuses source overwrite and preserves source bytes; no actual recording parity claim");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally
        {
            foreach (string file in Directory.EnumerateFiles(directory)) File.Delete(file);
            Directory.Delete(directory);
        }
    }
}
