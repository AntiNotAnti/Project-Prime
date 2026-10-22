using System.Reflection;
using System.Text;
using MphRead.Mods.Launcher.RmlUi.Core;

internal static class TheatreMetadataAcceptance
{
    private const BindingFlags StaticInternal = BindingFlags.Static | BindingFlags.NonPublic;
    private static readonly Assembly OwnerAssembly = typeof(RmlUiTheatreIndex).Assembly;
    private sealed record Snapshot(bool Available, int Bookmarks, int Highlights,
        string[] Tags, string[] Collections, string Names);

    public static void Run()
    {
        int checks = 0;
        void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException("Theatre Metadata: " + message);
            checks++;
        }
        var annotations = OwnerAssembly.GetType("MphRead.Mods.Replay.ReplayAnnotations", throwOnError: true)!;
        var read = annotations.GetMethod("ReadLibrarySnapshot", StaticInternal)!;
        int maximum = (int)annotations.GetField("MaximumLibrarySidecarBytes", StaticInternal)!.GetRawConstantValue()!;
        Snapshot Read(string replay)
        {
            object snapshot = read.Invoke(null, new object[] { replay })!;
            T Fact<T>(string name) => (T)snapshot.GetType().GetProperty(name)!.GetValue(snapshot)!;
            return new(Fact<bool>("Available"), Fact<int>("BookmarkCount"), Fact<int>("HighlightCount"),
                Fact<string[]>("Tags"), Fact<string[]>("Collections"), Fact<string>("Names"));
        }
        void Unknown(string replay, string message)
        {
            var value = Read(replay);
            Check(!value.Available && value.Bookmarks == 0 && value.Highlights == 0
                && value.Tags.Length == 0 && value.Collections.Length == 0 && value.Names == "", message);
        }

