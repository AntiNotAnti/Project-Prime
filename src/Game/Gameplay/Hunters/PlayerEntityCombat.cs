using System;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    public partial class PlayerEntity
    {
        internal CombatShot CombatBurnSource { get; private set; }

        public CombatActor ServerCombatIdentity => _scene.IsHeadless && (_networkInputActive || IsBot)
            ? new((byte)SlotIndex, _serverConnectionId, _serverLife) : CombatActor.None;

        private void NoteServerCombatSpawn()
        {
            if (!_scene.IsHeadless || (!_networkInputActive && !IsBot) || _serverConnectionId == 0) return;
            CombatBurnSource = default;
            // A respawn is the authoritative input-epoch boundary. Clear any
            // held controls left by the dead life before the next command can
            // be applied; edge state and boost intents are also reset by
            // Spawn().
            Controls.ClearAll();
            Input.ClearBoostIntents();
            _serverLife = unchecked(_serverLife + 1);
            if (_serverLife == 0) _serverLife = 1;
            _serverWasAlive = true;
            _scene.Services.Combat?.NoteSpawn(this);
        }
    }
}
