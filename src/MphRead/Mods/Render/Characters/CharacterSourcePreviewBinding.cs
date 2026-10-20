using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenTK.Mathematics;
namespace MphRead.Mods.Render.Characters;

// Internal acceptance-only attachment. No launcher choice, serialized identity,
// controller, animation clock or native rig mapping is added.
internal sealed class CharacterSourcePreviewBinding
{
 private readonly Scene _scene;
 private readonly CharacterModelPart _part;
 private readonly Hunter _hunter;
 private readonly Model _nativeModel;
 private readonly CharacterModelPack _pack;
 private readonly PrivateCharacterAppearance _appearance;
 private readonly CharacterSourcePreviewInstance _instance;
 private CharacterSourceClip? _clip;
 internal CharacterSourceClip? SelectedSourceClip => _clip;
 private Matrix4[]? _pose;
 internal Vector3 DiagnosticRootDisplacement { get; private set; }
 private static Vector3 RootDisplacement(CharacterWeightedRenderModel model,IReadOnlyList<Matrix4> pose)
 {
  var rig=model.SourceRig!;
  for(int i=0;i<rig.Nodes.Count;i++)
   if(rig.Nodes[i].Parent<0&&rig.Nodes[i].Name=="source_bone_0_root")
    return pose[i].Row3.Xyz-rig.Nodes[i].Local.Row3.Xyz;
  return Vector3.Zero;
 }
 private bool _released;
 private CharacterSourcePreviewBinding(Scene scene,Hunter hunter,Model nativeModel,CharacterModelPack pack,PrivateCharacterAppearance appearance,string? sourceMaterialAnchor=null,CharacterModelPart part=CharacterModelPart.Biped,object? stageOwner=null)
 {(_scene,_hunter,_nativeModel,_pack,_appearance)=(scene,hunter,nativeModel,pack,appearance);_part=part;_instance=new(sourceMaterialAnchor,part,stageOwner);}
 internal static bool TryCreate(Scene scene,Hunter hunter,Model nativeModel,CharacterModelPack pack,
  PrivateCharacterAppearance appearance,IReadOnlyList<Matrix4>? pose,out CharacterSourcePreviewBinding binding)
 {
  binding=new(scene,hunter,nativeModel,pack,appearance);
  if(binding.TrySetPose(pose))return true;
  binding.Release();binding=null!;return false;
 }
 // Explicit private source-material mode. A name selects an existing native draw
 // anchor; provenance and complete source identity are still verified by runtime.
 internal static bool TryCreateWithSourceMaterials(Scene scene,Hunter hunter,Model nativeModel,CharacterModelPack pack,
  PrivateCharacterAppearance appearance,string nativeMaterialAnchor,out CharacterSourcePreviewBinding binding,object? stageOwner=null)
 {
  binding=new(scene,hunter,nativeModel,pack,appearance,nativeMaterialAnchor,stageOwner:stageOwner);
  if(!string.IsNullOrWhiteSpace(nativeMaterialAnchor)&&binding.TrySetPose(null))return true;
  binding.Release();binding=null!;return false;
 }
 internal static bool TryCreateSourceCannon(Scene scene,Hunter hunter,Model nativeModel,CharacterModelPack pack,
  PrivateCharacterAppearance appearance,string nativeMaterialAnchor,out CharacterSourcePreviewBinding binding,object? stageOwner=null)
 {
  binding=new(scene,hunter,nativeModel,pack,appearance,nativeMaterialAnchor,CharacterModelPart.ViewModel,stageOwner);
  if(!string.IsNullOrWhiteSpace(nativeMaterialAnchor)&&binding.TryResolve(out var model)&&model.SourceCannon!=null)return true;
  binding.Release();binding=null!;return false;
 }
#if !MPHREAD_SERVER && !ANDROID
 // Explicit private GPU-fixture capability; never installed into a default draw.
 internal CharacterModelRuntime.Material4FixtureTicket CaptureMaterial4Fixture()
 {
  if(!TryResolve(out var model))throw new InvalidOperationException("Current attached source preview required.");
  return CharacterModelRuntime.CaptureMaterial4Fixture(_scene,_instance,model);
 }
#endif
 internal bool TrySelectSourceClip(int ordinal)
 {
  if(_part!=CharacterModelPart.Biped||!TryResolve(out var model)||!CharacterModelRuntime.TryLoadSourceClip(_scene,_instance,model,ordinal,out var clip))return false;
  try{var pose=clip.Sample(0).ToArray();model.UpdateSourcePreviewPalette(pose);_pose=pose;_clip=clip;DiagnosticRootDisplacement=RootDisplacement(model,pose);return true;}
  catch(Exception e)when(e is InvalidDataException or InvalidOperationException or ArgumentException){return false;}
 }
 internal bool TrySetSourceTime(double seconds)
 {
  if(_part!=CharacterModelPart.Biped||_clip==null||!TryResolve(out var model)||!CharacterModelRuntime.IsSourceClipCurrent(_scene,_instance,model,_clip))return false;
  try{var pose=_clip.Sample(seconds).ToArray();model.UpdateSourcePreviewPalette(pose);_pose=pose;DiagnosticRootDisplacement=RootDisplacement(model,pose);return true;}
  catch(Exception e)when(e is InvalidDataException or InvalidOperationException or ArgumentException){return false;}
 }
 internal bool TrySetPose(IReadOnlyList<Matrix4>? pose)
 {
  if(_released||_part!=CharacterModelPart.Biped)return false;
  // Capture first. Validate the complete pose through the actual runtime before
  // replacing the previous binding pose; draw-only input cannot change later.
  var captured=pose?.ToArray();
  if(!TryResolve(out var model))return false;
  try{model.UpdateSourcePreviewPalette(captured);_pose=captured;_clip=null;DiagnosticRootDisplacement=captured==null?Vector3.Zero:RootDisplacement(model,captured);return true;}
  catch(Exception e)when(e is InvalidDataException or InvalidOperationException or ArgumentException){return false;}
 }
 private bool TryResolve(out CharacterWeightedRenderModel model)
 {
  model=null!;
  return !_released&&CharacterModelRuntime.TryGetSourcePreview(_scene,_hunter,_part,_nativeModel,
   _pack,_appearance,_instance,true,out model);
 }
 internal bool TryGet(Scene scene,Hunter hunter,CharacterModelPart part,Model nativeModel,out CharacterWeightedRenderModel model)
 {
  model=null!;
  if(_released||!ReferenceEquals(scene,_scene)||hunter!=_hunter||part!=_part||!ReferenceEquals(nativeModel,_nativeModel)
   ||!TryResolve(out model))return false;
  try{model.UpdateSourcePreviewPalette(_pose);return true;}
  catch(Exception e)when(e is InvalidDataException or InvalidOperationException or ArgumentException){model=null!;return false;}
 }
 internal void Release(){if(_released)return;_released=true;CharacterModelRuntime.ReleaseSourcePreview(_scene,_instance);_pose=null;_clip=null;DiagnosticRootDisplacement=Vector3.Zero;}
}
