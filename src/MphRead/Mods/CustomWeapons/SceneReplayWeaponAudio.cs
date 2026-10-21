using System;
using MphRead.Mods;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;
using MphRead.Sound;

namespace MphRead;

public partial class Scene
{
    // Device voices are private presentation state. These cursors never enter a
    // checkpoint or feed back into accepted weapon state.
    private readonly WeaponOwnerContext[] _replayAudioOwners = new WeaponOwnerContext[8];
    private readonly CustomActionIdentity?[] _replayAudioLaunches = new CustomActionIdentity?[8];
    private readonly uint[] _replayAudioReloads = new uint[8], _replayAudioBolts = new uint[8];
    private bool _replayAudioEnabled;

    internal void PrepareReplayCustomAudio(bool audible)
    {
        _replayAudioEnabled = audible && !Headless.Active && !ReplayVideoExporter.Rendering
            && ReplayAudioOwner.MayPlay(this) && TryReplayCustomPresentation(out _, out _);
        if (!_replayAudioEnabled)
        {
            StopCustomRifleAudio();
            SynchronizeReplayCustomAudio(play: false);
            return;
        }
        if (_customRifleAudio?.MatchesDevice == true) return;
        _customRifleAudio?.Dispose(); _customRifleAudio = null;
        if (_customRifleAudioWaves == null) return;
        var device = MusicPlayer.PlaybackDevice;
        if (device == null || ReferenceEquals(device, _customRifleAudioFailedDevice)) return;
        try { _customRifleAudio = CustomRifleAudio.Prewarm(_customRifleAudioWaves); }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        { _customRifleAudioFailedDevice = device; DebugLog.Line("replay", "Custom replay audio unavailable: " + error.GetType().Name); }
    }

    internal void StepReplayCustomAudio() => SynchronizeReplayCustomAudio(_replayAudioEnabled);

    private void SynchronizeReplayCustomAudio(bool play)
    {
        if (!TryReplayCustomPresentation(out var replay, out var custom)) return;
        uint frame = replay.Session.RecordingFrame;
        for (int slot = 0; slot < 8; slot++)
        {
            if (!custom.TrySlot(slot, out var actor) || !actor.Occupied || actor.Spectating || !actor.HasWeapon
                || slot >= Players.Items.Count || !replay.State.MatchesLife(slot, actor.Generation, actor.Life)
                || Players.Items[slot].Health <= 0)
            { _customRifleAudio?.Stop(slot); _replayAudioOwners[slot] = default; _replayAudioLaunches[slot] = null; _replayAudioBolts[slot] = 0; continue; }
            bool sameLife = _replayAudioOwners[slot] == actor.State.Owner;
            _replayAudioOwners[slot] = actor.State.Owner;
            bool selected = actor.Selected.Value == 10;
            bool launch = custom.TryLatestLaunch(actor.State.Owner, out var fact, out _);
            bool fresh = launch && _replayAudioLaunches[slot] != fact.Identity;
            _replayAudioLaunches[slot] = launch ? fact.Identity : null;
            uint reload = selected && actor.State.Phase == CustomWeaponPhase.Reloading ? actor.State.ReloadCompleteTick : 0;
            var cue = (CustomRifleCue?)null;
            if (!play || !sameLife) _replayAudioBolts[slot] = 0;
            else if (fresh)
            { cue = CustomRifleCue.Shot; _replayAudioBolts[slot] = frame <= uint.MaxValue - 16 ? frame + 16 : 0; }
            else if (reload != 0 && reload != _replayAudioReloads[slot]) cue = CustomRifleCue.Reload;
            else if (selected && _replayAudioBolts[slot] != 0 && frame >= _replayAudioBolts[slot])
            { cue = CustomRifleCue.Bolt; _replayAudioBolts[slot] = 0; }
            _replayAudioReloads[slot] = reload;
            if (!selected) { _customRifleAudio?.Stop(slot); _replayAudioBolts[slot] = 0; }
            if (cue is { } accepted)
                _customRifleAudio?.Play(slot, accepted, accepted == CustomRifleCue.Shot ? fact.Origin : Players.Items[slot].Position,
                    Audio.GetListenerPosition());
        }
    }
}
