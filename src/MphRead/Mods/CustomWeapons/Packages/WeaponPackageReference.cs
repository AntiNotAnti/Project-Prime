using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MphRead.Mods.CustomWeapons.Packages;

/// <summary>Exports original bundled assets and the existing registry definition, without game data or devices.</summary>
public static class WeaponPackageReference
{
    private const string Project = """
        {"schema":1,"contentId":"9a776851-f39f-4a76-8c13-c5d88875ef61","productId":"prime.riftline-m7","name":"Riftline M7","author":"Project Prime contributors","license":{"spdx":"CC0-1.0","path":"LICENSE.txt"},"readme":"README.txt","mechanics":"mechanics.json","presentation":{"schema":1,"mount":"native-arm-module-v1","model":{"format":"prime-rigid-boxes-v1","path":"model/model-v1.json"},"fallback":"builtin-original-module-v1","audio":{"format":"pcm16-mono-22050-v1","shot":"audio/shot.wav","bolt":"audio/bolt.wav","reload":"audio/reload.wav","empty":"audio/empty.wav"}},"dependencies":"dependencies.lock.json"}
        """;
    public static byte[] BuildRiftlineM7()
    {
        var project = WeaponProject.Parse(Encoding.UTF8.GetBytes(Project));
        var resources = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["mechanics.json"] = RiftlineM7.Create().Mechanics.ToCanonicalBytes(),
            [WeaponProject.LockPath] = (byte[])WeaponProject.EmptyLock.Clone(),
            ["model/model-v1.json"] = ReadResource("model-v1.json", CustomRifleModel.MaximumBytes),
            ["LICENSE.txt"] = ReadResource("LICENSE.txt", WeaponPackageLimits.MaximumTextBytes),
            ["README.txt"] = ReadResource("README.md", WeaponPackageLimits.MaximumTextBytes)
        };
        if (WeaponPackage.Hash(resources["model/model-v1.json"]) != CustomRifleModel.Digest) throw WeaponPackageJson.Bad("Bundled model digest mismatch.");
        string[] names = { "shot", "bolt", "reload", "empty" };
        for (int i = 0; i < names.Length; i++) resources["audio/" + names[i] + ".wav"] = CustomRifleCues.BuildWave((CustomRifleCue)i);
        return WeaponPackageBuilder.Build(project, resources);
    }
    private static byte[] ReadResource(string name, int maximum)
    {
        using var stream = typeof(WeaponPackageReference).Assembly.GetManifestResourceStream("Weapons/RiftlineM7/" + name)
            ?? throw WeaponPackageJson.Bad("Missing bundled reference resource.");
        if (stream.Length is <= 0 || stream.Length > maximum) throw WeaponPackageJson.Bad("Reference resource budget.");
        var bytes = new byte[(int)stream.Length]; stream.ReadExactly(bytes); return bytes;
    }
}
