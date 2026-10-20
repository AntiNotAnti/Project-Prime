"""Execute the production receipt against an asset-free scene stub."""
from pathlib import Path
import os
import shutil
import subprocess
import tempfile
import unittest
from xml.sax.saxutils import escape


class ProvenancePhaseTests(unittest.TestCase):
    def test_actual_receipt_identity_and_boundary_observations(self):
        root = Path(__file__).resolve().parent
        dotnet = os.environ.get("DOTNET_EXE") or shutil.which("dotnet")
        self.assertIsNotNone(dotnet, "Set DOTNET_EXE to .NET 10 dotnet")
        code = r'''
using System;
using System.Text.Json;
void Check(bool v,string message){if(!v)throw new Exception(message);}
foreach(bool late in new[]{false,true}) {
 var window=new CheckWindow(late);
 window.BeforeWarmup();
 var early=window.Receipt();
 Check(early.GetProperty("clientBinary").ValueKind==(late?JsonValueKind.Null:JsonValueKind.Object),"identity runs in selected phase");
 window.Scene.FrameCount=240;window.Scene.Random.Rng1=999;
 window.Start();
 var start=window.Receipt();
 Check(start.GetProperty("measurementStartTick").GetUInt64()==240,"actual tick stays boundary observed");
 Check(start.GetProperty("measurementStartRng").GetProperty("rng1").GetUInt32()==999,"actual RNG stays boundary observed");
 var binary=start.GetProperty("clientBinary");
 Check(binary.GetProperty("sha256").GetString()!.Length==64,"loaded PE hashed");
 Check(binary.GetProperty("mvid").GetGuid()==typeof(MphRead.Scene).Module.ModuleVersionId,"hash bound to loaded module MVID");
 window.Scene.FrameCount=720;window.End();
 var end=window.Receipt();
 Check(end.GetProperty("binaryIdentityPhase").GetString()==(late?"measurement-start-control":"before-warmup"),"phase explicit");
 Check(end.GetProperty("eligibleNormalTiming").GetBoolean()==!late,"late diagnostic cannot qualify normal timing");
 Check(end.GetProperty("measurementEndTick").GetUInt64()==720,"end observation retained");
}
Console.WriteLine("PROVENANCE_PHASE_PASS");
sealed partial class CheckWindow {
 public MphRead.Scene Scene=new();
 private string? _shots=null;
 private bool _windowCycle=false,_outlineCheck=false,_bombCheck=false,_impactCheck=false,_stableHud=false,_poseCheck=false;
 private bool ResourceStressEnabled=>false;
 public CheckWindow(bool late){_lateProvenanceControl=late;ObserveAssignedWorkloadSeeds();}
 public void BeforeWarmup(){if(!_lateProvenanceControl)ObserveWorkloadBinaryIdentities();}
 public void Start()=>ObserveWorkloadStart();
 public void End()=>ObserveWorkloadEnd();
 public JsonElement Receipt()=>JsonSerializer.SerializeToElement(ObservedWorkloadReceipt());
}
namespace MphRead {
 public class Scene {
  public ulong FrameCount;
  public Rng Random=new();public Roster Players=new();public Service Services=new();
 }
 public class Rng {public uint Rng1=1,Rng2=2;}
 public class Roster {public Player[] Items={new()};}
 public class Player {public int SlotIndex=0;public Hunter Hunter=Hunter.Samus;public bool IsBot=true;}
 public enum Hunter {Samus}
 public class Service {public bool IsReplica=false;}
}
namespace MphRead.Mods.Network {public static class NetSession {public static bool Active=>false;}}
namespace MphRead.Mods.Render {
 public static class OpenGlFrameProfiler {public static bool Enabled=>false;}
 public static class OpenGlSubmissionProfiler {public static bool Enabled=>false;}
}
'''
        with tempfile.TemporaryDirectory(prefix="prime-provenance-phase-") as temp:
            scratch = Path(temp)
            (scratch / "Program.cs").write_text(code)
            (scratch / "check.csproj").write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
                '<OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable>'
                '<NuGetAudit>false</NuGetAudit></PropertyGroup><ItemGroup>'
                f'<Compile Include="{escape(str(root / "ObservedWorkloadReceipt.cs"))}" />'
                '</ItemGroup></Project>')
            result = subprocess.run([dotnet,"run","--project",str(scratch / "check.csproj"),"-c","Release","--verbosity","quiet"],
                                    text=True,capture_output=True,timeout=90)
            self.assertEqual(result.returncode,0,result.stdout+result.stderr)
            self.assertIn("PROVENANCE_PHASE_PASS",result.stdout)


if __name__ == "__main__":
    unittest.main()
