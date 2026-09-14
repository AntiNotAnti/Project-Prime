using System;
using System.Collections.Generic;
using System.IO;
using MphRead.Mods;

namespace MphRead.Cosmetics.Presentation;

/// <summary>
/// Bounded loader for official cooked death clips. Published effect/Hunter
/// pairs map to fixed embedded resources; runtime input can never become a
/// path and clips are accepted only against the exact live skeleton.
/// </summary>
internal static class DeathAnimationCatalog
{
    internal const string SamusBackwardCollapseAsset
        = "death-animations/samus-backward-collapse.pda";
    internal const string GlobalBackwardCollapseAsset
        = "death-animations/backward-collapse.pda";
    private const string ResourcePrefix
        = "MphRead.Cosmetics.DeathAnimations.";
    private static readonly object Sync = new();
    private static readonly DeathAnimationClip?[] Clips = new DeathAnimationClip?[8];
    private static readonly bool[] LoadAttempted = new bool[8];

    internal static bool TryResolve(DeathEffectDefinition effect, Hunter hunter,
        IReadOnlyList<Node> nodes, out DeathAnimationClip? clip)
    {
        ArgumentNullException.ThrowIfNull(effect);
        ArgumentNullException.ThrowIfNull(nodes);
        clip = null;
        if (!TryGetResource(effect, hunter, out string? resource)) return false;

        byte[] signature;
        try { signature = DeathAnimationSkeleton.ComputeSignature(hunter, nodes); }
        catch (ArgumentException) { return false; }
        DeathAnimationClip? loaded = Load(hunter, resource!);
        if (loaded == null) return false;
        if (!loaded.Matches(hunter, signature))
        {
            DebugLog.Line("cosmetics/death",
                $"Rejected backward-collapse clip for {hunter}: skeleton mismatch.");
            return false;
        }
        clip = loaded;
        return true;
    }

    private static bool TryGetResource(DeathEffectDefinition effect,
        Hunter hunter, out string? resource)
    {
        resource = null;
        if (hunter is < Hunter.Samus or > Hunter.Weavel) return false;
        if (effect.Id == BuiltInCosmeticIds.DeathSamusBackwardCollapse)
        {
            if (hunter != Hunter.Samus
                || !String.Equals(effect.Animation, SamusBackwardCollapseAsset,
                    StringComparison.Ordinal)) return false;
        }
        else if (effect.Id != BuiltInCosmeticIds.DeathBackwardCollapse
            || !String.Equals(effect.Animation, GlobalBackwardCollapseAsset,
                StringComparison.Ordinal)) return false;

        string hunterName = hunter.ToString().ToLowerInvariant();
        resource = $"{ResourcePrefix}{hunterName}-backward-collapse.pda";
        return true;
    }

    private static DeathAnimationClip? Load(Hunter hunter, string resource)
    {
        int index = (int)hunter;
        lock (Sync)
        {
            if (LoadAttempted[index]) return Clips[index];
            LoadAttempted[index] = true;
            try
            {
                using Stream? stream = typeof(DeathAnimationCatalog).Assembly
                    .GetManifestResourceStream(resource);
                if (stream == null || stream.Length is <= 0
                    or > DeathAnimationCodec.MaximumCookedBytes)
                    throw new InvalidDataException(
                        "Cooked death animation resource is missing or invalid.");
                byte[] bytes = new byte[checked((int)stream.Length)];
                stream.ReadExactly(bytes);
                if (!DeathAnimationCodec.TryRead(bytes,
                        DeathAnimationCodec.MaximumTracks, out DeathAnimationClip? clip)
                    || clip == null || clip.Hunter != hunter)
                    throw new InvalidDataException(
                        "Cooked death animation failed strict validation.");
                Clips[index] = clip;
                return clip;
            }
            catch (Exception error) when (error is IOException
                or InvalidDataException or NotSupportedException)
            {
                DebugLog.Line("cosmetics/death",
                    $"Could not load {hunter} backward-collapse clip ({error.GetType().Name}).");
                return null;
            }
        }
    }
}
