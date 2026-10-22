using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace MphRead.Mods.Render.Characters;

/// <summary>Share encoded image bytes across LODs/forms without owning their lifetime.</summary>
internal static class CharacterEmbeddedImages
{
    private static readonly object Gate=new();
    private static readonly Dictionary<string,WeakReference<byte[]>> Images=new(StringComparer.Ordinal);
    internal static byte[] Intern(ReadOnlySpan<byte> payload)
    {
        string key=Convert.ToHexString(SHA256.HashData(payload));
        lock (Gate)
        {
            if (Images.TryGetValue(key,out var weak) && weak.TryGetTarget(out byte[]? cached)) return cached;
            byte[] image=payload.ToArray();
            if (Images.Count>=1024)
            {
                var dead=new List<string>();
                foreach (var pair in Images) if (!pair.Value.TryGetTarget(out _)) dead.Add(pair.Key);
                foreach (string name in dead) Images.Remove(name);
            }
            if (Images.Count<1024 || Images.ContainsKey(key)) Images[key]=new(image);
            return image;
        }
    }
}
