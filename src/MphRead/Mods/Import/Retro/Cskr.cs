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
        uint rawGroups = ReadU32(source, at); at += 4;
        if (rawGroups > MaxGroups)
            throw new InvalidDataException("CSKR group count exceeds budget.");
        int groups = (int)rawGroups;
        for (int group = 0; group < groups; group++)
        {
            uint rawWeights = ReadU32(source, at); at += 4;
            if (rawWeights > MaxWeightsPerGroup)
                throw new InvalidDataException("CSKR bone weight count exceeds budget.");
            int count = (int)rawWeights;
            if ((long)count * 8 > source.Length - at)
                throw new InvalidDataException("CSKR weights extend beyond the local source.");
            var weights = new (uint Bone, float Weight)[count];
            for (int i = 0; i < count; i++)
            {
                int position = at + 8 * i;
                float weight = Be.F32(source, position + 4);
                if (!float.IsFinite(weight) || weight < 0 || weight > 1f)
                    throw new InvalidDataException("CSKR bone weight is invalid.");
                weights[i] = (Be.U32(source, position), weight);
            }
            at += 8 * count;
            uint rawVertices = ReadU32(source, at); at += 4;
            if (rawVertices > MaxVertices - per.Count)
                throw new InvalidDataException("CSKR vertex count exceeds budget.");
            int vertices = (int)rawVertices;
            for (int i = 0; i < vertices; i++) per.Add(weights);
        }
        return per;
    }

    private static uint ReadU32(byte[] source, int at)
    {
        if (at < 0 || source.Length - at < 4)
            throw new InvalidDataException("Truncated CSKR bone/weight source.");
        return Be.U32(source, at);
    }
}
