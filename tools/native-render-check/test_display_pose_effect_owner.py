"""Source-linked pure owner regression; never builds a Scene or invokes GL."""
import os
import hashlib
from pathlib import Path
import re
import subprocess
import tempfile
import unittest
from xml.sax.saxutils import escape
from frozen_pose_runtime import frozen_runtime

REPO = Path(__file__).resolve().parents[2]
PROPOSED = Path(os.environ.get("PROJECT_PRIME_POSE_EFFECT_SOURCE", str(REPO / "src/MphRead")))

def source(name):
    path = PROPOSED / name
    if not path.is_file():
        path = PROPOSED / {"PlayerDraw.cs": "Entities/Players", "Effects.cs": "Formats"}.get(name, "") / name
    return path.read_text()

def body(source, marker):
    start=source.index(marker); opening=source.index("{",start); depth=0
    for end in range(opening,len(source)):
        if source[end]=="{": depth+=1
        elif source[end]=="}":
            depth-=1
            if depth==0:return source[start:end+1]
    raise AssertionError("unterminated source body")

class EffectOwnerTests(unittest.TestCase):
    def test_real_transforms_and_actual_presentation_owner_branches(self):
        dotnet, reference = frozen_runtime(self)
        renderer=source("Renderer.cs"); player=source("PlayerDraw.cs"); effects=source("Effects.cs")
        begin=renderer.index("private readonly List<EffectEntry> _presentationOverrideEntries")
        end=renderer.index("internal int PresentationEffectOverrideCount",begin)
        end=renderer.index(";",end)+1
        owner=renderer[begin:end]
        fields="\n".join(re.findall(r"internal (?:Matrix4\?|bool\??) Draw(?:Transform\w+|EnabledOverride) \{ get; set; \}",effects))
        self.assertIn("DrawTransformUsesViewModel",fields)
        self.assertIn("DrawEnabledOverride",fields)
        expression=re.search(r"bool presentationOnlyMainEffect\s*=\s*(.*?);",player,re.S).group(1)
        identity=re.search(r"ReferenceEquals\(this,\s*_scene\.Players\.Main\)",expression)
        self.assertIsNotNone(identity,"main ownership must survive an external camera view")
        real_identity=identity.group(0).replace("this","actor").replace("_scene","Owner(actor)")
        expression=expression.replace(identity.group(0),"main")
        for old,new in {"_scene.Services.IsReplica":"replica","Mods.Network.NetSession.Active":"network","Mods.Network.DemoPlayback.IsActive":"replay","Mods.KillCam.Active":"killcam","Mods.SpectatorMode.IsSpectating":"spectator","FrameTiming.Active":"active"}.items():expression=expression.replace(old,new)
        region=player[player.index("bool presentationOnlyMainEffect"):]
        blocks=[body(region,"if ("+name+" != null)") for name in ["_chargeEffect","_muzzleEffect"]]
        enabled=body(player,"private void ModSetPlayerEffectDrawEnabled(").replace("private void", "private static void", 1)
        muzzle=re.search(r"Vector3 effectDrawMuzzle\s*=\s*(.*?);",region,re.S).group(1)
        projection=re.search(r"bool viewModelEffect\s*=\s*(.*?);",renderer,re.S).group(1).replace("particle.Owner.EffectEntry","entry")
        code=r"""
using OpenTK.Mathematics;
using System.Reflection;
using System.Runtime.CompilerServices;
using MphRead;
using MphRead.Entities;
using MphRead.Effects;
using EffectEntry=ProbeEffectEntry;
class ProbeEffectEntry:MphRead.Effects.EffectEntry { ACTUAL_FIELDS }
class ServicesOwner { public bool IsReplica; }
class SceneOwner
{
    public int _pendingEffectSteps;
    public bool ModThirdPersonCameraActive;
    public ServicesOwner Services=new();
    ACTUAL_OWNER
    internal static bool UsesViewModel(EffectEntry? entry)=>ACTUAL_PROJECTION;
}
class Program
{
 static int checks;
 static void Check(bool x,string why){checks++;if(!x)throw new Exception(why);}
 static SceneOwner _scene=new();
 static Scene Owner(PlayerEntity actor)=>(Scene)typeof(EntityBase).GetField("_scene",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(actor)!;
 static bool StableIdentity(PlayerEntity actor)=>ACTUAL_IDENTITY;
 ACTUAL_ENABLED
 static void Apply(bool main,bool replica,bool network,bool replay,bool killcam,bool spectator,bool active,bool thirdPerson,EffectEntry effect,bool charge)
 {
  _scene.Services.IsReplica=replica;_scene.ModThirdPersonCameraActive=thirdPerson;
  EffectEntry? _chargeEffect=charge?effect:null,_muzzleEffect=charge?null:effect;
  Vector3 _gunVec2=Vector3.UnitZ,_gunVec1=Vector3.UnitY,muzzlePos=new(13,17,19);
  bool localThirdPersonActor=main&&thirdPerson;
  Vector3 displayMuzzle=new(23,29,31);
  bool presentationOnlyMainEffect=ACTUAL_EXPRESSION;
  Vector3 effectDrawMuzzle=ACTUAL_MUZZLE_POSITION;
  ACTUAL_CHARGE
  ACTUAL_MUZZLE
 }
 static Matrix4 GetTransformMatrix(Vector3 facing,Vector3 up,Vector3 position)=>EntityBase.GetTransformMatrix(facing,up,position);
 static void Main()
 {
  Matrix4 baseline=Matrix4.CreateTranslation(1,2,3),desired=GetTransformMatrix(Vector3.UnitZ,Vector3.UnitY,new(13,17,19));
  foreach(bool charge in new[]{false,true})
  foreach(bool thirdPerson in new[]{false,true})
  foreach(int pending in new[]{0,1,4})
  for(int excluded=-1;excluded<7;excluded++)
  {
   bool main=excluded!=0,replica=excluded==1,network=excluded==2,replay=excluded==3,killcam=excluded==4,spectator=excluded==5,active=excluded!=6;
   var effect=new EffectEntry();var element=new EffectElementEntry { OwnTransform=baseline,Transform=baseline,Flags=EffElemFlags.DrawEnabled|EffElemFlags.UseTransform };effect.Elements.Add(element);
   _scene._pendingEffectSteps=pending;Apply(main,replica,network,replay,killcam,spectator,active,thirdPerson,effect,charge);
   bool presentation=(excluded==-1||(excluded==2&&thirdPerson))&&pending==0;
   bool only=presentation||replica;
   Matrix4 pictured=presentation&&thirdPerson?GetTransformMatrix(Vector3.UnitZ,Vector3.UnitY,new(23,29,31)):desired;
   Check(element.OwnTransform==(only?baseline:desired),"source predicate preserves exact canonical positive-pending/excluded path");
   Check(element.Transform==baseline,"source branch never substitutes particle simulation transform");
   Check(effect.DrawTransformOverride==(only?pictured:(Matrix4?)null),"main zero-pending and replica paths install exact current presentation");
   Check(effect.DrawEnabledOverride==(replica?(bool?)true:null),"replica visibility is draw-only; local visibility stays canonical");
   Check(!SceneOwner.UsesViewModel(effect),"world path retains world projection");
   Check(element.Flags==(EffElemFlags.DrawEnabled|EffElemFlags.UseTransform),"existing enabled flags unchanged");
   _scene.ClearPresentationEffectTransforms();Check(effect.DrawTransformOverride==null&&!effect.DrawTransformUsesViewModel&&effect.DrawEnabledOverride==null,"existing owner clears matrix, projection and visibility");
  }
  var first=new EffectEntry();_scene.SetPresentationEffectTransform(first,desired);
  Check(SceneOwner.UsesViewModel(first),"existing two-argument caller retains viewmodel default");
  _scene.SetPresentationEffectTransform(first,baseline,viewModel:false);
  Check(!SceneOwner.UsesViewModel(first)&&_scene.PresentationEffectOverrideCount==1,"same owner replaces projection and deduplicates exact entry");
  _scene.ClearPresentationEffectTransforms();Check(first.DrawTransformOverride==null&&!first.DrawTransformUsesViewModel&&_scene.PresentationEffectOverrideCount==0,"clear fully retires presentation");
  Check(!SceneOwner.UsesViewModel(null)&&!SceneOwner.UsesViewModel(first),"null/cleared effects retain world projection");
  var real=new MphRead.Effects.EffectEntry();var realElement=new EffectElementEntry();real.Elements.Add(realElement);real.Transform(Vector3.UnitZ,Vector3.UnitY,new(13,17,19));
  Check(realElement.OwnTransform==desired,"world override matrix matches exact frozen real Transform math");
  // Closed object data only: neither a Scene nor PlayerEntity constructor runs.
  // Execute the real frozen IsMainPlayer property and exact current identity
  // expression so a view-dependent ownership boolean cannot hide this case.
  var flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
  var world=(Scene)RuntimeHelpers.GetUninitializedObject(typeof(Scene));
  var players=new ScenePlayerRegistry();
  typeof(Scene).GetField("<Players>k__BackingField",flags)!.SetValue(world,players);
  var actor=(PlayerEntity)RuntimeHelpers.GetUninitializedObject(typeof(PlayerEntity));
  var foreign=(PlayerEntity)RuntimeHelpers.GetUninitializedObject(typeof(PlayerEntity));
  var replacement=(PlayerEntity)RuntimeHelpers.GetUninitializedObject(typeof(PlayerEntity));
  foreach(var player in new[]{actor,foreign,replacement})typeof(EntityBase).GetField("_scene",flags)!.SetValue(player,world);
  var values=(PlayerEntity[])players.Items;values[0]=actor;values[1]=foreign;
  var camera=typeof(Scene).GetField("_cameraMode",flags)!;
  camera.SetValue(world,CameraMode.Player);
  Check(actor.IsMainPlayer&&StableIdentity(actor),"actual Player view recognizes exact main");
  Check(!foreign.IsMainPlayer&&!StableIdentity(foreign),"foreign actor never gains main ownership");
  camera.SetValue(world,CameraMode.Roam);
  Check(!actor.IsMainPlayer&&StableIdentity(actor),"actual Roam flips legacy property while exact identity remains");
  Check(!StableIdentity(foreign),"external view does not grant foreign ownership");
  players.MainPlayerIndex=1;
  Check(!StableIdentity(actor)&&StableIdentity(foreign),"main switch retires old exact actor witness");
  Check(foreign.SlotIndex==replacement.SlotIndex&&!ReferenceEquals(foreign,replacement),"replacement reuses numeric slot but not lifetime identity");
  values[1]=replacement;
  Check(!StableIdentity(foreign)&&StableIdentity(replacement),"new object lifetime rejects old same-slot actor");
  camera.SetValue(world,CameraMode.Player);
  Check(replacement.IsMainPlayer&&!foreign.IsMainPlayer,"actual native property follows current registry object");
  Console.WriteLine("EFFECT_OWNER_SOURCE_PASS checks="+checks);
 }
}
"""
        for old,new in {"ACTUAL_FIELDS":fields,"ACTUAL_OWNER":owner,"ACTUAL_PROJECTION":projection,"ACTUAL_EXPRESSION":expression,"ACTUAL_IDENTITY":real_identity,"ACTUAL_CHARGE":blocks[0],"ACTUAL_MUZZLE_POSITION":muzzle,"ACTUAL_MUZZLE":blocks[1],"ACTUAL_ENABLED":enabled}.items():code=code.replace(old,new)
        with tempfile.TemporaryDirectory(prefix="prime-pose-effect-owner-") as tmp:
            scratch=Path(tmp);(scratch/"Program.cs").write_text(code)
            refs="".join('<Reference Include="'+p.stem+'"><HintPath>'+escape(str(p))+'</HintPath></Reference>' for p in [reference,reference.with_name("OpenTK.Mathematics.dll")])
            (scratch/"check.csproj").write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings><NuGetAudit>false</NuGetAudit><UseSharedCompilation>false</UseSharedCompilation></PropertyGroup><ItemGroup>'+refs+'</ItemGroup></Project>')
            env=dict(os.environ,MSBuildEnableWorkloadResolver="false",DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE="true",DOTNET_SKIP_FIRST_TIME_EXPERIENCE="true")
            result=subprocess.run([str(dotnet),"run","--project",str(scratch/"check.csproj"),"-c","Release","--verbosity","quiet"],cwd=scratch,env=env,capture_output=True,text=True,timeout=60)
            self.assertEqual(result.returncode,0,result.stdout+result.stderr);self.assertIn("EFFECT_OWNER_SOURCE_PASS",result.stdout)
            self.assertNotRegex(result.stdout+result.stderr,r"warning (CS|NU|MSB)")

    def test_canonical_simulation_and_frozen_schema_unchanged(self):
        proposed=source("Renderer.cs")
        # Exact methods pinned before the approved main-player picture override.
        # Native effect stepping/spawning/collision source must not change here.
        frozen={"private void PrepareEffectDraw()":"f55db2322b32b38e3fd2850556bd48cc4ade62cc11f84aecbbf1a38402865106",
                "private void ProcessEffectsCore(":"42be7da826aeb39793ee9574f0a20b387bf517ffbad40edda6fd2e82b7cc718d"}
        for marker,digest in frozen.items():
            self.assertEqual(hashlib.sha256(body(proposed,marker).encode()).hexdigest(),digest,marker)
        self.assertLess(proposed.index("player.Draw();",proposed.index("private void GetDrawItems()")),proposed.index("PrepareEffectDraw();",proposed.index("private void GetDrawItems()")))
        schema=(REPO/"src/MphRead/Mods/Replay/ReplayWorldSchemas.cs").read_text();effect=schema[schema.index('["MphRead.Effects.EffectEntry"]'):schema.index('["MphRead.Effects.EffectElementEntry"]')]
        self.assertNotIn("DrawTransform",effect)
        projection=(REPO/"src/MphRead/Mods/Replay/SceneReplayProjection.cs").read_text();self.assertNotIn("DrawTransform",projection)

    def test_existing_draw_and_cleanup_owner_clear_before_use(self):
        wrapper=body(source("Mods/Render/SceneDsLayout.cs"),"public void OnDrawFrame()")
        self.assertEqual(wrapper.count("OnDrawFrameCore();"),1)
        self.assertNotIn("GetDrawItems();",wrapper)
        text=source("Renderer.cs");draw=body(text,"private void OnDrawFrameCore()")
        self.assertIn("ClearPresentationEffectTransforms();",draw)
        self.assertLess(draw.index("ClearPresentationEffectTransforms();"),draw.index("GetDrawItems();"))
        self.assertIn("ClearPresentationEffectTransforms();",body(text,"public void ClearEffects()"))
        self.assertEqual(text.count("private readonly List<EffectEntry> _presentationOverrideEntries"),1)

if __name__=="__main__":unittest.main(verbosity=2)
