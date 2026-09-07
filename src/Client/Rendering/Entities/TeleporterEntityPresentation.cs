using System;
using System.Diagnostics;
using MphRead.Formats;
using MphRead.Formats.Culling;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    public sealed class TeleporterEntityPresentation : EntityPresentation
    {
        private readonly TeleporterEntity _entity;
        public TeleporterEntityPresentation(TeleporterEntity entity, ScenePresentation presentation) : base(entity, presentation)
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
            if (Presentation.ShowVolumes == VolumeDisplay.Teleporter)
            {
                CollisionVolume volume;
                if (_entity._data.Invisible != 0 || _entity._data.ArtifactId < 8)
                {
                    volume = new CollisionVolume(Entity.Position.AddY(1.0f), 1.0f);
                }
                else
                {
                    volume = new CollisionVolume(Entity.Position.AddY(1.5f), 1.0f);
                }

                AddVolumeItem(volume, Vector3.UnitX);
            }
        }
    }
}
