using MphRead.Entities;
using MphRead.Effects;
using MphRead.Formats;
using MphRead.Formats.Collision;
using MphRead.Formats.Culling;
using OpenTK.Mathematics;
namespace MphRead
{
    public partial class Scene
    {
        public Matrix4 GetPerspectiveMatrix(float fov)
        {
            // Preserve the former zero-sized headless viewport's undefined aspect;
            // clients supply their current viewport and optional room clipping plane.
            float aspect = Presentation?.ProjectionAspect ?? float.NaN;
            return Matrix4.CreatePerspectiveFieldOfView(fov, aspect, 0.0625f,
                Presentation?.ProjectionFarClip ?? 10000f);
        }
        public Vector3 ViewPosition => Presentation?.ViewPosition ?? LocalPlayer?.CameraInfo.Position ?? Vector3.Zero;
        public bool ControlsPlayer => Presentation?.ControlsPlayer ?? false;
        public bool FrameAdvance => Presentation?.FrameAdvance ?? false;
        public bool FrameAdvanceLastFrame => Presentation?.FrameAdvanceLastFrame ?? false;
        public void InitEntity(EntityBase entity) => Presentation?.InitEntity(entity);
        public void SetFade(FadeType type, float length, bool overwrite, AfterFade afterFade = AfterFade.None, float delay = 0)
            => Presentation?.SetFade(type, length, overwrite, afterFade, delay);
        public int CountElements(int effectId) => Presentation?.CountElements(effectId) ?? 0;
        public void UnloadModel(Model model) => Presentation?.UnloadModel(model);
        public void LoadModel(string name, bool firstHunt = false) => Presentation?.LoadModel(name, firstHunt);
        public void LoadModel(Model model, bool isRoom = false) => Presentation?.LoadModel(model, isRoom);
        public void LoadEffect(int effectId, bool persistent) => Presentation?.LoadEffect(effectId, persistent);
        public EffectEntry? SpawnEffectGetEntry(int effectId, Vector3 facing, Vector3 up, Vector3 position, EntityCollision? entCol = null) => Presentation?.SpawnEffectGetEntry(effectId, facing, up, position, entCol);
        public EffectEntry? SpawnEffectGetEntry(int effectId, Matrix4 transform, EntityCollision? entCol = null) => Presentation?.SpawnEffectGetEntry(effectId, transform, entCol);
        public void SpawnEffect(int effectId, Vector3 facing, Vector3 up, Vector3 position, bool child = false, EntityCollision? entCol = null) => Presentation?.SpawnEffect(effectId, facing, up, position, child, entCol);
        public void SpawnEffect(int effectId, Matrix4 transform, bool child = false, EntityCollision? entCol = null) => Presentation?.SpawnEffect(effectId, transform, child, entCol);
        public void SpawnImpactEffect(int effectId, Matrix4 transform,
            EntityCollision? entCol = null)
            => Presentation?.SpawnImpactEffect(effectId, transform, entCol);
        public void UnlinkEffectEntry(EffectEntry entry) => Presentation?.UnlinkEffectEntry(entry);
        public void DetachEffectEntry(EffectEntry entry, bool setExpired) => Presentation?.DetachEffectEntry(entry, setExpired);
        public void ClearEffects() => Presentation?.ClearEffects();
        public void ClearNonPersistentEffects() => Presentation?.ClearNonPersistentEffects();
        public void AddSingleParticle(SingleType type, Vector3 position, Vector3 color, float alpha, float scale) => Presentation?.AddSingleParticle(type, position, color, alpha, scale);
        public BeamEffectEntity? InitBeamEffect(BeamEffectEntityData data) => Presentation?.InitBeamEffect(data);
        public void UnlinkBeamEffect(BeamEffectEntity entry) => Presentation?.UnlinkBeamEffect(entry);
        public bool IsEntityVisible(NodeRef nodeRef) => Presentation?.IsEntityVisible(nodeRef) ?? true;
        public bool IsEntityAudible(NodeRef nodeRef) => Presentation?.IsEntityAudible(nodeRef) ?? true;
    }
}
