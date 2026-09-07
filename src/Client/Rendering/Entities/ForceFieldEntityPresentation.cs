using System.Collections.Generic;
using System.Diagnostics;
using MphRead.Formats;
using MphRead.Sound;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    public sealed class ForceFieldEntityPresentation : EntityPresentation
    {
        private readonly ForceFieldEntity _entity;
        public ForceFieldEntityPresentation(ForceFieldEntity entity, ScenePresentation presentation) : base(entity, presentation)
        {
            _entity = entity;
        }

        public override void GetDrawInfo()
        {
            if (Entity.Alpha > 0 && IsVisible(Entity.NodeRef))
            {
                base.GetDrawInfo();
            }
        }
    }
}
