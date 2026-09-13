using System.Runtime.CompilerServices;
using MphRead.Formats.Culling;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    public partial class EntityPresentation
    {
        private static readonly ConditionalWeakTable<EntityBase, EntityPresentation> _presentations = new();
        public static EntityPresentation Get(EntityBase entity, ScenePresentation presentation)
        {
            if (_presentations.TryGetValue(entity, out EntityPresentation? existing))
            {
                return existing;
            }

            return _presentations.GetValue(entity, value => Create(value, presentation));
        }

        private static EntityPresentation Create(EntityBase entity, ScenePresentation presentation) => entity switch
        {
            PlayerEntity player => player.GetPresentation(),
            HalfturretEntity value => new HalfturretEntityPresentation(value, presentation),
            AreaVolumeEntity value => new AreaVolumeEntityPresentation(value, presentation),
            FhAreaVolumeEntity value => new FhAreaVolumeEntityPresentation(value, presentation),
            BeamProjectileEntity value => new BeamProjectileEntityPresentation(value, presentation),
            BombEntity value => new BombEntityPresentation(value, presentation),
            DoorEntity value => new DoorEntityPresentation(value, presentation),
            FlagBaseEntity value => new FlagBaseEntityPresentation(value, presentation),
            ForceFieldEntity value => new ForceFieldEntityPresentation(value, presentation),
            ForceFieldLockEntity value => new ForceFieldLockEntityPresentation(value, presentation),
            ItemInstanceEntity value => new ItemInstanceEntityPresentation(value, presentation),
            ItemSpawnEntity value => new ItemSpawnEntityPresentation(value, presentation),
            JumpPadEntity value => new JumpPadEntityPresentation(value, presentation),
            FhJumpPadEntity value => new FhJumpPadEntityPresentation(value, presentation),
            LightSourceEntity value => new LightSourceEntityPresentation(value, presentation),
            MorphCameraEntity value => new MorphCameraEntityPresentation(value, presentation),
            FhMorphCameraEntity value => new FhMorphCameraEntityPresentation(value, presentation),
            NodeDefenseEntity value => new NodeDefenseEntityPresentation(value, presentation),
            ObjectEntity value => new ObjectEntityPresentation(value, presentation),
            PlatformEntity value => new PlatformEntityPresentation(value, presentation),
            RoomEntity value => new RoomEntityPresentation(value, presentation),
            TeleporterEntity value => new TeleporterEntityPresentation(value, presentation),
            TriggerVolumeEntity value => new TriggerVolumeEntityPresentation(value, presentation),
            FhTriggerVolumeEntity value => new FhTriggerVolumeEntityPresentation(value, presentation),
            _ => new EntityPresentation(entity, presentation)};
        protected bool IsVisible(NodeRef nodeRef) => Entity.IsVisible(nodeRef);
        protected LightInfo GetLightInfo() => Entity.GetLightInfo();
        protected void UpdateTransforms(ModelInstance instance, int index)
        {
            Entity.UpdateTransforms(instance, index, Presentation.TransformRoomNodes);
            Presentation.UpdateMaterials(instance.Model, Entity.GetModelRecolor(instance, index));
            if (Presentation.ShowCollision)
            {
                Entity.UpdateDrawCollision();
            }
        }

        protected void UpdateTransforms(ModelInstance instance, Matrix4 transform, int recolor)
        {
            Entity.UpdateTransforms(instance, transform, recolor);
            Presentation.UpdateMaterials(instance.Model, recolor);
        }

        protected void UpdateMaterials(ModelInstance instance, int recolor)
        {
            Entity.UpdateMaterials(instance, recolor);
            Presentation.UpdateMaterials(instance.Model, recolor);
        }
    }
}
