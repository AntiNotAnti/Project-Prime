using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MphRead.Formats;
using MphRead.Formats.Collision;

namespace MphRead.Mods.Network;

/// <summary>Asset bounds and gameplay readers, without constructing Scenes or warming every room.</summary>
internal static class ServerAssetPreflight
{
    internal static void SharedPlayerAssets()
    {
        var hunters = Metadata.HunterModels.Values.SelectMany(models => models).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var names = hunters.Concat(new[]
        { "gunSmoke", "nox_ice", "samus_ice", "alt_ice", "doubleDamage_img", "octolith_simple", "trail" });
        var checkedFiles = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        try
        {
            foreach (string name in names.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                ModelMetadata meta = Metadata.GetModelByName(name)
                    ?? throw new InvalidDataException("Unknown required player model: " + name);
                string root = meta.FirstHunt ? Paths.FhFileSystem : Paths.FileSystem;
                CheckModel(Paths.Combine(root, meta.ModelPath), checkedFiles, geometry: hunters.Contains(name));
                CheckAnimation(root, meta.AnimationPath, checkedFiles);
                // Recolors may use raw texture payloads, separate palette models,
                // and missing-alternate fallbacks. Use the gameplay reader's exact
                // contract instead of treating every texture blob as a model header.
                _ = Read.GetModelInstance(name, noCache: true);
            }
        }
        finally { Read.ClearCache(); }
    }

    internal static void Room(RoomMetadata room)
    {
        string modelRoot = room.FirstHunt || room.Hybrid ? Paths.FhFileSystem : Paths.FileSystem;
        var checkedFiles = new HashSet<string>(StringComparer.Ordinal);
        CheckModel(Paths.Combine(modelRoot, room.ModelPath), checkedFiles);
        CheckAnimation(modelRoot, room.AnimationPath, checkedFiles);
        try { _ = Read.PrepareRoomModel(room); }
        finally { Read.ClearCache(); }
        string collision = Paths.Combine(modelRoot, room.CollisionPath);
        using (FileStream stream = File.OpenRead(collision))
        {
            Span<byte> signature = stackalloc byte[4];
            stream.ReadExactly(signature);
            if (!room.FirstHunt && !room.Hybrid && !signature.SequenceEqual("wc01"u8) && !signature.SequenceEqual("wc02"u8))
                throw new InvalidDataException("Invalid collision signature: " + collision);
        }
        _ = Collision.GetCollision(room);
        if (room.EntityPath == null) throw new InvalidDataException("No multiplayer entities for " + room.Name);
        _ = Read.GetEntities(room.EntityPath, -1, room.FirstHunt, allowHook: false);
        if (room.NodePath != null)
            _ = ReadNodeData.ReadData(room.NodePath, room.FirstHunt);
    }

    private static void CheckModel(string path, HashSet<string> checkedFiles, bool geometry = true)
    {
        if (!checkedFiles.Add(path)) return;
        try { CheckModelFile(path, geometry); }
        catch (Exception ex) { throw new InvalidDataException($"{path}: {ex.Message}", ex); }
    }

    internal static void CheckModelFile(string path, bool geometry = true)
    {
        using FileStream stream = File.OpenRead(path);
        Span<byte> bytes = stackalloc byte[100];
        stream.ReadExactly(bytes);
        ValidateModelHeader(bytes, stream.Length, geometry);
    }

    internal static void ValidateModelHeader(ReadOnlySpan<byte> bytes, long length, bool geometry = true)
    {
        if (bytes.Length < Sizes.Header || length < Sizes.Header) throw new InvalidDataException("Truncated model header.");
        Header header = Read.ReadStruct<Header>(bytes);
        if (geometry && (header.NodeCount == 0 || header.MeshCount == 0 || header.MaterialCount == 0))
            throw new InvalidDataException("Model has no geometry or material table.");
        Table(header.NodeOffset, header.NodeCount, Sizes.Node, length);
        Table(header.MeshOffset, header.MeshCount, Sizes.Mesh, length);
        Table(header.DlistOffset, header.MeshCount, Sizes.Dlist, length);
        Table(header.MaterialOffset, header.MaterialCount, Sizes.Material, length);
        Table(header.TextureOffset, header.TextureCount, Sizes.Texture, length);
        Table(header.PaletteOffset, header.PaletteCount, Sizes.Palette, length);
    }

    private static void Table(uint offset, uint count, int size, long length)
    {
        if (count != 0 && (offset < Sizes.Header || (ulong)offset + (ulong)count * (uint)size > (ulong)length))
            throw new InvalidDataException("Model table lies outside its file.");
    }

    private static void CheckAnimation(string root, string? relative, HashSet<string> checkedFiles)
    {
        if (relative == null) return;
        string path = Paths.Combine(root, relative);
        if (!checkedFiles.Add(path)) return;
        using FileStream stream = File.OpenRead(path);
        Span<byte> bytes = stackalloc byte[24];
        stream.ReadExactly(bytes);
        AnimationHeader header = Read.ReadStruct<AnimationHeader>(bytes);
        foreach (uint offset in new[] { header.NodeGroupOffset, header.UnusedGroupOffset,
            header.MaterialGroupOffset, header.TexcoordGroupOffset, header.TextureGroupOffset })
            if (header.Count > 0 && offset != 0 && (offset < Sizes.AnimationHeader
                || (ulong)offset + (ulong)header.Count * sizeof(uint) > (ulong)stream.Length))
                throw new InvalidDataException("Animation table lies outside its file: " + path);
    }
}
