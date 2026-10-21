using System;
using System.IO;
using System.Threading;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.CustomWeapons.Packages;

namespace MphRead.Mods.Network;

// Worker-prepared historical resources. Preparation never creates a Scene,
// activates gameplay, downloads content or substitutes a current definition.
internal sealed class PreparedReplayWeaponPackage : IDisposable
{
    private readonly object _gate = new();
    private WeaponMatchPackageBinding? _binding;
    internal ReplayCustomWeaponDescriptor Descriptor { get; }
    internal ReplayCustomWeaponContext Context { get; }
    private PreparedReplayWeaponPackage(ReplayCustomWeaponDescriptor descriptor, ReplayCustomWeaponContext context,
        WeaponMatchPackageBinding binding) { Descriptor = descriptor; Context = context; _binding = binding; }
    internal static PreparedReplayWeaponPackage Prepare(ReplayCustomWeaponDescriptor descriptor, WeaponPackageStore store,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor); ArgumentNullException.ThrowIfNull(store);
        cancellationToken.ThrowIfCancellationRequested();
        var metadata = descriptor.InspectContext(); // Invalid metadata fails before store access.
        var binding = descriptor.Bootstrap.PrepareHistorical(store, cancellationToken);
        try
        {
            if (binding.Count != 1) throw new InvalidDataException("Replay requires one exact historical weapon package.");
            var definition = binding.Definition(new WeaponRuntimeId(10));
            var context = descriptor.Bind(definition);
            if (context.Digest != metadata.Digest || !StringComparer.Ordinal.Equals(definition.Name, metadata.Definition.Name)
                || !definition.Mechanics.ToCanonicalBytes().AsSpan().SequenceEqual(metadata.Definition.Mechanics.ToCanonicalBytes()))
                throw new InvalidDataException("Recorded weapon metadata differs from its exact historical package.");
            _ = binding.Resources(0); // All bounded CPU resources are prepared before publication.
            cancellationToken.ThrowIfCancellationRequested();
            return new(descriptor, context, binding);
        }
        catch { binding.Dispose(); throw; }
    }
    internal WeaponMatchPackageBinding Retain()
    {
        lock (_gate) return (_binding ?? throw new ObjectDisposedException(nameof(PreparedReplayWeaponPackage))).Retain();
    }
    internal ReplayReplicaState InspectCheckpoint(ReplayReplicaCheckpoint checkpoint)
    {
        lock (_gate) { if (_binding == null) throw new ObjectDisposedException(nameof(PreparedReplayWeaponPackage)); }
        var state = new ReplayReplicaState(); state.RestoreCustomCheckpoint(checkpoint);
        if (state.CustomWeaponDescriptor == null || !state.CustomWeaponDescriptor.CopyBytes().AsSpan().SequenceEqual(Descriptor.CopyBytes())
            || state.CustomWeapons is not { CompleteForRestore: true } custom || custom.Context.Digest != Context.Digest)
            throw new InvalidDataException("Replay checkpoint differs from the prepared historical weapon scope.");
        return state;
    }
    public void Dispose()
    {
        WeaponMatchPackageBinding? binding;
        lock (_gate) { binding = _binding; _binding = null; }
        binding?.Dispose();
    }
}
