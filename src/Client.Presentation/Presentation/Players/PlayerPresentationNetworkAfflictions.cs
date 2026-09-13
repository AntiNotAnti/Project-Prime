using System;
using System.Diagnostics;
using MphRead.Combat;
using MphRead.Effects;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    public partial class PlayerPresentation
    {
        public NetworkAfflictionState NetworkAfflictions { get; } = new();
        private bool _networkFrozen, _networkDisrupted;
        private int _networkBurnEffectId;
        private EffectEntry? _networkBurnEffect;

        public void ReconcileNetworkAfflictions(in SnapshotPlayer state, uint tick, bool legacy = false)
        {
            CombatActor identity = new(state.Slot, state.ConnectionId, state.Life);
            if (NetworkAfflictions.Identity != identity) ClearNetworkAfflictions();
            // Old snapshots do not encode burn/disruption durations; keep cues
            // from recorded events instead of interpreting missing fields as a clear.
            AfflictionTimers previous = NetworkAfflictions.At(tick);
            var timers = new AfflictionTimers(state.FrozenTicks,
                legacy && (state.Flags & SnapshotPlayerFlags.Burning) != 0 ? previous.Burn : state.BurnTicks,
                legacy && (state.Flags & SnapshotPlayerFlags.Disrupted) != 0 ? previous.Disrupt : state.DisruptTicks);
            if (NetworkAfflictions.Reconcile(identity, tick, timers))
                UpdateNetworkAfflictionPresentation();
        }

        public void ClearNetworkAfflictions()
        {
            NetworkAfflictions.Reset();
            _networkFrozen = _networkDisrupted = false;
            _player._drawIceLayer = false;
            if (_player._scene.Services.IsReplica) _player._frozenGfxTimer = 0;
            if (_networkBurnEffect != null) _player._scene.UnlinkEffectEntry(_networkBurnEffect);
            _networkBurnEffect = null;
            HudEndDisrupted();
        }

        private uint AfflictionPresentationTick()
        {
            if (ClientSceneServices.PlayFor(_player._scene) is { } play && play.Client.HasSnapshot)
            {
                double tick = play.Client.Clock.Synchronized
                    ? play.Client.Clock.EstimateServerTick(Stopwatch.GetTimestamp())
                    : play.Client.Snapshot.ServerTick + Stopwatch.GetElapsedTime(play.Client.SnapshotReceivedAt).TotalSeconds * 60;
                return unchecked((uint)(long)Math.Floor(tick));
            }
            return ReplayPlayback.SnapshotServerTick ?? NetworkAfflictions.Tick;
        }

        private void PresentNetworkAffliction(in CombatEvent value)
        {
            if (NetworkAfflictions.Apply(value)) UpdateNetworkAfflictionPresentation();
        }

        private void UpdateNetworkAfflictionPresentation()
        {
            if (!_player._scene.Services.IsReplica) return;
            AfflictionTimers timers = NetworkAfflictions.At(AfflictionPresentationTick());
            bool frozen = timers.Frozen > 0 && _player.Health > 0;
            if (frozen && !_networkFrozen) _player._soundSource.PlaySfx(SfxId.SHOTGUN_FREEZE);
            if (frozen) _player._frozenGfxTimer = NetworkFrozenGraphicsTicks(timers.Frozen);
            else if (_player.Health <= 0) _player._frozenGfxTimer = 0;
            _networkFrozen = frozen;
            _player._drawIceLayer = _player._frozenGfxTimer > 0 && _player.IsMainPlayer;
            bool disrupted = timers.Disrupt > 0 && _player.Health > 0;
            if (disrupted && !_networkDisrupted) HudOnDisrupted();
            if (disrupted) _hudDisruptedTimer = timers.Disrupt;
            else if (_networkDisrupted) HudEndDisrupted();
            _networkDisrupted = disrupted;
            bool burning = timers.Burn > 0 && _player.Health > 0 && !_player.IsUnmorphing;
            int effect = _player.IsAltForm || _player.IsMorphing ? 187 : _player.IsMainPlayer ? 188 : 189;
            if (_networkBurnEffect != null && (!burning || effect != _networkBurnEffectId))
            {
                _player._scene.UnlinkEffectEntry(_networkBurnEffect);
                _networkBurnEffect = null;
            }
            if (!burning) return;
            Vector3 position = effect == 188 ? _player._muzzlePos : _player.Position;
            Vector3 facing = _player.FacingVector;
            Vector3 up = _player.UpVector;
            if (_networkBurnEffect == null)
            {
                _networkBurnEffect = _player._scene.SpawnEffectGetEntry(effect, facing, up, position);
                _networkBurnEffect?.SetElementExtension(true);
                _networkBurnEffectId = effect;
            }
            _networkBurnEffect?.Transform(facing, up, position);
        }

        internal static ushort NetworkFrozenGraphicsTicks(ushort frozenTicks)
        {
            if (frozenTicks == 0) return 0;
            int duration = frozenTicks + SimTicks.From30HzFrames(5);
            return (ushort)Math.Min(duration, UInt16.MaxValue);
        }
    }
}
