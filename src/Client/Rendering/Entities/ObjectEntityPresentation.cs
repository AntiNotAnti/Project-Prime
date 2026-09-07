using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics;
using MphRead.Effects;
using MphRead.Formats.Collision;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    public sealed class ObjectEntityPresentation : EntityPresentation
    {
        private readonly ObjectEntity _entity;
        public ObjectEntityPresentation(ObjectEntity entity, ScenePresentation presentation) : base(entity, presentation)
        {
            _entity = entity;
        }

        public override void GetDrawInfo()
        {
            _entity._flags |= ObjectFlags.IsVisible;
            if (!_entity._flags.TestFlag(ObjectFlags.NoAnimation) && _entity._data.ModelId != -1)
            {
                // the game sets non-looping anims for AlimbicGhost_01/GhostSwitch here when scan visor is off,
                // but they're not visible so I don't know what the point is
                if (Presentation.ScanVisor || _entity._data.ModelId != 0 && _entity._data.ModelId != 41)
                {
                    if (IsVisible(Entity.NodeRef))
                    {
                        base.GetDrawInfo();
                    }
                }
            }
            else
            {
                if (_entity._data.EffectId != 0)
                {
                    if (!IsVisible(Entity.NodeRef))
                    {
                        _entity._flags &= ~ObjectFlags.IsVisible;
                    }
                }

                if (Presentation.ShowInvisibleEntities)
                {
                    base.GetDrawInfo();
                }
            }
        }

        public override void GetDisplayVolumes()
        {
            if (_entity._data.EffectId > 0 && Presentation.ShowVolumes == VolumeDisplay.Object)
            {
                AddVolumeItem(_entity._effectVolume, Vector3.UnitX);
            }
        }
    }
}
