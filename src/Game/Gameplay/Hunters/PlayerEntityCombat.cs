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
            _serverLife = unchecked(_serverLife + 1);
            if (_serverLife == 0) _serverLife = 1;
            _serverWasAlive = true;
            _scene.Services.Combat?.NoteSpawn(this);
        }
    }
}