        // Resolve platform aliases such as /var -> /private/var before testing
        // symlink rejection, so the ordinary fixture has no linked ancestors.
        string originalDirectory = Path.Combine(Path.GetTempPath(), "prime-theatre-metadata-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(originalDirectory);
        string directory = CanonicalDirectory(originalDirectory);
        string replay = Path.Combine(directory, "fixture.ppdemo");
        string sidecar = replay + ".studio.json";
        byte[] replayBytes = { 0x50, 0x52, 0x49, 0x4d, 0x45 };
        File.WriteAllBytes(replay, replayBytes);
        const string valid = """
            {"Version":1,"Bookmarks":[null,
              {"Id":"11111111-1111-1111-1111-111111111111","Frame":12,"Name":"Opening pick","CreatedUtc":"2026-10-13T00:00:00Z"},
              {"Id":"22222222-2222-2222-2222-222222222222","Frame":15,"Name":" ","CreatedUtc":"2026-10-13T00:00:00Z"}],
             "Highlights":[null,
              {"Id":"33333333-3333-3333-3333-333333333333","StartFrame":20,"EndFrame":30,"Name":"Final escape","CreatedUtc":"2026-10-13T00:00:00Z"},
              {"Id":"44444444-4444-4444-4444-444444444444","StartFrame":30,"EndFrame":30,"Name":"Invalid range","CreatedUtc":"2026-10-13T00:00:00Z"}],
             "Tags":[null," practice ","PRACTICE",""],"Collections":[" Tournament "]}
            """;
        try
        {
            var missing = Read(replay);
            Check(missing.Available && missing.Bookmarks == 0 && missing.Highlights == 0 && missing.Names == "",
                "a missing sidecar is known empty metadata");
            Unknown("", "an unavailable replay path is unknown metadata");
            foreach (string json in new[] { "{}", "{\"Version\":1,\"Bookmarks\":null,\"Highlights\":null,\"Tags\":null,\"Collections\":null}" })
            {
                File.WriteAllText(sidecar, json);
                var empty = Read(replay);
                Check(empty.Available && empty.Bookmarks == 0 && empty.Highlights == 0
                    && empty.Tags.Length == 0 && empty.Collections.Length == 0 && empty.Names == "",
                    "valid empty schema remains known empty after sanitization");
            }
            File.WriteAllText(sidecar, valid);
            byte[] sidecarBytes = File.ReadAllBytes(sidecar);
            var named = Read(replay);
            Check(named.Available && named.Bookmarks == 1 && named.Highlights == 1
                && named.Tags.SequenceEqual(new[] { "practice" })
                && named.Collections.SequenceEqual(new[] { "Tournament" })
                && named.Names == "Opening pick Final escape",
                "one real sidecar snapshot keeps valid named moments and normalized organization");
            Check(File.ReadAllBytes(sidecar).SequenceEqual(sidecarBytes), "library reads do not rewrite authored sidecars");
            foreach (Encoding encoding in new Encoding[] { new UTF8Encoding(true), new UnicodeEncoding(false, true) })
            {
                File.WriteAllText(sidecar, valid, encoding);
                var bom = Read(replay);
                Check(bom.Available && bom.Bookmarks == 1 && bom.Highlights == 1,
                    "library reader preserves retained owner's BOM-aware decoding: " + encoding.WebName);
            }
            foreach (string json in new[] { "", "{", "null", "{\"Version\":2}", "{\"Version\":1,\"Bookmarks\":true}" })
            {
                File.WriteAllText(sidecar, json);
                Unknown(replay, "empty, malformed, unsupported or mistyped JSON stays unknown: " + json);
            }
            // Valid JSON padded at the byte limit distinguishes the accepted
            // boundary from an oversized file without allocating a huge input.
            string emptyJson = "{\"Version\":1}";
            File.WriteAllText(sidecar, emptyJson.PadRight(maximum), new UTF8Encoding(false));
            Check(new FileInfo(sidecar).Length == maximum && Read(replay).Available,
                "a valid sidecar at the exact bounded byte limit is accepted");
            File.AppendAllText(sidecar, " ", new UTF8Encoding(false));
            Unknown(replay, "a valid JSON sidecar one byte over the bound is rejected");
            Check(new FileInfo(sidecar).Length == maximum + 1, "oversize rejection leaves the input unchanged");

            File.Delete(sidecar);
            string target = Path.Combine(directory, "linked-target.json");
            File.WriteAllText(target, valid);
            File.CreateSymbolicLink(sidecar, target);
            Unknown(replay, "a sidecar symlink cannot import metadata from its target");
            File.Delete(sidecar);
            File.CreateSymbolicLink(sidecar, Path.Combine(directory, "missing-target.json"));
            Unknown(replay, "a dangling sidecar symlink is unknown rather than known empty");
            File.Delete(sidecar);
            Directory.CreateDirectory(sidecar);
            Unknown(replay, "a directory at the sidecar path is unknown metadata");
            Directory.Delete(sidecar);
            string nested = Path.Combine(directory, "real-library");
            Directory.CreateDirectory(nested);
            File.WriteAllText(Path.Combine(nested, "nested.ppdemo.studio.json"), valid);
            string alias = Path.Combine(directory, "linked-library");
            Directory.CreateSymbolicLink(alias, nested);
            Unknown(Path.Combine(alias, "nested.ppdemo"), "a linked sidecar ancestor is rejected");
            Directory.Delete(alias);

            File.WriteAllText(sidecar, valid);
            CheckProjection(replay, checks: Check);
            Check(File.ReadAllBytes(replay).SequenceEqual(replayBytes), "metadata reads preserve the underlying recording bytes");
        }
        finally { Directory.Delete(directory, recursive: true); }
        Console.WriteLine($"Theatre Metadata acceptance passed: {checks} assertions.");
    }

