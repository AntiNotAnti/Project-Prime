using System;
using System.Diagnostics;
using MphRead.Formats;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    public sealed class FhTriggerVolumeEntityPresentation : EntityPresentation
    {
        private readonly FhTriggerVolumeEntity _entity;
        public FhTriggerVolumeEntityPresentation(FhTriggerVolumeEntity entity, ScenePresentation presentation) : base(entity, presentation)
        {
            _entity = entity;
        }

        public override void GetDisplayVolumes()
        {
            if (_entity._data.Subtype != FhTriggerType.Threshold && (Presentation.ShowVolumes == VolumeDisplay.TriggerParent || Presentation.ShowVolumes == VolumeDisplay.TriggerChild))
            {
                Vector3 color = Presentation.ShowVolumes == VolumeDisplay.TriggerParent ? _entity._parentEventColor : _entity._childEventColor;
                AddVolumeItem(_entity._volume, color);
            }
        }
    }
}
