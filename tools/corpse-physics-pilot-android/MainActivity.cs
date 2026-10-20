using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Util;
using Android.Widget;
using System.Text.Json;
using Environment = System.Environment;

namespace CorpsePilotAndroid;

[Activity(Name = "com.projectprime.corpsepilot.MainActivity", Label = "Corpse Physics Pilot",
    MainLauncher = true, Exported = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize)]
public sealed class MainActivity : Activity
{
    private const string Tag = "CorpsePilot";
    private static int _started;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        var status = new TextView(this) { Text = "Standalone corpse physics pilot. Results stay in app-private files." };
        SetContentView(status);
        if (Interlocked.CompareExchange(ref _started, 1, 0) != 0)
        {
            status.Text += "\nAlready started in this process; inspect CorpsePilot log and pilot-results.json.";
            return;
        }
        string output = Path.Combine(FilesDir!.AbsolutePath, "pilot-results.json");
        // One synchronous benchmark invocation owns construction, stepping,
        // sampling and disposal on one background thread. No async continuation
        // enters a physics world or arena, and nothing touches the game engine.
        _ = Task.Run(() =>
        {
            int owner = Environment.CurrentManagedThreadId;
            string outcome;
            try
            {
                Log.Info(Tag, $"START owner={owner} output={output}");
#if SHARED_PILOT_BENCHMARKS
                string benchmarkDirectory = Path.Combine(Path.GetDirectoryName(output)!, "benchmark");
                global::CorpsePilot.PilotBenchmarks.Run(benchmarkDirectory, steps: 600);
                File.Copy(Path.Combine(benchmarkDirectory, "benchmark.json"), output, overwrite: true);
                const string kind = "shared-benchmark-600-steps";
#else
                PilotSmoke.Run(output, steps: 20);
                const string kind = "fallback-smoke-20-steps";
#endif
                if (owner != Environment.CurrentManagedThreadId || !File.Exists(output))
                    throw new InvalidOperationException("Benchmark changed owner thread or did not produce its result file.");
                outcome = $"DONE {kind}; owner={owner}; {output}";
                Log.Info(Tag, outcome);
            }
            catch (Exception error)
            {
                outcome = "FAILED: " + error.Message;
                try
                {
                    File.WriteAllText(output, JsonSerializer.Serialize(new
                    {
                        status = "failed", ownerThread = owner, scope = "Standalone Android CPU/AOT pilot only",
                        exception = error.ToString(), utc = DateTimeOffset.UtcNow
                    }, new JsonSerializerOptions { WriteIndented = true }));
                }
                catch (Exception persistence) { Log.Error(Tag, "Result persistence also failed: " + persistence); }
                Log.Error(Tag, error.ToString());
            }
            RunOnUiThread(() => { if (!IsFinishing && !IsDestroyed) status.Text = outcome; });
        });
    }
}
