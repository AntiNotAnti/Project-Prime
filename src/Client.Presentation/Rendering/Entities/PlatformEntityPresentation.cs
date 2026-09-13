using System;
using System.Collections.Generic;
using System.Diagnostics;
using MphRead.Effects;
using MphRead.Formats;
using MphRead.Formats.Collision;
using MphRead.Formats.Culling;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    public sealed class PlatformEntityPresentation : EntityPresentation
    {
        private readonly PlatformEntity _entity;
        public PlatformEntityPresentation(PlatformEntity entity, ScenePresentation presentation) : base(entity, presentation)
        {
            _entity = entity;
        }

        public override void GetDrawInfo()
        {
            _entity._animFlags &= ~PlatAnimFlags.WasDrawn;
            if (Entity._models[0].IsPlaceholder)
            {
                base.GetDrawInfo();
            }
            else if (_entity._animFlags.TestFlag(PlatAnimFlags.Draw))
            {
                bool draw = false;
                if (_entity.Flags.TestFlag(PlatformFlags.DrawAlways))
                {
                    draw = true;
                }
                else if (IsVisible(Entity.NodeRef))
                {
                    // todo?: the game has two conditions, one for checking the node ref when that flag is set,
                    // and one for full is_entity_visible -- but these flags are never set anyway
                    draw = true;
                }

                if (_entity.Flags.TestFlag(PlatformFlags.SyluxShip) && _entity._parentEntCol != null)
                {
                    Debug.Assert(_entity._parent != null);
                    if (!_entity._parent.StateFlags.TestFlag(PlatStateFlags.Awake) && !_entity._parent.StateFlags.TestFlag(PlatStateFlags.WasAwake))
                    {
                        draw = false;
                    }
                }

                if (_entity._animFlags.TestFlag(PlatAnimFlags.HasAnim) && _entity._currentAnimId < 0)
                {
                    draw = false;
                }

                if (draw)
                {
                    base.GetDrawInfo();
                    _entity._animFlags |= PlatAnimFlags.WasDrawn;
                }
            }
        }
    }
}
