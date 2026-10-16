using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MphRead.Mods.Import.Retro;

namespace MphRead.Mods.Import;

/// <summary>
/// Local-only exact CSKR skin weight sidecar for a Retro CMDL geometry preview.
/// CSKR repeats influence groups across source positions. Store that repetition
/// as group IDs rather than expanding millions of identical weight arrays,
/// preserving exact original bone IDs/weights for later offline rig transfer.
/// Does not mutate the native MPH rig or a network/replay player.
/// </summary>
public static class RetroCharacterSkinWeights
{
    public const int MaxVertices = 2_000_000;
    public const int MaxGroups = 65_536;
    public const int MaxInfluencesPerGroup = 64;

    public static void Write(Cmdl model, Stream output)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(output);
        if (!output.CanWrite)
            throw new ArgumentException("Skin weight sidecar requires a writable local stream.", nameof(output));
        if (model.Skin is not { } skin ||
            model.PositionCount is < 3 or > MaxVertices ||
            skin.Count != model.PositionCount)
            throw new InvalidDataException("Source model has no complete CSKR skin-weight data.");

        var byReference = new Dictionary<(uint Bone, float Weight)[], int>(
            ReferenceEqualityComparer.Instance);
        var groups = new List<(uint Bone, float Weight)[]>();
        int[] indices = new int[skin.Count];
        for (int vertex = 0; vertex < skin.Count; vertex++)
        {
            (uint Bone, float Weight)[] weights = skin[vertex];
            if (weights == null || weights.Length is < 1 or > MaxInfluencesPerGroup)
                throw new InvalidDataException("Source CSKR group has no valid bone influences.");
            if (!byReference.TryGetValue(weights, out int group))
            {
                if (groups.Count >= MaxGroups)
                    throw new InvalidDataException("Source CSKR contains too many unique weight groups.");
                double total = 0;
                foreach ((uint _, float weight) in weights)
                {
                    if (!float.IsFinite(weight) || weight < 0 || weight > 1)
                        throw new InvalidDataException("Source CSKR contains an invalid vertex weight.");
                    total += weight;
                }
                if (total <= 0 || !double.IsFinite(total))
                    throw new InvalidDataException("Source CSKR influence group has zero total weight.");
                group = groups.Count;
                groups.Add(weights);
                byReference.Add(weights, group);
            }
            indices[vertex] = group;
        }

        // UTF-8 streaming avoids a second huge in-memory serialization of
        // every source vertex; group identity is stable in original source
        // position order, not dictionary enumeration order.
        using var json = new Utf8JsonWriter(output);
        json.WriteStartObject();
        json.WriteNumber("format", 1);
        json.WriteString("origin", "local-owned-retro-cskr");
        json.WriteNumber("vertexCount", skin.Count);
        json.WriteStartArray("groups");
        foreach ((uint Bone, float Weight)[] weights in groups)
        {
            json.WriteStartArray();
            foreach ((uint bone, float weight) in weights)
            {
                json.WriteStartObject();
                json.WriteNumber("boneId", bone);
                json.WriteNumber("weight", weight);
                json.WriteEndObject();
            }
            json.WriteEndArray();
        }
        json.WriteEndArray();
        json.WriteStartArray("vertexGroupIds");
        foreach (int group in indices) json.WriteNumberValue(group);
        json.WriteEndArray();
        json.WriteEndObject();
    }
}
