using MphRead.Entities;
using MphRead.Effects;
using MphRead.Formats;
using MphRead.Formats.Collision;
using MphRead.Formats.Culling;
using OpenTK.Mathematics;
namespace MphRead
{
    public enum AfterFade
    {
        None,
        Exit,
        LoadRoom,
    }

    /// <summary>Optional visual requests consumed by a client host. The world requires no device.</summary>
    public interface IScenePresentation
    {
        float ProjectionAspect { get; }
        float ProjectionFarClip { get; }
        Vector3 ViewPosition { get; }
        bool ControlsPlayer { get; }
        bool FrameAdvance { get; }
        bool FrameAdvanceLastFrame { get; }
        bool MinimalResources { get; }
        void PrepareEntity(EntityBase entity);
        void InitEntity(EntityBase entity);
        void SetRoomValues(RoomMetadata metadata);
        void RoomLoaded(RoomMetadata metadata);
        void BeforeWorldUpdate();
        void AfterWorldUpdate();
        void SetFade(FadeType type, float length, bool overwrite, AfterFade afterFade = AfterFade.None, float delay = 0);
        int CountElements(int effectId);
        void UnloadModel(Model model);
        void LoadModel(string name, bool firstHunt = false);
        void LoadModel(Model model, bool isRoom = false);
        void LoadEffect(int effectId, bool persistent);
        EffectEntry? SpawnEffectGetEntry(int effectId, Vector3 facing, Vector3 up, Vector3 position, EntityCollision? entCol = null);
        EffectEntry? SpawnEffectGetEntry(int effectId, Matrix4 transform, EntityCollision? entCol = null);
        void SpawnEffect(int effectId, Vector3 facing, Vector3 up, Vector3 position, bool child = false, EntityCollision? entCol = null);
        void SpawnEffect(int effectId, Matrix4 transform, bool child = false, EntityCollision? entCol = null);
        void UnlinkEffectEntry(EffectEntry entry);
        void DetachEffectEntry(EffectEntry entry, bool setExpired);
        void ClearEffects();
        void ClearNonPersistentEffects();
        void AddSingleParticle(SingleType type, Vector3 position, Vector3 color, float alpha, float scale);
        BeamEffectEntity? InitBeamEffect(BeamEffectEntityData data);
        void UnlinkBeamEffect(BeamEffectEntity entry);
        bool IsEntityVisible(NodeRef nodeRef);
        bool IsEntityAudible(NodeRef nodeRef);
    }
}
