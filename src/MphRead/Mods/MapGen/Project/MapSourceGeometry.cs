using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using MphRead.Mods.Import;

namespace MphRead.Mods.MapGen;

/// <summary>
/// Versioned, private paired float UVs in final native vertex-emission order.
/// This is transport evidence; it does not enable a shader or texture binding.
/// </summary>
public static class MapSourceGeometry
{
    public const int HeaderBytes = 192, EntryBytes = 96, ProfileVersion = 1;
    public const int MaxEntries = ushort.MaxValue, MaxCorners = 1_200_000;
    public const int MaxComponentBytes = 64 * 1024 * 1024, MaxNativeModelBytes = 256 * 1024 * 1024;
    private static readonly byte[] ContractHash = SHA256.HashData(Encoding.UTF8.GetBytes(EchoesWorldGeometrySource.CoordinateContract));
    public sealed record Input(int DlistId, int MaterialId, IReadOnlyList<EchoesSourceCoordinates> Corners);
    public sealed class Entry
    {
        public int DlistId { get; }
        public int MaterialId { get; }
        public int Triangles { get; }
        public int Quads { get; }
        public IReadOnlyList<EchoesSourceCoordinates> Corners { get; }
        internal Entry(int dlist, int material, int triangles, int quads, EchoesSourceCoordinates[] corners)
        { DlistId = dlist; MaterialId = material; Triangles = triangles; Quads = quads; Corners = Array.AsReadOnly(corners); }
    }
    public sealed class Snapshot
    {
        public Guid MapId { get; }
        public string RecipeSha256 { get; }
        public string NativeModelSha256 { get; }
        public string ComponentSha256 { get; }
        public IReadOnlyList<Entry> Entries { get; }
        internal Snapshot(Guid map, string recipe, string model, string component, Entry[] entries)
        { MapId = map; RecipeSha256 = recipe; NativeModelSha256 = model; ComponentSha256 = component; Entries = Array.AsReadOnly(entries); }
    }
    private readonly record struct NativeList(int Material, DisplayList List, int Corners, int Triangles, int Quads);

