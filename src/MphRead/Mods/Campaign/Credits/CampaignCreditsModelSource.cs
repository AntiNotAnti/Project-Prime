using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace MphRead.Mods.Campaign;

/// <summary>Closed, bounded native model profile for credits widgets and their font atlas.
/// Admission owns the exact bytes; it does not load files, decode a Model or create GPU resources.</summary>
public static class CampaignCreditsModelSource
{
    public const int MaxInputBytes = 4 * 1024 * 1024, MaxNodes = 64, MaxMaterials = 64,
        MaxMeshes = 128, MaxTextures = 64, MaxMatrices = 31, MaxPixels = 262144,
        MaxVertices = 65536, MaxTriangles = 32768, MaxCommands = 32768, MaxNodeDepth = 16;

    public sealed class Snapshot
    {
        private readonly byte[] _bytes;
        private readonly HashSet<string> _materials;
        public string SourceSha256 { get; }
        public int ByteLength => _bytes.Length;
        public int NodeCount { get; }
        public int MaterialCount { get; }
        public int MeshCount { get; }
        public int TextureCount { get; }
        public int MatrixCount { get; }
        public int PixelCount { get; }
        public int VertexCount { get; }
        public int TriangleCount { get; }
        public int MaximumNodeDepth { get; }
        internal ReadOnlyMemory<byte> Memory => _bytes;
        internal bool HasMaterial(string name) => _materials.Contains(name);
        internal Snapshot(byte[] bytes, HashSet<string> materials, int nodes, int meshes, int textures,
            int matrices, int pixels, int vertices, int triangles, int depth)
        {
            _bytes = bytes; _materials = materials; SourceSha256 = Hash(bytes);
            NodeCount = nodes; MaterialCount = materials.Count; MeshCount = meshes; TextureCount = textures;
            MatrixCount = matrices; PixelCount = pixels; VertexCount = vertices; TriangleCount = triangles; MaximumNodeDepth = depth;
        }
    }

