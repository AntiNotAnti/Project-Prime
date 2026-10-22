#if !MPHREAD_SERVER
using System.Collections.Generic;

namespace MphRead.Mods.Launcher.Core;

public readonly record struct NativeCampaignShipDock(byte Slot, int Planet);
public readonly record struct NativeCampaignShipDestination(int Planet, string Name, bool Unlocked,
    bool Visited, bool Cleared, int Octoliths, int OctolithTotal, int Artifacts, int ArtifactTotal);
public sealed record NativeCampaignShipRead(string Fingerprint, NativeCampaignShipDestination[] Destinations, string Error = "");
public readonly record struct NativeCampaignShipWitness(NativeCampaignShipDock Dock, string Fingerprint, int Hunter);
public readonly record struct NativeCampaignShipRequest(NativeCampaignShipWitness Witness, int Destination, bool ExitShipToSurface);
public readonly record struct NativeCampaignShipEffect<TLaunch>(bool Launch, TLaunch Plan, NativeCampaignShipDock Dock)
{
    public string Fingerprint { get; init; } = "";
}
public sealed record NativeCampaignShipSnapshot(long Revision, NativeCampaignShipDock Dock, int Hunter,
    bool StarMap, int Selected, IReadOnlyList<NativeCampaignShipDestination> Destinations,
    bool Verified, bool Closed, string Error);

/// <summary>Only the adapter reads saves or creates an existing launcher plan.</summary>
public interface INativeCampaignShipBackend<TLaunch>
{
    NativeCampaignShipRead Read(NativeCampaignShipDock dock);
    bool TryCreateLaunch(NativeCampaignShipRequest request, out TLaunch plan, out string error);
}
#endif