    public static byte[] Encode(Guid mapId, string recipeSha256, ReadOnlySpan<byte> nativeModel, IReadOnlyList<Input> inputs)
    {
        Identity(mapId, recipeSha256);
        ArgumentNullException.ThrowIfNull(inputs);
        Require(inputs.Count is >= 1 and <= MaxEntries, "Source geometry entry budget exceeded.");
        // Copy the caller's inventory, then admit all extents before allocating output.
        Input[] sorted = new Input[inputs.Count];
        for (int i = 0; i < sorted.Length; i++) sorted[i] = inputs[i]
            ?? throw new InvalidDataException("Missing source geometry entry.");
        Array.Sort(sorted, (a, b) => a.DlistId.CompareTo(b.DlistId));
        var model = NativeDirectory(nativeModel);
        long total = HeaderBytes + (long)sorted.Length * EntryBytes;
        int corners = 0;
        var layouts = new NativeList[sorted.Length];
        for (int i = 0; i < sorted.Length; i++)
        {
            Input input = sorted[i] ?? throw new InvalidDataException("Missing source geometry entry.");
            Require(input.DlistId >= 0 && (i == 0 || sorted[i - 1].DlistId < input.DlistId)
                && input.Corners != null && input.Corners.Count is >= 3 and <= 65000,
                "Invalid source geometry entry or corner budget.");
            layouts[i] = Native(nativeModel, model, input.DlistId, input.MaterialId);
            Require(input.Corners!.Count == layouts[i].Corners, "Source coordinates do not match final native emission.");
            corners = checked(corners + input.Corners!.Count);
            Require(corners <= MaxCorners, "Source geometry aggregate corner budget exceeded.");
            total += (long)input.Corners.Count * 16;
            Require(total <= MaxComponentBytes, "Source geometry component budget exceeded.");
        }
        // Snapshot and validate by the same bounded index contract used to
        // serialize. A caller-owned list's enumerator cannot vouch for values
        // subsequently returned by its indexer or changed between passes.
        for (int i = 0; i < sorted.Length; i++)
        {
            var detached = new EchoesSourceCoordinates[layouts[i].Corners];
            for (int c = 0; c < detached.Length; c++)
            { detached[c] = sorted[i].Corners[c]; Require(Finite(detached[c]), "Source coordinates must be finite."); }
            sorted[i] = sorted[i] with { Corners = detached };
        }
        byte[] bytes = new byte[(int)total];
        Span<byte> header = bytes.AsSpan(0, HeaderBytes);
        "PPSG"u8.CopyTo(header);
        Put(header, 4, 1); Put(header, 8, HeaderBytes); Put(header, 12, sorted.Length);
        Put(header, 16, EntryBytes); Put(header, 20, HeaderBytes + sorted.Length * EntryBytes);
        Put(header, 24, bytes.Length); Put(header, 28, ProfileVersion);
        mapId.TryWriteBytes(header[32..48]); Convert.FromHexString(recipeSha256).CopyTo(header[48..]);
        SHA256.HashData(nativeModel).CopyTo(header[80..]); ContractHash.CopyTo(header[112..]);
        int next = 0, payloadOffset = HeaderBytes + sorted.Length * EntryBytes;
        for (int i = 0; i < sorted.Length; i++)
        {
            Input input = sorted[i]; NativeList layout = layouts[i];
            Span<byte> row = bytes.AsSpan(HeaderBytes + i * EntryBytes, EntryBytes);
            int length = input.Corners.Count * 16;
            Put(row, 0, input.DlistId); Put(row, 4, input.MaterialId); Put(row, 8, input.Corners.Count);
            Put(row, 12, layout.Triangles); Put(row, 16, layout.Quads); Put(row, 20, next); Put(row, 24, length);
            SHA256.HashData(nativeModel.Slice((int)layout.List.Offset, (int)layout.List.Size)).CopyTo(row[32..]);
            Span<byte> payload = bytes.AsSpan(payloadOffset + next, length);
            for (int c = 0; c < input.Corners.Count; c++)
            {
                var uv = input.Corners[c]; Span<byte> corner = payload.Slice(c * 16, 16);
                BinaryPrimitives.WriteSingleLittleEndian(corner, uv.BaseU);
                BinaryPrimitives.WriteSingleLittleEndian(corner[4..], uv.BaseV);
                BinaryPrimitives.WriteSingleLittleEndian(corner[8..], uv.LightmapU);
                BinaryPrimitives.WriteSingleLittleEndian(corner[12..], uv.LightmapV);
            }
            SHA256.HashData(payload).CopyTo(row[64..]); next += length;
        }
        SHA256.HashData(bytes.AsSpan(HeaderBytes, sorted.Length * EntryBytes)).CopyTo(header[144..]);
        return bytes;
    }

