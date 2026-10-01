using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using MphRead.Mods.Render.Materials;

string root = Path.Combine(Path.GetTempPath(), "prime-material-check-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
int assertions = 0;
void Check(bool value, string message) { assertions++; if (!value) throw new Exception(message); }
void Reject(Action action, string message)
{
    bool rejected = false;
    try { action(); } catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or ArgumentException) { rejected = true; }
    Check(rejected, message);
}
var key = MaterialAssetKey.Model("Samus", 3, 0, 0);
void Manifest(params MaterialPackEntry[] entries) => MaterialInventory.Write(Path.Combine(root, "materials.json"),
    new MaterialPackManifest { Materials = entries.ToList() });
try
{
    Check(key.Value == "model/samus/texture/3/palette/0/recolor/0", "canonical identity");
    Check(MaterialAssetKey.Model("a b", 0, 0, 0) != MaterialAssetKey.Model("a_20b", 0, 0, 0), "encoded names cannot alias");
    Check(new MaterialAssetKey("MODEL/SAMUS") == new MaterialAssetKey("model/samus"), "case normalization");
    Reject(() => _ = new MaterialAssetKey("../bad"), "key traversal");
    // Synthetic one-pixel RGBA PNG, no game assets.
    File.WriteAllBytes(Path.Combine(root, "pixel.png"), Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg=="));
    Manifest(new MaterialPackEntry() { Key = key.Value, Albedo = "pixel.png", Normal = "pixel.png", SpecularRoughness = "pixel.png", Emissive = "pixel.png" });
    var pack = MaterialPack.Load(root);
    Check(pack.TryResolve(key, out var material) && material.Albedo?.Width == 1 && material.Normal != null
        && material.SpecularRoughness != null && material.Emissive != null, "all channels decoded");
    Check(new MaterialResolver(root).Resolve("Samus", 3, 0, 0) == material, "shared manifest resolution");
    Manifest(new MaterialPackEntry() { Key = key.Value, Normal = "missing.png" });
    pack = MaterialPack.Load(root);
    Check(pack.Issues.Count == 1 && !pack.Issues[0].Error && pack.TryResolve(key, out material) && material.Albedo == null, "optional maps fail soft");
    Manifest(new MaterialPackEntry() { Key = key.Value }, new MaterialPackEntry() { Key = key.Value.ToUpperInvariant() });
    Reject(() => MaterialPack.Load(root), "duplicate normalized keys");
    foreach (string path in new[] { "../pixel.png", "/pixel.png", "a\\pixel.png", "C:/pixel.png", "pixel.exe" })
    {
        Manifest(new MaterialPackEntry() { Key = key.Value, Normal = path });
        Reject(() => MaterialPack.Load(root), "unsafe or unsupported map: " + path);
    }
    File.WriteAllText(Path.Combine(root, "bad.png"), "not an image");
    Manifest(new MaterialPackEntry() { Key = key.Value, Emissive = "bad.png", Albedo = "pixel.png" });
    pack = MaterialPack.Load(root);
    Check(pack.TryResolve(key, out material) && material.Albedo != null && material.Emissive == null, "bad optional image preserves albedo");
    byte[] oversized = File.ReadAllBytes(Path.Combine(root, "pixel.png"));
    System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(oversized.AsSpan(16,4), int.MaxValue);
    File.WriteAllBytes(Path.Combine(root, "huge.png"), oversized);
    Reject(() => MaterialPack.ValidateImage(Path.Combine(root, "huge.png")), "dimensions bounded before decode");
    File.CreateSymbolicLink(Path.Combine(root, "linked.png"), Path.Combine(root, "pixel.png"));
    Manifest(new MaterialPackEntry() { Key = key.Value, Albedo = "linked.png" });
    Reject(() => MaterialPack.Load(root), "linked image rejected");
    File.Delete(Path.Combine(root, "linked.png"));
    File.WriteAllText(Path.Combine(root, "materials.json"), "{\"format\":2,\"id\":\"test\",\"name\":\"test\",\"materials\":[]}");
    Reject(() => MaterialPack.Load(root), "unknown version");
    File.WriteAllText(Path.Combine(root, "materials.json"), "{\"format\":1,\"format\":1}");
    Reject(() => MaterialPack.Load(root), "duplicate property");
    File.Delete(Path.Combine(root, "materials.json"));
    Directory.CreateDirectory(Path.Combine(root, "Samus"));
    File.Copy(Path.Combine(root, "pixel.png"), Path.Combine(root, "Samus", "3_0_0.png"));
    Check(new MaterialResolver(root).Resolve("Samus", 3, 0, 0).Albedo != null, "legacy filename preserved");
    Manifest(new MaterialPackEntry() { Key = key.Value });
    Check(new MaterialResolver(root).Resolve("Samus", 3, 0, 0).Albedo == null, "explicit empty entry overrides legacy");
    string inventory = Path.Combine(root, "inventory.json");
    MaterialInventory.Write(inventory, new[] { new MaterialObservation(key.Value, 16, 32, "Samus") });
    string starter = Path.Combine(root, "starter");
    Check(MaterialPackCommand.Run(new[] { "starter", inventory, starter }) == 0, "starter command");
    var starterPack = MaterialPack.Load(starter);
    Check(starterPack.Manifest.Materials.Single().Albedo == "" && starterPack.Issues.Count == 0, "starter does not guess filenames");
    Check(MaterialPackCommand.Run(new[] { "starter", inventory, starter }) != 0, "starter refuses overwrite");
    string authoring = Path.Combine(root, "authoring");
    MaterialPackAuthoring.Assign(authoring, key, "albedo", Path.Combine(root, "pixel.png"));
    var authored = MaterialPack.Load(authoring);
    Check(authored.TryResolve(key, out material) && material.Albedo?.Width == 1, "authoring copies valid image into pack");
    MaterialPackAuthoring.Assign(authoring, key, "normal", Path.Combine(root, "pixel.png"));
    MaterialPackAuthoring.Assign(authoring, key, "albedo", null);
    authored = MaterialPack.Load(authoring);
    Check(authored.TryResolve(key, out material) && material.Albedo == null && material.Normal != null, "clear retains other assignments");
    byte[] corrupt = File.ReadAllBytes(Path.Combine(root, "pixel.png"));
    corrupt[29] ^= 1;
    File.WriteAllBytes(Path.Combine(root, "checksum.png"), corrupt);
    Reject(() => MaterialPack.ValidateImage(Path.Combine(root, "checksum.png")), "PNG checksum enforced");
    using (var large = File.Create(Path.Combine(root, "oversized.png"))) large.SetLength(MaterialPack.MaximumImageBytes + 1L);
    Reject(() => MaterialPack.ValidateImage(Path.Combine(root, "oversized.png")), "image file bound");
    File.Delete(Path.Combine(root, "oversized.png"));
    string linkedDirectory = Path.Combine(root, "linked-directory");
    Directory.CreateSymbolicLink(linkedDirectory, authoring);
    Reject(() => MaterialPack.Load(root), "linked directory rejected without following it");
    Directory.Delete(linkedDirectory);
    using (var large = File.Create(Path.Combine(root, "unreferenced.bin"))) large.SetLength(MaterialPack.MaximumPackBytes + 1L);
    Reject(() => MaterialPack.Load(root), "pack budget includes unreferenced files");
    File.Delete(Path.Combine(root, "unreferenced.bin"));
    Check(MaterialPackCommand.Run(new[] { "inspect", root, "extra" }) == 2, "command argument count");
    var definition = new MphRead.Mods.MapGen.MapDefinition { Name = "Stable material test", MapId = Guid.NewGuid() };
    string originalDefinition = JsonSerializer.Serialize(definition);
    var makeMetadata = typeof(MphRead.Mods.MapGen.CustomRooms).GetMethod("MakeMetadata", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
    var metadata = (MphRead.RoomMetadata)makeMetadata.Invoke(null, new object[] { definition, 9999 })!;
    Check(MaterialAssetKey.RoomScope(metadata) == "map/" + definition.MapId.ToString("N"), "community metadata uses existing MapId");
    Check(JsonSerializer.Serialize(definition) == originalDefinition, "presentation identity does not alter package definition");
    definition.MapId = Guid.Empty;
    metadata = (MphRead.RoomMetadata)makeMetadata.Invoke(null, new object[] { definition, 9999 })!;
    Check(MaterialAssetKey.RoomScope(metadata) == "room/" + MaterialAssetKey.Identifier(definition.Name), "legacy room uses known metadata name without invented MapId");
    var roomKey = MaterialAssetKey.Scoped("room/mp1-sanctorus", 3, 0, 0);
    var communityKey = MaterialAssetKey.Scoped("map/" + Guid.Parse("e5cf5163-b05a-4c50-a1cc-9919d0738f56").ToString("N"), 3, 0, 0);
    var effectKey = MaterialAssetKey.Scoped("effect/model/particle", 3, 0, 0);
    Check(roomKey != key && roomKey != communityKey && communityKey != effectKey, "scope identities cannot alias");
    Manifest(new MaterialPackEntry { Key = key.Value, Albedo = "pixel.png" });
    Check(new MaterialResolver(root).Resolve("Samus", 3, 0, 0, roomKey).Albedo != null, "old model manifest remains fallback for room identity");
    Manifest(new MaterialPackEntry { Key = key.Value, Albedo = "pixel.png" }, new MaterialPackEntry { Key = roomKey.Value });
    Check(new MaterialResolver(root).Resolve("Samus", 3, 0, 0, roomKey).Albedo == null, "scoped manifest precedes generic model manifest");
    foreach (var scoped in new[] { communityKey, effectKey })
    {
        Manifest(new MaterialPackEntry { Key = scoped.Value, Albedo = "pixel.png" });
        Check(new MaterialResolver(root).Resolve("Samus", 3, 0, 0, scoped).Albedo != null, "map/effect manifest identity resolves");
    }
    var sharedEffectKey = MaterialAssetKey.Scoped(MaterialAssetKey.EffectScope("Samus"), 3, 0, 0);
    Manifest(new MaterialPackEntry { Key = sharedEffectKey.Value, Albedo = "pixel.png" });
    Check(new MaterialResolver(root).Resolve("Samus", 3, 0, 0).Albedo != null, "shared effect alias resolves independently of particle load order");
    File.WriteAllText(Path.Combine(root, "materials.json"), "{broken");
    Check(new MaterialResolver(root, useManifest: false).Resolve("Samus", 3, 0, 0).Albedo != null, "malformed manifest cannot disable legacy filename path");
    if (args.Contains("--gpu", StringComparer.Ordinal)) GpuCheck.Run(root, key);
    Console.WriteLine($"Material pack checks PASS ({assertions} assertions)");
}
finally { Directory.Delete(root, true); }
