using System;
using System.Diagnostics;
using MphRead.Formats.Culling;
using MphRead.Formats;
using MphRead.Effects;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    public sealed class ForceFieldLockEntityPresentation : EntityPresentation
    {
        private readonly ForceFieldLockEntity _entity;
        public ForceFieldLockEntityPresentation(ForceFieldLockEntity entity, ScenePresentation presentation) : base(entity, presentation)
        {
            _entity = entity;
        }

        public override void GetDrawInfo()
        {
            if (_entity._health == 0)
            {
                return;
            }

            if (_entity._timeSinceDamage < 10)
            {
                Entity.PaletteOverride = Metadata.RedPalette;
            }

            base.GetDrawInfo();
            Entity.PaletteOverride = null;
        }
    }
}
