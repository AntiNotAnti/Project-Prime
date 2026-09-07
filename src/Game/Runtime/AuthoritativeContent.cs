using MphRead.Entities;
namespace MphRead
{
    public static class AuthoritativeContent
    {
        public static void ValidateRoom(Scene scene)
        {
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