    public static Snapshot Read(ReadOnlySpan<byte> source)
    {
        Require(source.Length is >= 100 and <= MaxInputBytes, "Credits model extent is unsupported.");
        byte[] owned = source.ToArray(); // Validate and later decode the SAME detached bytes.
        ReadOnlySpan<byte> b = owned;
        Require(U32(b, 0) <= 12 && I32(b, 4) == 4096 && b[30] == 0, "Credits model scale or flags are unsupported.");
        foreach (int pointer in new[] { 60, 64, 68, 76, 80, 84, 88, 92 })
            Require(U32(b, pointer) == 0, "Credits skin/runtime pointer graphs are unsupported.");
        // The source can declare one unused texture matrix; TexgenNone/MatrixId0 below never dereferences it.
        Require(U16(b, 98) <= 1, "Credits texture matrix metadata is unsupported.");
        int nodeCount = Count(U16(b, 74), MaxNodes), materialCount = Count(U16(b, 72), MaxMaterials),
            meshCount = Count(U16(b, 96), MaxMeshes), textureCount = Count(U16(b, 40), MaxTextures),
            paletteCount = Count(U16(b, 48), MaxTextures), matrixCount = Count(U16(b, 28), MaxMatrices);
        var nodes = Table(b, 24, nodeCount, 240); var materials = Table(b, 16, materialCount, 132);
        var meshes = Table(b, 36, meshCount, 4); var dlists = Table(b, 20, meshCount, 32);
        var textures = Table(b, 44, textureCount, 40); var palettes = Table(b, 52, paletteCount, 16);
        var matrices = Table(b, 32, matrixCount, 4); var posCounts = Table(b, 56, matrixCount, 4);
        for (int i = 0; i < matrixCount; i++)
            Require(U32(matrices, i * 4) < nodeCount && U32(posCounts, i * 4) == 1,
                "Credits matrix table requires one existing node per entry.");
        var nodeNames = new HashSet<string>(StringComparer.Ordinal);
        var parent = new int[nodeCount]; var child = new int[nodeCount]; var next = new int[nodeCount];
        var meshOwners = new int[meshCount];
        for (int i = 0; i < nodeCount; i++)
        {
            var n = nodes.Slice(i * 240, 240);
            Require(nodeNames.Add(Name(n)), "Credits node names must be unique.");
            parent[i] = Index(I16(n, 64), nodeCount); child[i] = Index(I16(n, 66), nodeCount); next[i] = Index(I16(n, 68), nodeCount);
            Require(U32(n, 72) == 1 && n[140] == 0, "Credits node visibility/billboard profile is unsupported.");
            for (int axis = 0; axis < 3; axis++)
            {
                int value = I32(n, 80 + axis * 4);
                Require(value != 0 && value is >= -65536 and <= 65536, "Credits static node scale is unsupported.");
            }
            int meshId = U16(n, 78), count = U16(n, 76);
            Require(meshId % 2 == 0 && meshId / 2 + count <= meshCount, "Credits node mesh span is invalid.");
            for (int m = meshId / 2; m < meshId / 2 + count; m++)
                Require(++meshOwners[m] == 1, "Credits mesh has multiple node owners.");
        }
        Require(parent[0] == -1 && next[0] == -1, "Credits root graph is invalid.");
        var visited = new bool[nodeCount]; int maximumDepth = 0, visits = 0;
        Walk(0, -1, 1);
        Require(visits == nodeCount, "Credits node graph contains unreachable nodes.");
        foreach (int owners in meshOwners) Require(owners == 1, "Credits mesh has no node owner.");
        void Walk(int index, int expectedParent, int depth)
        {
            Require(depth <= MaxNodeDepth, "Credits node graph exceeds its depth bound.");
            while (index != -1)
            {
                Require(!visited[index] && parent[index] == expectedParent, "Credits node graph is cyclic or inconsistent.");
                visited[index] = true; visits++; maximumDepth = Math.Max(maximumDepth, depth);
                if (child[index] != -1) Walk(child[index], index, depth + 1);
                index = next[index];
            }
        }
        int pixels = 0;
        for (int i = 0; i < textureCount; i++)
        {
            var t = textures.Slice(i * 40, 40); int format = t[0], width = U16(t, 2), height = U16(t, 4);
            Require(format is 0 or 1 or 2 or 6 && Dimension(width) && Dimension(height), "Credits texture encoding or dimensions are unsupported.");
            int count = width * height;
            pixels += count; Require(pixels <= MaxPixels, "Credits model exceeds its pixel bound.");
            int length = count / (format == 0 ? 4 : format == 1 ? 2 : 1);
            Require(U32(t, 12) == length, "Credits encoded image length differs from its dimensions.");
            Data(b, U32(t, 8), (uint)length);
        }
        for (int i = 0; i < paletteCount; i++)
        {
            var p = palettes.Slice(i * 16, 16); uint length = U32(p, 4);
            Require(length is >= 2 and <= 512 && length % 2 == 0, "Credits palette size is unsupported.");
            Data(b, U32(p, 0), length);
        }
        var names = new HashSet<string>(StringComparer.Ordinal);
        var checkedPairs = new HashSet<(int, int)>();
        for (int i = 0; i < materialCount; i++)
        {
            var m = materials.Slice(i * 132, 132);
            Require(names.Add(Name(m)), "Credits material names must be unique.");
            Require(m[64] == 1 && m[65] == 2 && m[66] <= 31 && m[67] == 0 && m[72] <= 1 && m[73] <= 1
                && U32(m, 84) == 0 && m[88] == 0 && m[89] == 0 && U32(m, 92) == 0 && U16(m, 96) == 0
                && U32(m, 100) == 0 && U16(m, 124) == 0 && U16(m, 126) == 0,
                "Credits material rendering profile is unsupported.");
            for (int c = 74; c < 83; c++) Require(m[c] <= 31, "Credits material color is outside five-bit range.");
            int palette = I16(m, 68), texture = I16(m, 70);
            Require((palette == -1 && texture == -1)
                || (palette >= 0 && palette < paletteCount && texture >= 0 && texture < textureCount),
                "Credits material texture/palette reference is invalid.");
            if (texture >= 0 && checkedPairs.Add((texture, palette)))
            {
                var t = textures.Slice(texture * 40, 40); var p = palettes.Slice(palette * 16, 16);
                var encoded = Data(b, U32(t, 8), U32(t, 12)); uint entries = U32(p, 4) / 2;
                int format = t[0], bits = format == 0 ? 2 : format == 1 ? 4 : format == 6 ? 5 : 8;
                int mask = (1 << bits) - 1;
                foreach (byte value in encoded)
                {
                    Require((value & mask) < entries, "Credits encoded palette index exceeds its bound palette.");
                    if (bits is 2 or 4) for (int shift = bits; shift < 8; shift += bits)
                        Require(((value >> shift) & mask) < entries, "Credits encoded palette index exceeds its bound palette.");
                }
            }
        }
        var listOwners = new bool[meshCount];
        for (int i = 0; i < meshCount; i++)
        {
            int list = U16(meshes, i * 4 + 2);
            Require(U16(meshes, i * 4) < materialCount && list < meshCount,
                "Credits mesh material/display-list reference is invalid.");
            Require(!listOwners[list], "Credits display list has multiple mesh owners.");
            listOwners[list] = true;
        }
        int vertices = 0, triangles = 0, primitives = 0, commands = 0;
        for (int i = 0; i < meshCount; i++)
        {
            var d = dlists.Slice(i * 32, 32); Data(b, U32(d, 0), U32(d, 4));
            int mode = -1, emitted = 0;
            try
            {
                MphRead.Read.VisitRenderInstructions(b, MphRead.Read.ReadStruct<DisplayList>(d), (instruction, arguments) =>
                {
                    Require(++commands <= MaxCommands, "Credits model exceeds its command bound.");
                    uint code = ((uint)instruction - 0x400) / 4;
                    Require(code is 0 or 0x14 or 0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x40 or 0x41,
                        "Credits display-list command is unsupported.");
                    if (code == 0x14) Require(arguments[0] < matrixCount, "Credits matrix restore reference is invalid.");
                    if (code == 0x40)
                    {
                        Require(mode == -1 && arguments[0] is 1 or 3, "Credits primitive mode or nesting is unsupported.");
                        mode = (int)arguments[0]; emitted = 0;
                    }
                    else if (code == 0x41)
                    {
                        Require(mode != -1 && emitted >= 4 && (mode == 1 ? emitted % 4 == 0 : emitted % 2 == 0),
                            "Credits primitive is unbalanced or incomplete.");
                        int quads = mode == 1 ? emitted / 4 : emitted / 2 - 1;
                        primitives += quads; triangles += quads * 2;
                        Require(triangles <= MaxTriangles, "Credits model exceeds its triangle bound."); mode = -1;
                    }
                    else if (code is >= 0x23 and <= 0x26)
                    {
                        Require(mode != -1 && ++vertices <= MaxVertices, "Credits vertex is outside a primitive or exceeds its bound.");
                        emitted++;
                    }
                });
            }
            catch (ProgramException error) { throw new InvalidDataException("Credits display-list extent is invalid.", error); }
            Require(mode == -1, "Credits primitive is unterminated.");
        }
        Require(vertices > 0 && U32(b, 8) == primitives && U32(b, 12) == vertices,
            "Credits declared primitive/vertex counts differ from their bounded command output.");
        return new(owned, names, nodeCount, meshCount, textureCount, matrixCount, pixels, vertices, triangles, maximumDepth);
    }

