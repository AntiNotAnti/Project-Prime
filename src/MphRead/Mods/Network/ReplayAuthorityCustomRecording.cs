using System;
using System.IO;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.CustomWeapons.Packages;

namespace MphRead.Mods.Network;

internal static partial class ReplayCapture
{
    // Stable across private-world replacement; the dedicated process owns this
    // provider until shutdown. No client preparation or recipient ACK is borrowed.
    internal static DedicatedServer? AuthorityRecordingOwner { get; set; }
    internal static event Action<ReplayCaptureRecord>? AuthorityPhaseAccepted;
    internal static Action<PassiveReplayScene>? AuthorityRecordingAfterTickForTests { get; set; }
    internal static bool CustomAuthorityRecordingScope() => NetSession.Active && NetSession.IsAuthority
        && !DemoPlayback.IsActive && AuthorityRecordingOwner?.HasCustomReplayRecordingScope == true;
    internal static PreparedReplayWeaponPackage RetainAuthorityPackage(ReplayCustomWeaponDescriptor descriptor)
        => AuthorityRecordingOwner?.RetainCustomReplayPackage(descriptor)
            ?? throw new InvalidOperationException("Authority replay resources are no longer available.");
    internal static void AcceptedAuthorityPhase(ReplayCaptureRecord record) => AuthorityPhaseAccepted?.Invoke(record);
    internal static bool CanStartServerRecording(out string? error)
    {
        if (CanStartLiveRecording(out error)) return true;
        if (!CustomAuthorityRecordingScope() || DemoRecorder.IsRecording || DemoClip.IsSaving) return false;
        error = null; return true;
    }
}

public sealed partial class DedicatedServer
{
    private WeaponMatchPackageBinding? _authorityReplayPackages;
    private bool _authorityReplayCaptureFailed;
    private ulong _authorityReplayRevision;
    private readonly CustomReplicaSlot[] _authorityReplaySlots = new CustomReplicaSlot[8];
    private readonly CustomReplicaFlight[] _authorityReplayFlights = new CustomReplicaFlight[64];
    internal void FailCustomReplayRecording() => _authorityReplayCaptureFailed = true;

