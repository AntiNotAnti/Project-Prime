using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead.Mods.MapGen;

namespace MphRead.NetTest;

/// <summary>Read-only, format-level inventory. Completeness is not a gameplay compatibility verdict.</summary>
internal static class MultiplayerContentAudit
{
    private sealed record Entry(int Index, uint Offset, int RawType, string Type, short EntityId, int LayerMask);
    private sealed record Count(string Type, int CountValue);
    private sealed record Layer(int Id, Count[] Types);
    private sealed record Scenario(string Mode, int Players, int Layer);
    private sealed class RoomResult
    {
        public int Id { get; init; }
        public string Name { get; init; } = "";
        public string Source { get; init; } = "";
        public string? EntityPath { get; init; }
        public string? Sha256 { get; set; }
        public uint? FormatVersion { get; set; }
        public List<Entry> Entries { get; } = new();
        public Layer[] Layers { get; set; } = Array.Empty<Layer>();
        public List<string> Errors { get; } = new();
    }

    public static int Run(string[] args)
    {
        if (args.Length is < 3 or > 5)
        {
            Console.Error.WriteLine("--audit-multiplayer DATA OUTPUT_JSON [FH_DATA|-] [MAP_DIRECTORY|-]");
            return 2;
        }
        try
        {
            string retail = Path.GetFullPath(args[1]);
            string? fh = args.Length > 3 && args[3] != "-" ? Path.GetFullPath(args[3]) : null;
            string maps = args.Length > 4 && args[4] != "-" ? Path.GetFullPath(args[4]) : CustomRooms.MapDirectory;
            string output = Path.GetFullPath(args[2]);
            foreach (string root in new[] { retail, fh, Path.GetFullPath(maps) }.OfType<string>())
            {
                if (output == root || output.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar)
                    + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    throw new ArgumentException("The developer artifact must be outside all input content directories.");
            }
            CustomRooms.MapDirectory = maps;
            // In-memory path selection only. Do not open a server package, generate custom maps, or write paths.txt.
            Paths.SetPath("AMHE1", retail);
            Paths.MphKey = "AMHE1";
            Paths.SetPath("AMFE0", fh ?? "");
            Paths.FhKey = "AMFE0";
            var scenarios = new List<Scenario>();
            for (GameMode mode = GameMode.Battle; mode <= GameMode.PrimeHunter; mode++)
                for (int players = 1; players <= 8; players++)
                    scenarios.Add(new(mode.ToString(), players, Metadata.GetMultiplayerEntityLayer(mode, players)));
            var custom = CustomRooms.Definitions.Select(d => d.Name).ToHashSet(StringComparer.Ordinal);
            var rooms = new List<RoomResult>();
            foreach (RoomMetadata room in Metadata.RoomList.Where(r => r.Multiplayer).OrderBy(r => r.Id))
            {
                var result = new RoomResult { Id = room.Id, Name = room.Name,
                    Source = room.FirstHunt ? "FirstHunt" : custom.Contains(room.Name) ? "Custom" : "Retail",
                    EntityPath = room.EntityPath?.Replace('\\', '/') };
                rooms.Add(result);
                string? root = room.FirstHunt ? fh : retail;
                if (root == null) { result.Errors.Add("Missing First Hunt extracted data root (supply FH_DATA)."); continue; }
                if (room.EntityPath == null) { result.Errors.Add("Catalog entry has no entity path; absence is not proof of unused types."); continue; }
                string path = Paths.Combine(root, room.EntityPath);
                try
                {
                    byte[] bytes = File.ReadAllBytes(path);
                    result.Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                    Inspect(bytes, room.FirstHunt, result);
                    if (result.Errors.Count == 0)
                    {
                        // Exercise the real parser after structural checks, including every mask, not just Battle/2.
                        foreach (int layer in room.FirstHunt ? new[] { -1 } : Enumerable.Range(0, 16).Prepend(-1))
                        {
                            var parsed = Read.GetEntitiesFromPath(path, layer, room.FirstHunt);
                            int expected = result.Entries.Count(e => layer == -1 || (e.LayerMask & (1 << layer)) != 0);
                            if (parsed.Count != expected) result.Errors.Add($"Parser count mismatch at layer {layer}: {parsed.Count} != {expected}.");
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or ProgramException or InvalidOperationException)
                { result.Errors.Add($"{ex.GetType().Name}: {ex.Message}"); }
            }
            bool complete = rooms.Count > 0 && rooms.All(r => r.Errors.Count == 0);
            var report = new
            {
                SchemaVersion = 1, RetailVersion = "AMHE1", FirstHuntVersion = "AMFE0",
                Complete = complete,
                Scope = "Catalog Multiplayer=true entries including registered custom maps; binary entity inventory only. No model, collision, objective, spawn viability or live-client validation.",
                DeletionCaveat = "Missing or malformed rooms prohibit global absence claims. Entries include unused masks. First Hunt version 1 has no layer masks. Custom registration may omit definitions with missing import sources; registration messages must be retained with this report. Runtime-generated entities are outside this inventory.",
                RetailRoot = retail, FirstHuntRoot = fh, MapDirectory = Path.GetFullPath(maps),
                Scenarios = scenarios, Rooms = rooms,
                SourceSummary = rooms.GroupBy(r => r.Source).OrderBy(g => g.Key, StringComparer.Ordinal)
                    .Select(g => new { Source = g.Key, CatalogEntries = g.Count(), Audited = g.Count(r => r.Errors.Count == 0),
                        Errors = g.Sum(r => r.Errors.Count), ObservedEntities = g.Sum(r => r.Entries.Count) }),
                ObservedTypes = Counts(rooms.SelectMany(r => r.Entries)),
                MutableCollisionTypes = Counts(rooms.SelectMany(r => r.Entries).Where(e => e.Type is "Door" or "ForceField" or "Platform" or "FhDoor" or "FhPlatform"))
            };
            // Never overwrite a previous audit or an existing user file.
            using var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write);
            JsonSerializer.Serialize(stream, report, new JsonSerializerOptions { WriteIndented = true });
            Console.WriteLine($"MULTIPLAYERAUDIT rooms={rooms.Count} errors={rooms.Sum(r => r.Errors.Count)} complete={complete} output={output}");
            return complete ? 0 : 1;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or ProgramException)
        { Console.Error.WriteLine(ex.Message); return 2; }
    }

    private static Count[] Counts(IEnumerable<Entry> entries) => entries.GroupBy(e => e.Type)
        .OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => new Count(g.Key, g.Count())).ToArray();

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidDataException(message); }

    private static void Inspect(byte[] bytes, bool firstHunt, RoomResult result)
    {
        Require(bytes.Length >= 4, "Truncated entity version.");
        uint version = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        result.FormatVersion = version;
        Require(version == (firstHunt ? 1u : 2u), $"Unexpected entity version {version} for catalog format.");
        int headerSize = firstHunt ? 4 : 36;
        int stride = firstHunt ? 20 : 24;
        Require(bytes.Length >= headerSize, "Truncated entity header.");
        int tableEnd = headerSize;
        var ranges = new List<(uint Offset, int Length)>();
        for (int index = 0; ; index++)
        {
            int entryAt = checked(headerSize + index * stride);
            Require(entryAt <= bytes.Length - stride, "Missing entity table terminator or truncated entry.");
            uint offset = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(entryAt + stride - 4));
            tableEnd = entryAt + stride;
            if (offset == 0) break;
            Require(offset <= bytes.Length - 40L, $"Entry {index}: entity header offset {offset} outside file.");
            int rawType = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan((int)offset));
            int typeId = rawType + (firstHunt ? 100 : 0);
            string name = Enum.GetName(typeof(EntityType), (ushort)typeId) ?? $"Unknown({typeId})";
            int mask = firstHunt ? 0 : BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(entryAt + 16));
            result.Entries.Add(new(index, offset, rawType, name,
                BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan((int)offset + 2)), mask));
            // Match the exact on-disk parser support, not the larger runtime EntityType enum.
            bool supported = firstHunt ? rawType is 1 or 3 or 4 or 6 or 9 or 10 or 11 or 12 or 13 or 14
                : rawType is >= 0 and <= 19 and not 5;
            int length = firstHunt ? 40 : BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(entryAt + 18));
            if (!supported) result.Errors.Add($"Entry {index} offset {offset}: unsupported/unknown on-disk entity type {rawType} ({name}).");
            else
            {
                string dataTypeName = name is "FhPlayerSpawn" ? "PlayerSpawn" : name is "FhPointModule" ? "PointModule" : name;
                Type dataType = typeof(EntityType).Assembly.GetType("MphRead." + dataTypeName + "EntityData", throwOnError: true)!;
                int expected = Marshal.SizeOf(dataType);
                if (firstHunt) length = expected;
                else if (length != expected) result.Errors.Add($"Entry {index}: payload length {length}, parser expects {expected} for {name}.");
            }
            Require(length >= 40 && offset <= bytes.Length - (long)length, $"Entry {index}: truncated entity payload.");
            ranges.Add((offset, length));
        }
        foreach (var range in ranges)
            Require(range.Offset >= tableEnd, $"Payload at {range.Offset} overlaps entity table ending at {tableEnd}.");
        var sorted = ranges.OrderBy(r => r.Offset).ToArray();
        for (int i = 1; i < sorted.Length; i++)
            Require(sorted[i].Offset >= sorted[i - 1].Offset + (long)sorted[i - 1].Length, "Overlapping entity payloads.");
        result.Layers = (firstHunt ? new[] { -1 } : Enumerable.Range(0, 16))
            .Select(layer => new Layer(layer, Counts(result.Entries.Where(e => layer == -1 || (e.LayerMask & (1 << layer)) != 0)))).ToArray();
        if (!firstHunt)
            for (int layer = 0; layer < 16; layer++)
            {
                int declared = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4 + layer * 2));
                int actual = result.Entries.Count(e => (e.LayerMask & (1 << layer)) != 0);
                if (declared != actual) result.Errors.Add($"Layer {layer}: header count {declared}, actual {actual}.");
            }
    }

    public static int SelfTest()
    {
        static RoomResult Check(byte[] data, bool fh = false)
        { var result = new RoomResult(); Inspect(data, fh, result); return result; }
        static void Reject(byte[] data, bool fh = false)
        {
            try { Check(data, fh); } catch (InvalidDataException) { return; }
            throw new InvalidOperationException("Malformed entity fixture was accepted.");
        }
        byte[] empty = new byte[60]; empty[0] = 2;
        Require(Check(empty).Entries.Count == 0, "Empty retail table failed.");
        byte[] fhEmpty = new byte[24]; fhEmpty[0] = 1;
        Require(Check(fhEmpty, true).Layers.Single().Id == -1, "FH layerless table failed.");
        Reject(Array.Empty<byte>()); Reject(new byte[] { 3, 0, 0, 0 }); Reject(empty[..59]); Reject(fhEmpty);
        // Known disk types and FH-specific numbering remain supported, including deletion-sensitive types.
        foreach (var fixture in new[] { (Type: EntityType.PlayerSpawn, Fh: false),
            (Type: EntityType.FhDoor, Fh: true), (Type: EntityType.FhPlatform, Fh: true) })
        {
            string typeName = fixture.Type == EntityType.PlayerSpawn ? "PlayerSpawn" : fixture.Type.ToString();
            int size = Marshal.SizeOf(typeof(EntityType).Assembly.GetType("MphRead." + typeName + "EntityData", true)!);
            int offset = fixture.Fh ? 44 : 84;
            byte[] data = new byte[offset + size]; data[0] = fixture.Fh ? (byte)1 : (byte)2;
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(fixture.Fh ? 20 : 56), (uint)offset);
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset), (ushort)((int)fixture.Type - (fixture.Fh ? 100 : 0)));
            if (!fixture.Fh)
            {
                data[4] = 1; data[34] = 1;
                BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(52), 0x8001);
                BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(54), (ushort)size);
            }
            var valid = Check(data, fixture.Fh);
            Require(valid.Errors.Count == 0 && valid.Entries.Single().Type == fixture.Type.ToString(), "Known type fixture failed.");
            if (!fixture.Fh) Require(valid.Layers[0].Types.Single().CountValue == 1
                && valid.Layers[15].Types.Single().CountValue == 1 && valid.Layers[1].Types.Length == 0,
                "Multi-mask entity was counted incorrectly.");
            Reject(data[..^1], fixture.Fh);
        }
        byte[] unknown = new byte[124]; unknown[0] = 2; unknown[4] = 1;
        unknown[52] = 1; unknown[54] = 40; unknown[56] = 84; unknown[84] = 250;
        var parsed = Check(unknown);
        Require(parsed.Errors.Any(e => e.Contains("unsupported/unknown")) && parsed.Entries.Single().RawType == 250,
            "Unknown type must remain visible and fail completeness.");
        unknown[56] = 123; Reject(unknown);
        unknown[56] = 36; Reject(unknown);
        empty[4] = 1;
        Require(Check(empty).Errors.Any(e => e.Contains("header count")), "Incorrect layer counts accepted.");
        Console.WriteLine("MULTIPLAYERAUDIT SELFTEST PASS: empty retail/FH, truncation, versions, offsets, unknown types, counts.");
        return 0;
    }
}
