using OpenTK.Mathematics;

namespace MphRead.Entities
{
    public sealed class LightSourceEntityPresentation : EntityPresentation
    {
        private readonly LightSourceEntity _entity;
        public LightSourceEntityPresentation(LightSourceEntity entity, ScenePresentation presentation) : base(entity, presentation)
        {
            _entity = entity;
        }

        public override void GetDisplayVolumes()
        {
            if (Presentation.ShowVolumes == VolumeDisplay.LightColor1 || Presentation.ShowVolumes == VolumeDisplay.LightColor2)
            {
                Vector3 color = Vector3.Zero;
                if (Presentation.ShowVolumes == VolumeDisplay.LightColor1 && _entity._data.Light1Enabled != 0)
                {
                    color = _entity.Light1Color;
                }
                else if (Presentation.ShowVolumes == VolumeDisplay.LightColor2 && _entity._data.Light2Enabled != 0)
                {
                    color = _entity.Light2Color;
                }

                AddVolumeItem(_entity.Volume, color);
            }
        }
    }
}
