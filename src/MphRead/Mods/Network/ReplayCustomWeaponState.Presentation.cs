using System;
using MphRead.Mods.CustomWeapons;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

internal readonly record struct ReplayCustomWeaponContact(WorldBootstrapIdentity Bootstrap,
    CustomContactOutcome Outcome, Vector3 Direction, uint RecordingFrame);

internal sealed partial class ReplayCustomWeaponState
{
    internal bool TryKillWeaponName(ReplayKillIdentity kill, out string name)
    {
        name = "";
        if (!CompleteForRestore || Context.Bootstrap.Start.MatchId != kill.MatchId
            || Context.Bootstrap.Start.AuthorityEpoch != kill.AuthorityEpoch) return false;
        foreach (var action in _actions)
        {
            if (!action.Used || action.Contact is not { } fact || !fact.TryContact(Context, out _, out var hit)
                || !hit.Lethal || hit.Disposition != CustomContactDisposition.Canonical || hit.AuthorityFrame != kill.ServerTick) continue;
            var owner = hit.Contact.Key.Action.Owner; var victim = hit.Contact.Key.Victim;
            if (owner.Slot != kill.KillerSlot || owner.Generation != kill.KillerGeneration
                || owner.Life != kill.KillerLifeId || victim.Slot != kill.VictimSlot
                || victim.Generation != kill.VictimGeneration || victim.Life != kill.VictimLifeId) continue;
            name = Context.Definition.Name; return true;
        }
        return false;
    }
    internal bool TryLatestLaunch(WeaponOwnerContext owner, out CustomReplicaLaunch launch, out uint frame)
    {
        launch = default; frame = 0; bool found = false;
        if (!CompleteForRestore) return false;
        foreach (var action in _actions)
        {
            if (!action.Used || action.Identity.Owner != owner || action.Launch is not { } fact
                || !fact.TryLaunch(Context, out var candidate)) continue;
            if (!found || action.LaunchFrame > frame || action.LaunchFrame == frame && candidate.Identity.Sequence > launch.Identity.Sequence)
            { found = true; launch = candidate; frame = action.LaunchFrame; }
        }
        return found;
    }
    // Derived from retained accepted anchors. No animation clock, cue queue or
    // mutable track is needed to reproduce a cold checkpoint's rifle pose.
    internal CustomRiflePose RiflePose(int slot, uint recordingFrame, float alpha = 0)
    {
        if (!CompleteForRestore || !TrySlot(slot, out var actor) || !actor.Occupied || actor.Spectating
            || !actor.HasWeapon || actor.Selected.Value != 10 || recordingFrame < RecordingFrame) return default;
        alpha = float.IsFinite(alpha) ? Math.Clamp(alpha, 0, 1) : 0;
        float age = TryLatestLaunch(actor.State.Owner, out _, out uint at) && recordingFrame >= at
            ? (float)Math.Min(100u, recordingFrame - at) + alpha : 100;
        float bolt = age < 8 ? 0 : age < 16 ? (age - 8) / 8 : age < 24 ? 1 : age < 34 ? 1 - (age - 24) / 10 : 0;
        float reload = 0;
        if (actor.State.Phase == CustomWeaponPhase.Reloading)
        {
            int duration = Context.Definition.Mechanics.ReloadTicks;
            int remaining = unchecked((int)(actor.State.ReloadCompleteTick - actor.State.Tick));
            // Source ticks and authority ticks are separate clocks. Repeated
            // authority tables can contain the same source snapshot; render
            // only its accepted progress rather than restart an inferred clock.
            reload = (float)Math.Clamp((duration - (double)remaining) / duration, 0, 1);
        }
        float magazine = reload < .2f ? reload / .2f : reload < .7f ? 1 : Math.Clamp((1 - reload) / .3f, 0, 1);
        return new(Math.Clamp(1 - age / 6, 0, 1), bolt, magazine, 0, Math.Clamp(1 - age / 3, 0, 1));
    }
    internal int CopyContacts(Span<ReplayCustomWeaponContact> contacts, uint firstFrame, uint lastFrame)
    {
        if (!CompleteForRestore) return 0;
        if (contacts.Length < ActionCapacity) throw new ArgumentException("Complete bounded contact buffer required.");
        int count = 0;
        foreach (var action in _actions)
        {
            if (!action.Used || action.Contact is not { } fact || action.ContactFrame < firstFrame || action.ContactFrame > lastFrame
                || !fact.TryContact(Context, out var bootstrap, out var outcome)) continue;
            var direction = action.BaselineLaunch?.Direction ?? Vector3.Zero;
            // A later accepted launch cannot retroactively orient an earlier
            // contact. Complete-frame playback may use a same-frame launch;
            // construction flights were already known at the restore origin.
            if (action.LaunchFrame <= action.ContactFrame
                && action.Launch is { } launchFact && launchFact.TryLaunch(Context, out var launch)) direction = launch.Direction;
            contacts[count++] = new(bootstrap, outcome, direction, action.ContactFrame);
        }
        for (int i = 1; i < count; i++)
        {
            var value = contacts[i]; int j = i - 1;
            while (j >= 0 && (contacts[j].RecordingFrame > value.RecordingFrame
                || contacts[j].RecordingFrame == value.RecordingFrame && Compare(contacts[j].Outcome.Contact.Key.Action, value.Outcome.Contact.Key.Action) > 0))
            { contacts[j + 1] = contacts[j]; j--; }
            contacts[j + 1] = value;
        }
        return count;
    }
}
