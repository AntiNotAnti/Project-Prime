using System;
using MphRead.Mods.HeroModes;
using MphRead.Mods.Network;
namespace MphRead.Entities;

public partial class PlayerEntity
{
    private PlayerEntity? _ctzAuraHealer;
    private ushort _ctzAuraHealerGeneration;
    private int _ctzDamageMeasurementDepth;

    private int ModCtzCombinedHealth => Math.Max(0, _health)
        + (Flags2.TestFlag(PlayerFlags2.Halfturret) ? Math.Max(0, Halfturret?.Health ?? 0) : 0);

    /// <summary>Measure the accepted health change, including split turret damage,
    /// after every early return, invulnerability gate and damage modifier has run.</summary>
    private readonly struct CtzDamageMeasurement : IDisposable
    {
        private readonly PlayerEntity? _player;
        private readonly int _before;
        private readonly PlayerEntity? _attacker;
        internal CtzDamageMeasurement(PlayerEntity player, EntityBase? source)
        {
            _attacker=CtzSpawnRooms.Owner(source);
            _player = CtzCombatStats.CanRecord(player._scene) ? player : null;
            _before = _player?.ModCtzCombinedHealth ?? 0;
            if (_player != null) _player._ctzDamageMeasurementDepth++;
        }
        public void Dispose()
        {
            if (_player == null || --_player._ctzDamageMeasurementDepth != 0) return;
            int lost = Math.Max(0, _before - _player.ModCtzCombinedHealth);
            if (lost > 0) {
                _player._scene.GameState.CtzStats.DamageTaken(_player.SlotIndex,lost);
                if(_attacker!=null && _attacker!=_player && (!_player._scene.GameState.Teams || _attacker.TeamIndex!=_player.TeamIndex))
                    _player._scene.GameState.CtzStats.DamageDone(_attacker.SlotIndex,lost);
            }
        }
    }

    private void ModRecordCtzHealing(int gain, PlayerEntity? healer, ushort sourceGeneration)
    {
        if (gain <= 0 || !CtzCombatStats.CanRecord(_scene)) return;
        // ModGainCtzHealth is used only by CTZ support projectiles/aura pulses.
        // Native orb and Sylux self-sustain paths do not award support statistics.
        _scene.GameState.CtzStats.HealingReceived(SlotIndex, gain);
        if (healer == null || healer == this || healer.OwningScene != _scene
            || healer.TeamIndex != TeamIndex) return;
        _scene.GameState.CtzStats.HealingDone(healer.SlotIndex, sourceGeneration, gain);
    }
}
