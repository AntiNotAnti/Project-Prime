"""Source-linked schedule checks only; no window, GL, assets or main build."""
from pathlib import Path
import os
import shutil
import subprocess
import tempfile
import unittest
from xml.sax.saxutils import escape


class DisplayPosePlanTests(unittest.TestCase):
    def test_actual_frame_clock_and_capture_plan(self):
        root = Path(__file__).resolve().parents[2]
        bundled = Path.home() / ".dotnet/dotnet"
        dotnet = str(bundled) if bundled.exists() else shutil.which("dotnet")
        if not dotnet:
            self.skipTest("Local .NET 10 SDK unavailable; GL acceptance remains NOT_RUN")
        code = r'''
using MphRead.Mods.Render;
int checks=0;
void Check(bool ok,string label){if(!ok)throw new Exception(label);++checks;}
Check(DisplayPosePlan.ResolveScenario(null,false)==DisplayPosePlan.OriginalScenario&&DisplayPosePlan.ResolveScenario(null,true)==DisplayPosePlan.OriginalScenario,"missing CLI scenario preserves original default");
foreach(string scenario in new[]{DisplayPosePlan.OriginalScenario,DisplayPosePlan.RetreatScenario})
{
 Check(DisplayPosePlan.ResolveScenario(scenario,true)==scenario,"explicit scenario identity exact");
 try{DisplayPosePlan.ResolveScenario(scenario,false);throw new Exception("scenario without pose admitted");}catch(ArgumentException){checks++;}
 for(int tick=1;tick<=1440;tick++)Check(DisplayPosePlan.RetreatsDuringCharge(scenario,tick)==(scenario==DisplayPosePlan.RetreatScenario&&tick>=361&&tick<=390),"only fixed thirty charge inputs change");
}
foreach(string invalid in new[]{"","NATIVE-POSE-V1","future"}){try{DisplayPosePlan.ResolveScenario(invalid,true);throw new Exception("unknown scenario");}catch(ArgumentException){checks++;}}
Check(DisplayPosePlan.OutputRates.SequenceEqual(new[]{60,120,144,240}),"bounded Mac output matrix");
Check(DisplayPosePlan.Captures.Count==13&&DisplayPosePlan.Captures.Keys.All(k=>k>240&&k<=1440),"all captures bounded after warmup");
Check(Enumerable.Range(1,1440).Select(DisplayPosePlan.Phase).Distinct().Count()==6,"every declared fixture phase reachable");
foreach(int invalid in new[]{0,-1,1441}){try{DisplayPosePlan.Phase(invalid);throw new Exception("invalid phase");}catch(ArgumentOutOfRangeException){checks++;}}
foreach(int hz in DisplayPosePlan.OutputRates)
{
 FrameTiming.Reset();FrameTiming.ResetDiagnostics();FrameTiming.FrameRateCap=hz;
 int ticks=0,outputs=0,zero=0;bool reset=false;
 while(ticks<1440&&outputs<hz*26)
 {
  if(ticks==1389&&!reset){FrameTiming.Reset();reset=true;}
  double accumulator=(double)typeof(FrameTiming).GetField("_accumulator",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!.GetValue(null)!;
  int prepared=DisplayPosePlan.PreviewSteps(accumulator,1.0/hz,FrameTiming.StepSeconds);
  int actual=FrameTiming.Advance(1.0/hz);
  Check(prepared==actual&&actual<=1,"input preparation follows actual sole production scheduler");
  ticks+=actual;outputs++;if(actual==0)zero++;
 }
 Check(ticks==1440&&reset&&FrameTiming.DroppedSteps==0&&FrameTiming.Stalls==0,"bounded exact canonical step count/reset");
 Check(hz==60?zero==0:zero>0,"correct current/high-refresh zero-step schedule");
 Check(hz==60?FrameTiming.PresentationAlpha==1:FrameTiming.HighRefreshPresentation,"60Hz current state preserved");
}
foreach(var values in new[]{new[]{double.NaN,.01,1.0/60},new[]{0.0,double.PositiveInfinity,1.0/60},new[]{-.01,.01,1.0/60},new[]{0.0,.1,1.0/60},new[]{1.0/60,.01,1.0/60}})
{try{DisplayPosePlan.PreviewSteps(values[0],values[1],values[2]);throw new Exception("bad schedule admitted");}catch(ArgumentException){checks++;}}
Console.WriteLine($"DISPLAY_POSE_SOURCE_PASS {checks}; actual FrameTiming + plan only, no GL/world acceptance");
'''
        with tempfile.TemporaryDirectory(prefix="prime-display-pose-plan-") as temporary:
            scratch = Path(temporary)
            (scratch / "Program.cs").write_text(code)
            (scratch / "ClosedLog.cs").write_text('namespace MphRead.Mods.Render { internal static class DebugLog { public static void Line(string category, string text) { } } }')
            paths = [root / "tools/native-render-check/DisplayPosePlan.cs",
                     root / "src/MphRead/Mods/Render/FrameTiming.cs"]
            links = "".join(f'<Compile Include="{escape(str(p))}" />' for p in paths)
            (scratch / "check.csproj").write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
                '<OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable>'
                '<ImplicitUsings>enable</ImplicitUsings><NuGetAudit>false</NuGetAudit>'
                '</PropertyGroup><ItemGroup>' + links + '</ItemGroup></Project>')
            env = dict(os.environ, DOTNET_CLI_HOME=str(scratch / "cli"), MSBuildEnableWorkloadResolver="false",
                       DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE="true", DOTNET_NOLOGO="1")
            run = subprocess.run([dotnet, "run", "--project", str(scratch / "check.csproj"), "-c", "Release",
                                  "--no-launch-profile", "--verbosity", "quiet"],
                                 cwd=scratch, env=env, capture_output=True, text=True, timeout=60)
            self.assertEqual(run.returncode, 0, run.stdout + run.stderr)
            self.assertIn("DISPLAY_POSE_SOURCE_PASS", run.stdout)


if __name__ == "__main__":
    unittest.main()
