using System.Collections.Generic;
using MphRead.Identity;
using MphRead.Mods.Accounts;
using MphRead.Mods.Network;

namespace MphRead.Mods.Launcher.Gui;

/// <summary>
/// Synthetic state supplied only by the offline capture command/tests.
/// Production shell construction never passes this object, so the normal
/// account, Node, platform, and preview paths remain the source of live state.
/// Snapshot payloads are still the same immutable DTOs consumed by the
/// production presentations.
///
/// This type is kept in the shared launcher sources rather than UiCapture.cs:
/// the Android launcher uses the capture-aware shell constructor but must not
/// carry the desktop capture runner and its bitmap dependencies.
/// </summary>
internal sealed record PrimeShellCaptureState(
    GatewayState? Gateway = null,
    PlayerId? PendingConfirmationPlayerId = null,
    PlayState? Play = null,
    PlaySubsection? PlaySubsection = null,
    bool ExpandAdvancedNetwork = false,
    HunterLicensePageState? License = null,
    IReadOnlyList<HunterDossier>? Hunters = null,
    HunterSection? HunterSection = null,
    bool HunterPreviewFailure = false,
    PrimeShellCaptureIdentity Identity = PrimeShellCaptureIdentity.None);

internal enum PrimeShellCaptureIdentity
{
    None,
    SignedIn,
    Guest
}
