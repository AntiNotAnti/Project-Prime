using MphRead.Formats;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    public sealed class FlagBaseEntityPresentation : EntityPresentation
    {
        private readonly FlagBaseEntity _entity;
        public FlagBaseEntityPresentation(FlagBaseEntity entity, ScenePresentation presentation) : base(entity, presentation)
        {
            _entity = entity;
        }

        // todo: is_visible
        public override void GetDisplayVolumes()
        {
            if (Presentation.ShowVolumes == VolumeDisplay.FlagBase)
            {
                AddVolumeItem(_entity._volume, Vector3.One);
            }
        }
    }
}
