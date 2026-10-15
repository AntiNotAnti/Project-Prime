using System.Reflection;
using System.Text;
using MphRead;
using MphRead.Mods;
using MphRead.Mods.MapGen;
using MphRead.Mods.Network;

// Real compiler/package/output integrity fixtures; no ROM, GL, device, scene
// simulation or multiplayer certification is implied by this tool.
string root = Path.Combine(Path.GetTempPath(), "prime-map-ready-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
int checks = 0;
void Check(bool value, string label)
{ if (!value) throw new InvalidOperationException(label); checks++; }
void Rejected(Action action, string label)
{
    bool rejected = false;
    try { action(); }
    catch (TargetInvocationException ex) when (ex.InnerException is InvalidDataException) { rejected = true; }
    catch (InvalidDataException) { rejected = true; }
    Check(rejected, label);
}
try
{
    Headless.Enter();
    Paths.SetPath(Paths.MphKey, Path.Combine(root, "runtime"));
    CustomRooms.MapDirectory = Path.Combine(root, "maps");
    CustomRooms.UserMapDirectory = Path.Combine(root, "installed");
    Directory.CreateDirectory(CustomRooms.MapDirectory);
    string texture = Path.Combine(CustomRooms.MapDirectory, "tile.tex");
    using (var writer = new BinaryWriter(File.Create(texture)))
    {
        writer.Write(Encoding.ASCII.GetBytes("FPTX")); writer.Write((ushort)1); writer.Write((ushort)1);
        writer.Write((ushort)0); writer.Write((ushort)8); writer.Write((ushort)8); writer.Write((ushort)1);
        writer.Write((ushort)0); writer.Write((ushort)32767); writer.Write(new byte[64]);
    }
    byte[] validTexture = File.ReadAllBytes(texture);
    var source = new MapDefinition { FormatVersion = 2, MapId = Guid.NewGuid(), Name = "MAP_READY_ACCEPTANCE" };
    source.Materials.Add(new() { Texture = "tile.tex" }); source.Assets.Add(new() { Path = "tile.tex" });
    source.Geometry.Add(new MapBox { Transform = new() { Position = new[] { 0f, -1, 0 }, Scale = new[] { 8f, 1, 8 } } });
    source.Spawns.Add(new() { Position = new[] { 0f, 2, 0 } });
    source.Save(Path.Combine(CustomRooms.MapDirectory, "ready.json"));
    CustomRooms.Reload();
    var definition = CustomRooms.Definitions.Single(d => d.Name == source.Name);
    Check(CustomRooms.WhyUnplayable(definition.Name) != null, "missing outputs cannot be played from a catalog entry");
    CustomRooms.GenerateMissing(definition.Name);
    Check(CustomRooms.WhyUnplayable(definition.Name) == null, "real compiler publishes current outputs");
    MapOutputSet outputs = CustomRooms.OutputsFor(definition);
    foreach (string file in outputs.Files)
    {
        File.Delete(file);
        Check(CustomRooms.WhyUnplayable(definition.Name) != null, "missing output rejected: " + Path.GetFileName(file));
        CustomRooms.GenerateMissing(definition.Name);
        Check(!CustomRooms.NeedsGenerating(definition), "missing output repaired: " + Path.GetFileName(file));
        byte[] bytes = File.ReadAllBytes(file); bytes[0] ^= 0x40; File.WriteAllBytes(file, bytes);
        Check(CustomRooms.WhyUnplayable(definition.Name) != null, "damaged output rejected: " + Path.GetFileName(file));
        CustomRooms.GenerateMissing(definition.Name);
        Check(!CustomRooms.NeedsGenerating(definition), "damaged output repaired: " + Path.GetFileName(file));
    }
    File.WriteAllText(outputs.Manifest, "{broken manifest");
    Check(CustomRooms.NeedsGenerating(definition), "malformed manifest rejected");
    CustomRooms.GenerateMissing(definition.Name);
    definition.PointLimit++;
    Check(CustomRooms.NeedsGenerating(definition), "recipe change invalidates prior output set");
    CustomRooms.GenerateMissing(definition.Name);
    Check(!CustomRooms.NeedsGenerating(definition), "stale recipe repaired");

    // Exercise the runtime entry point repeatedly with one failing dependency.
    // Count actual build attempts, rather than just inspecting the failure cache.
    File.Delete(texture);
    var log = new StringWriter(); TextWriter console = Console.Out;
    try
    {
        Console.SetOut(log);
        for (int i = 0; i < 120; i++) CustomRooms.GenerateMissing(definition.Name);
        Check(log.ToString().Split("[mapgen] building ").Length - 1 == 1,
            "120 repeated failed requests perform one build attempt");
        Check(CustomRooms.WhyUnplayable(definition.Name)?.Contains("Build failed:") == true,
            "build failure retains actionable original diagnostic");
        CustomRooms.RetryGeneration(definition.Name); CustomRooms.GenerateMissing(definition.Name);
        Check(log.ToString().Split("[mapgen] building ").Length - 1 == 2, "explicit retry allows one new attempt");
    }
    finally { Console.SetOut(console); }
    File.WriteAllBytes(texture, validTexture);
    CustomRooms.GenerateMissing(definition.Name);
    Check(!CustomRooms.NeedsGenerating(definition), "restored dependency automatically retries failed fingerprint");

    NetworkMapIdentity.PrepareRoom(definition.Name);
    var installed = CustomRooms.Installed;
    Check(installed.TryGet(definition.Name, out var package) && installed.HasExact(package.Identity),
        "network preparation stages verified immutable package");
    Check(Read.GetRoomModelInstance(definition.Name).Model.Meshes.Count > 0, "generated model decodes with real runtime reader");
    var room = CustomRooms.Definitions.Single(d => d.Name == definition.Name);
    Check(!CustomRooms.NeedsGenerating(room), "staged package and runtime fingerprints agree");

    var ownedOutputs = CustomRooms.OutputsFor(room);
    byte[] ownedModel = File.ReadAllBytes(ownedOutputs.Model);
    using (IDisposable owned = (IDisposable)typeof(MapRuntimeUsage)
        .GetMethod("AcquirePreparation", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { room.Name })!)
    {
        NetworkMapIdentity.PrepareRoom(room.Name);
        Check(File.ReadAllBytes(ownedOutputs.Model).SequenceEqual(ownedModel),
            "valid owned map can be advertised without replacing live runtime bytes");
        File.Delete(ownedOutputs.Model);
        Rejected(() => NetworkMapIdentity.PrepareRoom(room.Name),
            "preparation ownership rejects runtime regeneration beneath an active reader");
        Check(!File.Exists(ownedOutputs.Model), "rejected owned-map preparation leaves damaged output set untouched");
    }
    NetworkMapIdentity.PrepareRoom(room.Name);
    Check(File.Exists(ownedOutputs.Model) && CustomRooms.WhyUnplayable(room.Name) == null,
        "released preparation allows runtime repair on next request");

    // Exercise the same client load gate used immediately before room switching,
    // without a socket or a native scene. Ready state alone must never authorize
    // missing runtime bytes or a package changed after preparation.
    var required = NetworkMapIdentity.ForRoom(room.Name);
    var sessionProperty = typeof(NetSession).GetProperty(nameof(NetSession.ServerSession))!;
    var preparationProperty = typeof(NetSession).GetProperty(nameof(NetSession.MapPreparation))!;
    var oldSession = sessionProperty.GetValue(null);
    var oldPreparation = preparationProperty.GetValue(null);
    try
    {
        sessionProperty.SetValue(null, new SessionStatePacket
        { MatchId = 1, AuthorityEpoch = 1, Match = new MatchDefinition { RoomKey = room.Name, MapIdentity = required } });
        LobbyMapPreparationState Ready() => new() { RequiredMap = required, RoomKey = room.Name, State = MapAvailabilityState.Ready };
        preparationProperty.SetValue(null, Ready());
        NetSession.RequireExactMapForLoad();
        Check(NetSession.RequiredMapReady, "client switch accepts exact prepared package and runtime");
        var runtime = CustomRooms.OutputsFor(room);
        byte[] model = File.ReadAllBytes(runtime.Model);
        File.Delete(runtime.Model);
        Rejected(NetSession.RequireExactMapForLoad, "client switch rejects Ready state with missing runtime model");
        Check(NetSession.MapPreparation?.State == MapAvailabilityState.Failed
            && NetSession.MapPreparation.Error?.Contains("runtime") == true,
            "client switch records actionable failed preparation after runtime damage");
        File.WriteAllBytes(runtime.Model, model);
        preparationProperty.SetValue(null, Ready());
        byte[] exact = File.ReadAllBytes(package!.PackagePath);
        try
        {
            File.AppendAllText(package.PackagePath, "client-switch-damage");
            Rejected(NetSession.RequireExactMapForLoad, "client switch rejects Ready state with changed exact archive");
            Check(NetSession.MapPreparation?.State == MapAvailabilityState.Failed
                && NetSession.MapPreparation.Error?.Contains("changed") == true,
                "client switch exposes exact archive failure");
        }
        finally { File.WriteAllBytes(package.PackagePath, exact); }
        NetSession.RetryMapPreparation();
        Check(NetSession.MapPreparation == null, "explicit client retry clears failed preparation");
    }
    finally
    {
        sessionProperty.SetValue(null, oldSession);
        preparationProperty.SetValue(null, oldPreparation);
    }

    const BindingFlags hidden = BindingFlags.NonPublic | BindingFlags.Instance;
    var rotation = MapRotation.FromList(new[] { ("MP1 SANCTORUS", GameMode.Battle), (room.Name, GameMode.Battle) }, 0, 0);
    var server = new DedicatedServer(0, 2, rotation);
    Type peerType = typeof(DedicatedServer).GetNestedType("Peer", BindingFlags.NonPublic)!;
    object peer = Activator.CreateInstance(peerType, nonPublic: true)!;
    MethodInfo voteGate = typeof(DedicatedServer).GetMethod("TryPrepareVoteRoom", hidden)!;
    Check((bool)voteGate.Invoke(server, new[] { peer, (object)room.Name })!, "actual server vote gate accepts verified map");
    byte[] packageBytes = File.ReadAllBytes(package!.PackagePath);
    File.AppendAllText(package.PackagePath, "changed-after-indexing");
    Check(!installed.HasExact(package.Identity), "exact package hash detects changed archive");
    Check(CustomRooms.WhyUnplayable(room.Name)?.Contains("package") == true, "readiness exposes damaged package diagnostic");
    Check(!(bool)voteGate.Invoke(server, new[] { peer, (object)room.Name })!, "actual vote/pick gate rejects changed package");
    string prior = rotation.Current.RoomKey;
    typeof(DedicatedServer).GetMethod("AdvanceMap", hidden)!.Invoke(server, new object[] { 1d });
    Check(rotation.Current.RoomKey == prior, "failed host rotation preserves current entry without throwing out of server pump");
    Check((string?)typeof(DedicatedServer).GetField("_blockedRotationRoom", hidden)!.GetValue(server) == room.Name
        && (string?)typeof(DedicatedServer).GetField("_blockedRotationError", hidden)!.GetValue(server) is { Length: > 0 },
        "failed rotation records actionable blocked map diagnostic");
    for (int i = 0; i < 120; i++)
        typeof(DedicatedServer).GetMethod("AdvanceMap", hidden)!.Invoke(server, new object[] { 1d + i / 60d });
    Check((double)typeof(DedicatedServer).GetField("_rotationPreparationRetryAt", hidden)!.GetValue(server)! == 6d,
        "120 server pumps do not repeat failed rotation preparation");
    typeof(DedicatedServer).GetMethod("ReturnToLobby", hidden)!.Invoke(server, null);
    Check(rotation.Current.RoomKey == prior, "failed lobby rotation preserves current entry without stopping server");
    File.WriteAllBytes(package.PackagePath, packageBytes);
    NetworkMapIdentity.PrepareRoom(room.Name);
    Check(CustomRooms.WhyUnplayable(room.Name) == null, "exact package restoration recovers readiness");
    Check((bool)typeof(DedicatedServer).GetMethod("TryPrepareNextMap", hidden)!.Invoke(server, new object[] { 7d })!,
        "restored rotation package recovers after bounded retry delay");
    Console.WriteLine($"MAP READY PASS: {checks} checks; real compiler, five-output damage/missing repair, stale/malformed manifest, bounded failed generation, exact package and server vote/rotation gates.");
    Console.WriteLine("PENDING: real scenes, independent clients, Android, devices, 1v1/8-player gameplay and external editor acceptance.");
    return 0;
}
catch (Exception ex) { Console.Error.WriteLine("MAP READY FAIL: " + ex); return 1; }
finally { RoomPrewarm.Clear(); if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
