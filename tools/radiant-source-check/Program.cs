using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;

int checks = 0;
void Check(bool value, string label)
{
    if (!value) throw new InvalidOperationException(label);
    checks++; Console.WriteLine("PASS " + label);
}
void Refused(Action action, string label)
{
    bool refused = false;
    try { action(); } catch (Exception ex) when (ex is IOException or InvalidDataException) { refused = true; }
    Check(refused, label);
}
byte[] Identity(byte[] source) => JsonSerializer.SerializeToUtf8Bytes(new QuakeMapSidecar(
    1, Guid.NewGuid(), "TRANSACTION", 16, Convert.ToHexString(SHA256.HashData(source))));
MapDefinition Fixture(string name) => new()
{
    MapId = Guid.NewGuid(), FormatVersion = 2, Name = name,
    Author = "Native owner", Description = "Unchanged metadata", Version = "19",
    Materials = new() { new() { Id = Guid.NewGuid(), Name = "textures/prime/metal", Texture = "assets/metal.png",
        Albedo = "assets/metal-hd.png", Alpha = 24, TwoSided = true,
        Animation = new() { UvScroll = new[] { .1f, .2f } } } },
    Geometry = new() { new MapBox { Id = Guid.NewGuid(), Label = "Author label", Layer = "Floor", Shade = .7f, Transform = new() { Scale = new[] { 8f, 2f, 8f } } } },
    Spawns = new() { new() { Id = Guid.NewGuid(), Team = 0, Position = new[] { 0f, 4f, 0f } } },
    Items = new() { new() { Id = Guid.NewGuid(), SpawnInterval = 721, HasBase = false } },
    JumpPads = new() { new() { Id = Guid.NewGuid(), Speed = 22 } },
    Audio = new() { Volume = .6f }, KillHeight = -91,
    NativeRoom = new() { Room = "MP3 PROVING GROUND", PreserveEntities = true },
    Collision = new() { Source = "collision.obj", ZUp = true },
    Brushes = new() { new() { Id = Guid.NewGuid(), Label = "Native-only brush", Damaging = true } },
    NavigationLinks = new() { new() { Kind = MapNavigationLinkKind.Teleporter, To = new[] { 1f, 2f, 3f } } },
    Q3Triggers = new() { new() { Kind = "teleport", Destination = new[] { 4f, 5f, 6f } } }
};
string root = Path.Combine(Path.GetTempPath(), "prime-radiant-源-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    foreach (var crashPoint in Enum.GetValues<RadiantSourceCommitPoint>())
    {
        var crashMap = Fixture("WORKSPACE_CRASH");
        string crashSource = RadiantSourceWorkspace.SourcePath(root, crashMap.MapId);
        Refused(() => RadiantSourceWorkspace.Prepare(crashMap, root, point =>
        {
            if (point == crashPoint) throw new IOException("injected workspace crash");
        }), "workspace crash injected at " + crashPoint);
        string recoveredSource = RadiantSourceWorkspace.Prepare(crashMap, root);
        Check(recoveredSource == crashSource && File.Exists(crashSource + ".prime-source.json")
            && RadiantSourceWorkspace.Preview(crashMap, recoveredSource).Proposed.Serialize() == crashMap.Serialize(),
            "first workspace recovers source, sidecar and native adjunct after " + crashPoint);
        Check(!File.Exists(RadiantSourceWorkspace.SidecarPath(crashSource) + ".prime-transaction"),
            "workspace journal retires only after native adjunct commit at " + crashPoint);
    }
    var interrupted = Fixture("WORKSPACE_EXTERNAL");
    string interruptedSource = RadiantSourceWorkspace.SourcePath(root, interrupted.MapId);
    Refused(() => RadiantSourceWorkspace.Prepare(interrupted, root, point =>
    {
        if (point == RadiantSourceCommitPoint.SidecarCommitted) throw new IOException("crash before adjunct");
    }), "workspace can stop in the former source-to-adjunct crash gap");
    File.AppendAllText(interruptedSource, "\n// author save after crash\n");
    string savedExternal = File.ReadAllText(interruptedSource);
    Refused(() => RadiantSourceWorkspace.Prepare(interrupted, root),
        "workspace recovery preserves external save before the adjunct was committed");
    Check(File.ReadAllText(interruptedSource) == savedExternal,
        "failed workspace recovery never erases post-crash author content");
    var first = Fixture("FIRST"); var second = Fixture("SECOND");
    string firstJson = first.Serialize();
    string one = RadiantSourceWorkspace.Prepare(first, root);
    string two = RadiantSourceWorkspace.Prepare(second, root);
    Check(one != two && File.Exists(one) && File.Exists(two), "two MapIds have independent Unicode source directories");
    Check(QuakeMapInterchange.ReadIdentity(RadiantSourceWorkspace.SidecarPath(one)).MapId == first.MapId
        && QuakeMapInterchange.ReadIdentity(RadiantSourceWorkspace.SidecarPath(two)).MapId == second.MapId,
        "each source sidecar binds the correct identity");
    Check(first.Serialize() == firstJson, "geometry export leaves native project serialization byte-exact");
    var unchanged = RadiantSourceWorkspace.Preview(first, one);
    Check(unchanged.Proposed.Serialize() == firstJson, "unchanged roundtrip preserves all canonical project bytes");
    Check(unchanged.Proposed.Spawns[0].Team == 0 && unchanged.Proposed.Items[0].SpawnInterval == 721
        && !unchanged.Proposed.Items[0].HasBase && unchanged.Proposed.JumpPads[0].Speed == 22,
        "team spawns, pickup timing and launch pads survive geometry-only workflow");
    File.AppendAllText(one, "\n// external author edit\n");
    string external = File.ReadAllText(one);
    RadiantSourceWorkspace.Prepare(first, root);
    Check(File.ReadAllText(one) == external, "reopening preserves external edits across workspace recreation");
    Check(RadiantSourceWorkspace.State(first, one) == RadiantSourceState.ExternalChanges,
        "source state detects external changes");
    var review = RadiantSourceWorkspace.Preview(first, one);
    var document = new MapDocument(new(first));
    string before = document.Project.Definition.Serialize();
    document.Edit("Apply Radiant geometry", d => { d.Geometry = review.Proposed.Geometry; d.Materials = review.Proposed.Materials; });
    Check(document.Project.Definition.Author == first.Author && document.Project.Definition.KillHeight == -91
        && document.Project.Definition.Geometry[0].Layer == "Floor" && document.Project.Definition.Geometry[0].Shade == .7f,
        "reviewed source preserves metadata, author labels, layer and shade");
    if (document.History.CanUndo) document.History.Undo();
    Check(document.Project.Definition.Serialize() == before, "one undo restores exact prior native project");
    File.AppendAllText(one, "// newer edit\n");
    Refused(() => RadiantSourceWorkspace.RequireFresh(first, review), "review rejects a source revision changed before apply");
    var dirtyGeometry = MapProjectSerializer.Clone(first); dirtyGeometry.Geometry[0].Transform.Position[0] = 3;
    Refused(() => RadiantSourceWorkspace.Preview(dirtyGeometry, one), "dirty Studio geometry requires conflict reconciliation");
    var metadataEdit = MapProjectSerializer.Clone(first); metadataEdit.Description = "Concurrent metadata edit";
    Check(RadiantSourceWorkspace.Preview(metadataEdit, one).Proposed.Description == metadataEdit.Description,
        "independent native metadata edits are retained during review");
    var geometryEdit = Fixture("EDITED");
    string geometrySource = RadiantSourceWorkspace.Prepare(geometryEdit, root);
    string geometryText = File.ReadAllText(geometrySource);
    // Move every Quake X plane together: an actual geometry revision, rather
    // than only a source comment, must keep all native adjunct domains.
    geometryText = System.Text.RegularExpressions.Regex.Replace(geometryText,
        @"\( (?<x>-?\d+(?:\.\d+)?) ", match => "( "
            + (float.Parse(match.Groups["x"].Value, System.Globalization.CultureInfo.InvariantCulture) + 16)
                .ToString(System.Globalization.CultureInfo.InvariantCulture) + " ");
    File.WriteAllText(geometrySource, geometryText);
    var geometryReview = RadiantSourceWorkspace.Preview(geometryEdit, geometrySource);
    Check(geometryReview.Proposed.Geometry[0].Transform.Position[0] == 1f,
        "source geometry edits replace native geometry positions");
    var expectedHybrid = MapProjectSerializer.Clone(geometryEdit);
    expectedHybrid.Geometry = geometryReview.Proposed.Geometry;
    Check(expectedHybrid.Serialize() == geometryReview.Proposed.Serialize(),
        "changed geometry preserves native architecture, collision, triggers, navigation, gameplay and material bytes");
    RadiantSourceWorkspace.RequireFresh(geometryEdit, geometryReview);
    RadiantSourceWorkspace.Accept(geometryReview.Proposed, geometryReview);
    Check(RadiantSourceWorkspace.State(geometryReview.Proposed, geometrySource) == RadiantSourceState.Saved,
        "accepted review advances the source and native geometry baseline");
    string identityPath = RadiantSourceWorkspace.SidecarPath(geometrySource);
    string identityBytes = File.ReadAllText(identityPath);
    File.AppendAllText(identityPath, "\n");
    Refused(() => RadiantSourceWorkspace.RequireFresh(geometryEdit, geometryReview),
        "apply refuses identity sidecar changes after preview");
    File.WriteAllText(identityPath, identityBytes);

    var surfaces = Fixture("SURFACES");
    surfaces.Geometry.Add(new MapConvexBrush
    {
        Label = "Native ramp", Layer = "Ramps", Shade = .8f,
        Vertices = new() { new[] { 0f, 0f, 0f }, new[] { 8f, 0f, 0f }, new[] { 0f, 0f, -8f },
            new[] { 8f, 0f, -8f }, new[] { 0f, 4f, -8f }, new[] { 8f, 4f, -8f } },
        Faces = new() { new[] { 0, 2, 3, 1 }, new[] { 0, 4, 2 }, new[] { 1, 3, 5 },
            new[] { 2, 4, 5, 3 }, new[] { 0, 1, 5, 4 } }
    });
    var patch = new MapBezierPatch { Label = "Native patch", Solid = false, Tessellation = 2 };
    for (int col = 0; col < 3; col++)
        for (int row = 0; row < 3; row++)
            patch.ControlPoints.Add(new[] { (float)col, col == 1 && row == 1 ? 1f : 0f,
                (float)row, col / 2f, row / 2f });
    surfaces.Geometry.Add(patch);
    string surfacesSource = RadiantSourceWorkspace.Prepare(surfaces, root);
    File.AppendAllText(surfacesSource, "\n// reviewed revision\n");
    var surfacesReview = RadiantSourceWorkspace.Preview(surfaces, surfacesSource);
    Check(surfacesReview.Proposed.Geometry.Any(g => g is MapConvexBrush)
        && surfacesReview.Proposed.Geometry.OfType<MapBezierPatch>().Single().Solid == false
        && surfacesReview.Proposed.Geometry.OfType<MapBezierPatch>().Single().Tessellation == 2,
        "hybrid workflow retains editable convex geometry and patch collision/tessellation intent");
    Check(surfacesReview.Proposed.Geometry.Single(g => g.Label == "Native ramp").Layer == "Ramps"
        && surfacesReview.Proposed.Materials[0].Texture == surfaces.Materials[0].Texture
        && surfacesReview.Proposed.Materials[0].Albedo == surfaces.Materials[0].Albedo,
        "supported surfaces preserve native authoring metadata and material assets");

    foreach (var racePoint in Enum.GetValues<RadiantSourceCommitPoint>().Where(p => p != RadiantSourceCommitPoint.AdjunctCommitted))
    foreach (bool sidecarEdit in new[] { false, true })
    {
        string raceDirectory = Path.Combine(root, "commit-race-" + racePoint + "-" + sidecarEdit); Directory.CreateDirectory(raceDirectory);
        string raceSource = Path.Combine(raceDirectory, "world.map"), raceSidecar = Path.Combine(raceDirectory, "project-prime.json");
        byte[] exported = Encoding.UTF8.GetBytes("exported"), author = Encoding.UTF8.GetBytes("concurrent author save");
        using (var lease = RadiantSourceTransaction.Acquire(raceSource))
            Refused(() => RadiantSourceTransaction.CommitUnderLease(raceSource, raceSidecar,
                exported, Identity(exported), null, null, stage =>
                {
                    if (stage == racePoint)
                        File.WriteAllBytes(sidecarEdit ? raceSidecar : raceSource, author);
                }), "commit detects concurrent " + (sidecarEdit ? "sidecar" : "source") + " save at " + racePoint);
        Check(File.ReadAllBytes(sidecarEdit ? raceSidecar : raceSource).SequenceEqual(author)
            && File.Exists(raceSidecar + ".prime-transaction"), "concurrent save and recovery journal survive conflict");
    }
    var unsupported = Fixture("UNSUPPORTED"); unsupported.Geometry.Add(new MapMesh());
    Refused(() => RadiantSourceWorkspace.Prepare(unsupported, root), "unsupported meshes refuse export instead of flattening");
    Check(!File.Exists(RadiantSourceWorkspace.SourcePath(root, unsupported.MapId)), "failed export leaves no misleading editable source");

    foreach (var point in Enum.GetValues<RadiantSourceCommitPoint>().Where(p => p != RadiantSourceCommitPoint.AdjunctCommitted))
    {
        string directory = Path.Combine(root, point.ToString()); Directory.CreateDirectory(directory);
        string source = Path.Combine(directory, "world.map"), sidecar = Path.Combine(directory, "project-prime.json");
        byte[] nextSource = Encoding.UTF8.GetBytes("new source"), nextSidecar = Identity(nextSource);
        try
        {
            using var lease = RadiantSourceTransaction.Acquire(source);
            RadiantSourceTransaction.CommitUnderLease(source, sidecar, nextSource, nextSidecar, null, null,
                stage => { if (stage == point) throw new IOException("injected crash"); });
        }
        catch (IOException) { }
        RadiantSourceTransaction.Recover(source, sidecar);
        Check(File.ReadAllBytes(source).SequenceEqual(nextSource) && File.ReadAllBytes(sidecar).SequenceEqual(nextSidecar),
            "recover consistent pair after " + point);
        Check(!File.Exists(sidecar + ".prime-transaction"), "completed recovery removes journal after " + point);

        byte[] replacement = Encoding.UTF8.GetBytes("replacement source"), replacementIdentity = Identity(replacement);
        using (var lease = RadiantSourceTransaction.Acquire(source))
        {
            try
            {
                RadiantSourceTransaction.CommitUnderLease(source, sidecar, replacement, replacementIdentity,
                    Convert.ToHexString(SHA256.HashData(nextSource)), Convert.ToHexString(SHA256.HashData(nextSidecar)),
                    stage => { if (stage == point) throw new IOException("injected replacement crash"); });
            }
            catch (IOException) { }
        }
        RadiantSourceTransaction.Recover(source, sidecar);
        Check(File.ReadAllBytes(source).SequenceEqual(replacement) && File.ReadAllBytes(sidecar).SequenceEqual(replacementIdentity),
            "recover replacement of existing source pair after " + point);
    }
    string conflictDirectory = Path.Combine(root, "recovery-conflict"); Directory.CreateDirectory(conflictDirectory);
    string conflictSource = Path.Combine(conflictDirectory, "world.map"), conflictSidecar = Path.Combine(conflictDirectory, "project-prime.json");
    try
    {
        using var lease = RadiantSourceTransaction.Acquire(conflictSource);
        RadiantSourceTransaction.CommitUnderLease(conflictSource, conflictSidecar,
            Encoding.UTF8.GetBytes("exported"), Identity(Encoding.UTF8.GetBytes("exported")), null, null,
            stage => { if (stage == RadiantSourceCommitPoint.SourceCommitted) throw new IOException("crash"); });
    }
    catch (IOException) { }
    File.WriteAllText(conflictSource, "saved in Radiant after crash");
    Refused(() => RadiantSourceTransaction.Recover(conflictSource, conflictSidecar), "recovery refuses to erase post-crash author edits");
    Check(File.ReadAllText(conflictSource) == "saved in Radiant after crash" && File.Exists(conflictSidecar + ".prime-transaction"),
        "conflicted recovery retains author bytes and journal for reconciliation");
    Refused(() => RadiantSourceTransaction.Recover(conflictSource, Path.Combine(root, "other-sidecar.json")),
        "transaction rejects a sidecar outside the source directory");

    string strict = Path.Combine(root, "strict"); Directory.CreateDirectory(strict);
    string strictSource = Path.Combine(strict, "legacy.map");
    var native = Fixture("LEGACY"); native.Spawns.Clear(); native.Items.Clear(); native.JumpPads.Clear();
    native.NativeRoom = null; native.Collision = null; native.Brushes.Clear(); native.NavigationLinks.Clear(); native.Q3Triggers = null;
    QuakeMapInterchange.Export(native, strictSource);
    byte[] originalSource = File.ReadAllBytes(strictSource), originalSidecar = File.ReadAllBytes(RadiantSourceWorkspace.SidecarPath(strictSource));
    QuakeMapInterchange.Export(native, strictSource);
    Check(File.ReadAllBytes(strictSource).SequenceEqual(originalSource)
        && File.ReadAllBytes(RadiantSourceWorkspace.SidecarPath(strictSource)).SequenceEqual(originalSidecar),
        "legacy export remains byte-exact when unchanged");
    File.AppendAllText(strictSource, "// edited\n");
    Refused(() => QuakeMapInterchange.Export(native, strictSource), "strict exporter refuses changed external source");
    Check(File.ReadAllBytes(RadiantSourceWorkspace.SidecarPath(strictSource)).SequenceEqual(originalSidecar),
        "refused strict export leaves identity sidecar unchanged");
    string moved = Path.Combine(root, "moved"); Directory.Move(strict, moved);
    Check(QuakeMapInterchange.Import(Path.Combine(moved, "legacy.map"), Path.Combine(moved, "project-prime.json")).Definition.MapId == native.MapId,
        "moving a source and sidecar together preserves map identity");
    var duplicate = Fixture("DUPLICATE"); duplicate.Geometry.Add(new MapBox { Id = duplicate.Geometry[0].Id });
    Refused(() => RadiantSourceWorkspace.Prepare(duplicate, root), "duplicate source object GUIDs are refused");
    string malformed = Path.Combine(root, "malformed.map"); File.WriteAllText(malformed, "{ broken map }");
    Refused(() => QuakeMapInterchange.Import(malformed), "malformed source is rejected by existing parser");
    string oversized = Path.Combine(root, "oversized.map");
    using (var stream = File.Create(oversized)) stream.SetLength(32L * 1024 * 1024 + 1);
    Refused(() => QuakeMapInterchange.Import(oversized), "oversized source is bounded before parsing");
    if (!OperatingSystem.IsWindows())
    {
        string linkSource = Path.Combine(root, "source-link.map");
        File.CreateSymbolicLink(linkSource, one);
        Refused(() => RadiantSourceTransaction.Acquire(linkSource).Dispose(), "source file symbolic links are refused");
        string linkDirectory = Path.Combine(root, "directory-link");
        Directory.CreateSymbolicLink(linkDirectory, Path.GetDirectoryName(one)!);
        Refused(() => RadiantSourceTransaction.Acquire(Path.Combine(linkDirectory, "world.map")).Dispose(),
            "symbolic links at the workspace owner are refused");
        string linkLockSource = Path.Combine(root, "lock-link.map");
        File.CreateSymbolicLink(linkLockSource + ".prime-lock", Path.Combine(root, "missing-lock-target"));
        Refused(() => RadiantSourceTransaction.Acquire(linkLockSource).Dispose(), "dangling lock symbolic links are refused");
    }
    string? compiler = Environment.GetEnvironmentVariable("PROJECT_PRIME_Q3MAP2_EXE");
    if (!string.IsNullOrEmpty(compiler))
    {
        string compilerRoot = Path.Combine(root, "real-compiler");
        string maps = Path.Combine(compilerRoot, "baseq3", "maps"); Directory.CreateDirectory(maps);
        string textures = Path.Combine(compilerRoot, "baseq3", "textures", "prime"); Directory.CreateDirectory(textures);
        // An original solid gray 2x2 TGA, with no third-party asset input.
        byte[] tga = new byte[18 + 2 * 2 * 3]; tga[2] = 2; tga[12] = 2; tga[14] = 2; tga[16] = 24; tga[17] = 0x20;
        Array.Fill(tga, (byte)128, 18, 12); File.WriteAllBytes(Path.Combine(textures, "gray.tga"), tga);
        var arena = new MapDefinition { MapId = Guid.NewGuid(), FormatVersion = 2, Name = "REAL_COMPILER" };
        arena.Materials.Add(new() { Id = Guid.NewGuid(), Name = "prime/gray" });
        foreach (var bounds in new (float[] Lo, float[] Hi)[]
        {
            (new[] { -17f, -1f, -17f }, new[] { 17f, 0f, 17f }),
            (new[] { -17f, 16f, -17f }, new[] { 17f, 17f, 17f }),
            (new[] { -17f, 0f, -17f }, new[] { -16f, 16f, 17f }),
            (new[] { 16f, 0f, -17f }, new[] { 17f, 16f, 17f }),
            (new[] { -16f, 0f, -17f }, new[] { 16f, 16f, -16f }),
            (new[] { -16f, 0f, 16f }, new[] { 16f, 16f, 17f })
        })
            arena.Geometry.Add(new MapBox { Transform = new()
            {
                Position = bounds.Lo.Zip(bounds.Hi, (lo, hi) => (lo + hi) / 2).ToArray(),
                Scale = bounds.Lo.Zip(bounds.Hi, (lo, hi) => hi - lo).ToArray()
            } });
        arena.Spawns.Add(new() { Id = Guid.NewGuid(), Position = new[] { 0f, .5f, 0f } });
        string arenaSource = Path.Combine(maps, "real_compiler.map");
        QuakeMapInterchange.Export(arena, arenaSource);
        var start = new System.Diagnostics.ProcessStartInfo(Path.GetFullPath(compiler))
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = compilerRoot
        };
        foreach (string argument in new[] { "-game", "quake3", "-fs_basepath", compilerRoot, "-fs_game", "baseq3", "-meta", arenaSource })
            start.ArgumentList.Add(argument);
        using var process = System.Diagnostics.Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try { await process.WaitForExitAsync(deadline.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        string output = await stdout + await stderr;
        Check(process.ExitCode == 0, "real Q3Map2 accepts exported native geometry: " + (process.ExitCode == 0 ? "exit 0" : output));
        byte[] bsp = File.ReadAllBytes(Path.ChangeExtension(arenaSource, ".bsp"));
        Check(bsp.Length >= 144 && Encoding.ASCII.GetString(bsp, 0, 4) == "IBSP" && BitConverter.ToInt32(bsp, 4) == 46
            && BitConverter.ToInt32(bsp, 8 + 8 * 8 + 4) > 0 && BitConverter.ToInt32(bsp, 8 + 13 * 8 + 4) > 0,
            "real Q3Map2 emits nonempty collision brushes and draw surfaces from exported planes");
        Console.WriteLine("Real compiler SHA-256: " + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(compiler))));
    }
    else Console.WriteLine("SKIP real Q3Map2 regression: configure PROJECT_PRIME_Q3MAP2_EXE to enable it.");
    Console.WriteLine($"radiant-source-check: {checks} checks passed. Real editor/platform acceptance remains pending.");
}
finally { Directory.Delete(root, recursive: true); }