    private static bool Dimension(int value) => value is >= 8 and <= 512 && (value & (value - 1)) == 0;
    private static int Count(int value, int maximum) { Require(value >= 1 && value <= maximum, "Credits model table count is unsupported."); return value; }
    private static int Index(int value, int count) { Require(value >= -1 && value < count, "Credits node graph index is invalid."); return value; }
    private static ReadOnlySpan<byte> Table(ReadOnlySpan<byte> b, int field, int count, int size) => Data(b, U32(b, field), (ulong)count * (uint)size);
    private static ReadOnlySpan<byte> Data(ReadOnlySpan<byte> b, uint offset, ulong length)
    { Require(offset >= 100 && (ulong)offset + length <= (ulong)b.Length, "Credits model range is outside its data."); return b.Slice((int)offset, (int)length); }
    internal static string Name(ReadOnlySpan<byte> record)
    {
        var span = record[..64]; int length = span.IndexOf((byte)0);
        Require(length > 0, "Credits name requires an in-span terminator.");
        for (int i = 0; i < length; i++) Require(span[i] is >= 32 and <= 126, "Credits name is not printable ASCII.");
        return Encoding.ASCII.GetString(span[..length]);
    }
    internal static string Hash(ReadOnlySpan<byte> b) => Convert.ToHexStringLower(SHA256.HashData(b));
    internal static ushort U16(ReadOnlySpan<byte> b, int at) => BinaryPrimitives.ReadUInt16LittleEndian(b.Slice(at, 2));
    internal static short I16(ReadOnlySpan<byte> b, int at) => BinaryPrimitives.ReadInt16LittleEndian(b.Slice(at, 2));
    internal static uint U32(ReadOnlySpan<byte> b, int at) => BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(at, 4));
    internal static int I32(ReadOnlySpan<byte> b, int at) => BinaryPrimitives.ReadInt32LittleEndian(b.Slice(at, 4));
    internal static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
}