    private static void CheckProjection(string replay, Action<bool, string> checks)
    {
        var projector = OwnerAssembly.GetType("MphRead.Mods.Launcher.RmlUi.Render.RmlUiVirtualReplayLibrary", throwOnError: true)!
            .GetMethod("ProjectMetadata", StaticInternal)!;
        var metadataType = OwnerAssembly.GetType("MphRead.Mods.Network.ReplayMetadata", throwOnError: true)!;
        var playerType = OwnerAssembly.GetType("MphRead.Mods.Network.ReplayPlayerInfo", throwOnError: true)!;
        object metadata = Activator.CreateInstance(metadataType, nonPublic: true)!;
        metadataType.GetProperty("DurationFrames")!.SetValue(metadata, (uint)0);
        var entry = new RmlUiReplayEntry(replay, "Fixture", "20 minutes // bookmarks // Pilot", "Arena",
            1, 5, 2, false, false, false, true)
        {
            VirtualWitness = new("descriptor-sha", "/source.ppdemo", 20, 30)
        };
        RmlUiReplayEntry Project(object? facts, uint duration, bool safe = true)
            => (RmlUiReplayEntry)projector.Invoke(null, new object?[] { entry, facts, duration, safe })!;
        var projected = Project(metadata, 36000);
        checks(projected.DurationFrames == 36000 && projected.PlayerNamesText == ""
            && projected.BookmarkCount == 1 && projected.HighlightCount == 1
            && projected.Tags == "practice" && projected.Collections == "Tournament"
            && projected.AnnotationNames == "Opening pick Final escape",
            "projection uses supplied normalized recording duration even when metadata duration is zero, and reads real named sidecar facts");
        checks(projected.VirtualWitness == entry.VirtualWitness && projected.Path == entry.Path
            && projected.Bytes == entry.Bytes && projected.LastWriteTicks == entry.LastWriteTicks,
            "metadata projection retains descriptor/source witnesses and physical replay identity");
        var unknown = Project(null, 0);
        checks(unknown.DurationFrames == null && unknown.PlayerNamesText == null,
            "missing metadata and zero duration remain unknown despite descriptive prose");
        var knownEmpty = Project(metadata, 0);
        checks(knownEmpty.PlayerNamesText == "" && knownEmpty.DurationFrames == null,
            "an available empty roster stays distinct from unavailable player metadata");
        Array players = Array.CreateInstance(playerType, 2);
        players.SetValue(Activator.CreateInstance(playerType, new object[] { (byte)0, (byte)0, (sbyte)-1, "Al", false, (byte)0 }), 0);
        players.SetValue(Activator.CreateInstance(playerType, new object[] { (byte)1, (byte)0, (sbyte)-1, "Li\tNK", false, (byte)0 }), 1);
        metadataType.GetProperty("Players")!.SetValue(metadata, players);
        var roster = Project(metadata, 3599);
        checks(roster.PlayerNamesText == "Al Li\tNK" && roster.DurationFrames == 3599,
            "projection uses actual roster names and the supplied clip-range duration");
        var unsafeSidecar = Project(metadata, 3599, safe: false);
        checks(unsafeSidecar.BookmarkCount == null && unsafeSidecar.HighlightCount == null
            && unsafeSidecar.Tags == "" && unsafeSidecar.Collections == "" && unsafeSidecar.AnnotationNames == ""
            && unsafeSidecar.PlayerNamesText == "Al Li\tNK" && unsafeSidecar.DurationFrames == 3599,
            "unsafe annotations remain unknown without dropping independently available replay metadata");
        File.WriteAllText(replay + ".studio.json", "{");
        var malformed = Project(metadata, 3600);
        checks(malformed.BookmarkCount == null && malformed.HighlightCount == null,
            "malformed annotation counts cannot become known zero during projection");
        File.Delete(replay + ".studio.json");
        var missing = Project(metadata, 3600);
        checks(missing.BookmarkCount == 0 && missing.HighlightCount == 0 && missing.AnnotationNames == "",
            "a missing sidecar projects known empty annotation counts");
    }

    private static string CanonicalDirectory(string path, int remainingLinks = 40)
    {
        if (remainingLinks == 0) throw new IOException("Fixture directory has too many linked ancestors.");
        var directory = new DirectoryInfo(Path.GetFullPath(path));
        if (directory.Parent == null) return directory.FullName;
        string candidate = Path.Combine(CanonicalDirectory(directory.Parent.FullName, remainingLinks), directory.Name);
        return new DirectoryInfo(candidate).ResolveLinkTarget(returnFinalTarget: false) is { } target
            ? CanonicalDirectory(target.FullName, remainingLinks - 1) : candidate;
    }
}
