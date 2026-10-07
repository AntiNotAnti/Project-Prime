using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MphRead.Mods.Render;

internal static class ObservationControls
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool value, string name)
        {
            if (!value) throw new InvalidOperationException(name);
            checks++;
        }
        string critical = NativeValidationCriticalWarning.Prefix
            + "17 actual_completed=12 elapsed_ms=5001 wait_budget_ms=5000";
        var warning = new NativeValidationCriticalWarning();
        var warningWriter = new StringWriter();
        for (int i = 0; i < 1024; i++)
            Check(!warning.TryForward(true, "ordinary resource warning " + i, warningWriter), "ordinary flood cannot consume critical lane");
        Check(!warning.TryForward(false, critical, warningWriter), "default does not consume opt-in lane");
        foreach (string prefix in new[] { " GPU fence wait incomplete:", "gpu fence wait incomplete:",
            "GPU fence wait incomplete: wait_completed=true requested=", "GPU fence wait incomplete: requested=" })
            Check(!warning.TryForward(true, prefix, warningWriter), "nonexact warning does not consume lane");
        Check(warning.TryForward(true, critical, warningWriter), "critical warning survives flood");
        Check(warningWriter.ToString() == "[wgpu-validation-critical] Warn: " + critical + Environment.NewLine,
            "critical native event remains verbatim and separately labeled");
        for (int i = 0; i < 64; i++)
            Check(!warning.TryForward(true, critical, warningWriter), "critical warning has one bounded attempt");
        var brokenWarning = new NativeValidationCriticalWarning();
        Check(!brokenWarning.TryForward(true, critical, new ThrowingWriter()), "failed critical sink cannot unwind");
        Check(!brokenWarning.TryForward(true, critical, warningWriter), "failed sink does not allow an unbounded retry");

        var originalException = new InvalidOperationException("original assertion");
        Check(!ShaderDiagnosticPolicy.WriteException(new ThrowingWriter(), "fixture", originalException), "exception diagnostics cannot replace original failure");
        Check(!ShaderDiagnosticPolicy.WriteException(new StringWriter(), "fixture", new ThrowingException()), "throwing exception formatting cannot unwind");
        var exceptionWriter = new StringWriter();
        Check(ShaderDiagnosticPolicy.WriteException(exceptionWriter, "fixture", originalException)
            && exceptionWriter.ToString().Contains(originalException.ToString(), StringComparison.Ordinal), "original exception is recorded before cleanup");
        bool cleanup = false;
        try
        {
            try { throw originalException; }
            catch (Exception ex) { ShaderDiagnosticPolicy.WriteException(new ThrowingWriter(), "fixture", ex); throw; }
            finally { cleanup = true; }
        }
        catch (Exception ex) { Check(ReferenceEquals(ex, originalException) && cleanup, "nonthrowing actual helper preserves bare throw and finally"); }

        string root = Path.Combine(Path.GetTempPath(), "prime-shader-observation-" + Guid.NewGuid().ToString("N"));
        string directory = Path.Combine(root, "owned-source");
        var retained = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        void Write(string path, byte[] bytes) => retained.Add(path, bytes);
        string vertex = ShaderDiagnosticSourceRetention.VertexHeader + "struct VertexOutput { float4 position; }; // controlled vertex";
        string fragment = ShaderDiagnosticSourceRetention.FragmentHeader + "struct FragmentOutput { float4 color; }; // controlled fragment";
        var retention = new ShaderDiagnosticSourceRetention();
        Check(!retention.TryRetain("Info", vertex, Write) && retained.Count == 0, "earlier shader events outside fixture consume no slots");
        foreach (string? bad in new string?[] { null, "", "0", "true", "01", " 1", "1 " })
        {
            foreach (int position in Enumerable.Range(0, 3))
            {
                string?[] flags = { "1", "1", "1" }; flags[position] = bad;
                using var disabled = retention.BeginLayeredPbrScope(flags[0], flags[1], flags[2], directory, root, "merge-sha", "run-id");
                Check(!retention.TryRetain("Info", vertex, Write), "all nonexact opt-ins leave capture disabled");
            }
        }
        foreach ((string? output, string? temp) in new (string?, string?)[]
        {
            (directory, null), (directory, "relative"), (null, root), ("relative", root),
            (root, root), (root + "-sibling/source", root), (Path.Combine(root, "..", "escape"), root)
        })
        {
            using var disabled = retention.BeginLayeredPbrScope("1", "1", "1", output, temp, null, null);
            Check(!retention.TryRetain("Info", vertex, Write), "capture requires owned child of runner-temp directory");
        }
        IDisposable active = retention.BeginLayeredPbrScope("1", "1", "1", directory, root, "tested-merge", "123");
        using (retention.BeginLayeredPbrScope("1", "1", "1", directory, root, null, null))
            Check(retention.TryRetain("Info", vertex, Write), "nested inactive lease cannot steal scope ownership");
        Check(!retention.TryRetain("Info", vertex, Write), "duplicate source is not recopied");
        Check(!retention.TryRetain("Error", fragment, Write), "only actual source-emission levels retained");
        Check(!retention.TryRetain("Info", "prefix " + fragment, Write), "truncated or prefixed exception-tail text is not source identity");
        Check(retention.TryRetain("Warn", fragment, Write), "fragment is captured in same live owner scope");
        Check(retained.Count == 4, "each complete source has an independent receipt");
        foreach ((string path, byte[] bytes) in retained.Where(p => p.Key.EndsWith(".receipt.json", StringComparison.Ordinal)))
        {
            using var receipt = JsonDocument.Parse(bytes);
            var data = receipt.RootElement;
            string sourcePath = path.Replace(".receipt.json", ".native.txt", StringComparison.Ordinal);
            byte[] actual = retained[sourcePath];
            Check(data.GetProperty("nativeRecordBytes").GetInt32() == actual.Length, "receipt records exact full bytes");
            Check(data.GetProperty("nativeRecordSha256").GetString() == Convert.ToHexString(SHA256.HashData(actual)).ToLowerInvariant(), "receipt hashes actual native record bytes");
            Check(data.GetProperty("failedPipelineAssociation").GetString()!.StartsWith("Not proven:", StringComparison.Ordinal), "receipt cannot claim failing PSO association");
            Check(data.GetProperty("emissionTiming").GetString()!.Contains("after its shader compiler returns", StringComparison.Ordinal), "post-compiler emission limitation remains explicit");
            Check(data.GetProperty("githubSha").GetString() == "tested-merge", "literal GitHub SHA provenance is not relabeled as PR head");
        }
        using (var last = JsonDocument.Parse(retained[Path.Combine(directory, "layered-pbr-02-fragment.receipt.json")]))
            Check(last.RootElement.GetProperty("firstObservedSourceEventOrdinal").GetInt32() == 3, "deduplicated events retain truthful native source ordering");
        active.Dispose(); active.Dispose();
        Check(!retention.TryRetain("Info", fragment + "outside", Write), "scope ends after fixture and repeated dispose is harmless");
        using (retention.BeginLayeredPbrScope("1", "1", "1", directory, root, null, null))
        {
            active.Dispose();
            Check(retention.TryRetain("Info", vertex + "second scope", Write), "stale owner disposal cannot disable new scope");
            Check(retention.TryRetain("Info", ShaderDiagnosticSourceRetention.NativeVertexHeader + "controlled ToneMap vertex", Write), "actual native vs_main vertex header retained inside fixture");
            Check(retention.TryRetain("Info", ShaderDiagnosticSourceRetention.NativeFragmentHeader + "controlled ToneMap fragment", Write), "actual native fs_main fragment header retained inside fixture");
        }

        var bounded = new ShaderDiagnosticSourceRetention();
        var budgetWrites = new Dictionary<string, byte[]>();
        using (bounded.BeginLayeredPbrScope("1", "1", "1", directory, root, null, null))
        {
            Check(!bounded.TryRetain("Info", vertex + new string('x', ShaderDiagnosticSourceRetention.MaximumSourceBytes), Write), "oversized source rejected whole without truncation");
            Check(!bounded.TryRetain("Info", vertex + new string('é', ShaderDiagnosticSourceRetention.MaximumSourceBytes / 2), Write), "UTF8 byte bound exceeds character bound safely");
            Check(!bounded.TryRetain("Info", vertex + "\ud800", Write), "invalid Unicode does not unwind into callback");
            for (int i = 0; i < ShaderDiagnosticSourceRetention.MaximumDistinctRecords; i++)
                Check(bounded.TryRetain("Info", vertex + "distinct " + i, (p, b) => budgetWrites.Add(p, b)), "bounded distinct actual records preserved");
            Check(!bounded.TryRetain("Info", fragment + "over budget", Write), "per-process distinct record cap holds");
        }
        Check(budgetWrites.Count == ShaderDiagnosticSourceRetention.MaximumDistinctRecords * 2, "source and receipt count is bounded");
        var broken = new ShaderDiagnosticSourceRetention();
        using (broken.BeginLayeredPbrScope("1", "1", "1", directory, root, null, null))
        {
            Check(!broken.TryRetain("Info", vertex, (_, _) => throw new IOException("write failed")), "source disk error contained");
            Check(!broken.TryRetain("Info", vertex, Write), "failed record cannot retry indefinitely");
        }
        try
        {
            var disk = new ShaderDiagnosticSourceRetention();
            using (disk.BeginLayeredPbrScope("1", "1", "1", directory, root, "merge", "run"))
                Check(disk.TryRetain("Info", vertex), "actual diagnostic file writer completes");
            string sourceFile = Path.Combine(directory, "layered-pbr-01-vertex.native.txt");
            Check(File.ReadAllBytes(sourceFile).SequenceEqual(Encoding.UTF8.GetBytes(vertex)), "actual file preserves verbatim callback bytes");
            var collision = new ShaderDiagnosticSourceRetention();
            using (collision.BeginLayeredPbrScope("1", "1", "1", directory, root, null, null))
                Check(!collision.TryRetain("Info", vertex + "overwrite"), "writer does not overwrite prior source evidence");
            Check(File.ReadAllText(sourceFile) == vertex, "old evidence remains unchanged after collision");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }

        string repo = Directory.GetCurrentDirectory();
        while (!File.Exists(Path.Combine(repo, "src/MphRead/Mods/Render/ModernGraphicsWindowCheck.cs")))
            repo = Directory.GetParent(repo)?.FullName ?? throw new InvalidOperationException("Repository sources missing.");
        string pbr = File.ReadAllText(Path.Combine(repo, "src/MphRead/Mods/Render/ModernPbrLayerCheck.cs"));
        string window = File.ReadAllText(Path.Combine(repo, "src/MphRead/Mods/Render/ModernGraphicsWindowCheck.cs"));
        string device = File.ReadAllText(Path.Combine(repo, "src/MphRead/Mods/Render/ModernGraphicsDevice.cs"));
        Check(pbr.Contains("using var diagnosticScope = ModernGraphicsDevice.BeginLayeredPbrShaderDiagnosticScopeForCheck();", StringComparison.Ordinal), "actual fixture owns the callback capture scope");
        Check(pbr.Contains("before resource cleanup", StringComparison.Ordinal) && pbr.Contains("before state restoration", StringComparison.Ordinal), "actual fixture records original exceptions before both original finally blocks");
        Check(window.Contains("before Shutdown", StringComparison.Ordinal)
            && window.Contains("phase=Shutdown START", StringComparison.Ordinal) && window.Contains("phase=Shutdown DONE", StringComparison.Ordinal), "actual window records failure and cleanup phase separately");
        Check(device.IndexOf("_criticalFenceWarning.TryForward", StringComparison.Ordinal)
            < device.IndexOf("Mods.DebugLog.Checkpoint(\"wgpu\"", StringComparison.Ordinal), "critical lane precedes optional disk diagnostics");
        Check(device.Contains("forwarded <= limit && !criticalFence", StringComparison.Ordinal), "critical event is not duplicated by generic forwarding");
        foreach (string filename in new[] { "ShaderDiagnosticPolicy.cs", "NativeValidationCriticalWarning.cs", "ShaderDiagnosticSourceRetention.cs" })
            Check(File.ReadAllText(Path.Combine(repo, "src/MphRead/Mods/Render", filename)).StartsWith("#if !ANDROID && !MPHREAD_SERVER", StringComparison.Ordinal), "diagnostic helper remains desktop only");
        return checks;
    }

    private sealed class ThrowingWriter : StringWriter
    {
        public override void WriteLine(string? value) => throw new IOException("injected observation sink failure");
    }
    private sealed class ThrowingException : Exception
    {
        public override string ToString() => throw new IOException("injected exception formatting failure");
    }
}
