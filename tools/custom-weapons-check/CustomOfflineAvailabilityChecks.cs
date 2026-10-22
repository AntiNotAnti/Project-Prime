using System.Reflection;
using System.Text.Json;
using MphRead;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Network;

// Launcher/DTO boundaries with isolated real persistence. No UI toolkit,
// cartridge loading, human input or engine grant is implied by these checks.
internal static class CustomOfflineAvailabilityChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var native=new MenuSettings();
        check(!native.RiftlineM7Available&&!JsonSerializer.Deserialize<MenuSettings>("{}")!.RiftlineM7Available,
            "new and older saved menu settings default to native availability");
        var settings=new MenuSettings { RiftlineM7Available=true,RoomKey="original" };
        var plan=OfflineLaunch.Preview(settings,"MP3 PROVING GROUND",GameMode.Battle,Hunter.Samus,2,3,1);
        check(plan.MatchRules.RiftlineM7Available&&plan.Mode==GameMode.Battle&&plan.Bots==3
            &&settings.RoomKey=="original","offline Preview freezes M7 Battle rules and allows native bot targets without mutating preferences");
        settings.RiftlineM7Available=false;
        check(plan.MatchRules.RiftlineM7Available&&OfflineRematch.TryPlan(plan,"MP1 SANCTORUS",out var next)
            &&next.MatchRules==plan.MatchRules&&next.Bots==3&&next.RoomKey=="MP1 SANCTORUS",
            "settings edits cannot alter frozen availability and real rematch projection preserves it");
        check(!OfflineLaunch.Preview(native,"MP3 PROVING GROUND",GameMode.Battle,Hunter.Samus,0,0,0)
            .MatchRules.RiftlineM7Available,"native Preview keeps custom availability off");
        foreach(var kind in new[]{LaunchKind.Adventure,LaunchKind.Demo,LaunchKind.AimTrainer,LaunchKind.None})
        {
            check(!OfflineMatchPreflight.TryAdmit(plan with{Kind=kind},out string reason,prepareRoom:true)&&reason.Length>0,
                "direct custom local preflight refuses unsupported launch kind "+kind);
            check(OfflineMatchPreflight.TryAdmit(plan with{Kind=kind,MatchRules=plan.MatchRules with{RiftlineM7Available=false}},out _),
                "native non-offline launch retains its own existing admission boundary: "+kind);
        }
        Editor(check);Persistence(check);
    }

    private static void Editor(Action<bool,string> check)
    {
        var settings=new MenuSettings { RiftlineM7Available=true,FriendlyFire="on",ThirdPersonCamera="on" };
        var captured=RmlUiOfflineRules.Capture(settings);var copy=new MenuSettings();captured.CopyTo(copy);
        check(RmlUiOfflineRules.Capture(copy)==captured&&copy.RiftlineM7Available,
            "offline rule capture and copy retain explicit availability independently of native flag bits");
        check(RmlUiOfflineRuleEditor.Labels.Length==15&&RmlUiOfflineRuleEditor.Labels[13]=="MOVEMENT"
            &&RmlUiOfflineRuleEditor.Labels[14]=="RIFTLINE M7 AVAILABLE"&&captured.Enabled(1)&&captured.Enabled(12)
            &&!captured.Enabled(14),"offline existing flag indices remain stable and availability appends after movement");
        var editor=new RmlUiOfflineRuleEditor(GameMode.Battle,captured);
        check(editor.Toggle(14)&&!editor.Draft.RiftlineM7Available&&editor.Draft.Flags==captured.Flags
            &&editor.Draft.MovementModel==captured.MovementModel&&!editor.Toggle(15),
            "new offline toggle changes only availability and rejects the next out-of-range choice");
        check(editor.Toggle(13)&&!editor.Draft.RiftlineM7Available&&editor.Draft.Flags==captured.Flags,
            "existing offline movement choice retains its independent index");
        check(editor.Toggle(14)&&editor.Validate("7","7:00","1:30")&&editor.Original==captured,
            "valid Battle availability draft validates without mutating the saved original");
        foreach(var mode in new[]{GameMode.Survival,GameMode.BattleTeams,GameMode.SinglePlayer})
        {
            var bad=new RmlUiOfflineRuleEditor(mode,captured);
            check(!bad.Validate("7","7:00","1:30")&&bad.Error.Length>0,
                "offline editor refuses custom availability outside Battle: "+mode);
        }
        foreach(int flag in new[]{7,9,10})
        {
            var bad=new RmlUiOfflineRuleEditor(GameMode.Battle,captured with{Flags=(ushort)(1<<flag)});
            check(!bad.Validate("7","7:00","1:30")&&bad.Error.Length>0,
                "offline availability editor refuses incompatible native modifier index "+flag);
        }
        var lobbySource=new RmlUiRuleDraft(7,9,3,new MatchDefinition {RoomKey="MP3 PROVING GROUND",Mode=GameMode.Battle},LobbyRuleFlags.RequireReady);
        var lobby=new RmlUiRuleEditor(lobbySource);
        check(RmlUiRuleEditor.ChoiceCount==19&&lobby.Toggle(18)&&lobby.IsOn(18)&&!lobby.Toggle(19)
            &&!lobby.Source.Match.RiftlineM7Available&&lobby.Validate(out _),
            "lobby availability appends at18, validates Battle and leaves original authority draft unchanged");
        check(lobby.Toggle(16)&&lobby.Draft.Match.ThirdPersonCamera&&lobby.IsOn(18)
            &&lobby.Toggle(17)&&lobby.IsOn(18),"existing lobby camera16 and movement17 do not alias new availability18");
        var invalid=new RmlUiRuleEditor(lobbySource with{Match=lobbySource.Match with{Mode=GameMode.Survival}});
        check(invalid.Toggle(18)&&!invalid.Validate(out string reason)&&reason.Length>0,
            "lobby availability draft refuses unsupported mode before publication");
        int writes=0;var expected=RmlUiOfflineRules.Capture(settings);var changed=expected with{RiftlineM7Available=false};
        check(!RmlUiOfflineRules.TryCommit(settings,expected,changed,s=>{writes++;s.SfxVolume="0.9";throw new IOException("fixture");},out _)
            &&writes==1&&RmlUiOfflineRules.Capture(settings)==expected&&settings.SfxVolume=="0.9",
            "failed offline persistence restores exact availability/rule subset while preserving unrelated changes");
        settings.RiftlineM7Available=false;
        check(!RmlUiOfflineRules.TryCommit(settings,expected,changed,_=>writes++,out _)&&writes==1,
            "stale offline availability draft cannot overwrite newer settings or invoke persistence");
    }

    private static void Persistence(Action<bool,string> check)
    {
        string cwd=Directory.GetCurrentDirectory(),directory=LauncherPrefs.Directory;
        var previous=(LauncherPrefs.LastHunter,LauncherPrefs.LastColor,LauncherPrefs.Bots,LauncherPrefs.BotLevel,LauncherPrefs.LastKind);
        string isolated=Path.Combine(Path.GetTempPath(),"m7-offline-settings-"+Guid.NewGuid().ToString("N"));
        var pending=typeof(DemoRecorder).GetField("_pending",BindingFlags.Static|BindingFlags.NonPublic)!;
        bool recording=(bool)pending.GetValue(null)!;
        Directory.CreateDirectory(isolated);
        try
        {
            Directory.SetCurrentDirectory(isolated);LauncherPrefs.Directory=isolated;
            var settings=new MenuSettings {RiftlineM7Available=true,RoomKey="saved-original"};
            GameState.Current.CommitSettings(settings);
            string saved=Path.Combine(isolated,"Savedata","settings.json");
            using(var json=JsonDocument.Parse(File.ReadAllText(saved)))
            {
                var loaded=json.RootElement.GetProperty("MenuSettings").Deserialize<MenuSettings>()!;
                check(loaded.RiftlineM7Available&&RmlUiOfflineRules.Capture(loaded)==RmlUiOfflineRules.Capture(settings),
                    "actual generic settings persistence roundtrips availability in an isolated save directory");
            }
            var frozen=OfflineLaunch.Create(settings,"MP3 PROVING GROUND",GameMode.Battle,Hunter.Samus,2,2,1);
            check(frozen.MatchRules.RiftlineM7Available&&settings.RoomKey==frozen.RoomKey&&File.Exists(Path.Combine(isolated,"launcher.txt")),
                "valid Create persists launcher selection only after admitting frozen M7 offline rules");
            byte[] launcher=File.ReadAllBytes(Path.Combine(isolated,"launcher.txt")),menu=File.ReadAllBytes(saved);
            var prefs=(LauncherPrefs.LastHunter,LauncherPrefs.LastColor,LauncherPrefs.Bots,LauncherPrefs.BotLevel,LauncherPrefs.LastKind);
            void Refuses(MenuSettings candidate,GameMode mode,string label)
            {
                string room=candidate.RoomKey;bool refused=false;
                try{OfflineLaunch.Create(candidate,"must-not-persist",mode,Hunter.Weavel,3,7,3);}
                catch(ArgumentException){refused=true;}
                check(refused&&candidate.RoomKey==room&&prefs==(LauncherPrefs.LastHunter,LauncherPrefs.LastColor,LauncherPrefs.Bots,LauncherPrefs.BotLevel,LauncherPrefs.LastKind)
                    &&launcher.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(isolated,"launcher.txt")))&&menu.AsSpan().SequenceEqual(File.ReadAllBytes(saved)),
                    "invalid offline Create refuses before preference or settings writes: "+label);
            }
            Refuses(new(){RiftlineM7Available=true},GameMode.Survival,"mode");
            foreach(string modifier in new[]{"InstaGib","OneInTheChamber","NoImperialist","BalancedMode","EnhancedHunters"})
            {
                var candidate=new MenuSettings{RiftlineM7Available=true};typeof(MenuSettings).GetProperty(modifier)!.SetValue(candidate,"on");
                Refuses(candidate,GameMode.Battle,modifier);
            }
            check(!OfflineMatchPreflight.TryAdmit(frozen with{Mode=GameMode.Survival},out string mismatch,prepareRoom:true)
                &&mismatch.Contains("frozen",StringComparison.OrdinalIgnoreCase),
                "direct preflight rejects mode/rule disagreement before room preparation");
            check(!OfflineMatchPreflight.TryAdmit(frozen with{MatchRules=frozen.MatchRules with{NoImperialist=true}},out string incompatible,prepareRoom:true)
                &&incompatible.Contains("No Imp",StringComparison.OrdinalIgnoreCase),
                "direct preflight repeats availability modifier admission before room preparation");
            pending.SetValue(null,true);
            Refuses(new(){RiftlineM7Available=true},GameMode.Battle,"active recorder");
            check(!OfflineMatchPreflight.TryAdmit(frozen,out string error,prepareRoom:true)&&error==CustomReplayBoundary.Error,
                "active recording refuses direct public offline preflight before room preparation");
            check(!OfflineLaunch.Preview(new MenuSettings(),"MP3 PROVING GROUND",GameMode.Battle,Hunter.Samus,0,0,0).MatchRules.RiftlineM7Available,
                "native-only Preview remains available while recording without selecting custom combat");
        }
        finally
        {
            pending.SetValue(null,recording);Directory.SetCurrentDirectory(cwd);LauncherPrefs.Directory=directory;
            (LauncherPrefs.LastHunter,LauncherPrefs.LastColor,LauncherPrefs.Bots,LauncherPrefs.BotLevel,LauncherPrefs.LastKind)=previous;
            Directory.Delete(isolated,true);
        }
    }
}
