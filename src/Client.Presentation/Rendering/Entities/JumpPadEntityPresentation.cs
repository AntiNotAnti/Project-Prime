using MphRead.Formats;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    public sealed class JumpPadEntityPresentation : EntityPresentation
    {
        private readonly JumpPadEntity _entity;
        public JumpPadEntityPresentation(JumpPadEntity entity, ScenePresentation presentation) : base(entity, presentation)
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

        public override void GetDisplayVolumes()
        {
            if (Presentation.ShowVolumes == VolumeDisplay.JumpPad)
            {
                AddVolumeItem(_entity._volume, Vector3.UnitY);
            }
        }
    }
}
