#if (MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID || PROJECTPRIME_RMLUI_ANDROID_EXPERIMENT) && !MPHREAD_SERVER
using System;
using System.Linq;
using MphRead.Mods.Campaign;
using MphRead.Mods.Launcher.Core;

namespace MphRead.Mods.Launcher.RmlUi.Render;

/// <summary>Existing Adventure owners authorize every departure using a fresh save and exact dock/hash witness.</summary>
internal sealed class CurrentNativeCampaignShipBackend : INativeCampaignShipBackend<LaunchPlan>
{
    private readonly Func<byte, StorySave?> _read;
    private readonly Func<byte, string> _fingerprint;
    private readonly Func<CampaignDockReceipt?> _dock;
    private readonly Func<NativeCampaignShipRequest, LaunchPlan> _create;
    private readonly Func<bool> _ready;

    public CurrentNativeCampaignShipBackend() : this(GameState.PeekSave, AdventureSave.ContentFingerprint,
        () => MatchStart.RecentShipDock, request => AdventureLaunch.Create(request.Witness.Dock.Slot,
            newGame: false, (Hunter)request.Witness.Hunter, (CampaignPlanet)request.Destination, request.ExitShipToSurface),
        () => GameFiles.Ready) { }

    internal CurrentNativeCampaignShipBackend(Func<byte, StorySave?> read, Func<byte, string> fingerprint,
        Func<CampaignDockReceipt?> dock, Func<NativeCampaignShipRequest, LaunchPlan> create, Func<bool> ready)
        => (_read, _fingerprint, _dock, _create, _ready) = (read, fingerprint, dock, create, ready);

    public NativeCampaignShipRead Read(NativeCampaignShipDock expected)
    {
        try { return ReadCore(expected); }
        catch (Exception error) when (error is not OutOfMemoryException
            and not StackOverflowException and not AccessViolationException)
        { return Failure("THE DOCKED SAVE CANNOT BE VERIFIED. REFRESH TO TRY AGAIN."); }
    }

    private NativeCampaignShipRead ReadCore(NativeCampaignShipDock expected)
    {
        if (!ValidDock(expected) || !SameDock(expected)) return Failure("THE GUNSHIP DOCK HAS CHANGED. RETURN TO CAMPAIGN FILE SELECT.");
        string before = _fingerprint(expected.Slot);
        if (!NativeCampaignShipController<LaunchPlan>.ValidFingerprint(before)) return Failure("THE DOCKED SAVE CANNOT BE VERIFIED.");
        StorySave? save = _read(expected.Slot);
        if (save == null) return Failure("THE DOCKED SAVE CANNOT BE LOADED.");
        var rows = CampaignNavigation.StarMap(save).Select(status => new NativeCampaignShipDestination(
            (int)status.Planet.Id, status.Planet.Name, status.Unlocked, status.Visited, status.Cleared,
            status.Octoliths, status.OctolithTotal, status.Artifacts, status.ArtifactTotal)).ToArray();
        string after = _fingerprint(expected.Slot);
        if (!SameDock(expected) || !String.Equals(before, after, StringComparison.Ordinal))
            return Failure("THE DOCKED SAVE HAS CHANGED. REFRESH BEFORE DEPARTURE.");
        return new(after, rows);
    }

    public bool TryCreateLaunch(NativeCampaignShipRequest request, out LaunchPlan plan, out string error)
    {
        try { return TryCreateLaunchCore(request, out plan, out error); }
        catch (Exception problem) when (problem is not OutOfMemoryException
            and not StackOverflowException and not AccessViolationException)
        { plan = default; error = "DEPARTURE COULD NOT BE VERIFIED. REFRESH TO TRY AGAIN."; return false; }
    }

    private bool TryCreateLaunchCore(NativeCampaignShipRequest request, out LaunchPlan plan, out string error)
    {
        plan = default;
        var witness = request.Witness;
        if (!ValidDock(witness.Dock) || witness.Hunter is < 0 or > 6 || request.Destination is < 0 or > 4
            || !NativeCampaignShipController<LaunchPlan>.ValidFingerprint(witness.Fingerprint)
            || request.ExitShipToSurface && request.Destination != witness.Dock.Planet)
        { error = "INVALID GUNSHIP DEPARTURE."; return false; }
        if (!_ready()) { error = "CONFIGURE GAME FILES BEFORE DEPARTURE."; return false; }
        if (!SameDock(witness.Dock)) { error = "THE GUNSHIP DOCK HAS CHANGED."; return false; }
        string before = _fingerprint(witness.Dock.Slot);
        if (!String.Equals(before, witness.Fingerprint, StringComparison.Ordinal))
        { error = "THE DOCKED SAVE HAS CHANGED. REFRESH BEFORE DEPARTURE."; return false; }
        StorySave? save = _read(witness.Dock.Slot);
        string after = _fingerprint(witness.Dock.Slot);
        if (!SameDock(witness.Dock) || !String.Equals(after, witness.Fingerprint, StringComparison.Ordinal))
        { error = "THE DOCKED SAVE HAS CHANGED. REFRESH BEFORE DEPARTURE."; return false; }
        var capturedDock = new CampaignDockReceipt(witness.Dock.Slot, (CampaignPlanet)witness.Dock.Planet);
        if (!CampaignShipTravel.TryChooseLanding(save, capturedDock, witness.Dock.Slot, newGame: false,
            (CampaignPlanet)request.Destination, out _, out error)) return false;
        if (!SameDock(witness.Dock) || !String.Equals(_fingerprint(witness.Dock.Slot), witness.Fingerprint, StringComparison.Ordinal))
        { error = "THE DOCKED SAVE OR GUNSHIP HAS CHANGED. REFRESH BEFORE DEPARTURE."; return false; }
        LaunchPlan candidate = _create(request);
        if (!SameDock(witness.Dock) || !String.Equals(_fingerprint(witness.Dock.Slot), witness.Fingerprint, StringComparison.Ordinal))
        { error = "THE DOCKED SAVE OR GUNSHIP HAS CHANGED. REFRESH BEFORE DEPARTURE."; return false; }
        plan = candidate; error = ""; return true;
    }

    private static bool ValidDock(NativeCampaignShipDock dock) => dock.Slot is >= 1 and <= AdventureSave.SlotCount && dock.Planet is >= 0 and <= 4;
    private bool SameDock(NativeCampaignShipDock dock) => _dock() == new CampaignDockReceipt(dock.Slot, (CampaignPlanet)dock.Planet);
    private static NativeCampaignShipRead Failure(string error) => new("", Array.Empty<NativeCampaignShipDestination>(), error);
}
#endif
