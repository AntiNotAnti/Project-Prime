using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead;
using MphRead.Mods.Import;
using MphRead.Mods.Launcher;
using MphRead.Mods.Render.Characters;

// Owned runtime acceptance is separate from the authoring-only source fixture.
// The tests-first admission check intentionally runs before any owned media I/O.
internal static class CharacterDroneRuntimeOwnedProbe
{
    internal static int Run(string source)
    {
        int checks=0;string? failed=null,error=null;bool sourceIo=false;
        string core=typeof(Scene).Assembly.Location,tool=typeof(CharacterDroneRuntimeOwnedProbe).Assembly.Location;
        string coreBefore=HashFile(core),toolBefore=HashFile(tool);
        void Check(bool condition,string why)
        {if(!condition){failed=why;throw new InvalidDataException(why);}checks++;}
        try
        {
            var appearance=typeof(LaunchPlan).Assembly.GetType("MphRead.Mods.Launcher.LocalDroneAppearance");
            Check(appearance is{IsEnum:true}&&appearance.IsPublic,
                "independent LocalDroneAppearance runtime contract exists before owned-source or native-asset I/O");
            Check(Enum.GetNames(appearance!).SequenceEqual(new[]{"Native","Alpha","Gamma"})
                &&Enum.GetValues(appearance!).Cast<object>().Select(Convert.ToInt32).SequenceEqual(new[]{0,1,2}),
                "local drone presentation identity appends no Hunter/gameplay/wire capability");
            Check(typeof(LaunchPlan).GetProperty("LocalDrone")?.PropertyType==appearance
                &&typeof(LauncherPrefs).GetProperty("LocalDrone",BindingFlags.Public|BindingFlags.Static)?.PropertyType==appearance,
                "detached launch plan and persisted preference carry the exact local drone identity");
            Check(typeof(LauncherHunterChoice).GetProperty("Drone")?.PropertyType==appearance
                &&typeof(LauncherHunterChoice).GetConstructors().Any(c=>c.GetParameters() is var p&&p.Length==4&&p[3].ParameterType==appearance&&p[3].IsOptional),
                "Hunters choice adds optional fourth drone preference without changing existing constructor defaults");
            var rules=typeof(LaunchPlan).Assembly.GetType("MphRead.Mods.Launcher.LocalDroneAppearanceRules");
            Check(rules?.GetMethods(BindingFlags.Public|BindingFlags.Static).Any(m=>m.Name=="TryAdmit")==true
                &&typeof(CharacterSourcePreviewSelection).GetMethods(BindingFlags.Public|BindingFlags.Static).Any(m=>m.Name=="LoadForLocalDrone"),
                "separate closed drone admission and same-generation source selection precede actual Scene creation");
            Check(false,"actual product drone runtime acceptance awaits production owner API freeze");
        }
        catch(Exception ex){error=ex.GetType().Name;Console.Error.WriteLine("OWNED DRONE RUNTIME failure: "+ex);}
        finally
        {
            try{Check(HashFile(core)==coreBefore&&HashFile(tool)==toolBefore,"test-first runtime contract check preserves core/tool bytes");}
            catch(Exception ex){error??=ex.GetType().Name;}
            Console.WriteLine(JsonSerializer.Serialize(new{Pass=error==null,Checks=checks,FailedCheck=failed,ErrorType=error,
                CoreSha256=coreBefore,ToolSha256=toolBefore,SourceIoAttempted=sourceIo,SourceSha256Before=(string?)null,SourceSha256After=(string?)null,
                NativeAssetIoAttempted=false,RenderingActivated=false,GameplayActivated=false,
                Scope="tests-first runtime contract only before any owned media, importer, native asset, Scene or GPU activation; actual runtime fixture pending frozen API"}));
        }
        return error==null?0:1;
    }
    private static string HashFile(string path){using var input=File.OpenRead(path);return Convert.ToHexStringLower(SHA256.HashData(input));}
}
