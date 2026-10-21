using System;
using System.Threading;

namespace MphRead.Mods.CustomWeapons.Packages;

// Detached immutable CPU resources. Consumers only read the model and samples;
// GL/audio-device objects remain scene-owned and never enter a worker or packet.
internal sealed class WeaponPackageResources
{
    internal string ArchiveHash { get; }
    internal CustomRifleModel Model { get; }
    private readonly float[][] _waves;
    internal ReadOnlySpan<float> Wave(int cue) => _waves[cue];
    internal float[][] AudioSamples => _waves; // Trusted read-only audio callback inputs.
    private WeaponPackageResources(WeaponPackage package, CustomRifleModel model, float[][] waves)
    { ArchiveHash = package.PackageHash; Model = model; _waves = waves; }
    internal static WeaponPackageResources Prepare(WeaponPackage package, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var model = CustomRifleModel.Decode(package.CopyResource(package.Project.ModelPath));
        var waves = new float[4][];
        for (int cue = 0; cue < 4; cue++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            waves[cue] = WeaponPackage.DecodeWave(package.CopyResource(package.Project.AudioPaths[cue]));
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(package, model, waves);
    }
}
