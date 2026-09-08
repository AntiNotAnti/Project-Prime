using MphRead.Entities;

namespace MphRead.Mods.Network
{
    public sealed class ServerSceneServices(ServerCombat combat) : ISceneServices
    {
        public ICombatAuthority Combat => combat;
        public void PublishWorldSignal(Scene scene, in WorldSignal signal)
            => combat.World.Publish(scene, signal, combat.Tick, combat.NextPresentationId());
        public uint GetWorldEntityId(Scene scene, EntityBase entity) => combat.World.Identity(scene, entity);
        public void ForgetWorldEntity(EntityBase entity) => combat.World.Forget(entity);
        public bool ShouldLeaveAfterMatch => false;
        public bool KeepSlotAlive(PlayerEntity player) => true;
    }
}
