using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using MphRead.Formats;
using MphRead.Mods.Network;
using MphRead.Hud;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    public sealed class NodeDefenseEntityPresentation : EntityPresentation
    {
        private readonly NodeDefenseEntity _entity;
        public NodeDefenseEntityPresentation(NodeDefenseEntity entity, ScenePresentation presentation) : base(entity, presentation)
        {
            _entity = entity;
        }

        // todo: is_visible
        public override void GetDrawInfo()
        {
            bool blinking = _entity._blinkTimer > 0;
            ColorRgb color = NodeDefenseEntity._neutralColor;
            if (_entity._currentTeam == NodeDefenseEntity.NeutralTeam)
            {
                if (blinking)
                {
                    if (_scene.Match.Rules.Teams)
                    {
                        color = Metadata.TeamColors[_entity._occupyingTeam];
                    }
                    else if (_entity._occupyingTeam == PlayerEntity.Main.TeamIndex)
                    {
                        color = NodeDefenseEntity._selfColor;
                    }
                    else
                    {
                        color = NodeDefenseEntity._enemyColor;
                    }
                }
            }
            else if (_scene.Match.Rules.Teams)
            {
                if (blinking)
                {
                    color = Metadata.TeamColors[_entity._occupyingTeam];
                }
                else
                {
                    color = Metadata.TeamColors[_entity._currentTeam];
                }
            }
            else
            {
                if (_entity._currentTeam == PlayerEntity.Main.TeamIndex)
                {
                    if (!blinking || _entity._occupyingTeam == PlayerEntity.Main.TeamIndex)
                    {
                        color = NodeDefenseEntity._selfColor;
                    }
                    else
                    {
                        color = NodeDefenseEntity._enemyColor;
                    }
                }
                else if (blinking && _entity._occupyingTeam == PlayerEntity.Main.TeamIndex)
                {
                    color = NodeDefenseEntity._selfColor;
                }
                else
                {
                    color = NodeDefenseEntity._enemyColor;
                }
            }

            _entity._terminalMat.Diffuse = color;
            _entity._ringMat.Diffuse = color;
            base.GetDrawInfo();
        }

        public override void GetDisplayVolumes()
        {
            if (Presentation.ShowVolumes == VolumeDisplay.DefenseNode)
            {
                AddVolumeItem(_entity._volume, Vector3.One);
            }
        }
    }
}
