using System.Globalization;
using System.Reflection;
using MphRead.Mods;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher;
using MphRead.Mods.Settings;

// Pure optical math and actual isolated controls.txt persistence. Golden lens
// angles were calculated separately using Python double precision, not this API.
internal static class CustomScopeChecks
{
    internal static void Run(Action<bool,string> check)
    { Lenses(check);Bounds(check);Settings(check);Allocation(check); }
    private static void Lenses(Action<bool,string> check)
    {
        (float Normal,int Milli,double Target)[] golden={
            (60,2500,26.007823885645635),(60,4000,16.42642140347638),
            (78,2500,35.89568965503107),(78,4000,22.889249183426322),
            (90,2500,43.60281897270362),(90,4000,28.072486935852957),
            (120,2500,69.43000790789643),(120,4000,46.82644889274106)};
        foreach(var vector in golden)
        {
            float actual=CustomScopeMath.TargetFov(vector.Normal,vector.Milli);
            double ratio=Math.Tan(vector.Normal*Math.PI/360)/Math.Tan(actual*Math.PI/360);
            check(float.IsFinite(actual)&&Math.Abs(actual-vector.Target)<.0001,
                "custom optical target matches independent double-precision lens angle");
            check(Math.Abs(ratio-vector.Milli/1000d)<.00001,
                "2.5x/4x custom optics preserve true tangent lens ratio at each user base FOV");
            check(Math.Abs(CustomScopeMath.AimScale(vector.Normal,actual)-1000f/vector.Milli)<.000001,
                "custom aim scaling inverts lens magnification rather than dividing FOV degrees");
            check(CustomScopeMath.TargetFov(vector.Normal,0)==vector.Normal,
                "unscoped target preserves each user FOV without inheriting another lens base");
        }
        foreach(float normal in new[]{1f,2f,30f,60f,78f,90f,120f,175f})
        {
            float previous=normal;
            foreach(int milli in new[]{1000,2500,4000,16000})
            {
                float target=CustomScopeMath.TargetFov(normal,milli);
                check(float.IsFinite(target)&&target>0&&target<=normal+.0001f&&target<=previous+.0001f,
                    "bounded custom magnification narrows a finite positive lens monotonically");
                double unclipped=2*Math.Atan(Math.Tan(normal*Math.PI/360)/(milli/1000d))*180/Math.PI;
                double expectedScale=unclipped<1?Math.Tan(Math.PI/360)/Math.Tan(normal*Math.PI/360):1000d/milli;
                check(Math.Abs(CustomScopeMath.AimScale(normal,target)-Math.Clamp(expectedScale,.01,1))<.00001,
                    "target/aim helpers preserve optical inverse or the explicit one-degree render-camera clamp");
                previous=target;
            }
        }
    }
    private static void Bounds(Action<bool,string> check)
    {
        foreach(float invalid in new[]{float.NaN,float.NegativeInfinity,float.PositiveInfinity})
        {
            check(CustomScopeMath.TargetFov(invalid,0)==78
                &&Math.Abs(CustomScopeMath.TargetFov(invalid,2500)-35.895689655)<.0001,
                "nonfinite base FOV falls back to the finite native78-degree lens");
            check(CustomScopeMath.AimScale(invalid,20)==1&&CustomScopeMath.AimScale(78,invalid)==1,
                "nonfinite aim-scaling inputs return neutral sensitivity without NaN propagation");
        }
        foreach(float finite in new[]{float.MinValue,-1f,0f,.5f,176f,float.MaxValue})
            check(CustomScopeMath.TargetFov(finite,0)==Math.Clamp(finite,1,175),
                "finite user lens input clamps at explicit1..175-degree bounds");
        foreach(int invalid in new[]{int.MinValue,-1,0,999,16001,int.MaxValue})
            check(CustomScopeMath.TargetFov(90,invalid)==90,
                "out-of-domain magnification is neutral instead of silently selecting a custom lens");
        check(CustomScopeMath.AimScale(78,0)==1&&CustomScopeMath.AimScale(78,-1)==1
            &&CustomScopeMath.AimScale(0,20)==1&&CustomScopeMath.AimScale(176,20)==1,
            "invalid finite aim geometry returns neutral sensitivity");
        check(CustomScopeMath.AimScale(78,1000)==1&&CustomScopeMath.AimScale(175,float.Epsilon)==.01f,
            "finite aim ratio clamps to the closed0.01..1 sensitivity interval");
    }
    private static void Settings(Action<bool,string> check)
    {
        bool hold=InputSettings.CustomScopeHold,stylus=PointerInput.StylusMode;
        float two=InputSettings.CustomScope25Sensitivity,four=InputSettings.CustomScope4Sensitivity;
        float nativeSensitivity=InputSettings.ImperialistZoomSensitivity,nativeAmount=InputSettings.ImperialistZoomAmount;
        float mouseSensitivity=InputSettings.MouseSensitivity;
        string directory=LauncherPrefs.Directory;
        var culture=CultureInfo.CurrentCulture;var uiCulture=CultureInfo.CurrentUICulture;
        var config=GamepadRuntimeConfig.Current;var frame=GamepadRuntimeConfig.Frame;
        var selectedConfig=(GamepadRuntimeConfig)typeof(GamepadRuntimeConfig)
            .GetField("_selected",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
        var profileFields=typeof(GamepadProfiles).GetFields(BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public)
            .Where(f=>!f.IsLiteral&&!f.IsInitOnly).Select(f=>(Field:f,Value:f.GetValue(null))).ToArray();
        var current=typeof(InputSettings).GetField("_current",BindingFlags.Static|BindingFlags.NonPublic)!;
        var priorControls=current.GetValue(null);
        string temporary=Path.Combine(Path.GetTempPath(),"prime-custom-scope-"+Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(temporary);LauncherPrefs.Directory=temporary;GamepadRuntimeConfig.Current=new();
            CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("fr-FR");CultureInfo.CurrentUICulture=CultureInfo.CurrentCulture;
            foreach(float invalid in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity})
            {
                InputSettings.CustomScope25Sensitivity=invalid;InputSettings.CustomScope4Sensitivity=invalid;
                check(InputSettings.CustomScope25Sensitivity==1&&InputSettings.CustomScope4Sensitivity==1,
                    "nonfinite custom sensitivity setters return finite default1");
            }
            foreach(float finite in new[]{float.MinValue,-1f,0f,.099f,.1f,.625f,1f,1.875f,3f,3.001f,float.MaxValue})
            {
                InputSettings.CustomScope25Sensitivity=finite;InputSettings.CustomScope4Sensitivity=finite;
                check(InputSettings.CustomScope25Sensitivity==Math.Clamp(finite,.1f,3f)
                    &&InputSettings.CustomScope4Sensitivity==Math.Clamp(finite,.1f,3f),
                    "both custom optic sensitivities clamp independently to0.1..3");
            }
            InputSettings.CustomScopeHold=true;InputSettings.CustomScope25Sensitivity=.625f;InputSettings.CustomScope4Sensitivity=1.875f;
            check(InputSettings.TrySave(),"actual custom scope settings save to isolated launcher preferences directory");
            string file=Path.Combine(temporary,"controls.txt");var saved=File.ReadAllLines(file);
            check(saved.Contains("custom_scope_hold=true")&&saved.Contains("custom_scope_25_sensitivity=0.625")
                &&saved.Contains("custom_scope_4_sensitivity=1.875"),
                "production writer persists exact independent optic settings with invariant decimal separators");
            var scopeLines=saved.Where(s=>s.StartsWith("custom_scope_",StringComparison.Ordinal)).ToArray();
            check(scopeLines.Length==3,"production scope settings have three unique canonical control keys");
            // Replay just the exact lines written by production. This keeps the
            // persistence test from reconfiguring unrelated pointer/pad options.
            File.WriteAllLines(file,scopeLines);InputSettings.CustomScopeHold=false;
            InputSettings.CustomScope25Sensitivity=InputSettings.CustomScope4Sensitivity=1;
            InputSettings.Load();
            check(InputSettings.CustomScopeHold&&InputSettings.CustomScope25Sensitivity==.625f
                &&InputSettings.CustomScope4Sensitivity==1.875f,
                "actual settings loader round-trips both custom lenses and hold mode from production-written values");
            File.WriteAllLines(file,new[]{"custom_scope_hold=false","custom_scope_25_sensitivity=-12","custom_scope_4_sensitivity=99"});
            InputSettings.Load();
            check(!InputSettings.CustomScopeHold&&InputSettings.CustomScope25Sensitivity==.1f&&InputSettings.CustomScope4Sensitivity==3,
                "finite out-of-range persisted lens settings are clamped by actual runtime setters");
            foreach(string text in new[]{"NaN","Infinity","-Infinity","not-a-number"})
                check(!PreferenceText.IsValid("custom_scope_25_sensitivity",text,false)
                    &&!PreferenceText.IsValid("custom_scope_4_sensitivity",text,false),
                    "detached controls grammar rejects malformed/nonfinite custom lens settings");
            check(!PreferenceText.IsValid("custom_scope_hold","not-a-bool",false)
                &&PreferenceText.IsValid("custom_scope_hold","true",false)
                &&PreferenceText.IsValid("custom_scope_25_sensitivity","0.625",false),
                "custom hold and sensitivity keys participate in typed detached preference validation");
            InputSettings.CustomScopeHold=true;InputSettings.CustomScope25Sensitivity=.5f;InputSettings.CustomScope4Sensitivity=1.5f;
            File.WriteAllLines(file,new[]{"custom_scope_hold=invalid","custom_scope_25_sensitivity=NaN","custom_scope_4_sensitivity=Infinity"});
            InputSettings.Load();
            check(InputSettings.CustomScopeHold&&InputSettings.CustomScope25Sensitivity==.5f&&InputSettings.CustomScope4Sensitivity==1.5f,
                "malformed persisted lens settings cannot replace existing valid runtime preferences");
            File.AppendAllText(file,"future_custom_scope=preserved\n");check(InputSettings.TrySave()&&InputSettings.TrySave(),
                "repeated real settings saves remain supported after custom scope validation");
            saved=File.ReadAllLines(file);
            check(saved.Count(s=>s.StartsWith("custom_scope_hold=",StringComparison.Ordinal))==1
                &&saved.Count(s=>s.StartsWith("custom_scope_25_sensitivity=",StringComparison.Ordinal))==1
                &&saved.Count(s=>s.StartsWith("custom_scope_4_sensitivity=",StringComparison.Ordinal))==1
                &&saved.Contains("future_custom_scope=preserved"),
                "settings update replaces known custom keys once and preserves unrelated future extensions");
            check(InputSettings.MouseSensitivity==mouseSensitivity&&InputSettings.ImperialistZoomSensitivity==nativeSensitivity
                &&InputSettings.ImperialistZoomAmount==nativeAmount,
                "custom scope persistence leaves native mouse/Imperialist tuning unchanged");
        }
        finally
        {
            InputSettings.CustomScopeHold=hold;InputSettings.CustomScope25Sensitivity=two;InputSettings.CustomScope4Sensitivity=four;
            PointerInput.StylusMode=stylus;LauncherPrefs.Directory=directory;current.SetValue(null,priorControls);
            GamepadRuntimeConfig.Current=selectedConfig;GamepadRuntimeConfig.Frame=frame;
            foreach(var saved in profileFields)saved.Field.SetValue(null,saved.Value);
            CultureInfo.CurrentCulture=culture;CultureInfo.CurrentUICulture=uiCulture;
            if(Directory.Exists(temporary))Directory.Delete(temporary,true);
        }
        check(InputSettings.CustomScopeHold==hold&&InputSettings.CustomScope25Sensitivity==two&&InputSettings.CustomScope4Sensitivity==four
            &&LauncherPrefs.Directory==directory&&PointerInput.StylusMode==stylus&&ReferenceEquals(GamepadRuntimeConfig.Current,config),
            "scope settings test restores prior globals and never writes the user's actual controls file");
    }
    private static void Allocation(Action<bool,string> check)
    {
        float value=0;for(int i=0;i<1000;i++)value+=CustomScopeMath.AimScale(78,CustomScopeMath.TargetFov(78,2500));
        long before=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<100000;i++)value+=CustomScopeMath.AimScale(78,CustomScopeMath.TargetFov(78,(i&1)==0?2500:4000));
        long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
        check(allocated==0&&float.IsFinite(value),"100000 warmed custom lens target/aim-scale probes allocate zero bytes");
        Console.WriteLine($"CUSTOM SCOPE MATH ALLOCATION:100000 warmed probes={allocated}B");
    }
}
