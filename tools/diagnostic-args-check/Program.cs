using System.Reflection;
using System.Text;
using MphRead.Mods;

int passed = 0, failed = 0;
const string sentinel = "11111111222233334444555555555555";
const string sentinel2 = "aaaaaaaa111122223333bbbbbbbbbbbb";
var fields = typeof(DebugLog).GetField("_writer", BindingFlags.NonPublic | BindingFlags.Static)!;
var ring = (Queue<string>)typeof(DebugLog).GetField("_recent", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
string directory = Path.Combine(Path.GetTempPath(), "prime-args-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
void Check(string name, string[] args, params string[] secrets)
{
    string path = Path.Combine(directory, "fixture.log");
    string[] before = (string[])args.Clone();
    try
    {
        ring.Clear();
        using var writer = new StreamWriter(path);
        fields.SetValue(null, writer);
        DebugLog.WriteArguments(args);
        DebugLog.Checkpoint("test", "header complete");
        writer.Flush();
        writer.Dispose(); // Windows requires closing the exclusive fixture writer before reading.
        var report = new StringBuilder();
        DebugLog.AppendRecent(report);
        string disk = File.ReadAllText(path), crash = report.ToString();
        if (secrets.Any(secret => disk.Contains(secret, StringComparison.Ordinal) || crash.Contains(secret, StringComparison.Ordinal)))
            throw new InvalidOperationException("synthetic credential retained");
        if (!disk.Contains("ProjectPrime -server") || !disk.Contains("-port 27000") || !crash.Contains("[args]") || !args.SequenceEqual(before))
            throw new InvalidOperationException("diagnostic identity/input preservation failed");
        if (secrets.Length == 0 && !disk.Contains(String.Join(' ', args), StringComparison.Ordinal))
            throw new InvalidOperationException("nonsensitive arguments changed");
        foreach (string secret in secrets)
            if (!disk.Contains("(redacted)")) throw new InvalidOperationException("redaction marker missing");
        passed++; Console.WriteLine("PASS " + name);
    }
    catch (Exception ex) { failed++; Console.WriteLine($"FAIL {name}: {ex.Message}"); }
    finally { fields.SetValue(null, null); }
}
try
{
    foreach (string spelling in new[] { "ownertoken", "-ownertoken", "--ownertoken", "---OwNeRtOkEn" })
    foreach (string value in new[] { sentinel, Guid.Parse(sentinel).ToString("D"), Guid.Parse(sentinel).ToString("B"), Guid.Parse(sentinel).ToString("P"), Guid.Parse(sentinel).ToString("X") })
        Check("parser spelling and Guid format", ["ProjectPrime", "-server", spelling, value, "-port", "27000"], value);
    Check("all repeated owner options redact", ["ProjectPrime", "-server", "-ownertoken", sentinel, "--OWNERTOKEN", sentinel2, "-port", "27000"], sentinel, sentinel2);
    Check("invalid credential value also redacts", ["ProjectPrime", "-server", "-ownertoken", "synthetic-invalid-value", "-port", "27000"], "synthetic-invalid-value");
    Check("missing value and ordinary diagnostics preserved", ["ProjectPrime", "-server", "-port", "27000", "-ownertoken"]);
    Check("unrelated owner option prefix unchanged", ["ProjectPrime", "-server", "-ownertokenhint", "public-value", "-port", "27000"]);
}
finally { Directory.Delete(directory, recursive: true); }
Console.WriteLine($"{passed} PASS / {failed} FAIL");
return failed == 0 ? 0 : 1;