    private void PrepareCustomReplayPackage(WeaponPackageStore store, CustomWeaponPackageManifest manifest)
    {
        if (Environment.GetEnvironmentVariable("PROJECT_PRIME_CUSTOM_WEAPON_SERVER_RECORDING") != "1"
            || !ReplayPolicy.Normalize().Enabled) return;
        try { _authorityReplayPackages = WeaponMatchPackageBinding.PrepareInstalled(store, manifest); }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        {
            _authorityReplayCaptureFailed = true;
            Console.WriteLine("[replay] Custom authority resource preparation unavailable: " + error.Message);
        }
    }
    internal bool HasCustomReplayRecordingScope => !_authorityReplayCaptureFailed && _authorityReplayPackages != null
        && ReplayPolicy.Normalize().Enabled && _phase == SessionPhase.InMatch && _bots.Count == 0 && _peers.Count > 0
        && CurrentDefinition.PackagedWeaponAvailable && _weaponPackages != null
        && _authorityReplayPackages.Manifest.Digest == _weaponPackages.Manifest.Digest
        && _sim?.EvidenceScene is { CustomCombat.Pickups: not null } scene
        && scene.CustomCombat.IsEligible && !scene.Services.IsReplica && !scene.PrivateCharacterGameplayUnsupported
        && scene.DeveloperMomentumPracticeTuning == null && scene.OfflineWeapons == null;
    internal PreparedReplayWeaponPackage RetainCustomReplayPackage(ReplayCustomWeaponDescriptor descriptor)
    {
        if (!HasCustomReplayRecordingScope || descriptor.Bootstrap.OriginKind != ReplayCustomWeaponOriginKind.Authority
            || descriptor.Bootstrap.Start != CurrentStartIdentity
            || descriptor.Bootstrap.PackageConfiguration != _weaponPackageConfiguration)
            throw new InvalidDataException("Authority recording lost its exact prepared match scope.");
        return PreparedReplayWeaponPackage.RetainPrepared(descriptor, _authorityReplayPackages!);
    }
    internal void CaptureCustomReplayState(Scene scene)
    {
        if (!ServerReplayRecorder.IsCustomRecording) return;
        var recorder = ReplayCapture.Recorder;
        try
        {
            if (!HasCustomReplayRecordingScope || _sim?.EvidenceScene != scene || recorder.CustomWeaponCaptureFailed
                || scene.CustomCombat is not { Pickups: { } pickups } root
                || !root.TryCaptureReplica(_authorityReplaySlots, _authorityReplayFlights, out var capture))
                throw new InvalidDataException("Authority custom replay capture lost its committed state scope.");
            if (capture.World == 0) return; // Wait for the first real authority frame; never invent frame1.
            if (recorder.CustomWeaponContext == null)
            {
                var context = new ReplayCustomWeaponContext(ReplayCustomWeaponBootstrap.ForAuthority(CurrentStartIdentity,
                    capture.World, _weaponPackageConfiguration, pickups.Layout.Digest, _authorityReplayPackages!.Manifest),
                    root.Definition, pickups.Layout);
                _authorityReplayRevision = 0;
                if (!recorder.AcceptCustomWeaponBaseline(context, capture, _authorityReplaySlots, _authorityReplayFlights, NetSession.NetFrame))
                    throw new InvalidDataException("Authority replay baseline was not accepted.");
            }
            else if (recorder.CustomWeaponContext.Bootstrap.OriginKind != ReplayCustomWeaponOriginKind.Authority
                || recorder.CustomWeaponContext.Bootstrap.Start != CurrentStartIdentity
                || recorder.CustomWeaponContext.Bootstrap.PackageConfiguration != _weaponPackageConfiguration)
                throw new InvalidDataException("Authority replay construction belongs to a different start.");
            if (_authorityReplayRevision == ulong.MaxValue
                || !recorder.AcceptCustomWeaponState(++_authorityReplayRevision, capture.World, _authorityReplaySlots, NetSession.NetFrame))
                throw new InvalidDataException("Authority replay state was not accepted.");
            for (int index = 0; index < pickups.Layout.Count; index++)
            {
                if (!pickups.TryState(index, out var item)) throw new InvalidDataException("Authority replay pickup table is incomplete.");
                recorder.AcceptCustomWeaponPickup(index, capture.World, item, NetSession.NetFrame);
            }
            if (recorder.CustomWeaponCaptureFailed) throw new InvalidDataException("Authority replay pickup capture failed.");
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        {
            _authorityReplayCaptureFailed = true;
            recorder.FailCustomWeaponFacts(); ServerReplayRecorder.InterruptCustomRecording(error.Message);
        }
        finally { Array.Clear(_authorityReplaySlots); Array.Clear(_authorityReplayFlights); }
    }
    private void ObserveCustomReplayLaunch(in CustomReplicaLaunch launch)
    {
        if (!ServerReplayRecorder.IsCustomRecording || ReplayCapture.Recorder.CustomWeaponContext == null) return;
        if (!ReplayCapture.Recorder.AcceptAuthorityCustomLaunch(launch, NetSession.NetFrame))
            ReplayCapture.Recorder.FailCustomWeaponFacts();
    }
    private void ObserveCustomReplayContact(WorldBootstrapIdentity source, in CustomContactOutcome outcome)
    {
        if (!ServerReplayRecorder.IsCustomRecording || ReplayCapture.Recorder.CustomWeaponContext == null) return;
        if (!ReplayCapture.Recorder.AcceptAuthorityCustomContact(source, outcome, NetSession.NetFrame))
            ReplayCapture.Recorder.FailCustomWeaponFacts();
    }
    private void ReleaseCustomReplayPackage()
    {
        if (ReferenceEquals(ReplayCapture.AuthorityRecordingOwner, this)) ReplayCapture.AuthorityRecordingOwner = null;
        _authorityReplayPackages?.Dispose(); _authorityReplayPackages = null;
    }
}
