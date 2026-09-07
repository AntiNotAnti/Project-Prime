using MphRead.Formats;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    public sealed class FhAreaVolumeEntityPresentation : EntityPresentation
    {
        private readonly FhAreaVolumeEntity _entity;
        public FhAreaVolumeEntityPresentation(FhAreaVolumeEntity entity, ScenePresentation presentation) : base(entity, presentation)
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
