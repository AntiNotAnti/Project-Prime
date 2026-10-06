using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MphRead.Entities;
using MphRead.Mods.Input;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network
{
    /// <summary>A scene and reader with a common lifetime, separate from the foreground host.</summary>
    internal sealed class PassiveReplayScene : IDisposable
    {
        public ReplayPlaybackSession Session { get; }
        public ReplayReplicaState State { get; }
        public Scene Scene { get; } = null!;
        internal Replay.ReplayWorldCheckpoint.Bindings CheckpointBindings { get; private set; } = null!;
        internal ReplayReplicaCheckpoint InitialState { get; }
        internal ulong MapHash { get; }
        internal bool HasStepped { get; private set; }
        private bool _disposed;
        private bool _ownsScene = true;
        private ReplayTimelineClip? _ownedClip;
        internal Scene DetachScene() { _ownsScene = false; return Scene; }
        public PassiveReplayScene(string path, Vector2i size) : this(Open(path), size)
        {
            try
            {
                if (Session.Metadata?.WorldCheckpoint is { Length: > 0 } bytes)
                {
                    using var checkpoint = Replay.ReplayWorldCheckpoint.FromBytes(bytes, Session.Metadata?.BuildId);
                    // The origin world is required state. Any failed restore disposes
                    // this unpublished scene in the outer catch before it can be used.
                    checkpoint.Restore(this, playbackFrame: 0);
                }
                Scene.ReplayPoses = new(this, path);
            }
            catch { Dispose(); throw; }
        }
        internal PassiveReplayScene(PreparedReplaySource prepared, Vector2i size, int ownerThreadId,
            Replay.ReplayWorldCheckpoint? memoryCheckpoint = null)
            : this(AdoptPrepared(prepared, size, ownerThreadId), size)
        {
            try
            {
                _ownedClip = prepared.TakeClip();
                using var checkpoint = prepared.TakeCheckpoint();
                if (memoryCheckpoint != null) memoryCheckpoint.Restore(this);
                else checkpoint?.Restore(this, prepared.PlaybackFrame);
                if (_ownedClip != null)
                {
                    Session.Transport.ContinueSeek(_ownedClip.StartRecordingFrame, resume: true);
                    Session.Transport.AfterFrame();
                    Scene.ReplayPoses = new(this, _ownedClip);
                }
                else Scene.ReplayPoses = new(this, prepared.Path!);
            }
            catch { Dispose(); throw; }
        }
        private static ReplayPlaybackSession AdoptPrepared(PreparedReplaySource prepared, Vector2i size, int ownerThreadId)
        { prepared.ValidateRequiredOrigin(size, ownerThreadId); return prepared.AdoptSession(ownerThreadId); }
        internal PassiveReplayScene(ReplayReplicaCheckpoint initial, uint frame, ulong mapHash, Vector2i size)
            : this(OpenLive(initial, frame, mapHash), size) { }
        public PassiveReplayScene(ReplayTimelineClip clip, Vector2i size) : this(Open(clip), size)
        {
            try
            {
                using var checkpoint = Checkpoint(clip);
                checkpoint.Restore(this);
                Session.Transport.ContinueSeek(clip.StartRecordingFrame, resume: true);
                Session.Transport.AfterFrame();
                Scene.ReplayPoses = new(this, clip);
            }
            catch { Dispose(); throw; }
        }
        internal static Replay.ReplayWorldCheckpoint Checkpoint(ReplayTimelineClip clip)
        {
            if (clip.RestorePoint.Kind != ReplayRestoreKind.ReplicaCheckpoint)
                throw new InvalidDataException("A network baseline cannot restore a historical world.");
            var record = clip.RestorePoint.Records.Single(r => r.Kind == ReplayFactKind.World);
            var checkpoint = Replay.ReplayWorldCheckpoint.FromBytes(record.Payload);
            if (checkpoint.Frame != clip.RestorePoint.RecordingFrame)
            { checkpoint.Dispose(); throw new InvalidDataException("Checkpoint frame differs from the clip index."); }
            return checkpoint;
        }
        private static ReplayPlaybackSession Open(string path)
        {
            var session = new ReplayPlaybackSession(new PassiveReplaySessionHost());
            if (session.Join(path)) return session;
            session.Dispose(); throw new InvalidDataException(session.LastError);
        }
        private static ReplayPlaybackSession OpenLive(ReplayReplicaCheckpoint initial, uint frame, ulong mapHash)
        {
            var session = new ReplayPlaybackSession(new PassiveReplaySessionHost());
            try { session.JoinLive(initial, frame, mapHash); return session; }
            catch { session.Dispose(); throw; }
        }
        private static ReplayPlaybackSession Open(ReplayTimelineClip clip)
        {
            using var checkpoint = Checkpoint(clip);
            var session = new ReplayPlaybackSession(new PassiveReplaySessionHost());
            try { session.Join(clip, checkpoint.ConstructionState()); return session; }
            catch { session.Dispose(); throw; }
        }
        private PassiveReplayScene(ReplayPlaybackSession session, Vector2i size)
        {
            using var constructionPerf = ReplayPerfTelemetry.Measure(ReplayPerfOperation.WorldConstruction);
            Session = session;
            try
            {
                if (PreparedReplaySource.IsWorker)
                    throw new InvalidOperationException("Replay construction must run on its scene owner.");
                State = ((PassiveReplaySessionHost)session.Host).State;
                MatchStatePacket match = State.Match ?? throw new InvalidDataException("Replay has no room.");
                InitialState = State.CaptureCheckpoint();
                MapHash = Session.Metadata?.MapHash is > 0 ? Session.Metadata.MapHash : ReplayMapIdentity.Compute(match.RoomKey);
                Scene = new Scene(size, SyntheticInput.CreateKeyboard(), SyntheticInput.CreateMouse(), _ => { }, () => { },
                    new ReplaySceneServices(Session, State));
                // Replay worlds use the network slot contract, not the retail four-player cap.
                // Every placeholder must exist before AddRoom/OnLoad so later roster activation
                // cannot turn an unregistered PlayerEntity into a half-constructed actor.
                Scene.Players.MaxPlayers = PlayerEntity.SlotCapacity;
                Scene.GameState.Mode = (GameMode)match.Mode;
                if (Scene.GameState.SinglePlayer) throw new InvalidDataException("Passive reconstruction requires a recorded multiplayer world.");
                ((ReplaySceneServices)Scene.Services).ApplyRules(Scene, 0);
                for (int slot = 0; slot < PlayerEntity.SlotCapacity; slot++)
                {
                    var occupant = State.Occupant(slot);
                    Scene.AddPlayer(occupant.Generation == 0 ? Hunter.Samus : occupant.Hunter, occupant.Color);
                    Scene.Players.Items[slot].IsBot = false;
                }
                if (Scene.Players.PlayersCreated != PlayerEntity.SlotCapacity)
                    throw new InvalidDataException("Replay failed to construct all network player slots before room load.");
                Scene.AddRoom(match.RoomKey, (GameMode)match.Mode,
                    playerCount: Scene.Services.NetworkWorldProfile?.EntityLayerPlayers ?? match.PlayerCount);
                ((ReplaySceneServices)Scene.Services).ApplyRules(Scene, 0);
                Scene.OnLoad();
                CheckpointBindings = new(Scene);
            }
            catch { Dispose(); throw; }
        }
        public bool Step()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (Session.AtEnd) return false;
            Session.PumpFrame();
            if (Session.LastResult != ReplayOpenResult.Success) throw new InvalidDataException(Session.LastError);
            Scene.StepReplica();
            HasStepped = true;
            if (!Session.IsWarming) Session.Transport.AfterFrame();
            return true;
        }
        internal void StepLive(uint frame, IReadOnlyList<ReplayTimelineRecord> records)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Session.AdvanceLive(frame, records);
            Scene.StepReplica(); HasStepped = true;
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                try { Scene?.ReplayPoses?.Dispose(); }
                finally
                {
                    if (Scene != null)
                    {
                        Scene.ReplayPoses = null;
                        if (_ownsScene) try { Scene.DoCleanup(); } finally { Scene.UnloadGl(); }
                    }
                }
            }
            finally { try { Session.Dispose(); } finally { _ownedClip?.Dispose(); _ownedClip = null; } }
        }
    }
}
