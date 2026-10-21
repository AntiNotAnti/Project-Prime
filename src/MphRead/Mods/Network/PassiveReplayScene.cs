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
        public Scene Scene { get; }
        internal Replay.ReplayWorldCheckpoint.Bindings CheckpointBindings { get; private set; } = null!;
        internal ReplayReplicaCheckpoint InitialState { get; }
        internal ulong MapHash { get; }
        internal bool HasStepped { get; private set; }
        internal bool HasRestoredCustomWorld { get; private set; }
        internal void MarkCustomWorldRestored() => HasRestoredCustomWorld = true;
        private bool _disposed;
        private bool _ownsScene = true;
        internal Scene DetachScene() { _ownsScene = false; return Scene; }
        public PassiveReplayScene(string path, Vector2i size) : this(Open(path), size)
        {
            try
            {
                bool? indexedFirePresentation = Session.Metadata?.Type == ReplayType.Clip ? Session.Metadata.IndexedFireContinuation : null;
                if (Session.Metadata?.WorldCheckpoint is { Length: > 0 } bytes)
                {
                    try
                    {
                        using var checkpoint = Replay.ReplayWorldCheckpoint.FromBytes(bytes, Session.Metadata?.BuildId);
                        checkpoint.Restore(this, playbackFrame: 0);
                        if (Session.Metadata?.Type == ReplayType.Clip && !checkpoint.HasAuthorityFormHistory)
                            Session.WarnVerification("Historical clip baseline lacks authority form history; transition animation continuity is not verified.");
                        // An explicit clip contract continues its producer's
                        // firing world. Legacy capsules retain their adapters;
                        // full-match packet reconstruction stays indexed.
                        if (indexedFirePresentation == null && Session.Metadata?.Type == ReplayType.Clip && checkpoint.HasPresentationContract)
                            indexedFirePresentation = checkpoint.IndexedFirePresentation;
                    }
                    catch (InvalidDataException ex)
                    {
                        // The session has already reconstructed a usable decoder from
                        // bootstrap/packets. An old world capsule is only an acceleration
                        // hint and must not make the replay unwatchable.
                        Session.WarnVerification("World checkpoint compatibility fallback: " + ex.Message);
                    }
                }
                Scene.ReplayPoses = new(this, path, indexedFirePresentation);
                Scene.BeginOwnerPresentation(path);
            }
            catch { Dispose(); throw; }
        }
        internal PassiveReplayScene(ReplayReplicaCheckpoint initial, uint frame, ulong mapHash, Vector2i size)
            : this(OpenLive(initial, frame, mapHash), size) { }
        public PassiveReplayScene(ReplayTimelineClip clip, Vector2i size) : this(Open(clip), size)
        {
            try
            {
                using var checkpoint = Checkpoint(clip);
                checkpoint.Restore(this);
                if (!checkpoint.HasAuthorityFormHistory)
                    Session.WarnVerification("Historical clip baseline lacks authority form history; transition animation continuity is not verified.");
                Session.Transport.ContinueSeek(clip.StartRecordingFrame, resume: true);
                Session.Transport.AfterFrame();
                Scene.ReplayPoses = new(this, clip, checkpoint.HasPresentationContract ? checkpoint.IndexedFirePresentation : null);
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
        internal static PassiveReplayScene CreateCustomInspection(ReplayReplicaCheckpoint initial, PreparedReplayWeaponPackage prepared,
            ulong mapHash, Vector2i size)
        {
            if (mapHash == 0) throw new InvalidDataException("Custom Scene inspection requires the recorded exact map hash.");
            var session = new ReplayPlaybackSession(new PassiveReplaySessionHost());
            try
            {
                session.JoinCustomInspection(initial, prepared, mapHash);
                if (!((PassiveReplaySessionHost)session.Host).State.HasRecordedSceneBaseline)
                    throw new InvalidDataException("Custom Scene inspection requires accepted native roster and snapshot coverage.");
                if (ReplayMapIdentity.Validate(session.Metadata!) != ReplayOpenResult.Success)
                    throw new InvalidDataException("The exact recorded replay map is unavailable or differs from installed data.");
                return new PassiveReplayScene(session, size, prepared);
            }
            catch { session.Dispose(); throw; }
        }
        internal static PassiveReplayScene RestoreCustomInspection(Replay.ReplayWorldCheckpoint checkpoint, PreparedReplayWeaponPackage prepared, Vector2i size)
        {
            if (!checkpoint.HasCustomWeaponContract) throw new InvalidDataException("An explicit custom world12 capsule is required.");
            var metadata = checkpoint.ClipMetadata(0, Array.Empty<ReplayPlayerInfo>());
            var scene = CreateCustomInspection(checkpoint.ConstructionState(), prepared, metadata.MapHash, size);
            try { checkpoint.RestoreCustom(scene); return scene; }
            catch { scene.Dispose(); throw; }
        }
        internal static PassiveReplayScene OpenCustomArchiveInspection(PreparedCustomReplayArchive prepared, Vector2i size)
        {
            using var world = prepared.CopyWorld();
            var scene = RestoreCustomInspection(world, prepared.Resources, size);
            try
            {
                scene.Session.AttachCustomArchive(prepared);
                while (scene.Session.RecordingFrame < prepared.TargetFrame)
                    if (!scene.StepCustomArchiveInspection()) throw new InvalidDataException("Custom archive ended before its requested seek frame.");
                return scene;
            }
            catch { scene.Dispose(); throw; }
        }
        private PassiveReplayScene(ReplayPlaybackSession session, Vector2i size, PreparedReplayWeaponPackage? prepared = null)
        {
            Session = session;
            State = ((PassiveReplaySessionHost)session.Host).State;
            MatchStatePacket match = State.Match ?? throw new InvalidDataException("Replay has no room.");
            if (State.CustomWeapons != null && prepared == null)
                throw new InvalidDataException("Custom Scene construction requires independently prepared historical resources.");
            InitialState = State.CaptureCheckpoint();
            MapHash = Session.Metadata?.MapHash is > 0 ? Session.Metadata.MapHash : ReplayMapIdentity.Compute(match.RoomKey);
            Scene = new Scene(size, SyntheticInput.CreateKeyboard(), SyntheticInput.CreateMouse(), _ => { }, () => { },
                new ReplaySceneServices(Session, State));
            // Replay worlds use the network slot contract, not the retail four-player cap.
            // Every placeholder must exist before AddRoom/OnLoad so later roster activation
            // cannot turn an unregistered PlayerEntity into a half-constructed actor.
            Scene.Players.MaxPlayers = PlayerEntity.SlotCapacity;
            try
            {
                if (prepared != null) Scene.BindReplayWeaponPackage(prepared);
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
            if (Session.IsCustomInspection) throw new InvalidOperationException("Custom inspection requires explicit accepted-record stepping.");
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
        internal void StepCustomInspection(uint frame, IReadOnlyList<ReplayTimelineRecord> records)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Session.AdvanceCustomInspection(frame, records);
            Scene.StepReplica(); HasStepped = true;
        }
        internal bool StepCustomArchiveInspection()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!Session.AdvanceCustomArchiveInspection()) return false;
            Scene.StepReplica(); HasStepped = true; return true;
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Scene.EndOwnerPresentation();
            Scene.ReplayPoses?.Dispose(); Scene.ReplayPoses = null;
            if (_ownsScene) { Scene.DoCleanup(); Scene.UnloadGl(); }
            Session.Dispose();
        }
    }
}
