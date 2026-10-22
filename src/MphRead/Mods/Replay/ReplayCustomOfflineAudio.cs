#if !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.CustomWeapons.Packages;
using MphRead.Mods.Network;

namespace MphRead.Mods.Replay;

// Value-only archive projection. No Scene, sound device or live session is created.
internal static class ReplayCustomOfflineAudio
{
    internal static bool Append(string path, List<StudioAudioClip> clips, uint start, uint end,
        CancellationToken cancellation)
    {
        using (var header = DemoReader.Open(path, out var result))
        {
            if (header == null) throw new InvalidDataException("Cannot read offline audio recording: " + result);
            if (header.Metadata?.CustomWeapons == null) return false;
        }
        string store = Environment.GetEnvironmentVariable("PROJECT_PRIME_WEAPON_STORE") is { Length: > 0 } configured
            ? configured : Path.Combine(Platform.AppPaths.UserDataDirectory, "weapons");
        using var prepared = PreparedCustomReplayArchive.PrepareInspection(path, new WeaponPackageStore(store), cancellation: cancellation);
        using var world = prepared.CopyWorld();
        using var binding = prepared.Resources.Retain();
        var waves = binding.Resources(0).AudioSamples;
        var pcm = new StudioPcmAudio[4];
        for (int i = 0; i < pcm.Length; i++) pcm[i] = new(22050, 1, (float[])waves[i].Clone());
        using var session = new ReplayPlaybackSession(new PassiveReplaySessionHost());
        var metadata = world.ClipMetadata(0, Array.Empty<ReplayPlayerInfo>());
        if (prepared.FormatVersion == 8) session.JoinPostWorldInspection(world.ConstructionState(), prepared.Resources, metadata.MapHash);
        else session.JoinCustomInspection(world.ConstructionState(), prepared.Resources, metadata.MapHash);
        session.RestoreCustomInspection(world.DecoderState());
        session.AttachCustomArchive(prepared);
        var state = ((PassiveReplaySessionHost)session.Host).State;
        var launches = new CustomActionIdentity?[8];
        var owners = new WeaponOwnerContext[8];
        var reloads = new uint[8];
        var bolts = new uint[8];
        void Observe(bool audible)
        {
            uint absolute = session.RecordingFrame;
            ulong visibleOrigin = (ulong)session.Metadata!.OriginRecordingFrame + session.Metadata.LeadInFrames;
            for (int slot = 0; slot < 8; slot++)
            {
                var custom = state.CustomWeapons!;
                if (!custom.TrySlot(slot, out var actor) || !actor.Occupied || actor.Spectating || !actor.HasWeapon
                    || !state.TryGetPlayer(slot, out var player) || player.Health == 0
                    || !state.MatchesLife(slot, actor.Generation, actor.Life))
                { owners[slot] = default; launches[slot] = null; reloads[slot] = bolts[slot] = 0; continue; }
                bool sameLife = owners[slot] == actor.State.Owner;
                owners[slot] = actor.State.Owner;
                bool selected = actor.Selected.Value == 10;
                bool eligible = selected && (player.Flags & (PlayerState.FlagAltForm | PlayerState.FlagFrozen | PlayerState.FlagSpectating)) == 0
                    && (player.Flags & (PlayerState.FlagActive | PlayerState.FlagSpawned)) == (PlayerState.FlagActive | PlayerState.FlagSpawned);
                bool launch = custom.TryLatestLaunch(actor.State.Owner, out var fact, out _);
                bool fresh = launch && launches[slot] != fact.Identity;
                launches[slot] = launch ? fact.Identity : null;
                uint reload = selected && actor.State.Phase == CustomWeaponPhase.Reloading ? actor.State.ReloadCompleteTick : 0;
                CustomRifleCue? cue = null;
                if (!audible || !sameLife || !eligible) bolts[slot] = 0;
                else if (fresh) { cue = CustomRifleCue.Shot; bolts[slot] = absolute <= uint.MaxValue - 16 ? absolute + 16 : 0; }
                else if (reload != 0 && reload != reloads[slot]) cue = CustomRifleCue.Reload;
                else if (bolts[slot] != 0 && absolute >= bolts[slot]) { cue = CustomRifleCue.Bolt; bolts[slot] = 0; }
                reloads[slot] = reload;
                if (cue is not { } accepted || absolute < visibleOrigin) continue;
                uint frame = checked((uint)(absolute - visibleOrigin));
                var audio = pcm[(int)accepted];
                if (frame <= end && frame + audio.Frames * 60d / audio.SampleRate > start)
                    clips.Add(new(frame, StudioAudioBus.Game, audio));
                if (clips.Count > 100000) throw new InvalidDataException("Audio selection exceeds 100,000 recorded cues.");
            }
        }
        Observe(false); // Complete baseline is history, never a new audible event.
        uint steps = 0;
        while (!session.AtEnd)
        {
            cancellation.ThrowIfCancellationRequested();
            if (++steps > ReplayFormatV3.MaxFrame) throw new InvalidDataException("Offline custom audio exceeds its frame budget.");
            if (!session.AdvanceCustomArchiveInspection()) break;
            state.Advance(session.RecordingFrame);
            Observe(true);
            if (session.CurrentFrame > end && !session.IsWarming) break;
        }
        return true;
    }
}
#endif
