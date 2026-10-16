using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MphRead.Mods.Import.Retro;

namespace MphRead.Mods.Import;

/// <summary>
/// Bounded, source-local animation inventory and sparse pose samples for
/// off-line rig-authoring. No actor state, render pose or network/replay state
/// is affected. Composite meta clips are identified but not silently
/// approximated by a single constituent animation.
/// </summary>
public static class RetroMotionPreview
{
    public const int MaxSampledClips = 12;
    public const int SamplesPerClip = 5;
    public const int MaxAnimationResourceBytes = 16 * 1024 * 1024;
    public const int MaxSampledBones = 256;
    public const int MaxAnimationKeys = 20000;
    public const double MaxDurationSeconds = 120;

    public sealed record BonePose(uint Bone, double[]? Rotation, double[]? Translation);
    public sealed record Pose(double TimeSeconds, IReadOnlyList<BonePose> Bones);
    public sealed record MotionClip(string Name, IReadOnlyList<uint> ResourceIds,
        string Status, double? DurationSeconds, IReadOnlyList<Pose> Samples);
    public sealed record Inventory(int Format, string Origin, int TotalClips,
        int SampledClips, IReadOnlyList<MotionClip> Clips);

    public static Inventory FromLocalSource(byte[] ancs,
        Func<uint, byte[]> resource, bool echoes)
    {
        ArgumentNullException.ThrowIfNull(resource);
        return Build(ancs, id =>
        {
            byte[] raw = resource(id);
            if (raw.Length is < 4 or > MaxAnimationResourceBytes)
                throw new InvalidDataException("Retro ANIM resource is outside preview budget.");
            return Anim.Load(raw, echoes);
        });
    }

    /// <summary>
    /// Pure authoring layer accepts an animation resolver so fixtures can
    /// validate interpolation without bundling proprietary binary streams.
    /// The production entry point above resolves IDs only from a validated
    /// local GameCube disc/PAK.
    /// </summary>
    public static Inventory Build(byte[] ancs, Func<uint, Anim> resolve)
    {
        ArgumentNullException.ThrowIfNull(resolve);
        var descriptors = RetroAnimationIndex.Parse(ancs);
        var clips = new List<MotionClip>(descriptors.Count);
        int sampled = 0;
        foreach (RetroAnimationIndex.Clip entry in descriptors)
        {
            if (entry.AnimationResourceIds.Count != 1)
            {
                clips.Add(new MotionClip(entry.Name, entry.AnimationResourceIds,
                    "composite-needs-blend-graph", null, Array.Empty<Pose>()));
                continue;
            }
            if (sampled >= MaxSampledClips)
            {
                clips.Add(new MotionClip(entry.Name, entry.AnimationResourceIds,
                    "indexed-not-sampled", null, Array.Empty<Pose>()));
                continue;
            }
            try
            {
                Anim anim = resolve(entry.AnimationResourceIds[0]);
                if (!Valid(anim))
                {
                    clips.Add(new MotionClip(entry.Name, entry.AnimationResourceIds,
                        "unsupported-timing-or-channel-budget", null, Array.Empty<Pose>()));
                    continue;
                }
                Pose[] poses = Sample(anim);
                clips.Add(new MotionClip(entry.Name, entry.AnimationResourceIds,
                    "sparse-source-pose-preview", anim.Duration, Array.AsReadOnly(poses)));
                sampled++;
            }
            catch (Exception ex) when (ex is InvalidDataException or OverflowException
                or IndexOutOfRangeException or ArgumentOutOfRangeException
                or KeyNotFoundException or EndOfStreamException)
            {
                // Some upstream clips use unsupported encodings or reference
                // animation data not present in the selected validated PAKs.
                // Preserve the exact source IDs rather than manufacture motion.
                clips.Add(new MotionClip(entry.Name, entry.AnimationResourceIds,
                    "source-animation-unavailable", null, Array.Empty<Pose>()));
            }
        }
        return new Inventory(1, "local-owned-retro-source", descriptors.Count,
            sampled, clips.AsReadOnly());
    }

    private static bool Valid(Anim? anim)
    {
        if (anim == null || !double.IsFinite(anim.Duration) || !double.IsFinite(anim.Tick)
            || anim.Duration <= 0 || anim.Duration > MaxDurationSeconds
            || anim.Tick <= 0 || anim.Keys is < 2 or > MaxAnimationKeys
            || anim.Rot.Count + anim.Trans.Count > MaxSampledBones * 2)
            return false;
        return anim.Rot.Values.All(v => v.Count >= anim.Keys)
            && anim.Trans.Values.All(v => v.Count >= anim.Keys);
    }

    public static Pose[] Sample(Anim anim)
    {
        if (!Valid(anim))
            throw new InvalidDataException("Retro animation timing/channel source is invalid.");
        var samples = new Pose[SamplesPerClip];
        for (int i = 0; i < samples.Length; i++)
        {
            double time = anim.Duration * i / (SamplesPerClip - 1);
            anim.Sample(time, out Dictionary<uint, double[]> rot,
                out Dictionary<uint, double[]> trans);
            uint[] bones = rot.Keys.Concat(trans.Keys).Distinct().OrderBy(id => id).ToArray();
            if (bones.Length > MaxSampledBones)
                throw new InvalidDataException("Retro preview has too many sampled bones.");
            var points = new List<BonePose>(bones.Length);
            foreach (uint bone in bones)
            {
                rot.TryGetValue(bone, out double[]? rotation);
                trans.TryGetValue(bone, out double[]? translation);
                if (rotation != null &&
                    (rotation.Length != 4 || !rotation.All(double.IsFinite)))
                    throw new InvalidDataException("Non-finite or malformed animation quaternion.");
                if (translation != null &&
                    (translation.Length != 3 || !translation.All(double.IsFinite)))
                    throw new InvalidDataException("Non-finite or malformed animation translation.");
                points.Add(new BonePose(bone, rotation, translation));
            }
            samples[i] = new Pose(time, points.AsReadOnly());
        }
        return samples;
    }
}
