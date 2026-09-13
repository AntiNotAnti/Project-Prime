using MphRead.Formats;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    public sealed class FhMorphCameraEntityPresentation : EntityPresentation
    {
        private readonly FhMorphCameraEntity _entity;
        public FhMorphCameraEntityPresentation(FhMorphCameraEntity entity, ScenePresentation presentation) : base(entity, presentation)
        {
            _entity = entity;
        }

        public override void GetDisplayVolumes()
        {
            if (Presentation.ShowVolumes == VolumeDisplay.MorphCamera)
            {
                AddVolumeItem(_entity._volume, FhMorphCameraEntity._volumeColor);
            }
        }
    }
}
