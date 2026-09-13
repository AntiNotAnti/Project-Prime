using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using MphRead.Mods;

namespace MphRead.Cosmetics.Presentation;

/// <summary>
/// Bounded loader for official cooked death clips. Authored asset keys map to
/// fixed embedded resources; runtime input can never become a file path.
/// </summary>
internal static class DeathAnimationCatalog
{
    internal const string SamusBackwardCollapseAsset
        = "death-animations/samus-backward-collapse.pda";
    private const string SamusBackwardCollapseResource
        = "MphRead.Cosmetics.DeathAnimations.samus-backward-collapse.pda";
    private static readonly object Sync = new();
    private static DeathAnimationClip? _samusBackwardCollapse;
    private static bool _loadAttempted;

    internal static bool TryResolve(DeathEffectDefinition effect, Hunter hunter,
        IReadOnlyList<Node> nodes, out DeathAnimationClip? clip)
    {
        ArgumentNullException.ThrowIfNull(effect);
        ArgumentNullException.ThrowIfNull(nodes);
        clip = null;
        if (!String.Equals(effect.Animation, SamusBackwardCollapseAsset,
                StringComparison.Ordinal) || hunter != Hunter.Samus)
            return false;

        byte[] signature;
        try { signature = DeathAnimationSkeleton.ComputeSignature(hunter, nodes); }
        catch (ArgumentException) { return false; }
        DeathAnimationClip? loaded = LoadSamusBackwardCollapse();
        if (loaded == null) return false;
        if (!loaded.Matches(hunter, signature))
        {
            DebugLog.Line("cosmetics/death",
                "Rejected Samus backward-collapse clip for a mismatched skeleton.");
            return false;
        }
        clip = loaded;
        return true;
    }

    private static DeathAnimationClip? LoadSamusBackwardCollapse()
    {
        lock (Sync)
        {
            if (_loadAttempted) return _samusBackwardCollapse;
            _loadAttempted = true;
            try
            {
                using Stream? stream = typeof(DeathAnimationCatalog).Assembly
                    .GetManifestResourceStream(SamusBackwardCollapseResource);
                if (stream == null || stream.Length is <= 0
                    or > DeathAnimationCodec.MaximumCookedBytes)
                    throw new InvalidDataException("Cooked death animation resource is missing or invalid.");
                byte[] bytes = new byte[checked((int)stream.Length)];
                stream.ReadExactly(bytes);
                if (!DeathAnimationCodec.TryRead(bytes,
                        DeathAnimationCodec.MaximumTracks,
                        out _samusBackwardCollapse))
                    throw new InvalidDataException("Cooked death animation failed strict validation.");
                return _samusBackwardCollapse;
            }
            catch (Exception error) when (error is IOException
                or InvalidDataException or NotSupportedException)
            {
                DebugLog.Line("cosmetics/death",
                    $"Could not load Samus backward-collapse clip ({error.GetType().Name}).");
                return null;
            }
        }
    }
}
