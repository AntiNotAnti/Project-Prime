using System;
using System.Collections.Generic;
using System.IO;

namespace MphRead.Mods.Import.Retro;

/// <summary>
/// Retro CSKR skin-group decoder, adapted from Second Hunt's MIT reader with
/// explicit group/weight/vertex budgets. No extracted data is persisted here.
/// </summary>
public static class Cskr
{
    public const int MaxGroups = 65536;
    public const int MaxWeightsPerGroup = 64;
    public const int MaxVertices = 2_000_000;

    public static List<(uint Bone, float Weight)[]> Load(byte[] source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var per = new List<(uint, float)[]>();
        int at = 0;
        int groupCount = checked((int)Be.U32(source, at)); at += 4;
        if (groupCount < 0 || groupCount > MaxGroups)
            throw new InvalidDataException("CSKR group count exceeds budget.");
        for (int group = 0; group < groupCount; group++)
        {
            int count = checked((int)Be.U32(source, at)); at += 4;
            if (count < 0 || count > MaxWeightsPerGroup)
                throw new InvalidDataException("CSKR bone weight count exceeds budget.");
            var weights = new (uint Bone, float Weight)[count];
            for (int i = 0; i < count; i++)
            {
                float weight = Be.F32(source, at + 8 * i + 4);
                if (!float.IsFinite(weight) || weight < 0 || weight > 1f)
                    throw new InvalidDataException("CSKR bone weight is invalid.");
                weights[i] = (Be.U32(source, at + 8 * i), weight);
            }
            at = checked(at + 8 * count);
            int vertices = checked((int)Be.U32(source, at)); at += 4;
            if (vertices < 0 || vertices > MaxVertices - per.Count)
                throw new InvalidDataException("CSKR vertex count exceeds budget.");
            for (int i = 0; i < vertices; i++) per.Add(weights);
        }
        return per;
    }
}
