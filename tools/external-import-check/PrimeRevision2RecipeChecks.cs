using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MphRead.Mods.Import;

internal static class PrimeRevision2RecipeChecks
{
    private static int _checks;
    private const string LegacyFingerprint = "ae1541ffdf70bf87a49cac0f72422d69f32ef7ea";
    private static readonly (string Id, string Recipe)[] Legacy =
    {
        ("trooper-power", "cf04e8f85575877e15be49960c9190a9d428723846ffb4353bd91ab97ffc41d8"),
        ("trooper-ice", "15a2e0fe73eb6983ef8baa88131576c7632e33dbaa13e266063c3511ab43b546"),
        ("trooper-wave", "b2131444ade0323c04795ad3a2b6637b552b36ba584d32fb5ed08b92e411d204"),
        ("trooper-plasma", "9ebb847f18b5c3930a7ceeb8ce46bed433cacdbef51f778f4a3979a13eb9add5")
    };
    private static readonly (string Id, string Recipe)[] PreviousRevision2 =
    {
        ("trooper-power", "5f2bcfeb64962b16303aaf425b9fd6a72b448e4ee633efd863406d80d7da5d24"),
        ("trooper-ice", "f3e306915b55dc7705699e90409675b2c9bf35ffb6f312499a58c5bb7e5ced57"),
        ("trooper-wave", "307ee117dddbc3bdfbe3498162cb67b1c624f7a1d44bf5c7120dd9f51856f75b"),
        ("trooper-plasma", "701b4768bd93f3457f4f53e81ec5de4b3fd5a49f88a5c1bd8546af43905f6fb4")
    };
    // Literal receipt identities from frozen fc8 qualification, not computed
    // from the current recipe factory or an optional local evidence file.
    internal static ImportBundleSpec PreviousRev2(string id) => new(id, 2,
        PreviousRevision2.Single(p => p.Id == id).Recipe, "c55bf0ecb6cac32fa90d43cb0ad9e4a917d43ed0",
        new[] { id + ".obj", id + ".skeleton.json", id + ".weights.json", id + ".motions.json" });
    private static readonly (string Id, string Recipe)[] FrozenRevision3 =
    {
        ("trooper-power", "da2ada3b677642a599fb383b128d096d6a4a2c6dcffcb4592b166b87537076de"),
        ("trooper-ice", "90ae436ff8bd6a85a4e4c5119d4a8a222dbd14b7f8996e42b911e802af62ebe0"),
        ("trooper-wave", "085161ab3646fb21a228870e3c7bef650371e0972903a5fb831dfbade1b842b3"),
        ("trooper-plasma", "855b8b672e1e6109c3358770d2c4ed971196f56e78a789b3bdaed9776ad10452")
    };
    // Frozen before adding material transport; never derive the old identity
    // from the factory under test or an optional evidence file.
    internal static ImportBundleSpec PreviousRev3(string id) => new(id, 3,
        FrozenRevision3.Single(p => p.Id == id).Recipe, "c55bf0ecb6cac32fa90d43cb0ad9e4a917d43ed0", FullSourceComponents(id));
    private static readonly (string Id, string Recipe)[] FrozenRevision4 =
    {
        ("trooper-power", "c6664a265dee5eefcff4a8cb8f4549a641624a59285d4b1bffbc94ca95e97395"),
        ("trooper-ice", "ff33ba3267221e0acbe2d656f515210f382fbede7118ee108197cabd69e8b10e"),
        ("trooper-wave", "bbd70c0e4c4c3e0dcf96d5622781ff3787d3bc97c36219fc655ef1acbeea4fd1"),
        ("trooper-plasma", "fba9b16339515d7b377c750e7d3b5729f5017c4ac0125f6a2f75f20805c2a78a")
    };
    // Literal PR945 authoring receipts captured before generated previews.
    internal static ImportBundleSpec PreviousRev4(string id) => new(id, 4,
        FrozenRevision4.Single(p => p.Id == id).Recipe, "c55bf0ecb6cac32fa90d43cb0ad9e4a917d43ed0", MaterialComponents(id));
    private static readonly (string Id, string Recipe)[] FrozenRevision5 =
    {
        ("trooper-power", "ad97b060fe9506489f03887d695c607e4b9831fbd93a0f460f953cd9388405ce"),
        ("trooper-ice", "eb416c4b78f5975549b1d9b0f3bb6d2c5b33e426e33c25b73118d6bcc1851d68"),
        ("trooper-wave", "3f6aa579d08d37741bad46b01de3567ddd7d30edb6de8e0b1226ef1fd3b457fe"),
        ("trooper-plasma", "8a50df7808734c184f1f30e4c2f17e0463304d9d2cd77fd0935c99407d9fe119")
    };
    // Literal generated-body receipts reproduced by frozen 8ecc core before
    // cannon authoring; never derive a prior identity from the current factory.
    internal static ImportBundleSpec PreviousRev5(string id) => new(id, 5,
        FrozenRevision5.Single(p => p.Id == id).Recipe, "c55bf0ecb6cac32fa90d43cb0ad9e4a917d43ed0", PreviewComponents(id));
    internal static string[] FullSourceComponents(string id) => new[] { ".obj", ".skeleton.json", ".weights.json", ".motions.json",
        ".cinf.bin", ".animations.bin", ".source.glb", ".source.json", ".ancs-graph.json", ".ancs.bin" }.Select(s => id + s).ToArray();
    internal static string[] MaterialComponents(string id) => FullSourceComponents(id).Append(id + ".materials.bin").ToArray();
    internal static string[] PreviewComponents(string id) => MaterialComponents(id).Append("characters.json").ToArray();
    internal static string[] FullComponents(string id) => PreviewComponents(id).Append(id + ".cannon-source.json").ToArray();

