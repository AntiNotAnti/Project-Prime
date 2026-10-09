using System.Runtime.InteropServices;
using MphRead.Mods.Launcher.RmlUi.Host;

internal static class NativePageCheck
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Bounds(ulong document, [MarshalAs(UnmanagedType.LPUTF8Str)] string id,
        out float x,out float y,out float width,out float height);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Count();

    public static void Run(string library,string assets)
    {
        nint module=NativeLibrary.Load(Path.GetFullPath(library));
        NativeLibrary.SetDllImportResolver(typeof(RmlUiHost).Assembly,(name,_,_)=>name=="ProjectPrime.RmlUi.Native"?module:0);
        int checks=0;
        void Check(bool result,string message) { checks++;if(!result)throw new Exception("Real composed RmlUi: "+message); }
        try
        {
            var bounds=Marshal.GetDelegateForFunctionPointer<Bounds>(NativeLibrary.GetExport(module,"pp_rmlui_document_element_bounds"));
            var drawCount=Marshal.GetDelegateForFunctionPointer<Count>(NativeLibrary.GetExport(module,"pp_rmlui_draw_command_count"));
            foreach(var viewport in new[]{(Width:1280,Height:720,Density:1f),(Width:2560,Height:1440,Density:2f),(Width:2560,Height:1440,Density:1f)})
            {
                using var host=new RmlUiHost();
                Check(host.Initialize(viewport.Width,viewport.Height,viewport.Density,assets,RmlUiRenderBackend.DrawList),"initialize");
                using var launcher=new RmlUiLauncherPages(host);
                launcher.SetText("player_name","Actual Hunter λ");
                launcher.SetText("profile_state","LOCAL PROFILE // GAME DATA READY");
                launcher.SetText("build_version","TEST");
                launcher.SetBool("reduce_motion",true);
                launcher.ShowBaseline();
                void Update() { launcher.Flush();host.Update();launcher.AfterUpdate(); }
                void InViewport(RmlUiDocumentToken doc,string id)
                {
                    Check(bounds(doc.DocumentId,id,out float x,out float y,out float w,out float h)!=0,id+" has authored native bounds");
                    Check(x>=-1 && y>=-1 && x+w<=viewport.Width+1 && y+h<=viewport.Height+1,
                        $"{id} clipped at {viewport.Width}x{viewport.Height}/{viewport.Density}dp: {x},{y},{w},{h}");
                }
                RmlUiIntent Click(RmlUiDocumentToken doc,string id,string expected)
                {
                    Update();
                    InViewport(doc,id);
                    Check(host.FocusDocument(doc,id),id+" acquires native focus");
                    host.Input.Key(2,true);host.Input.Key(2,false);host.Update();
                    RmlUiIntent matched=default;
                    while(host.TryTakeIntent(out var input))
                    {
                        string action=RmlUiIntentRegistry.ToLegacy(input);
                        Check(launcher.HandleIntent(input,out var output),"DOM callback retains active document identity");
                        if(action==expected) matched=output.Kind==0?input:output;
                    }
                    Check(matched.Kind!=0,id+" emits "+expected+" through real native DOM");
                    Update();
                    return matched;
                }
                Update();
                launcher.ObserveRelease("v99.0.0"); Update();
                InViewport(launcher.Document,"build_button");
                var openVersions=Click(launcher.Document,"build_button","notice:versions");
                Check(openVersions.Kind==RmlUiIntentKind.NoticeAction && openVersions.Argument==2,
                    "Real native clickable build chip forwards Version Manager intent");
                InViewport(launcher.Document,"notice_button");
                Check(host.FocusDocument(launcher.Document,"notice_button"),"notice button receives focus");
                host.Input.Key(2,true);host.Input.Key(2,false);host.Update();
                bool toggled=false;
                while(host.TryTakeIntent(out var notificationIntent))
                {
                    if(notificationIntent.Kind==RmlUiIntentKind.NoticeAction && notificationIntent.Argument==0)
                    {
                        Check(launcher.HandleIntent(notificationIntent,out var local) && local.Kind==0,
                            "Real native notification toggle stays within shell owner");
                        toggled=true;
                    }
                }
                Check(toggled && launcher.NoticeOpen,"Real RmlUi bell can open drawer using keyboard action");
                Update();
                InViewport(launcher.Document,"notice_0_open");
                launcher.CloseNotices();Update();
                InViewport(launcher.Document,"activity_selector");
                InViewport(launcher.Document,"deploy");
                Click(launcher.Document,"activity_selector","home:drawer-open");
                Click(launcher.Document,"drawer_training","stage:training");
                var training=Click(launcher.Document,"deploy","home:deploy");
                Check(training.Kind==RmlUiIntentKind.Navigate && training.Argument==(int)RmlUiRouteArgument.Training,"Aim Lab deploy emits dedicated route");
                launcher.ShowBaseline();
                Click(launcher.Document,"activity_selector","home:drawer-open");
                Click(launcher.Document,"drawer_browser","stage:browser");
                var deployed=Click(launcher.Document,"deploy","home:deploy");
                Check(deployed.Kind==RmlUiIntentKind.PlayBrowse && deployed.Document==launcher.Document,"Deploy rewrites live page token");
                launcher.SetField("play_player_name","Real Player");
                launcher.SetField("play_create_name","Real lobby");
                launcher.SetText("play_status","REAL DIRECTORY EMPTY");
                launcher.SetBool("play_no_servers",true);
                launcher.SetText("play_create_map","MP3 PROVING GROUND");
                launcher.SetText("play_create_mode","BATTLE");
                launcher.SetText("play_create_host","HOSTED // ONLINE");
                Update();
                Click(launcher.Document,"play_create_open","play:create-open");
                Check(launcher.ReadField("play_create_name")=="Real lobby","create name seed survived real DOM transition");
                Click(launcher.Document,"play_create_submit","play:create");
                Click(launcher.Document,"play_create_cancel","play:cancel");
                Check(launcher.Page==RmlUiMenuPage.Play,"create Cancel returns browser");
                launcher.SetBool("lobby_mode",true);
                launcher.SetBool("lobby_require_ready",true);
                launcher.SetBool("lobby_owner",true);
                launcher.SetBool("lobby_can_start",true);
                launcher.SetText("lobby_ready_action","READY");
                launcher.SetText("lobby_local_hunter","SAMUS");
                launcher.SetText("lobby_name","REAL SESSION");
                launcher.SetBool("slot0_occupied",true);
                launcher.SetText("slot0_name","Real Player");
                launcher.SetText("slot0_hunter","SAMUS");
                Update();
                Click(launcher.Document,"lobby_ready","lobby:ready");
                host.SetField(launcher.Document,"lobby_chat_input","Ready for the next round?");
                Click(launcher.Document,"lobby_chat_send","lobby:chat-send");
                Check(host.ReadField(launcher.Document,"lobby_chat_input")=="Ready for the next round?","chat submission retains the editable message for the lobby controller");
                Click(launcher.Document,"lobby_match_rules","lobby:rules-open");
                launcher.SetBool("lobby_rules_open",true);
                launcher.SetBool("lobby_mode",true); // a live refresh must not dismiss the dialog
                launcher.SetBool("rules_owner",true);
                launcher.SetText("rules_map","MP3 PROVING GROUND");
                launcher.SetText("rules_mode","BATTLE");
                launcher.SetText("rules_format","FREE FOR ALL");
                launcher.SetText("rules_goal_label","POINT GOAL");
                launcher.SetField("rules_time","7:00");
                launcher.SetField("rules_goal","7");
                for(int i=0;i<16;i++)launcher.SetText("rules_toggle"+i,"OFF");
                Update();
                var rules=launcher.Manager.Top;
                Check(rules!=launcher.Document && launcher.Manager.ModalCount==1,"rules loaded into standalone native modal");
                InViewport(rules,"rules_time");InViewport(rules,"rules_goal");
                Click(rules,"rules_toggle15","lobby:rules-toggle:15");
                Click(rules,"rules_apply","lobby:rules-apply");
                host.SetField(rules,"rules_time","13:22");
                launcher.SetText("rules_status","UNSAVED RULES");Update();
                Check(launcher.ReadField("rules_time")=="13:22","periodic rules projection preserves real native input draft");
                launcher.SetBool("rules_owner",false);Update();
                host.FocusDocument(rules,"rules_apply");host.Input.Key(2,true);host.Input.Key(2,false);host.Update();
                Check(!host.TryTakeIntent(out _),"guest Apply disabled by generic boolean attribute");
                Click(rules,"rules_close_top","lobby:rules-close");
                launcher.SetBool("lobby_rules_open",false);Update();
                Check(launcher.Manager.ModalCount==0 && !host.IsAlive(rules),"rules close retired native modal lifetime");
                host.Render(viewport.Width,viewport.Height);
                Check(drawCount()>0,"composed shell/page produces actual native draw-list commands");
                Console.WriteLine($"Real composed page layout/input passed {viewport.Width}x{viewport.Height} density {viewport.Density}");
            }
            Console.WriteLine($"Real composed RmlUi page checks passed: {checks} assertions across 3 viewport/density targets.");
        }
        finally { NativeLibrary.Free(module); }
    }
}
