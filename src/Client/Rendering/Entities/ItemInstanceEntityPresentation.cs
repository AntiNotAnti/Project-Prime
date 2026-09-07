using System;
using System.Collections.Generic;
using MphRead.Effects;
using MphRead.Formats;
using MphRead.Mods.Network;
using MphRead.Formats.Culling;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    public sealed class ItemInstanceEntityPresentation : EntityPresentation
    {
        private readonly ItemInstanceEntity _entity;
        public ItemInstanceEntityPresentation(ItemInstanceEntity entity, ScenePresentation presentation) : base(entity, presentation)
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
    }
}
