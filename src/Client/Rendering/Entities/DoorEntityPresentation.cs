using System;
using System.Collections.Generic;
using System.Diagnostics;
using MphRead.Formats.Collision;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    public sealed class DoorEntityPresentation : EntityPresentation
    {
        private readonly DoorEntity _entity;
        public DoorEntityPresentation(DoorEntity entity, ScenePresentation presentation) : base(entity, presentation)
        {
            _entity = entity;
        }

        public override void GetDrawInfo()
        {
            if (!IsVisible(Entity.NodeRef) && (_entity.Portal == null || !IsVisible(_entity.Portal.NodeRef1) && !IsVisible(_entity.Portal.NodeRef2)))
            {
                return;
            }

            _entity._lock.Active = false;
            if (_entity.Locked && _entity.Flags.TestFlag(DoorFlags.ShowLock) && !_entity.Flags.TestFlag(DoorFlags.ShouldOpen) && (_entity.AnimInfo.Index[0] != 0 || _entity.AnimInfo.Flags[0].TestFlag(AnimFlags.Ended)))
            {
                _entity._lock.Active = true;
            }

            base.GetDrawInfo();
        }
    }
}
