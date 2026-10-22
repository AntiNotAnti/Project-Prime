"""Source-linked diagnostic/CLI checks; closed facades, no engine or DS evidence."""
from pathlib import Path
import json
import os
import shutil
import subprocess
import tempfile
import unittest
from xml.sax.saxutils import escape


ROOT = Path(__file__).resolve().parents[2]
PRODUCTION = ROOT / "src/MphRead/Mods/Fidelity"


class CaptureProducerSourceTests(unittest.TestCase):
    def test_actual_delta_catalog_and_cli_without_main_project(self):
        bundled = Path.home() / ".dotnet/dotnet"
        dotnet = str(bundled) if bundled.is_file() else shutil.which("dotnet")
        if not dotnet:
            self.skipTest("Source-linked C# checks need a local .NET 10 SDK; no F2/DS claim")
        baseline_ids = sorted(p.stem for p in (ROOT / "tools/fidelity-oracle/baselines").glob("*.json"))
        self.assertEqual(len(baseline_ids), 47)
        # Compile the actual catalog declaration/Create body, leaving the world
        # producer out. Match scenarios and the F1 catalog use closed fixtures.
        content = (PRODUCTION / "FidelityContentScenarios.cs").read_text()
        declaration = content[:content.index("    internal static List<FidelityCheckpoint> Capture(")] + "}\n"
        f1 = ["simulation.rng", "simulation.fixed-step", "simulation.pause-resume", "identity.spawn-life",
              "identity.slot-reuse", "identity.disconnect-resume", "identity.match-epoch", "weapon.continuous-phase"]
        match = [name for name in baseline_ids if name.startswith("match.")]
        literal = lambda values: ",".join(json.dumps(value) for value in values)
        facade = r'''
using System.Text.Json;
namespace MphRead.Entities { internal enum Hunter { Samus,Kanden,Trace,Sylux,Noxus,Spire,Weavel } }
namespace MphRead.Formats { }
namespace MphRead.Mods.Input { }
namespace MphRead.Mods.Network { }
namespace OpenTK.Mathematics { }
namespace MphRead.Mods.Platform { internal static class AppPaths { internal static string ResourceDirectory=>Environment.CurrentDirectory; } }
namespace MphRead.Mods.Fidelity {
internal enum FidelityTier { F0,F1,F2,F3,F4,F5 }
internal sealed record FidelityScenario(string Id,int Version,int Ticks,uint Seed,string Content,int CaptureInterval,FidelityTier Tier,string Description);
internal sealed record FidelityResult(string Id) { public string CanonicalHash=>"CLOSED_FIXTURE"; }
internal sealed record FidelityDifference(int? Tick,string Field,string? Expected,string? Actual);
internal static class ConsoleSetup { internal static string LaunchDirectory=>Environment.CurrentDirectory; }
internal static class FidelityOracle {
 internal static readonly JsonSerializerOptions Json=new();internal static readonly List<string> Calls=new();
 internal static FidelityResult Run(string id,string revision,int hz,bool allow){Calls.Add(id);return new(id);}
 internal static FidelityResult Read(string path)=>new(path);
 internal static FidelityDifference? Compare(FidelityResult a,FidelityResult b)=>null;
}
internal static class FidelityScenarios { internal static readonly FidelityScenario[] All=FixtureCatalog.Make(new[]{F1_IDS},false); }
internal static class FidelityMatchScenarios { internal static readonly FidelityScenario[] All=FixtureCatalog.Make(new[]{MATCH_IDS},true); }
internal static class FixtureCatalog { internal static FidelityScenario[] Make(string[] ids,bool content)=>ids.Select(id=>new FidelityScenario(id,1,1,0,content?"closed:fixture":"none",1,content?FidelityTier.F2:FidelityTier.F1,"Closed catalog only")).ToArray(); }
}
'''.replace("F1_IDS", literal(f1)).replace("MATCH_IDS", literal(match))
        program = r'''
using MphRead.Mods.Fidelity;
int checks=0;
void Check(bool ok,string message){if(!ok)throw new Exception(message);++checks;}
void Reject(Action action,string message){try{action();}catch(InvalidDataException){++checks;return;}throw new Exception(message);}
var counter=new FidelityEventDelta(9);
Check(counter.Sample(1,9,10)==1,"first accepted shot delta excludes warmup total");
Check(counter.Sample(2,10,10)==0,"persistent projectile/held trigger does not invent another shot");
Check(counter.Sample(3,10,13)==3,"more than one canonical event is retained");
Reject(()=>counter.Sample(3,13,14),"duplicate frame accepted");
Reject(()=>counter.Sample(5,13,14),"missing frame accepted");
Reject(()=>counter.Sample(4,12,14),"counter changed between observations");
Reject(()=>counter.Sample(4,13,12),"counter rewind accepted");
Check(counter.Sample(4,13,14)==1,"rejected observations did not advance sampler");
Reject(()=>new FidelityEventDelta(-1),"negative initial counter");
Reject(()=>new FidelityEventDelta(Int32.MaxValue),"saturated initial counter");
Reject(()=>counter.Sample(5,14,Int32.MaxValue),"counter saturation accepted");
Reject(()=>counter.Sample(5,Int32.MaxValue,Int32.MaxValue),"saturated before counter");
Reject(()=>counter.Sample(5,14,Int32.MinValue),"wrapped counter accepted");
Check(counter.Sample(5,14,14)==0,"overflow refusal did not advance sampler");
int before=14,after=15;
Check(counter.Sample(6,before,after)==1&&before==14&&after==15,"observation leaves canonical scalar inputs unchanged");
var max=new FidelityEventDelta(Int32.MaxValue-2);
Check(max.Sample(1,Int32.MaxValue-2,Int32.MaxValue-1)==1,"last observable unsaturated increment remains exact");
Reject(()=>max.Sample(2,Int32.MaxValue-1,Int32.MaxValue),"saturating increment must fail closed");
var expected=new HashSet<string>(new[]{BASELINE_IDS});
var actual=FidelityScenarios.All.Concat(FidelityContentScenarios.All).ToArray();
Check(actual.Select(s=>s.Id).Distinct().Count()==48,"one diagnostic added without duplicate scenario IDs");
Check(expected.SetEquals(actual.Where(s=>!FidelityContentScenarios.CaptureOnly(s)).Select(s=>s.Id)),"existing 47-case regression catalog unchanged");
Check(actual.Single(s=>FidelityContentScenarios.CaptureOnly(s)) is {Id:"weapon.powerbeam.cadence",Ticks:180,CaptureInterval:1,Tier:FidelityTier.F2},"diagnostic declares every-frame actual-engine capture");
Check(Enumerable.Range(0,90).All(pair=>FidelityContentScenarios.CadenceTrigger(pair*2+1)==FidelityContentScenarios.CadenceTrigger(pair*2+2)),"every original 30Hz logic tick has two matching Project input frames");
Check(!FidelityContentScenarios.CadenceTrigger(0)&&Enumerable.Range(1,180).All(tick=>FidelityContentScenarios.CadenceTrigger(tick)==((tick-1)%30<12)),"declared 12-held 18-released route matches every frame");
int Run(params string[] args){FidelityOracle.Calls.Clear();return FidelityOracleCommand.Run(new[]{"-fidelityoracle"}.Concat(args).ToArray());}
Check(Run("run","all","-allow-content")==0&&expected.SetEquals(FidelityOracle.Calls),"all content excludes capture-only diagnostic and retains exactly 47");
Check(Run("run","all")==0&&FidelityOracle.Calls.Count==8,"default all remains the existing eight content-free cases");
Check(Run("run",FidelityContentScenarios.CadenceScenario,"-allow-content")==0&&FidelityOracle.Calls.SequenceEqual(new[]{FidelityContentScenarios.CadenceScenario}),"explicit capture selects only named producer");
foreach(string action in new[]{"verify","record"})
 Check(Run(action,FidelityContentScenarios.CadenceScenario,"--developer-record","-engine-revision",new string('a',40))!=0&&FidelityOracle.Calls.Count==0,"capture-only "+action+" refuses before capture or baseline IO");
Check(Run("help")==0&&FidelityOracle.Calls.Count==0,"help does not capture or touch baseline");
Console.WriteLine($"SOURCE_LINKED_PRODUCER_PASS {checks}; closed CLI facade, no engine/DS accuracy claim");
'''.replace("BASELINE_IDS", literal(baseline_ids))
        with tempfile.TemporaryDirectory(prefix="prime-fidelity-producer-check-") as scratch:
            scratch = Path(scratch)
            (scratch / "Catalog.cs").write_text(declaration)
            (scratch / "Facades.cs").write_text(facade)
            (scratch / "Program.cs").write_text(program)
            sources = [PRODUCTION / "FidelityEventDelta.cs", PRODUCTION / "FidelityOracleCommand.cs"]
            includes = "".join(f'<Compile Include="{escape(str(path))}" />' for path in sources)
            (scratch / "check.csproj").write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
                '<OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable>'
                '<ImplicitUsings>enable</ImplicitUsings><NuGetAudit>false</NuGetAudit>'
                '</PropertyGroup><ItemGroup>' + includes + '</ItemGroup></Project>')
            environment = dict(os.environ, DOTNET_CLI_HOME=str(scratch / "cli"), DOTNET_NOLOGO="1",
                               DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1", MSBuildEnableWorkloadResolver="false",
                               DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE="true")
            run = subprocess.run([dotnet, "run", "--project", str(scratch / "check.csproj"), "-c", "Release",
                                  "--no-launch-profile", "--verbosity", "quiet"],
                                 cwd=scratch, env=environment, capture_output=True, text=True, timeout=60)
            self.assertEqual(run.returncode, 0, run.stdout + run.stderr)
            self.assertIn("SOURCE_LINKED_PRODUCER_PASS 28", run.stdout)


if __name__ == "__main__":
    unittest.main()
