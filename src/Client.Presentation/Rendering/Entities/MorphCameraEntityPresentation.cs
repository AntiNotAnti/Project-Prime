using MphRead.Formats;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    public sealed class MorphCameraEntityPresentation : EntityPresentation
    {
        private readonly MorphCameraEntity _entity;
        public MorphCameraEntityPresentation(MorphCameraEntity entity, ScenePresentation presentation) : base(entity, presentation)
        {
            _entity = entity;
        }

        public override void GetDisplayVolumes()
        {
            if (Presentation.ShowVolumes == VolumeDisplay.MorphCamera)
            {
                AddVolumeItem(_entity._volume, MorphCameraEntity._volumeColor);
            }
        }
    }
}
