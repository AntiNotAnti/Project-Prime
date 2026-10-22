using System;
using MphRead.Mods;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Network;
using MphRead.Mods.Render;

namespace MphRead;

public partial class Scene
{
    private ReplayCustomWeaponContact[]? _replayCustomContacts;
    private CustomReplicaFlight[]? _replayCustomFlights;
    private int _replayCustomContactCount, _replayCustomFlightCount;
    internal bool ReplayCustomOwnsBipedPresentation(Entities.PlayerEntity player)
        => TryReplayCustomPresentation(out var replay, out var custom) && player.OwningScene == this
            && custom.TrySlot(player.SlotIndex, out var actor) && actor.Occupied && !actor.Spectating && actor.HasWeapon
            && actor.Selected.Value == 10 && replay.State.MatchesLife(player.SlotIndex, actor.Generation, actor.Life);
    private bool TryReplayCustomPresentation(out ReplaySceneServices replay, out ReplayCustomWeaponState custom)
    {
        replay = Services as ReplaySceneServices ?? null!; custom = null!;
        if (replay == null || !replay.Session.IsActive || !replay.Session.IsCustomInspection
            || replay.State.CustomWeapons is not { CompleteForRestore: true } state
            || replay.State.CustomWeaponDescriptor is not { } descriptor || !HasReplayWeaponPackage(descriptor)) return false;
        custom = state; return true;
    }
    private void UpdateReplayCustomWeaponPresentation(bool offerContacts = true)
    {
        if (_customHudStates != null) Array.Clear(_customHudStates);
        _replayCustomContactCount = _replayCustomFlightCount = 0;
        if (Headless.Active || !TryReplayCustomPresentation(out var replay, out var custom)) return;
        PrewarmCustomRifleModel(); // Reuses the already prepared model, never package bytes.
        _customHudStates ??= new CustomWeaponHudState[8];
        foreach (var player in Players.Items)
        {
            int slot = player.SlotIndex;
            if ((uint)slot >= 8 || !custom.TrySlot(slot, out var actor) || !actor.Occupied || actor.Spectating || !actor.HasWeapon
                || !replay.State.MatchesLife(slot, actor.Generation, actor.Life) || player.Health <= 0
                || !replay.State.TryGetPlayer(slot, out var native) || native.Health == 0) continue;
            _customHudStates[slot] = new(actor.Selected.Value == 10, true, actor.State, (byte)actor.State.Optic,
                custom.Context.Definition.Mechanics.MagazineCapacity);
        }
        _replayCustomFlights ??= new CustomReplicaFlight[ReplayCustomWeaponState.FlightCapacity];
        _replayCustomFlightCount = custom.CopyFlights(_replayCustomFlights);
        _replayCustomContacts ??= new ReplayCustomWeaponContact[ReplayCustomWeaponState.ActionCapacity];
        uint frame = replay.Session.RecordingFrame;
        _replayCustomContactCount = custom.CopyContacts(_replayCustomContacts,
            frame > HeadDeathTrack.MaximumDuration ? frame - HeadDeathTrack.MaximumDuration : 0, frame);
        for (int i = 0; offerContacts && i < _replayCustomContactCount; i++)
        {
            var contact = _replayCustomContacts[i]; var hit = contact.Outcome; var victim = hit.Contact.Key.Victim;
            if (victim.Slot >= Players.Items.Count || !replay.State.MatchesLife(victim.Slot, victim.Generation, victim.Life)) continue;
            var player = Players.Items[victim.Slot];
            if (HeadHitOwner(player) is not { } slot) continue;
            if (hit.Disposition == CustomContactDisposition.Canonical && hit.Lethal)
            {
                CancelHeadHit(slot); slot.Terminated = true;
                AcceptCustomHeadDeath(player, contact.Bootstrap, hit, contact.Direction, contact.RecordingFrame);
            }
            else if (HeadHitFact.TryCustom(slot.Track.Victim, contact.Bootstrap, hit, contact.Direction, out var normalized))
                OfferHeadHit(player, slot, normalized with { ResolveWorld = contact.RecordingFrame });
        }
    }
    internal void DrawReplayCustomPickups()
    {
        if (Headless.Active || !TryReplayCustomPresentation(out _, out var custom)) return;
        for (int i = 0; i < custom.Context.Layout.Count; i++)
            if (custom.TryPickup(i, out var item) && item.Available)
                CustomPickupPresentation.Draw(this, custom.Context.Layout.Placements[i].Position);
    }
    private void DrawReplayCustomWorldEffects()
    {
        if (Headless.Active || !TryReplayCustomPresentation(out var replay, out _)) return;
        // World-clock positions come from the recorded anchors. A draw cannot
        // advance flight, collide, retarget its endpoint or pay accepted damage.
        for (int i = 0; i < _replayCustomFlightCount; i++)
        {
            var flight = _replayCustomFlights![i];
            CustomRiflePresentation.Trail(this, flight.Position - flight.Direction * .4f, flight.Position, 1);
        }
        for (int i = 0; i < _replayCustomContactCount; i++)
        {
            var contact = _replayCustomContacts![i]; uint age = unchecked(replay.Session.RecordingFrame - contact.RecordingFrame);
            if (age < 8 && contact.Outcome is { Disposition: CustomContactDisposition.Canonical } hit
                && hit.Health.BodyRemoved + hit.Health.TurretRemoved > 0)
                CustomRiflePresentation.Spark(this, hit.Contact.Point, 1 - age / 8f);
        }
    }
    private void ReleaseReplayCustomPresentation()
    { _replayCustomContacts = null; _replayCustomFlights = null; _replayCustomContactCount = _replayCustomFlightCount = 0; }
    internal void RebuildReplayCustomPresentation(bool retainHeadHistory = false)
    {
        // A fresh unpublished Scene only: rebuild derived cosmetics without a
        // simulation tick, input, world clock, RNG or health advance.
        ReleaseCustomWeaponPresentation();
        if (!retainHeadHistory) { ReleaseHeadHitPresentation(); ReleaseHeadDeathPresentation(); }
        UpdateReplayCustomWeaponPresentation(offerContacts: !retainHeadHistory);
        if (!retainHeadHistory) { StepHeadHitPresentation(); StepHeadDeathPresentation(); }
    }
}
