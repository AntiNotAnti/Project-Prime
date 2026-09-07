using MphRead.Formats;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    public sealed class AreaVolumeEntityPresentation : EntityPresentation
    {
        private readonly AreaVolumeEntity _entity;
        public AreaVolumeEntityPresentation(AreaVolumeEntity entity, ScenePresentation presentation) : base(entity, presentation)
        {
            _entity = entity;
        }

        public override void GetDisplayVolumes()
        {
            if (Presentation.ShowVolumes == VolumeDisplay.AreaInside || Presentation.ShowVolumes == VolumeDisplay.AreaExit)
            {
                Vector3 color = Presentation.ShowVolumes == VolumeDisplay.AreaInside ? _entity._insideEventColor : _entity._exitEventColor;
                AddVolumeItem(_entity._volume, color);
            }
        }
    }
}