    /// <summary>Bind to the exact bytes used by the model parser, including headless callers.</summary>
    public static Snapshot Decode(ReadOnlySpan<byte> bytes, ReadOnlySpan<byte> nativeModel, Guid mapId, string recipeSha256)
    {
        Identity(mapId, recipeSha256);
        Require(bytes.Length is >= HeaderBytes and <= MaxComponentBytes, "Invalid source geometry component extent.");
        var header = bytes[..HeaderBytes];
        Require(header[..4].SequenceEqual("PPSG"u8) && U32(header, 4) == 1 && U32(header, 8) == HeaderBytes
            && U32(header, 16) == EntryBytes && U32(header, 24) == bytes.Length && U32(header, 28) == ProfileVersion
            && header[176..].IndexOfAnyExcept((byte)0) < 0, "Invalid source geometry header.");
        Require(new Guid(header[32..48]) == mapId && header[48..80].SequenceEqual(Convert.FromHexString(recipeSha256))
            && header[112..144].SequenceEqual(ContractHash), "Source geometry recipe or coordinate contract changed.");
        Require(nativeModel.Length <= MaxNativeModelBytes && header[80..112].SequenceEqual(SHA256.HashData(nativeModel)),
            "Source geometry belongs to different native model bytes.");
        int count = Int(header, 12);
        Require(count is >= 1 and <= MaxEntries, "Source geometry entry budget exceeded.");
        int payloadOffset = HeaderBytes + count * EntryBytes;
        Require(U32(header, 20) == payloadOffset && payloadOffset < bytes.Length,
            "Source geometry directory is truncated.");
        var directory = bytes.Slice(HeaderBytes, count * EntryBytes);
        Require(header[144..176].SequenceEqual(SHA256.HashData(directory)), "Source geometry directory integrity failed.");
        var model = NativeDirectory(nativeModel);
        // First pass proves every range and native grammar before retaining UV arrays.
        int next = 0, totalCorners = 0, previousId = -1;
        var layouts = new NativeList[count];
        for (int i = 0; i < count; i++)
        {
            var row = directory.Slice(i * EntryBytes, EntryBytes);
            int id = Int(row, 0), material = Int(row, 4), corners = Int(row, 8);
            Require(id > previousId && row.Slice(28, 4).IndexOfAnyExcept((byte)0) < 0
                && corners is >= 3 and <= 65000, "Invalid source geometry entry identity or count.");
            previousId = id;
            var layout = layouts[i] = Native(nativeModel, model, id, material);
            Require(corners == layout.Corners && U32(row, 12) == layout.Triangles && U32(row, 16) == layout.Quads,
                "Source geometry primitive counts changed.");
            totalCorners = checked(totalCorners + corners);
            Require(totalCorners <= MaxCorners, "Source geometry aggregate corner budget exceeded.");
            int length = corners * 16;
            Require(U32(row, 20) == next && U32(row, 24) == length && (long)payloadOffset + next + length <= bytes.Length,
                "Source geometry payload has gaps, overlap or truncation.");
            Require(row[32..64].SequenceEqual(SHA256.HashData(nativeModel.Slice((int)layout.List.Offset, (int)layout.List.Size))),
                "Source geometry native display-list integrity failed.");
            var payload = bytes.Slice(payloadOffset + next, length);
            Require(row[64..96].SequenceEqual(SHA256.HashData(payload)), "Source geometry coordinate integrity failed.");
            for (int c = 0; c < corners; c++) Require(Finite(Corner(payload.Slice(c * 16, 16))), "Non-finite source coordinate.");
            next += length;
        }
        Require((long)payloadOffset + next == bytes.Length, "Unexpected source geometry trailing data.");
        var entries = new Entry[count]; next = 0;
        for (int i = 0; i < count; i++)
        {
            var row = directory.Slice(i * EntryBytes, EntryBytes); var layout = layouts[i];
            var corners = new EchoesSourceCoordinates[layout.Corners];
            for (int c = 0; c < corners.Length; c++) corners[c] = Corner(bytes.Slice(payloadOffset + next + c * 16, 16));
            entries[i] = new(Int(row, 0), layout.Material, layout.Triangles, layout.Quads, corners);
            next += corners.Length * 16;
        }
        return new(mapId, recipeSha256.ToLowerInvariant(), Convert.ToHexStringLower(header[80..112]),
            Convert.ToHexStringLower(SHA256.HashData(bytes)), entries);
    }

