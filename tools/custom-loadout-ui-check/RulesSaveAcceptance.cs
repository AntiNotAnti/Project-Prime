using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Threading;
using MphRead;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Network;
using MphRead.Mods.Settings;
using Menu = MphRead.Menu;

internal static class RulesSaveAcceptance
{
    internal static int Run(string output)
    {
        const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        var restart=typeof(SettingsPersistence).GetField("_restartRequired",BindingFlags.Static|BindingFlags.NonPublic)!;
        bool previousRestart=SettingsPersistence.RestartRequired,previousStill=Deck.Still;
        var previousSave=Menu.NeededSave;
        var scene=new Scene(default,null!,null!,_=>{},()=>{},initializeRuntime:false);
        GameState.Current=scene.GameState;PlayerEntity.LegacyRegistry=scene.Players;scene.GameState.Mode=GameMode.Battle;
        NetSession.Stop();Menu.NeededSave=SaveWhen.Never;Deck.Still=true;
        int checks=0;string? failure=null;
        string product=typeof(Scene).Assembly.Location;
        string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        string productHash=Hash(product);
        string path=Path.GetFullPath(Path.Combine("Savedata","settings.json"));
        void Check(bool pass,string label)
        {if(!pass)throw new InvalidOperationException(label);checks++;Console.WriteLine("RULES SAVE UI PASS "+label);}
        MenuSettings ReadSettings()
        {
            using var json=JsonDocument.Parse(File.ReadAllBytes(path));
            return json.RootElement.GetProperty("MenuSettings").Deserialize<MenuSettings>()!;
        }
        try
        {
            Check(!previousRestart,"isolated process begins without an inherited restart-required fence");
            Dispatcher.UIThread.Invoke(()=>
            {
                var settings=new MenuSettings {RoomKey="RULES SAVE FIXTURE",Language="French",SfxVolume="0.37",
                    MovementModel="Native",PointGoal="7",TimeLimit="7:00",TimeGoal="1:30"};
                using var home=new StartScreen(settings,new[]{"MP1 SANCTORUS"});
                // Explicit owner fixture only: the splash is not attached to a
                // window, and startup/Android lifecycle is outside this check.
                var startupField=typeof(StartScreen).GetField("_startup",Hidden)!;
                if(startupField.GetValue(home) is Control startup)
                {
                    ((Panel)typeof(StartScreen).GetField("_layers",Hidden)!.GetValue(home)!).Children.Remove(startup);
                    (startup as IDisposable)?.Dispose();startupField.SetValue(home,null);
                }
                Check(home.CanManageAdventureSaves,"real StartScreen owns local settings after declared startup staging");
                GameState.CommitSettings(settings);
                Check(File.Exists(path)&&!ReadSettings().RiftlineM7Available,"actual production serializer establishes native-only baseline file");
                var expected=home.RmlUiLocalRules;
                var next=expected with {RiftlineM7Available=true,MovementModel="Momentum",PointGoal="31",TimeLimit="8:20"};
                var change=new RmlUiOfflineRulesChange(GameMode.Battle,expected,next);
                // A newer unrelated in-memory choice must survive the captured
                // rules subset. The writer persists this exact settings owner.
                settings.Language="German";
                var saved=home.RmlUiSaveOfflineRules(change,CancellationToken.None);
                var disk=ReadSettings();
                Check(saved is {Success:true}&&saved.Change==change&&saved.Current==next&&home.RmlUiLocalRules==next,
                    "actual StartScreen save returns exact true-M7 capture and verified current snapshot");
                Check(RmlUiOfflineRules.Capture(disk)==next&&disk.RiftlineM7Available&&disk.MovementModel=="Momentum"
                    &&disk.Language=="German"&&disk.SfxVolume=="0.37"&&disk.RoomKey=="RULES SAVE FIXTURE",
                    "actual settings.json readback preserves accepted rules and newest unrelated settings");
                var cache=new RmlUiOfflineRulesCache(expected);cache.Publish(saved.Current);
                Check(cache.Read()==next,"verified actual owner result can refresh copied cross-thread rules cache");
                string acceptedHash=Hash(path);

                var stale=home.RmlUiSaveOfflineRules(change,CancellationToken.None);
                Check(!stale.Success&&stale.Current==next&&stale.Status.Contains("RULES CHANGED")
                    &&Hash(path)==acceptedHash&&home.RmlUiLocalRules==next,
                    "stale expected subset refuses before any settings byte or owner mutation");

                foreach(var invalid in new[]{
                    new RmlUiOfflineRulesChange(GameMode.Survival,next,next with {PointGoal="32"}),
                    new RmlUiOfflineRulesChange(GameMode.Battle,next,next with {Flags=(ushort)(next.Flags|(1<<7))})})
                {
                    var denied=home.RmlUiSaveOfflineRules(invalid,CancellationToken.None);
                    Check(!denied.Success&&denied.Current==next&&Hash(path)==acceptedHash&&home.RmlUiLocalRules==next,
                        "actual settings owner refuses invalid custom mode/modifiers without writes: "+invalid.Mode+"/"+invalid.Next.Flags);
                }

                using(var cancellation=new CancellationTokenSource())
                {
                    cancellation.Cancel();
                    var canceled=home.RmlUiSaveOfflineRules(new(GameMode.Battle,next,next with {PointGoal="33"}),cancellation.Token);
                    Check(!canceled.Success&&canceled.Current==next&&Hash(path)==acceptedHash&&home.RmlUiLocalRules==next,
                        "cancellation before owner write leaves exact accepted bytes and fields intact");
                }

                string backup=path+".accepted";File.Move(path,backup);Directory.CreateDirectory(path);
                try
                {
                    var refused=home.RmlUiSaveOfflineRules(new(GameMode.Battle,next,next with
                        {RiftlineM7Available=false,MovementModel="Native",PointGoal="99",TimeLimit="9:30"}),CancellationToken.None);
                    Check(!refused.Success&&refused.Status.Contains("COULD NOT BE SAVED")&&refused.Current==next
                        &&home.RmlUiLocalRules==next&&settings.Language=="German"&&settings.SfxVolume=="0.37"
                        &&Directory.Exists(path)&&Hash(backup)==acceptedHash,
                        "actual persistence failure rolls back every rule field including M7 while retaining unrelated settings and accepted bytes");
                }
                finally {Directory.Delete(path);File.Move(backup,path);}
                Check(Hash(path)==acceptedHash,"failed-write fixture restores exact durable accepted settings file");

                // Exercise the real replacement fence last; only this isolated
                // process flag is restored to its captured value on teardown.
                SettingsPersistence.Replace(()=>{});
                var fenced=home.RmlUiSaveOfflineRules(new(GameMode.Battle,next,next with {PointGoal="34"}),CancellationToken.None);
                Check(SettingsPersistence.RestartRequired&&!fenced.Success&&fenced.Status.Contains("SETTINGS WERE REPLACED")
                    &&fenced.Current==next&&Hash(path)==acceptedHash&&home.RmlUiLocalRules==next,
                    "real SettingsPersistence replacement fence refuses owner write without changing bytes");
            });
            Check(Hash(product)==productHash,"actual owner persistence acceptance does not modify product binary");
            return 0;
        }
        catch(Exception error)
        {failure=error.ToString();Console.Error.WriteLine("RULES SAVE UI FAIL "+failure);return 1;}
        finally
        {
            lock(SettingsPersistence.Gate)restart.SetValue(null,previousRestart);
            Menu.NeededSave=previousSave;Deck.Still=previousStill;
            File.WriteAllText(Path.Combine(output,"rules-save-ui-evidence.json"),JsonSerializer.Serialize(new {
                checks,failure,product,productHash,productMvid=typeof(Scene).Assembly.ManifestModule.ModuleVersionId,
                settingsPath=path,settingsHash=File.Exists(path)?Hash(path):null,
                scope="Real production Avalonia StartScreen settings owner and GameState.CommitSettings in isolated user data/cwd; startup completed by declared private-field staging; actual JSON readback, stale/invalid/canceled refusal, filesystem failure rollback and real replacement fence. No Android runtime, GL, dispatcher-post/cancellation-race or physical input qualification."
            },new JsonSerializerOptions {WriteIndented=true}));
            try{scene.DoCleanup();}finally{scene.UnloadGl();}
        }
    }
}
