using MphRead.Formats;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    public sealed class FhJumpPadEntityPresentation : EntityPresentation
    {
        private readonly FhJumpPadEntity _entity;
        public FhJumpPadEntityPresentation(FhJumpPadEntity entity, ScenePresentation presentation) : base(entity, presentation)
        {
            _entity = entity;
        }

        public override void GetDisplayVolumes()
        {
            if (Presentation.ShowVolumes == VolumeDisplay.JumpPad)
            {
                AddVolumeItem(_entity._volume, Vector3.UnitY);
            }
        }
    }
}
