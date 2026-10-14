using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;

internal static class Program
{
    private static int Main(string[] args)
    {
        string Arg(string key) => Array.IndexOf(args, key) is int i && i >= 0 && i + 1 < args.Length
            ? args[i + 1] : throw new ArgumentException("Required " + key);
        string candidate = Path.GetFullPath(Arg("--binary-dir"));
        string source = Path.GetFullPath(Arg("--source")), paths = Path.GetFullPath(Arg("--paths"));
        string output = Path.GetFullPath(Arg("--output"));
        Directory.CreateDirectory(output);
        string profile = Path.Combine(output, "profile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(profile); File.Copy(paths, Path.Combine(profile, "paths.txt"));
        Environment.SetEnvironmentVariable("PROJECT_PRIME_USER_DATA", profile);
        Directory.SetCurrentDirectory(profile);
        AssemblyLoadContext.Default.Resolving += (_, name) => File.Exists(Path.Combine(candidate, name.Name + ".dll"))
            ? AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(candidate, name.Name + ".dll")) : null;
        return Fixture.Run(candidate, source, output, uint.Parse(Arg("--start")), uint.Parse(Arg("--end")),
            Arg("--client-sha256"), Arg("--source-sha256"), args.Contains("--require-a3-facts"));
    }
}

internal static class Fixture
{
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static readonly List<string> Checks = new();
    private static void Check(bool condition, string label)
    { if (!condition) throw new InvalidDataException(label); Checks.Add(label); }
    // Reflection keeps the same diagnostic binary usable on an A2 client that has
    // no grant presentation type. Missing historical capability remains null.
    private static string? GrantState(ReplayReplicaState state)
    {
        object? grants = state.GetType().GetProperty("TimedGrants", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(state);
        if (grants == null) return null;
        using var bytes = new MemoryStream(); using var writer = new BinaryWriter(bytes);
        grants.GetType().GetMethod("Write", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(grants, [writer]);
        writer.Flush(); return Convert.ToHexString(SHA256.HashData(bytes.ToArray())).ToLowerInvariant();
    }
    private static void Ready(PassiveReplayPlayer player)
    { int iterations = 0; while (!player.Ready) { Check(player.Update() <= 120, "bounded private seek update");
        if (++iterations > 100000) throw new InvalidDataException("Private seek did not finish."); } }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static int Run(string candidate, string source, string output, uint start, uint end,
        string expectedClient, string expectedSource, bool requireA3)
    {
        var report = new Dictionary<string, object?> { ["schemaVersion"] = 1, ["status"] = "FAIL",
            ["source"] = source, ["start"] = start, ["end"] = end, ["checks"] = Checks,
            ["gpu"] = "NOT_RUN", ["runtimeGrantToShotAcceptance"] = "NOT_RUN" };
        try
        {
            var client = typeof(Scene).Assembly; var tool = typeof(Fixture).Assembly;
            report["clientSha256"] = Hash(client.Location); report["clientMvid"] = client.ManifestModule.ModuleVersionId;
            report["clientBuildId"] = client.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            report["toolSha256"] = Hash(tool.Location); report["toolMvid"] = tool.ManifestModule.ModuleVersionId;
            report["toolBuildId"] = tool.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            report["sourceSha256"] = Hash(source);
            Check(Hash(client.Location) == Hash(Path.Combine(candidate, "ProjectPrime.dll")) && Hash(client.Location) == expectedClient,
                "actual loaded client matches supplied immutable physical hash");
            Check(Hash(source) == expectedSource, "genuine recording matches supplied receipt hash");
            Headless.Enter(); Paths.UpdatePaths(); Paths.ChooseMphPath(); Paths.SetPath("Export", output);
            Check(ReplayArchive.Validate(source) == ReplayOpenResult.Success, "production source archive validation");
            using (var reader = DemoReader.Open(source)!)
            {
                report["protocol"] = reader.ProtocolVersion; report["sourceMetadata"] = reader.Metadata;
                Check(start > 0 && end > start && end <= reader.DurationFrames, "nonempty interior source range without EOF clipping");
                Check(reader.Metadata?.BuildId == report["clientBuildId"] as string, "recording producer equals loaded client build");
                int grantRows = 0, grantRowsInRange = 0, bootstraps = 0;
                while (reader.ReadNext() is { } record)
                {
                    // Stable protocol discriminants are resolved from the loaded client.
                    string name = ((PacketType)record.Data[0]).ToString();
                    if (name == "TimedGrant") { grantRows++; if (record.Frame >= start && record.Frame <= end) grantRowsInRange++; }
                    if (name == "TimedGrantBootstrap") bootstraps++;
                }
                report["grantPacketRecords"] = grantRows; report["grantPacketRecordsInRange"] = grantRowsInRange;
                report["grantBootstrapRecords"] = bootstraps;
                report["a3GrantCoverage"] = reader.ProtocolVersion == 56 && grantRowsInRange > 0 ? "PACKET_POSITIVE_ONLY" : "NOT_VERIFIED";
                if (requireA3) Check(reader.ProtocolVersion == 56 && grantRowsInRange > 0, "A3 positive grant packet denominator inside selected range");
            }
            var foreground = GameState.Current; var players = PlayerEntity.LegacyRegistry; var random = Rng.Current;
            string virtualClip = ReplayVirtualClips.Save(source, start, end, "Acceptance genuine source range");
            string clip = Path.Combine(output, "clip.ppdemo");
            Check(ReplayVirtualClips.Materialize(virtualClip, clip) == ReplayOpenResult.Success, "actual Studio materialize/export succeeds");
            report["virtualClip"] = virtualClip; report["clip"] = clip; report["clipSha256"] = Hash(clip);
            using (var reader = DemoReader.Open(clip)!)
            {
                Check(reader.Metadata?.Type == ReplayType.Clip && reader.DurationFrames == end - start, "exact exported clip duration/type");
                report["clipMetadata"] = reader.Metadata;
            }
            Check(ReplayClipFidelity.Run(source, clip, start) == 0, "production every-frame gameplay/presentation clip fidelity");
            using (var original = ReplayDeterminism.Open(source))
            using (var cut = ReplayDeterminism.Open(clip))
            {
                foreach (uint frame in new[] { end - start, 0u, (end - start) / 2, 0u })
                {
                    original.Seek(start + frame); Ready(original); cut.Seek(frame); Ready(cut);
                    Check(original.Current.Session.RecordingFrame == cut.Current.Session.RecordingFrame, "original recording clock preserved by clip mapping");
                    Check(ReplayDeterminism.Projection(original.Current.Scene, frame).SequenceEqual(ReplayDeterminism.Projection(cut.Current.Scene, frame)),
                        "private forward/backward seek gameplay/presentation equality");
                    Check(GrantState(original.Current.State) == GrantState(cut.Current.State), "private grant state equal or historical unsupported on both");
                }
                report["sourceSeekCheckpoint"] = original.CheckpointSource; report["clipSeekCheckpoint"] = cut.CheckpointSource;
                report["rejectedSourceCheckpoints"] = original.RejectedCheckpoints; report["rejectedClipCheckpoints"] = cut.RejectedCheckpoints;
                uint restoreAt = (end - start) / 2;
                cut.Seek(restoreAt); Ready(cut);
                using var capsule = ReplayWorldCheckpoint.Capture(cut.Current);
                using var restored = new PassiveReplayScene(clip, new(256, 192));
                capsule.Restore(restored);
                Check(restored.Session.CurrentFrame == restoreAt && restored.Session.RecordingFrame == cut.Current.Session.RecordingFrame,
                    "actual private world capsule restore preserves visible and recording clocks");
                Check(ReplayDeterminism.Projection(restored.Scene, restoreAt).SequenceEqual(ReplayDeterminism.Projection(cut.Current.Scene, restoreAt)),
                    "actual private world capsule restore gameplay/presentation equality");
                Check(GrantState(restored.State) == GrantState(cut.Current.State), "actual private world capsule restore grant state equality");
                report["privateRestoreFrame"] = restoreAt;
                report["privateRestoreRecordingFrame"] = restored.Session.RecordingFrame;
            }
            Check(ReferenceEquals(foreground, GameState.Current) && ReferenceEquals(players, PlayerEntity.LegacyRegistry)
                && ReferenceEquals(random, Rng.Current), "private export/playback preserves foreground facade owners");
            Check(ReplayClipFidelity.Run(source, clip, end) != 0, "wrong source-to-clip mapping rejected");
            string truncated = Path.Combine(output, "negative-truncated.ppdemo");
            byte[] data = File.ReadAllBytes(clip); File.WriteAllBytes(truncated, data[..(data.Length / 2)]);
            var negative = ReplayArchive.Validate(truncated); report["truncatedResult"] = negative.ToString();
            Check(negative != ReplayOpenResult.Success, "truncated exported clip rejected");
            Check(Hash(source) == expectedSource && Hash(client.Location) == expectedClient, "immutable source/client hashes unchanged after checks");
            report["status"] = "PASS";
        }
        catch (Exception ex) { report["error"] = ex.ToString(); Console.WriteLine(ex); }
        File.WriteAllText(Path.Combine(output, "receipt.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("GENUINE CLIP " + report["status"] + " checks=" + Checks.Count);
        return Equals(report["status"], "PASS") ? 0 : 1;
    }
}