    public static void Run()
    {
        _checks = 0;
        CheckRequirements();
        string root = Path.Combine(Path.GetTempPath(), "prime-rev2-recipes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            foreach (var expected in Legacy)
            {
                // Literal goldens derived independently from the frozen d098
                // legacy recipe strings and archive pins, before Rev 2 support.
                var old = RetroCharacterPreviewExporter.BundleSpec(expected.Id);
                Check(old.Id == expected.Id && old.Version == 1 && old.RecipeHash == expected.Recipe
                    && old.SourceFingerprint == LegacyFingerprint, "legacy v1 identity remains exact");
                Check(old.RequiredComponents.SequenceEqual(new[] { expected.Id + ".obj", expected.Id + ".skeleton.json",
                    expected.Id + ".weights.json", expected.Id + ".motions.json" }), "legacy four components remain exact");
                var next = RetroCharacterPreviewExporter.BundleSpec(expected.Id, ExternalSourceEdition.PrimeUsaRevision2);
                string nativeName = expected.Id["trooper-".Length..] + "_trooper";
                string cannonRecipe = "prime-usa-rev2-full-source-v6:c55bf0ecb6cac32fa90d43cb0ad9e4a917d43ed0:"
                    + $"retro-character-preview-v1:{expected.Id}:MetroidPrime:8169653a:{nativeName}:obj-skeleton-weights-motions-optional-graph"
                    + ":root3-parent2-v1:full-anim-bank-v1:generated-glb-v1:source-only-provenance-v1:required-graph"
                    + ":body-raw-materials-txtr-authoring-v1-all-local-slots:generated-trooper-source-program-manifest-v1"
                    + ":trooper-fixed-generated-cannon-v1-global-triangle-ids";
                Check(next.RecipeHash == Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(cannonRecipe))),
                    "current v6 recipe binds the exact frozen prefix, source/body contract and fixed cannon policy suffix");
                Check(next.Id == old.Id && next.Version == 6 && next.RecipeHash != old.RecipeHash
                    && next.SourceFingerprint != old.SourceFingerprint
                    && next.RequiredComponents.Length == 13 && next.RequiredComponents.ToHashSet().SetEquals(FullComponents(expected.Id)),
                    "revision 2 v6 requires eleven authoring components, the generated preview manifest and fixed cannon recipe");
                Check(next.SourceFingerprint == "c55bf0ecb6cac32fa90d43cb0ad9e4a917d43ed0", "revision 2 archive fingerprint is fixed");
                Check(next.RecipeHash != PreviousRev2(expected.Id).RecipeHash, "cannon v6 recipe cannot masquerade as frozen four-component v2");
                Check(next.RecipeHash != PreviousRev3(expected.Id).RecipeHash, "cannon v6 recipe cannot masquerade as frozen ten-component v3");
                Check(next.RecipeHash != PreviousRev4(expected.Id).RecipeHash, "cannon v6 recipe cannot masquerade as frozen eleven-component v4");
                Check(next.RecipeHash != PreviousRev5(expected.Id).RecipeHash, "cannon v6 recipe cannot masquerade as frozen twelve-component v5");
                Check(RetroCharacterPreviewExporter.PartsFor(expected.Id).Single().Version == 1
                    && RetroCharacterPreviewExporter.PartsFor(expected.Id, ExternalSourceEdition.PrimeUsaRevision2).Single() is { Id: "body", Version: 5 },
                    "only Rev2 advances the expected body component version");
                VerifyInventory(Path.Combine(root, expected.Id), old, next);
            }
            foreach (string unknown in new[] { "trooper", "power_trooper", "TROOPER-POWER", "darksamus" })
            {
                Expect<InvalidDataException>(() => RetroCharacterPreviewExporter.BundleSpec(unknown), "unknown/alias source ID refuses recipe");
                Expect<InvalidDataException>(() => RetroCharacterPreviewExporter.InspectBundle(root, unknown), "unknown/alias source ID refuses inspection");
            }
            foreach (string echoes in new[] { "darksamus-echoes", "darksamus-overcharged-echoes" })
            {
                Expect<NotSupportedException>(() => RetroCharacterPreviewExporter.BundleSpec(echoes, ExternalSourceEdition.PrimeUsaRevision2),
                    "Prime edition cannot alias an Echoes appearance");
                Expect<NotSupportedException>(() => RetroCharacterPreviewExporter.PartsFor(echoes, ExternalSourceEdition.PrimeUsaRevision2),
                    "Prime edition cannot alias Echoes component versions");
            }
            Expect<NotSupportedException>(() => RetroCharacterPreviewExporter.BundleSpec("trooper-power", (ExternalSourceEdition)999),
                "unknown edition cannot select a recipe");
            Expect<NotSupportedException>(() => RetroCharacterPreviewExporter.PartsFor("trooper-power", (ExternalSourceEdition)999),
                "unknown edition cannot select component versions");
        }
        finally { Directory.Delete(root, recursive: true); }
        Console.WriteLine($"PRIME REVISION 2 RECIPES PASS: {_checks} checks; synthetic metadata/components only");
    }

    private static void CheckRequirements()
    {
        var legacy = ExternalSourceRequirements.For(ExternalGameKind.MetroidPrime);
        Check(legacy.Count == 2 && legacy[0] == new ExternalArchiveRequirement("Metroid1.pak", "1ae13e2325ed8b57462f7a39d9d7fac035745598")
            && legacy[1] == new ExternalArchiveRequirement("AudioGrp.pak", "157e0841155cc7b699176dd7afc746c7048dbc28"), "original archive pins remain exact");
        var revision2 = ExternalSourceRequirements.For(ExternalGameKind.MetroidPrime, ExternalSourceEdition.PrimeUsaRevision2);
        Check(revision2.Count == 2 && revision2[0] == new ExternalArchiveRequirement("Metroid1.pak", "2147a5ea1ff654d6bbce749d5f7c1170f98a601e")
            && revision2[1] == legacy[1], "revision 2 pins only its measured archive variant");
        foreach (var header in new[] { (Revision: 0, Disc: 0), (Revision: 1, Disc: 0), (Revision: 2, Disc: 0), (Revision: 3, Disc: 0), (Revision: 2, Disc: 1) })
        {
            using var disc = Disc("GM8E01", (byte)header.Revision, (byte)header.Disc);
            bool exact = header.Revision == 2 && header.Disc == 0;
            Check(ExternalSourceRequirements.EditionFor(disc) == (exact ? ExternalSourceEdition.PrimeUsaRevision2 : ExternalSourceEdition.OriginalRecipe),
                "only exact USA revision 2 disc zero selects the new edition");
            Check(ExternalSourceRequirements.For(disc).SequenceEqual(exact ? revision2 : legacy), "header chooses complete archive profile");
            Check(ExternalSourceRequirements.VerifyKnown(disc).All(c => c.Status == ExternalArchiveStatus.Missing),
                "matching header alone never verifies absent archives");
        }
        using (var echoes = Disc("G2ME01", 2, 0))
        {
            Check(ExternalSourceRequirements.EditionFor(echoes) == ExternalSourceEdition.OriginalRecipe
                && ExternalSourceRequirements.For(echoes).SequenceEqual(ExternalSourceRequirements.For(ExternalGameKind.Echoes)),
                "Echoes revision byte does not select Prime edition");
        }
        Expect<NotSupportedException>(() => ExternalSourceRequirements.For(ExternalGameKind.Echoes, ExternalSourceEdition.PrimeUsaRevision2), "Echoes refuses Prime profile");
        Expect<NotSupportedException>(() => ExternalSourceRequirements.For((ExternalGameKind)999), "unknown game refuses original profile");
        Expect<NotSupportedException>(() => ExternalSourceRequirements.For(ExternalGameKind.MetroidPrime, (ExternalSourceEdition)999), "unknown edition refuses profile");
    }

    private static void VerifyInventory(string root, ImportBundleSpec old, ImportBundleSpec next)
    {
        Directory.CreateDirectory(root);
        var previous = PreviousRev2(next.Id);
        string previousRoot = Path.Combine(root, "previous-rev2");
        var previousParts = previous.RequiredComponents.ToDictionary(n => n, n => Encoding.UTF8.GetBytes("own synthetic v2 component " + n));
        LocalImportBundle.Publish(previousRoot, previous, previousParts);
        var previousSnapshot = Snapshot(previousRoot);
        var previousInspection = RetroCharacterPreviewExporter.InspectBundle(previousRoot, previous.Id);
        Check(previousInspection.State == ImportBundleState.Outdated && previousInspection.Receipt is { Version: 2 }
            && LocalImportBundle.Inspect(previousRoot, previous.Id, previous).State == ImportBundleState.Ready,
            "literal frozen Rev2 v2 remains valid under its explicit old recipe but current inspection reports Outdated");
        Check(Same(previousSnapshot, Snapshot(previousRoot)), "inspection never migrates or rewrites the old v2 receipt/generation");
        Check(RetroCharacterPreviewExporter.InspectParts(previous.Id, previousInspection).Single() is { Version: 5, State: ImportBundleState.Outdated },
            "old Rev2 receipt reports expected bodyv5 as Outdated, never current Ready");
        var frozenV3 = PreviousRev3(next.Id);
        string frozenV3Root = Path.Combine(root, "previous-rev3");
        LocalImportBundle.Publish(frozenV3Root, frozenV3, frozenV3.RequiredComponents.ToDictionary(n => n, n => Encoding.UTF8.GetBytes("own synthetic v3 component " + n)));
        var v3Snapshot = Snapshot(frozenV3Root);
        var v3Inspection = RetroCharacterPreviewExporter.InspectBundle(frozenV3Root, next.Id);
        Check(v3Inspection.State == ImportBundleState.Outdated && v3Inspection.Receipt is { Version: 3 }
            && LocalImportBundle.Inspect(frozenV3Root, next.Id, frozenV3).State == ImportBundleState.Ready
            && RetroCharacterPreviewExporter.InspectParts(next.Id, v3Inspection).Single() is { Version: 5, State: ImportBundleState.Outdated },
            "literal frozen v3 remains explicitly valid while current preview bodyv5 reports Outdated");
        Check(Same(v3Snapshot, Snapshot(frozenV3Root)), "v3 inspection preserves all ten component bytes and receipt exactly");
        var frozenV4 = PreviousRev4(next.Id);
        string frozenV4Root = Path.Combine(root, "previous-rev4");
        LocalImportBundle.Publish(frozenV4Root, frozenV4, frozenV4.RequiredComponents.ToDictionary(n => n, n => Encoding.UTF8.GetBytes("own synthetic v4 component " + n)));
        var v4Snapshot = Snapshot(frozenV4Root);
        var v4Inspection = RetroCharacterPreviewExporter.InspectBundle(frozenV4Root, next.Id);
        Check(v4Inspection.State == ImportBundleState.Outdated && v4Inspection.Receipt is { Version: 4 }
            && LocalImportBundle.Inspect(frozenV4Root, next.Id, frozenV4).State == ImportBundleState.Ready
            && RetroCharacterPreviewExporter.InspectParts(next.Id, v4Inspection).Single() is { Version: 5, State: ImportBundleState.Outdated },
            "literal frozen material v4 remains explicitly valid while current preview bodyv5 reports Outdated");
        Check(Same(v4Snapshot, Snapshot(frozenV4Root)), "v4 inspection preserves all eleven component bytes and receipt exactly");
        var frozenV5 = PreviousRev5(next.Id);
        string frozenV5Root = Path.Combine(root, "previous-rev5");
        LocalImportBundle.Publish(frozenV5Root, frozenV5, frozenV5.RequiredComponents.ToDictionary(n => n, n => Encoding.UTF8.GetBytes("own synthetic v5 component " + n)));
        var v5Snapshot = Snapshot(frozenV5Root);
        var v5Inspection = RetroCharacterPreviewExporter.InspectBundle(frozenV5Root, next.Id);
        Check(v5Inspection.State == ImportBundleState.Outdated && v5Inspection.Receipt is { Version: 5 }
            && LocalImportBundle.Inspect(frozenV5Root, next.Id, frozenV5).State == ImportBundleState.Ready
            && RetroCharacterPreviewExporter.InspectParts(next.Id, v5Inspection).Single() is { Version: 5, State: ImportBundleState.Outdated },
            "literal frozen preview v5 remains explicitly valid while cannon bodyv5 reports Outdated");
        Check(Same(v5Snapshot, Snapshot(frozenV5Root)), "v5 inspection preserves all twelve component bytes and receipt exactly");
        var parts = next.RequiredComponents.ToDictionary(n => n, n => Encoding.UTF8.GetBytes("own synthetic component " + n));
        LocalImportBundle.Publish(root, next, parts);
        var ready = RetroCharacterPreviewExporter.InspectBundle(root, next.Id);
        Check(ready.State == ImportBundleState.Ready && ready.Receipt is { Version: 6 }
            && ready.Files.Count == 13 && ready.Receipt.SourceFingerprint == next.SourceFingerprint, "edition-aware inspection admits all thirteen revision 2 v6 components");
        Check(RetroCharacterPreviewExporter.InspectParts(next.Id, ready).Single() is { Version: 5, State: ImportBundleState.Ready },
            "current Rev2 complete preview bodyv5 status is Ready");
        Check(LocalImportBundle.Inspect(root, next.Id, old).State == ImportBundleState.UnsupportedSource,
            "legacy-only inspection still refuses revision 2 fingerprint");
        Check(RetroCharacterPreviewExporter.InspectBundle(root, "darksamus-echoes").State == ImportBundleState.NotInstalled,
            "Prime publication does not discover an Echoes ID");
        string pointer = Path.Combine(root, LocalImportBundle.StorageDirectory, next.Id, "current.json");
        byte[] original = File.ReadAllBytes(pointer);
        var receipt = ready.Receipt!;
        void Changed(ImportBundleReceipt changed, ImportBundleState state, string message)
        {
            File.WriteAllBytes(pointer, JsonSerializer.SerializeToUtf8Bytes(changed));
            var result = RetroCharacterPreviewExporter.InspectBundle(root, next.Id);
            Check(result.State == state && result.Files.Count == 0, message);
            File.WriteAllBytes(pointer, original);
        }
        Changed(receipt with { SourceFingerprint = new string('a', 40) }, ImportBundleState.UnsupportedSource, "unknown source fingerprint has no permissive fallback");
        Changed(receipt with { Version = 2 }, ImportBundleState.Outdated, "known revision 2 fingerprint cannot disguise old version");
        Changed(receipt with { RecipeHash = new string('b', 64) }, ImportBundleState.Outdated, "known revision 2 fingerprint cannot disguise wrong recipe");
        Changed(receipt with { Schema = 2 }, ImportBundleState.Failed, "malformed receipt cannot trigger edition fallback");
        string component = ready.Files[0]; byte[] bytes = File.ReadAllBytes(component); byte[] corrupt = (byte[])bytes.Clone(); corrupt[0] ^= 1;
        File.WriteAllBytes(component, corrupt);
        var failed = RetroCharacterPreviewExporter.InspectBundle(root, next.Id);
        Check(failed.State == ImportBundleState.Failed && failed.Files.Count == 0 && failed.Receipt == null,
            "known source edition still verifies every component digest fail closed");
        File.WriteAllBytes(component, bytes);
        Check(RetroCharacterPreviewExporter.InspectBundle(root, next.Id).State == ImportBundleState.Ready, "restoring exact component restores Ready");
        string material = ready.Files.Single(p => Path.GetFileName(p) == next.Id + ".materials.bin");
        byte[] materialBytes = File.ReadAllBytes(material);
        File.WriteAllBytes(material, Encoding.UTF8.GetBytes("different invented material component"));
        Check(RetroCharacterPreviewExporter.InspectBundle(root, next.Id).State == ImportBundleState.Failed,
            "a changed required material archive cannot leave the v6 body Ready");
        File.WriteAllBytes(material, materialBytes);
        Check(RetroCharacterPreviewExporter.InspectBundle(root, next.Id).State == ImportBundleState.Ready,
            "restoring the exact required material digest restores v6 Ready");
        File.Delete(material);
        try
        {
            var missing = RetroCharacterPreviewExporter.InspectBundle(root, next.Id);
            var body = RetroCharacterPreviewExporter.InspectParts(next.Id, missing).Single();
            Check(missing.State == ImportBundleState.Incomplete && body is { Version: 5, State: ImportBundleState.Incomplete }
                && body.Components.Count == 13
                && body.Components.Single(c => c.Role == "source-materials").State == ImportBundleState.Incomplete
                && body.Components.Where(c => c.Role != "source-materials").All(c => c.State == ImportBundleState.Ready),
                "missing required material component reports Incomplete while all twelve other component roles remain Ready");
        }
        finally { File.WriteAllBytes(material, materialBytes); }
        Check(RetroCharacterPreviewExporter.InspectBundle(root, next.Id).State == ImportBundleState.Ready,
            "restoring the missing exact material component restores current Ready");
        string manifest = ready.Files.Single(p => Path.GetFileName(p) == "characters.json");
        byte[] manifestBytes = File.ReadAllBytes(manifest);
        File.Delete(manifest);
        try
        {
            var missing = RetroCharacterPreviewExporter.InspectBundle(root, next.Id);
            var body = RetroCharacterPreviewExporter.InspectParts(next.Id, missing).Single();
            Check(missing.State == ImportBundleState.Incomplete && body is { Version: 5, State: ImportBundleState.Incomplete }
                && missing.ComponentStates!.Single(c => c.Id == "characters.json").State == ImportBundleState.Incomplete
                && missing.ComponentStates!.Where(c => c.Id != "characters.json").Count(c => c.State == ImportBundleState.Ready) == 12
                && body.Components.Count(c => c.State == ImportBundleState.Incomplete) == 1,
                "missing required preview manifest reports Incomplete while all twelve other authoring components remain Ready");
        }
        finally { File.WriteAllBytes(manifest, manifestBytes); }
        Check(RetroCharacterPreviewExporter.InspectBundle(root, next.Id).State == ImportBundleState.Ready,
            "restoring the missing exact generated manifest restores v6 Ready");
        string cannon = ready.Files.Single(p => Path.GetFileName(p) == next.Id + ".cannon-source.json");
        byte[] cannonBytes = File.ReadAllBytes(cannon);
        File.WriteAllBytes(cannon, Encoding.UTF8.GetBytes("different invented fixed cannon recipe"));
        Check(RetroCharacterPreviewExporter.InspectBundle(root, next.Id).State == ImportBundleState.Failed,
            "changed fixed cannon recipe cannot leave the v6 body Ready");
        File.WriteAllBytes(cannon, cannonBytes);
        File.Delete(cannon);
        try
        {
            var missing = RetroCharacterPreviewExporter.InspectBundle(root, next.Id);
            var body = RetroCharacterPreviewExporter.InspectParts(next.Id, missing).Single();
            Check(missing.State == ImportBundleState.Incomplete && body is { Version: 5, State: ImportBundleState.Incomplete }
                && body.Components.Single(c => c.Role == "source-cannon-recipe") is { Version: 1, State: ImportBundleState.Incomplete }
                && body.Components.Single(c => c.Role == "source-preview-manifest").Version == 2
                && body.Components.Count(c => c.State == ImportBundleState.Ready) == 12,
                "missing fixed cannon recipe reports Incomplete while twelve other components remain Ready with exact role versions");
        }
        finally { File.WriteAllBytes(cannon, cannonBytes); }
        Check(RetroCharacterPreviewExporter.InspectBundle(root, next.Id).State == ImportBundleState.Ready,
            "restoring the exact fixed cannon recipe restores v6 Ready");
        string alias = Path.Combine(root, LocalImportBundle.StorageDirectory, "darksamus-echoes"); Directory.CreateDirectory(alias);
        File.WriteAllBytes(Path.Combine(alias, "current.json"), original);
        Check(RetroCharacterPreviewExporter.InspectBundle(root, "darksamus-echoes").State == ImportBundleState.Failed,
            "copied receipt cannot cross canonical character IDs");
        string legacyRoot = Path.Combine(root, "legacy"); Directory.CreateDirectory(legacyRoot);
        LocalImportBundle.Publish(legacyRoot, old, parts.Where(p => old.RequiredComponents.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value));
        var oldReady = RetroCharacterPreviewExporter.InspectBundle(legacyRoot, old.Id);
        Check(oldReady.State == ImportBundleState.Ready && oldReady.Receipt is { Version: 1 }
            && oldReady.Receipt.RecipeHash == old.RecipeHash, "existing complete legacy publication stays Ready");
        Check(RetroCharacterPreviewExporter.InspectParts(old.Id, oldReady).Single() is { Version: 1, State: ImportBundleState.Ready },
            "complete original source still reports bodyv1 Ready");
    }

    private static Dictionary<string, string> Snapshot(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .ToDictionary(p => Path.GetRelativePath(root, p), p => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(p))));
    private static bool Same(Dictionary<string, string> a, Dictionary<string, string> b) =>
        a.Count == b.Count && a.All(p => b.TryGetValue(p.Key, out string? hash) && hash == p.Value);

    private static GameCubeDiscReader Disc(string game, byte revision, byte number)
    {
        var bytes = new byte[0x44c]; Encoding.ASCII.GetBytes(game).CopyTo(bytes, 0); bytes[6] = number; bytes[7] = revision;
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0x1c), GameCubeDiscReader.Magic);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0x424), 0x440);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0x428), 12);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0x440), 0x01000000);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0x448), 1);
        return new GameCubeDiscReader(new MemoryStream(bytes, writable: false), leaveOpen: false);
    }
    private static void Check(bool value, string message) { _checks++; if (!value) throw new InvalidOperationException("Prime revision 2 fixture: " + message); }
    private static void Expect<T>(Action action, string message) where T : Exception
    {
        try { action(); } catch (T) { _checks++; return; }
        throw new InvalidOperationException("Prime revision 2 fixture did not refuse: " + message);
    }
}
