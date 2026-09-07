using MphRead.Entities;
using MphRead.Mods.Network;
namespace MphRead
{
    public static class AuthoritativeContent
    {
        public static int MinimumWorldRecords(Scene scene)
        {
            int count = WorldPacket.CanonicalRecordCount;
            foreach (EntityBase entity in scene.Entities)
                count += entity is ItemSpawnEntity ? 2 : entity is NodeDefenseEntity or OctolithFlagEntity ? 1 : 0;
            return count;
        }
        public static void ValidateRoom(Scene scene)
        {
            int required = MinimumWorldRecords(scene);
            if (required > WorldPacket.Capacity)
                throw new ProgramException($"Room {scene.RoomId} requires at least {required} world records; capacity is {WorldPacket.Capacity}.");
            foreach (EntityBase entity in scene.Entities)
            {
                if (entity.Type is EntityType.Door or EntityType.ForceField or EntityType.Platform)
                {
                    throw new ProgramException($"Authoritative multiplayer does not support {entity.Type} entity {entity.Id} in room {scene.RoomId}. "
                        + "Retail multiplayer rooms do not contain mutable door, forcefield or platform entities.");
                }
            }
        }

    }
}
