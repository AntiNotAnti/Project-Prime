using System;
using System.IO;
using System.Linq;
using MphRead.Entities;
using MphRead.Mods.Multiplayer;
using OpenTK.Mathematics;

namespace MphRead.Mods.Render.Characters;

// One initialized native drone, one immutable import generation, one presentation clock.
internal sealed class CharacterSourceDroneOwner : IDisposable, ICharacterSourcePresentationOwner
{
    private readonly Scene _scene;
    private readonly PlayerEntity _player;
    private readonly Model _native;
    private readonly CharacterSourceDroneSelection _selection;
    private readonly CharacterSourcePreviewInstance _instance;
    private readonly CharacterWeightedRenderModel _model;
    private readonly CharacterSourceClip _clip;
    private readonly int _slot;
    private readonly long _generation;
    private readonly Matrix4 _fit;
    private bool _disposed;
    private ulong? _step;
    private long _life=-1;
    internal bool Ready {get;private set;}
    internal long CompletedSteps {get;private set;}
    internal double MaterialSeconds => CompletedSteps/60d;
    internal string Generation => _selection.Generation;
    internal CharacterWeightedRenderModel Model => _model;
    internal CharacterSourceClip Clip => _clip;
    internal Matrix4 Fit => _fit;
    internal CharacterSourceDroneOwner(Scene scene,PlayerEntity player,CharacterSourceDroneSelection selection)
    {
        if(!player.Initialized||player.Hunter!=Hunter.Weavel||player.IsBot
            ||player.ModCharacterGameplay!=CharacterGameplayRules.TrooperDroneV1||player.ModSourceNativeDrone==null)
            throw new InvalidOperationException("Initialized solo native Scout Drone practice owner required.");
        (_scene,_player,_native,_selection,_slot,_generation)=(scene,player,player.ModSourceNativeDrone,selection,player.SlotIndex,scene.LifetimeGeneration);
        string anchor=_native.Materials.GroupBy(m=>m.Name,StringComparer.Ordinal).Where(g=>g.Count()==1).Select(g=>g.Single())
            .First(m=>CharacterModelRuntime.CanUseAuthoredTexcoords(m.TexgenMode,true)).Name;
        _instance=new(anchor,CharacterModelPart.AlternateForm,bodyOwner:this);
        try
        {
            if(!CharacterModelRuntime.TryGetSourcePreview(scene,Hunter.Weavel,CharacterModelPart.AlternateForm,_native,
                selection.Pack,selection.Appearance,_instance,true,out _model)
                ||_model.Joints.Count!=33||_model.IndexCount!=1390*3||_model.Segments.Count!=2
                ||_model.Segments.Any(s=>s.SourceGx==null||s.SourceJointPalette is not {Count:>0 and <=32}||s.SourcePresentationPalette==null)
                ||!CharacterModelRuntime.TryLoadSourceClip(scene,_instance,_model,CharacterGeneratedDroneProfile.ClipOrdinal,out var raw))
                throw new InvalidDataException("Complete source drone could not bind to the current Scene.");
            _clip=raw.WithPinnedDroneRootMotion();
            var pose=_clip.Sample(0);var rig=_model.SourceRig!;
            int pelvis=Enumerable.Range(0,rig.Nodes.Count).Single(i=>rig.Nodes[i].Name=="source_bone_4_Skeleton_Root");
            Matrix4 global=pose[pelvis];
            for(int p=rig.Nodes[pelvis].Parent;p>=0;p=rig.Nodes[p].Parent)global*=pose[p];
            _fit=Matrix4.CreateTranslation(-global.Row3.Xyz)*Matrix4.CreateScale(.3f);
            _model.UpdateSourcePreviewPalette(pose);
        }
        catch {CharacterModelRuntime.ReleaseSourcePreview(scene,_instance);throw;}
    }
    public bool Owns(Scene scene,Model native,CharacterModelPack pack,PrivateCharacterAppearance appearance,CharacterSourcePreviewInstance instance)
        =>!_disposed&&ReferenceEquals(scene,_scene)&&!scene.Exiting&&!scene.SideScene&&!scene.IsReplayLab
        &&scene.LifetimeGeneration==_generation&&ReferenceEquals(native,_native)&&ReferenceEquals(_player.ModSourceNativeDrone,_native)
        &&ReferenceEquals(pack,_selection.Pack)&&appearance==_selection.Appearance&&ReferenceEquals(instance,_instance)
        &&_player.Initialized&&!_player.IsBot&&_player.Hunter==Hunter.Weavel&&_player.SlotIndex==_slot
        &&_slot==0&&scene.Players.PlayerCount==1&&scene.Players.PlayersCreated==1
        &&ReferenceEquals(scene.Players.Main,_player)&&scene.GameState.Mode==GameMode.Battle
        &&(uint)_slot<scene.Players.Items.Count&&ReferenceEquals(scene.Players.Items[_slot],_player)
        &&_player.ModCharacterGameplay==CharacterGameplayRules.TrooperDroneV1&&!scene.Services.IsReplica&&!scene.GameState.SinglePlayer
        &&!Network.NetSession.Active&&!Network.DemoPlayback.IsActive&&!Network.ReplayCapture.LiveRecordingActive;
    internal bool Current => Owns(_scene,_native,_selection.Pack,_selection.Appearance,_instance)
        &&CharacterModelRuntime.IsSourceBodyCurrent(_scene,_instance,_model);
    public bool IsCurrent(CharacterModelPart part) => part==CharacterModelPart.AlternateForm&&Current;
    internal void Step(ulong step,long life,bool supported)
    {
        if(_disposed||_step==step)return;
        _step=step;
        if(!Current){Ready=false;return;}
        if(_life!=life){_life=life;CompletedSteps=0;}
        Ready=supported;
        if(!Ready)return;
        CompletedSteps++;
        _model.UpdateSourcePreviewPalette(_clip.Sample(MaterialSeconds%_clip.Duration));
    }
    internal bool Prepare(Matrix4 nativeFacing,out Matrix4 presentation)
    {
        presentation=_fit*nativeFacing;
        if(!Ready||!Current)return false;
        _model.PrepareSourcePreviewPresentation(presentation);return true;
    }
    public void Dispose()
    {
        if(_disposed)return;
        _disposed=true;Ready=false;CharacterModelRuntime.ReleaseSourcePreview(_scene,_instance);
    }
}
