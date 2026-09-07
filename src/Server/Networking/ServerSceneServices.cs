using MphRead.Entities;

namespace MphRead.Mods.Network
{
    public sealed class ServerSceneServices(ServerCombat combat) : ISceneServices
    {
        public ICombatAuthority Combat => combat;
        public bool ShouldLeaveAfterMatch => false;
        public bool KeepSlotAlive(PlayerEntity player) => true;
    }
}
