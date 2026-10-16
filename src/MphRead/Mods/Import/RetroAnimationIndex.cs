using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MphRead.Mods.Import.Retro;

namespace MphRead.Mods.Import;

/// <summary>
/// Bounded ANCS animation index for later native-rig retargeting. Clip IDs,
/// names, blend/sequence leaves and source hierarchy are local metadata;
/// this type never injects foreign animation into player simulation.
/// </summary>
public static class RetroAnimationIndex
{
    public const int MaxAncsBytes = 32 * 1024 * 1024;
    public const int MaxCharacters = 256;
    public const int MaxClips = 2048;
    public const int MaxLeavesPerClip = 128;
    public sealed record Clip(string Name, IReadOnlyList<uint> AnimationResourceIds);

    public static IReadOnlyList<Clip> Parse(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length < 8 || bytes.Length > MaxAncsBytes)
            throw new InvalidDataException("ANCS animation index size exceeds budget.");
        uint characters = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(4, 4));
        if (characters > MaxCharacters)
            throw new InvalidDataException("ANCS character count exceeds accepted limit.");

        Ancs parsed;
        try
        {
            parsed = Ancs.Parse(bytes);
        }
        catch (Exception ex) when (ex is IndexOutOfRangeException
            or ArgumentOutOfRangeException or OverflowException
            or InvalidDataException or KeyNotFoundException)
        {
            throw new InvalidDataException("Unable to parse bounded source ANCS animation index.", ex);
        }
        if (parsed.Set == null || parsed.Set.Anims.Count > MaxClips)
            throw new InvalidDataException("ANCS source animation set is absent or exceeds budget.");

        var result = new List<Clip>(parsed.Set.Anims.Count);
        foreach ((string name, Ancs.MetaAnim meta) in parsed.Set.Anims)
        {
            if (name.Length is < 1 or > 256)
                throw new InvalidDataException("ANCS animation name exceeds limit.");
            List<Ancs.MetaAnim> leaves = Ancs.Prims(meta);
            if (leaves.Count == 0 || leaves.Count > MaxLeavesPerClip)
                throw new InvalidDataException("ANCS meta animation has no bounded play clips.");
            uint[] resourceIds = leaves.Select(x => x.AnimId).Distinct().ToArray();
            result.Add(new Clip(name, Array.AsReadOnly(resourceIds)));
        }
        return result.AsReadOnly();
    }
}
