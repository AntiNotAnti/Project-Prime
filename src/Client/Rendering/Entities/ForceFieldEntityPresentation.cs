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

        protected override EnhancedForceFieldDrawState? GetEnhancedForceFieldDrawState(
            ModelInstance inst, int modelIndex, int nodeIndex, int meshIndex)
        {
            ulong baseKey = _entity.Id >= 0
                ? VisualLightSourceKey.ForAuthored(
                    VisualLightSourceKind.ForceField, _entity.Id)
                : Presentation.GetVisualLightSourceKey(
                    VisualLightSourceKind.ForceField, _entity);
            ulong key = EnhancedPresentationSourceKey.ForSubresource(baseKey,
                nodeIndex, meshIndex);
            return new EnhancedForceFieldDrawState(key,
                EnhancedForceFieldProfiles.Default,
                Presentation.CapturedPresentationTime);
        }
    }
}
