using System.Buffers.Binary;
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
                Check(next.Id == old.Id && next.Version == 2 && next.RecipeHash != old.RecipeHash
                    && next.SourceFingerprint != old.SourceFingerprint
                    && next.RequiredComponents.SequenceEqual(old.RequiredComponents), "revision 2 has separate recipe/source identity with same authoring outputs");
                Check(next.SourceFingerprint == "c55bf0ecb6cac32fa90d43cb0ad9e4a917d43ed0", "revision 2 archive fingerprint is fixed");
                VerifyInventory(Path.Combine(root, expected.Id), old, next);
            }
            foreach (string unknown in new[] { "trooper", "power_trooper", "TROOPER-POWER", "darksamus" })
            {
                Expect<InvalidDataException>(() => RetroCharacterPreviewExporter.BundleSpec(unknown), "unknown/alias source ID refuses recipe");
                Expect<InvalidDataException>(() => RetroCharacterPreviewExporter.InspectBundle(root, unknown), "unknown/alias source ID refuses inspection");
            }
            foreach (string echoes in new[] { "darksamus-echoes", "darksamus-overcharged-echoes" })
                Expect<NotSupportedException>(() => RetroCharacterPreviewExporter.BundleSpec(echoes, ExternalSourceEdition.PrimeUsaRevision2),
                    "Prime edition cannot alias an Echoes appearance");
            Expect<NotSupportedException>(() => RetroCharacterPreviewExporter.BundleSpec("trooper-power", (ExternalSourceEdition)999),
                "unknown edition cannot select a recipe");
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
        var parts = next.RequiredComponents.ToDictionary(n => n, n => Encoding.UTF8.GetBytes("own synthetic component " + n));
        LocalImportBundle.Publish(root, next, parts);
        var ready = RetroCharacterPreviewExporter.InspectBundle(root, next.Id);
        Check(ready.State == ImportBundleState.Ready && ready.Receipt is { Version: 2 }
            && ready.Files.Count == 4 && ready.Receipt.SourceFingerprint == next.SourceFingerprint, "edition-aware inspection admits fully verified revision 2 inventory");
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
        Changed(receipt with { Version = 1 }, ImportBundleState.Outdated, "known revision 2 fingerprint cannot disguise old version");
        Changed(receipt with { RecipeHash = new string('b', 64) }, ImportBundleState.Outdated, "known revision 2 fingerprint cannot disguise wrong recipe");
        Changed(receipt with { Schema = 2 }, ImportBundleState.Failed, "malformed receipt cannot trigger edition fallback");
        string component = ready.Files[0]; byte[] bytes = File.ReadAllBytes(component); byte[] corrupt = (byte[])bytes.Clone(); corrupt[0] ^= 1;
        File.WriteAllBytes(component, corrupt);
        var failed = RetroCharacterPreviewExporter.InspectBundle(root, next.Id);
        Check(failed.State == ImportBundleState.Failed && failed.Files.Count == 0 && failed.Receipt == null,
            "known source edition still verifies every component digest fail closed");
        File.WriteAllBytes(component, bytes);
        Check(RetroCharacterPreviewExporter.InspectBundle(root, next.Id).State == ImportBundleState.Ready, "restoring exact component restores Ready");
        string alias = Path.Combine(root, LocalImportBundle.StorageDirectory, "darksamus-echoes"); Directory.CreateDirectory(alias);
        File.WriteAllBytes(Path.Combine(alias, "current.json"), original);
        Check(RetroCharacterPreviewExporter.InspectBundle(root, "darksamus-echoes").State == ImportBundleState.Failed,
            "copied receipt cannot cross canonical character IDs");
        string legacyRoot = Path.Combine(root, "legacy"); Directory.CreateDirectory(legacyRoot);
        LocalImportBundle.Publish(legacyRoot, old, parts);
        var oldReady = RetroCharacterPreviewExporter.InspectBundle(legacyRoot, old.Id);
        Check(oldReady.State == ImportBundleState.Ready && oldReady.Receipt is { Version: 1 }
            && oldReady.Receipt.RecipeHash == old.RecipeHash, "existing complete legacy publication stays Ready");
    }

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
