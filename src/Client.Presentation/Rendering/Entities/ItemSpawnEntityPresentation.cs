using MphRead.Formats;
using MphRead.Mods.Network;
using MphRead.Formats.Culling;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    public sealed class ItemSpawnEntityPresentation : EntityPresentation
    {
        private readonly ItemSpawnEntity _entity;
        public ItemSpawnEntityPresentation(ItemSpawnEntity entity, ScenePresentation presentation) : base(entity, presentation)
        {
            _entity = entity;
        }

        public override void GetDrawInfo()
        {
            if (IsVisible(Entity.NodeRef))
            {
                base.GetDrawInfo();
            }
        }
    }
}