    private sealed record ModelDirectory(Header Header, IReadOnlyDictionary<int, (int Material, int References)> Bindings, IReadOnlyList<DisplayList> Lists);
    private static ModelDirectory NativeDirectory(ReadOnlySpan<byte> bytes)
    {
        Require(bytes.Length >= Sizes.Header && bytes.Length <= MaxNativeModelBytes, "Invalid native model extent.");
        var h = Read.ReadStruct<Header>(bytes[..Sizes.Header]);
        Require(h.MeshCount > 0 && h.MaterialCount > 0 && h.DlistOffset >= Sizes.Header && h.MeshOffset >= Sizes.Header
            && (long)h.DlistOffset + h.MeshCount * Sizes.Dlist <= bytes.Length
            && (long)h.MeshOffset + h.MeshCount * Sizes.Mesh <= bytes.Length, "Invalid native geometry tables.");
        var bindings = new Dictionary<int, (int Material, int References)>();
        foreach (var mesh in Read.DoOffsets<RawMesh>(bytes, h.MeshOffset, h.MeshCount))
        {
            if (bindings.TryGetValue(mesh.DlistId, out var old))
                bindings[mesh.DlistId] = (-1, old.References + 1);
            else bindings.Add(mesh.DlistId, (mesh.MaterialId, 1));
        }
        return new(h, bindings, Read.DoOffsets<DisplayList>(bytes, h.DlistOffset, h.MeshCount));
    }
    private static NativeList Native(ReadOnlySpan<byte> bytes, ModelDirectory model, int id, int material)
    {
        Require(id >= 0 && id < model.Lists.Count && material >= 0 && material < model.Header.MaterialCount,
            "Source geometry references missing native geometry.");
        // Generated static maps have one mesh per list. Refuse aliases or retargeting.
        Require(model.Bindings.TryGetValue(id, out var binding) && binding.References == 1 && binding.Material == material,
            "Source geometry requires one unambiguous native mesh with the captured material.");
        DisplayList list = model.Lists[id];
        Require(list.Size is > 0 and <= 4 * 1024 * 1024, "Source native command budget exceeded.");
        int corners = 0, triangles = 0, quads = 0, inPrimitive = -1, groupCorners = 0, phase = 0, pending = 0;
        bool sawQuad = false;
        try
        {
            Read.VisitRenderInstructions(bytes, list, (code, args) =>
            {
                switch (code)
                {
                    case InstructionCode.BEGIN_VTXS:
                        Require(inPrimitive == -1 && args[0] <= 1 && !(sawQuad && args[0] == 0), "Invalid static source primitive order.");
                        inPrimitive = (int)args[0]; sawQuad |= inPrimitive == 1; groupCorners = 0; phase = 0; break;
                    case InstructionCode.COLOR:
                        Require(inPrimitive >= 0 && phase == 0 && pending == 0 && groupCorners % (inPrimitive == 0 ? 3 : 4) == 0,
                            "Invalid source face-color boundary."); phase = 1; break;
                    case InstructionCode.NORMAL:
                        Require(phase == 1 && pending == 0, "Invalid source face-normal boundary."); phase = 2; break;
                    case InstructionCode.TEXCOORD:
                        Require(inPrimitive >= 0 && phase == 2 && pending == 0 && args[0] == 0,
                            "Source transport requires zero native fallback UVs."); pending = 1; break;
                    case InstructionCode.VTX_16:
                        Require(inPrimitive >= 0 && phase == 2 && pending == 1, "Invalid source vertex emission.");
                        pending = 0; corners++; groupCorners++;
                        if (groupCorners % (inPrimitive == 0 ? 3 : 4) == 0) phase = 0;
                        Require(corners <= 65000, "Source display-list corner budget exceeded."); break;
                    case InstructionCode.END_VTXS:
                        Require(inPrimitive >= 0 && phase == 0 && pending == 0 && groupCorners > 0
                            && groupCorners % (inPrimitive == 0 ? 3 : 4) == 0, "Incomplete source primitive.");
                        if (inPrimitive == 0) triangles += groupCorners / 3; else quads += groupCorners / 4;
                        inPrimitive = -1; phase = 0; break;
                    case InstructionCode.NOP:
                        Require(inPrimitive == -1, "NOP inside source primitive."); break;
                    default: throw new InvalidDataException("Unsupported static source command.");
                }
            });
        }
        catch (ProgramException ex) { throw new InvalidDataException("Invalid bounded source display-list grammar.", ex); }
        Require(inPrimitive == -1 && pending == 0 && corners > 0, "Incomplete native source geometry.");
        return new(material, list, corners, triangles, quads);
    }
    private static EchoesSourceCoordinates Corner(ReadOnlySpan<byte> b) => new(
        BinaryPrimitives.ReadSingleLittleEndian(b), BinaryPrimitives.ReadSingleLittleEndian(b[4..]),
        BinaryPrimitives.ReadSingleLittleEndian(b[8..]), BinaryPrimitives.ReadSingleLittleEndian(b[12..]));
    private static bool Finite(EchoesSourceCoordinates uv) => float.IsFinite(uv.BaseU) && float.IsFinite(uv.BaseV)
        && float.IsFinite(uv.LightmapU) && float.IsFinite(uv.LightmapV);
    private static void Identity(Guid map, string hash) => Require(map != Guid.Empty && hash?.Length == 64
        && hash.All(Uri.IsHexDigit), "Invalid source geometry map or recipe identity.");
    private static uint U32(ReadOnlySpan<byte> b, int at) => BinaryPrimitives.ReadUInt32LittleEndian(b[at..]);
    private static int Int(ReadOnlySpan<byte> b, int at)
    { uint value = U32(b, at); Require(value <= int.MaxValue, "Source geometry integer overflow."); return (int)value; }
    private static void Put(Span<byte> b, int at, int value) => BinaryPrimitives.WriteInt32LittleEndian(b[at..], value);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
}
