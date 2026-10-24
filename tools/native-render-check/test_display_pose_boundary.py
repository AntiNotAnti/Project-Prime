"""Pure frozen-engine values + current source predicate; never draws a scene."""
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile
import unittest
from xml.sax.saxutils import escape
from frozen_pose_runtime import frozen_runtime


class DisplayPoseBoundaryTests(unittest.TestCase):
    def test_first_warmup_neutral_prevents_focus_alias_release(self):
        root = Path(__file__).resolve().parents[2]
        dotnet, reference = frozen_runtime(self)
        source = (root / "tools/native-render-check/DisplayPoseQualification.cs").read_text()
        match = re.search(r'case "warmup":(.*?)break;', source, re.S)
        self.assertIsNotNone(match)
        code = r'''
using System.Reflection;
using System.Runtime.CompilerServices;
using MphRead.Entities;
using MphRead.Mods.Input;
using MphRead.Mods.Network;
void Check(bool value,string label){if(!value)throw new Exception(label);}
var flags=BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Instance|BindingFlags.Static;
// Closed actor data only: avoid Scene, native/audio initialization and settings
// loading. Rest touches only Health, Controls and Input.HasInput on this path.
var player=(PlayerEntity)RuntimeHelpers.GetUninitializedObject(typeof(PlayerEntity));
var controls=(PlayerControls)typeof(PlayerControls).GetMethod("CreateDefault",flags)!.Invoke(null,null)!;
typeof(PlayerEntity).GetField("<Controls>k__BackingField",flags)!.SetValue(player,controls);
var input=typeof(PlayerEntity).GetField("<Input>k__BackingField",flags)!;
input.SetValue(player,Activator.CreateInstance(input.FieldType,true));
bool AliasesReleased(PointerBindings bindings)
{Check(bindings.Resolve(controls.Shoot)&&bindings.Resolve(controls.AltAttack),"native shoot/alt share left primary");return controls.Shoot.IsReleased&&controls.AltAttack.IsReleased;}
NetTestScript.Rest(player,true);
Check(controls.Shoot.IsDown,"actual Rest requests fire for initial Health0");
var ordinary=new PointerBindings();var focusCleared=new PointerBindings();
ordinary.Synchronize(controls.Shoot.IsDown,false);focusCleared.Synchronize(controls.Shoot.IsDown,false);
focusCleared.Synchronize(false,false);ordinary.Update(false,false);focusCleared.Update(false,false);
Check(AliasesReleased(ordinary)&&!AliasesReleased(focusCleared),"actual admission baseline reproduces historical alias release difference");
void Warmup(int tick){ACTUAL_WARMUP}
Warmup(1);
Check(controls.All.All(b=>!b.IsDown&&!b.IsPressed&&!b.IsReleased),"actual first fixture tick is completely neutral");
ordinary.Synchronize(controls.Shoot.IsDown,false);focusCleared.Synchronize(controls.Shoot.IsDown,false);focusCleared.Synchronize(false,false);
for(int tick=2;tick<=32;tick++)
{
 ordinary.Update(false,false);focusCleared.Update(false,false);
 Check(!AliasesReleased(ordinary)&&!AliasesReleased(focusCleared),"neutral admission avoids rather than postpones old release");
}
ordinary.Update(true,false);focusCleared.Update(true,false);ordinary.Resolve(controls.Shoot);
Check(controls.Shoot.IsPressed,"later real press remains available");focusCleared.Resolve(controls.Shoot);Check(controls.Shoot.IsPressed,"focus path later real press remains available");
ordinary.Update(false,false);focusCleared.Update(false,false);Check(AliasesReleased(ordinary)&&AliasesReleased(focusCleared),"later intended release remains exact in both paths");
Console.WriteLine("NEUTRAL_WARMUP_SOURCE_PASS; actual Rest/Controls/PointerBindings + actual fixture branch; no Scene/GL");
'''.replace("ACTUAL_WARMUP", match.group(1))
        with tempfile.TemporaryDirectory(prefix="prime-display-pose-neutral-") as temporary:
            scratch = Path(temporary)
            (scratch / "Program.cs").write_text(code)
            dependencies = (reference, reference.with_name("OpenTK.Mathematics.dll"),
                            reference.with_name("OpenTK.Windowing.GraphicsLibraryFramework.dll"))
            refs = "".join('<Reference Include="' + p.stem + '"><HintPath>' + escape(str(p))
                + '</HintPath></Reference>' for p in dependencies)
            (scratch / "check.csproj").write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
                '<OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable>'
                '<ImplicitUsings>enable</ImplicitUsings><NuGetAudit>false</NuGetAudit><UseSharedCompilation>false</UseSharedCompilation>'
                '</PropertyGroup><ItemGroup>' + refs + '</ItemGroup></Project>')
            env = dict(os.environ, DOTNET_CLI_HOME=str(scratch / "cli"), MSBuildEnableWorkloadResolver="false",
                       DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE="true", DOTNET_NOLOGO="1")
            run = subprocess.run([str(dotnet), "run", "--project", str(scratch / "check.csproj"), "-c", "Release",
                                  "--verbosity", "quiet"], cwd=scratch, env=env,
                                 capture_output=True, text=True, timeout=60)
            self.assertEqual(run.returncode, 0, run.stdout + run.stderr)
            self.assertIn("NEUTRAL_WARMUP_SOURCE_PASS", run.stdout)

    def test_actual_charge_branch_changes_only_retreat_input_for_fixed_steps(self):
        root = Path(__file__).resolve().parents[2]
        dotnet, reference = frozen_runtime(self)
        source = (root / "tools/native-render-check/DisplayPoseQualification.cs").read_text()
        match = re.search(r'case "charge":(.*?)break;', source, re.S)
        self.assertIsNotNone(match)
        code = r'''
using System.Reflection;
using System.Runtime.CompilerServices;
using MphRead.Entities;
using MphRead.Mods.Network;
void Check(bool value,string label){if(!value)throw new Exception(label);}
var flags=BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Instance|BindingFlags.Static;
var player=(PlayerEntity)RuntimeHelpers.GetUninitializedObject(typeof(PlayerEntity));
var controls=(PlayerControls)typeof(PlayerControls).GetMethod("CreateDefault",flags)!.Invoke(null,null)!;
typeof(PlayerEntity).GetField("<Controls>k__BackingField",flags)!.SetValue(player,controls);
var input=typeof(PlayerEntity).GetField("<Input>k__BackingField",flags)!;
input.SetValue(player,Activator.CreateInstance(input.FieldType,true));
void Charge(int tick,string _poseScenario){ACTUAL_CHARGE}
int changed=0;
for(int tick=361;tick<=480;tick++)
{
 controls.ClearAll();Charge(tick,DisplayPosePlan.OriginalScenario);
 var original=controls.All.Select(c=>(c.IsDown,c.IsPressed,c.IsReleased)).ToArray();
 Check(!controls.MoveDown.IsDown&&controls.Shoot.IsDown==(tick<460),"original fire and movement unchanged");
 controls.ClearAll();Charge(tick,DisplayPosePlan.RetreatScenario);
 Check(controls.MoveDown.IsDown==(tick<=390)&&controls.Shoot.IsDown==(tick<460),"fixed retreat and original fire timeline");
 for(int bind=0;bind<controls.All.Length;bind++)
 {
  var current=controls.All[bind];bool differs=original[bind]!=(current.IsDown,current.IsPressed,current.IsReleased);
  Check(!differs||(ReferenceEquals(current,controls.MoveDown)&&tick<=390),"only MoveDown changes during fixed thirty steps");
  if(differs)changed++;
 }
}
Check(changed==30,"exactly thirty source-linked input differences");
Console.WriteLine("RETREAT_CHARGE_SOURCE_PASS; actual HoldFire/controls and fixture branch; no Scene/GL");
'''.replace("ACTUAL_CHARGE", match.group(1))
        with tempfile.TemporaryDirectory(prefix="prime-display-pose-retreat-") as temporary:
            scratch = Path(temporary)
            (scratch / "Program.cs").write_text(code)
            refs = "".join('<Reference Include="' + p.stem + '"><HintPath>' + escape(str(p))
                + '</HintPath></Reference>' for p in (reference, reference.with_name("OpenTK.Mathematics.dll"),
                reference.with_name("OpenTK.Windowing.GraphicsLibraryFramework.dll")))
            (scratch / "check.csproj").write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
                '<OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable>'
                '<ImplicitUsings>enable</ImplicitUsings><NuGetAudit>false</NuGetAudit><UseSharedCompilation>false</UseSharedCompilation>'
                '</PropertyGroup><ItemGroup>' + refs + '<Compile Include="' + escape(str(root /
                "tools/native-render-check/DisplayPosePlan.cs")) + '" /></ItemGroup></Project>')
            env = dict(os.environ, MSBuildEnableWorkloadResolver="false",
                       DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE="true", DOTNET_NOLOGO="1")
            run = subprocess.run([str(dotnet), "run", "--project", str(scratch / "check.csproj"), "-c", "Release",
                                  "--verbosity", "quiet"], cwd=scratch, env=env,
                                 capture_output=True, text=True, timeout=60)
            self.assertEqual(run.returncode, 0, run.stdout + run.stderr)
            self.assertIn("RETREAT_CHARGE_SOURCE_PASS", run.stdout)

    def test_current_actor_fractional_animation_anchor_guard(self):
        root = Path(__file__).resolve().parents[2]
        dotnet, reference = frozen_runtime(self)
        source = (root / "src/MphRead/Entities/Players/PlayerDraw.cs").read_text()
        def expression(name):
            match = re.search(r"bool " + name + r"\s*=\s*(.*?);", source, re.S)
            self.assertIsNotNone(match, "actual source ownership predicate missing: " + name)
            return match.group(1)
        eligible = expression("ordinaryOfflinePresentation")
        for old, new in {"_scene.Services.IsReplica": "replica", "Mods.Network.NetSession.Active": "network",
                         "IsMainPlayer": "main", "Mods.SpectatorMode.IsSpectating": "spectator",
                         "FrameTiming.Active": "active", "FrameTiming.HighRefreshPresentation": "high"}.items():
            eligible = eligible.replace(old, new)
        isolation = expression("isolateBipedAnchors")
        code = r'''
using System.Reflection;
using MphRead;
using OpenTK.Mathematics;
void Check(bool value,string label){if(!value)throw new Exception(label);}
bool Guard(bool replica,bool network,bool main,bool spectator,bool drawAlive,bool active,bool high,bool carry,bool interpolatedPose,bool localThirdPersonActor=false,bool crouchInterpolated=false)
{
 bool ordinaryOfflinePresentation = ELIGIBLE;
 bool ordinaryOfflineActor = ordinaryOfflinePresentation && carry;
 bool isolateLocalOrOfflineActor = ACTOR_ISOLATION;
 bool isolateBipedAnchors = ISOLATION;
 return isolateBipedAnchors;
}
var flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
var assembly=typeof(AnimationInfo).Assembly;
double alpha=0.9999999999999841;
var lifetimeType=assembly.GetType("MphRead.Mods.Render.DisplayPoseLifetime",true)!;
var lifetime=Activator.CreateInstance(lifetimeType,new object?[]{1L,new object(),0UL,(ushort)0,(ushort)0,(ushort)0,(byte)0,0L,true,false,(byte)0})!;
var historyType=assembly.GetType("MphRead.Mods.Render.DisplayPoseHistory",true)!;
var history=Activator.CreateInstance(historyType)!;
Matrix4 previous=Matrix4.Identity,current=Matrix4.CreateTranslation(1,2,3);
var capture=historyType.GetMethod("Capture",flags)!;
object[] CaptureArgs(Matrix4 value,ulong tick) => capture.GetParameters().Length == 4
    ? new object[]{value,lifetime,tick,16f} : new object[]{value,lifetime,tick};
capture.Invoke(history,CaptureArgs(previous,377UL));capture.Invoke(history,CaptureArgs(current,378UL));
object?[] sampleArgs={current,lifetime,379UL,alpha,null};
Check((bool)historyType.GetMethod("TryGetTransform",flags)!.Invoke(history,sampleArgs)!,"actual history sampled");
bool actorCurrent=(Matrix4)sampleArgs[4]! == current;
var info=new AnimationInfo();info.FrameCount[0]=10;info.Index[0]=0;info.Frame[0]=2;
var samplesType=assembly.GetType("MphRead.Mods.Render.AnimationSamples",true)!;
var samples=Activator.CreateInstance(samplesType,true)!;var captureAnimation=samplesType.GetMethod("Record",flags)!;
captureAnimation.Invoke(samples,new object[]{info,0,377L});info.Frame[0]=3;captureAnimation.Invoke(samples,new object[]{info,0,379L});
object?[] animationArgs={info,0,379+alpha,null};
bool animationSample=(bool)samplesType.GetMethod("TryGet",flags)!.Invoke(samples,animationArgs)!;
float fraction=(float)animationArgs[3]!.GetType().GetProperty("Fraction")!.GetValue(animationArgs[3])!;
Check(actorCurrent&&(float)alpha==1f&&animationSample&&fraction==.5f&&info.Frame[0]==3,"float-current actor and fractional node sample coexist without canonical animation mutation");
Check(Guard(false,false,false,false,true,true,true,!actorCurrent,animationSample),"animation-only sample isolates canonical biped effect/ice anchors");
Check(Guard(false,false,false,false,true,true,true,true,false),"actor-carry isolation preserved without animation sample");
Check(!Guard(false,false,false,false,true,true,true,false,false),"unsampled canonical path avoids extra isolation work");
Check(Guard(false,false,true,false,true,true,true,false,false,true),"local third-person actor isolates canonical anchors");
Check(Guard(false,false,true,false,true,true,true,false,false,false,true),"interpolated crouch isolates canonical anchors");
foreach(int excluded in Enumerable.Range(0,7))
{
 bool[] gate={false,false,false,false,true,true,true};gate[excluded]=!gate[excluded];
 Check(!Guard(gate[0],gate[1],gate[2],gate[3],gate[4],gate[5],gate[6],false,true),"existing replica/network/main/spectator/dead/inactive/60Hz scope retained");
}
Console.WriteLine("DISPLAY_POSE_BOUNDARY_SOURCE_PASS; actual frozen value objects/current predicate only; GL/world NOT_RUN");
'''.replace("ACTOR_ISOLATION", expression("isolateLocalOrOfflineActor")).replace("ELIGIBLE", eligible).replace("ISOLATION", isolation)
        with tempfile.TemporaryDirectory(prefix="prime-display-pose-boundary-") as temporary:
            scratch = Path(temporary)
            (scratch / "Program.cs").write_text(code)
            refs = "".join('<Reference Include="' + p.stem + '"><HintPath>' + escape(str(p))
                + '</HintPath></Reference>' for p in (reference, reference.with_name("OpenTK.Mathematics.dll")))
            (scratch / "check.csproj").write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
                '<OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable>'
                '<ImplicitUsings>enable</ImplicitUsings><NuGetAudit>false</NuGetAudit><UseSharedCompilation>false</UseSharedCompilation>'
                '</PropertyGroup><ItemGroup>' + refs + '</ItemGroup></Project>')
            env = dict(os.environ, DOTNET_CLI_HOME=str(scratch / "cli"), MSBuildEnableWorkloadResolver="false",
                       DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE="true", DOTNET_NOLOGO="1")
            run = subprocess.run([str(dotnet), "run", "--project", str(scratch / "check.csproj"), "-c", "Release",
                                  "--verbosity", "quiet"], cwd=scratch, env=env,
                                 capture_output=True, text=True, timeout=60)
            self.assertEqual(run.returncode, 0, run.stdout + run.stderr)
            self.assertIn("DISPLAY_POSE_BOUNDARY_SOURCE_PASS", run.stdout)


if __name__ == "__main__":
    unittest.main()
