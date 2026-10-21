using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead;
using MphRead.Mods;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Render;
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

// Real native authored DOM, translated input and GL composition. Services are
// invented local values and explicit private preferences: no game files, match Scene or media capture.
internal static class OfflineCharacterGlAcceptance
{
    internal static int Run(string assets, int requestedWidth, int requestedHeight)
    {
        assets = Path.GetFullPath(assets);
        string root = Environment.GetEnvironmentVariable("PROJECT_PRIME_USER_DATA") ?? "";
        if (!Path.IsPathFullyQualified(root))
            throw new ArgumentException("Offline fixture requires explicit private PROJECT_PRIME_USER_DATA; map choices now persist.");
        root = Path.GetFullPath(root);
        Directory.CreateDirectory(root);
        Directory.SetCurrentDirectory(root);
        if (!String.Equals(Path.GetFullPath(LauncherPrefs.Directory), root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Offline fixture preferences must stay in the explicit private root.");
        int checks=0,translated=0;
        void Check(bool value,string why){checks++;if(!value)throw new InvalidOperationException(why);}
        GraphicsBackendPolicy.Configure("opengl");
        var settings=DesktopGlContext.Settings(background:true);settings.ClientSize=new(requestedWidth,requestedHeight);
        using var window=new NativeWindow(settings);
        using var graphics=new DesktopGraphicsSession(window);
        for(int i=0;i<3&&(window.FramebufferSize.X!=requestedWidth||window.FramebufferSize.Y!=requestedHeight);i++)
        {
            var c=window.ClientSize;var f=window.FramebufferSize;
            Check(c.X>0&&c.Y>0&&f.X>0&&f.Y>0,"native client/framebuffer dimensions remain positive");
            window.ClientSize=new(Math.Max(1,(int)Math.Round((double)requestedWidth*c.X/f.X)),Math.Max(1,(int)Math.Round((double)requestedHeight*c.Y/f.Y)));
            GLFW.PollEvents();DesktopGraphicsSession.Resize(window);
        }
        int width=window.FramebufferSize.X,height=window.FramebufferSize.Y;
        Check(width==requestedWidth&&height==requestedHeight,"actual framebuffer equals requested layout acceptance size");
        using var host=new RmlUiHost();
        Check(host.Initialize(width,height,1,Path.GetFullPath(assets),RmlUiRenderBackend.OpenGl),"real native OpenGL host initializes");
        using var manager=new RmlUiPageManager(host);
        using var layout=new RmlUiLauncherPages(host,manager);
        var services=new Services();using var controller=new RmlUiMenuController(services);
        host.Input.SetFramebufferScale(1,1);
        var priorOwner=GameState.Current.Owner;
        var prefs=(LauncherPrefs.LastHunter,LauncherPrefs.LastColor,LauncherPrefs.Bots,LauncherPrefs.BotLevel,LauncherPrefs.LastKind);
        long presented=0;RmlUiIntent selector=default;
        Check(controller.Dispatch(RmlUiIntentKind.Navigate,(int)RmlUiRouteArgument.Offline),"controller opens authored Offline page");
        Settle(250);var offline=layout.Document;
        Check(host.IsAlive(offline)&&host.CurrentInputDocument==offline,"actual Offline document owns input");
        Check(!host.TryGetElementBounds(offline,"offline_mechanics",out _,out _,out float mechanicsWidth,out float mechanicsHeight)
            || mechanicsWidth == 0 || mechanicsHeight == 0,"deferred Weavel mechanics section is hidden in shipped setup");
        if(width>800)
        {
            Check(host.TryGetElementBounds(offline,"offline_match_panel",out float matchX,out _,out float matchWidth,out _)
                &&host.TryGetElementBounds(offline,"offline_roster_panel",out float rosterX,out _,out float rosterWidth,out _)
                &&matchWidth>0&&rosterWidth>0&&matchX+matchWidth<=rosterX+1,
                "wide native bot-lobby setup and roster panels occupy separate columns");
        }
        Frame();
        string lobbyCapture=Path.Combine(root,$"offline-bot-lobby-{width}x{height}.png");
        Check(ScreenCapture.SaveWindow(width,height,lobbyCapture),"pristine native Bot Lobby screenshot saves before deferred mechanics reveal");
        Console.WriteLine("RML OFFLINE BOT LOBBY GL screenshot: "+lobbyCapture);
        // Only this fixture reveals the deferred controls so existing experimental
        // runtime coverage remains available without exposing them to players.
        host.SetBool(offline,"visible:offline_mechanics",true);
        Settle(150);
        // Exercise the actual authored bot controls before entering mechanics
        // practice, which deliberately forces its own opponent count.
        while(controller.Tick().Bindings["offline_bots_value"].Text != "2 BOTS")
            Check(controller.Dispatch(RmlUiIntentKind.OfflineChoice,3),"set two synthetic bots");
        Settle(150);
        Click("offline_bot_slot1",RmlUiIntentKind.OfflineChoice,25);
        Check(Label("offline_bot_slot1").Contains("BOT 2",StringComparison.Ordinal)
            &&Label("offline_bot_selected").Contains("BOT 2 / 2",StringComparison.Ordinal),
            "native roster row selects the corresponding occupied offline bot slot");
        Click("offline_bot_slot0",RmlUiIntentKind.OfflineChoice,24);
        foreach(var choice in new[]{(18,"offline_bot_hunter"),(19,"offline_bot_suit"),
            (20,"offline_bot_skill"),(22,"offline_bot_handicap"),(17,"offline_bot_selected")})
            Click(choice.Item2,RmlUiIntentKind.OfflineChoice,choice.Item1);
        Check(Label("offline_bot_selected").Contains("BOT 2 / 2",StringComparison.Ordinal)
            &&Label("offline_bot_hunter").Contains("RANDOM",StringComparison.Ordinal),
            "native bot selector displays the independent second bot profile");
        Click("offline_previous_18",RmlUiIntentKind.OfflineChoicePrevious,18);
        Check(controller.Dispatch(RmlUiIntentKind.OfflineChoice,2),"switch synthetic match to teams");Settle(150);
        Click("offline_bot_team",RmlUiIntentKind.OfflineChoice,21);
        Click("offline_player_team",RmlUiIntentKind.OfflineChoice,23);
        Check(Label("offline_bot_team").Contains("TEAM 1",StringComparison.Ordinal)
            &&Label("offline_player_team").Contains("TEAM 2",StringComparison.Ordinal),"native team controls update labels");
        Check(controller.Dispatch(RmlUiIntentKind.OfflineChoice,2),"restore FFA");Settle(150);

        string preview = Path.Combine(Path.GetTempPath(),"prime-offline-card-"+Guid.NewGuid().ToString("N")+".tga");
        File.WriteAllBytes(preview,RmlUiMapPreviewCache.EncodeTga(
            Enumerable.Range(0,64).SelectMany(_=>new byte[]{13,211,133,255}).ToArray(),8,8));
        services.Preview=preview;
        try
        {
            Click("offline_arena",RmlUiIntentKind.OfflineOpenArena);
            Check(Label("offline_arena_pick_0").Contains("Data Shrine",StringComparison.Ordinal),
                "native offline card displays retail map name");
            Focus("offline_arena_pick_0");
            Check(host.TryGetElementBounds(manager.Top,"offline_arena_image_0",out float ix,out float iy,out float iw,out float ih)
                &&iw>0&&ih>0,"offline card binds visible preview image");
            byte[] pixel=new byte[4];
            GL.ReadPixels((int)(ix+iw/2),height-1-(int)(iy+ih/2),1,1,PixelFormat.Rgba,PixelType.UnsignedByte,pixel);
            Check(Math.Abs(pixel[0]-13)<12&&Math.Abs(pixel[1]-211)<12&&Math.Abs(pixel[2]-133)<12,
                "offline map preview paints bound synthetic TGA pixels");
            Click("offline_arena_pick_1",RmlUiIntentKind.OfflineSelectArena,1);
            Check(Label("offline_arena").Contains("Sanctorus",StringComparison.Ordinal),
                "native arena selection keeps display name after closing picker");
        }
        finally { services.Preview=""; File.Delete(preview); }
        Check(Label("offline_character").Contains("NATIVE",StringComparison.Ordinal),"actual DOM begins with Native mechanics");
        selector=Click("offline_character",RmlUiIntentKind.OfflineChoice,16);
        Check((uint)selector.Kind==140&&selector.Argument==16,"literal native kind140/choice16 is translated from the real authored button");
        Check(Label("offline_character").Contains("TROOPER WALL-CLING",StringComparison.Ordinal),"actual mechanics control shows wall-cling selection");
        Check(controller.Tick().Bindings["offline_hunter_value"].Text=="WEAVEL"
            &&controller.Tick().Bindings["offline_bots_value"].Text=="0 BOTS"
            &&controller.Tick().Bindings["offline_mode_value"].Text=="Battle","controller selection has Weavel/Battle/zero bots");
        Check(controller.Tick().Bindings["offline_character_notice"].Text.Contains("Weavel model",StringComparison.Ordinal)
            &&controller.Tick().Bindings["offline_character_notice"].Text.Contains("No drone, blaster or replays",StringComparison.Ordinal),"selection notice accurately scopes existing model and missing abilities");
        Focus("offline_start");
        Check(Label("offline_start")=="START WALL-CLING PRACTICE","actual native start button displays practice action");
        Check(NativeSemanticReadback.Read(host,offline).ContainsText("No drone, blaster or replays"),"actual native DOM exposes the scoped practice notice");
        Click("offline_start",RmlUiIntentKind.OfflineLaunchMatch);
        Settle(200);
        Check(services.Validations==1&&services.Launches==1&&services.Captured is { } request
            &&request.Character==OfflineCharacterMode.TrooperWallClingPractice&&request.Hunter==Hunter.Weavel
            &&request.Mode==GameMode.Battle&&request.Bots==0,"actual controller validation/completion delivers the captured explicit practice request");
        Click("offline_character",RmlUiIntentKind.OfflineChoice,16);
        Check(Label("offline_character").Contains("TROOPER SCOUT DRONE",StringComparison.Ordinal)
            &&controller.Tick().Bindings["offline_hunter_value"].Text=="WEAVEL"
            &&controller.Tick().Bindings["offline_bots_value"].Text=="0 BOTS"
            &&controller.Tick().Bindings["offline_mode_value"].Text=="Battle","second real click selects valid explicit drone practice");
        Check(Label("offline_start")=="START DRONE PRACTICE"
            &&controller.Tick().Bindings["offline_character_notice"].Text.Contains("Native fallback models",StringComparison.Ordinal)
            &&controller.Tick().Bindings["offline_character_notice"].Text.Contains("No blaster or replays",StringComparison.Ordinal),
            "actual drone label and notice accurately scope its native fallbacks and missing abilities");
        Focus("offline_start");
        Check(NativeSemanticReadback.Read(host,offline).ContainsText("No blaster or replays"),
            "actual native DOM exposes the drone practice scope");
        services.Pending=new(TaskCreationOptions.RunContinuationsAsynchronously);
        Click("offline_start",RmlUiIntentKind.OfflineLaunchMatch);
        Check(controller.Busy&&services.Validations==2&&services.Launches==1
            &&services.Captured is {Character:OfflineCharacterMode.TrooperScoutDronePractice,Hunter:Hunter.Weavel,Mode:GameMode.Battle,Bots:0},
            "async map validation captures the exact immutable drone request before launch");
        Check(!controller.Dispatch(RmlUiIntentKind.OfflineChoice,16),"busy controller cannot replace its captured drone mechanics");
        services.Pending.SetResult("");Settle(200);services.Pending=null;
        Check(services.Validations==2&&services.Launches==2
            &&services.Captured is {Character:OfflineCharacterMode.TrooperScoutDronePractice,Hunter:Hunter.Weavel,Mode:GameMode.Battle,Bots:0},
            "validation completion delivers exactly one captured drone request to existing launch owner");
        Click("offline_character",RmlUiIntentKind.OfflineChoice,16);
        Check(Label("offline_character").Contains("DRONE COMBAT PRACTICE",StringComparison.Ordinal)
            &&controller.Tick().Bindings["offline_hunter_value"].Text=="WEAVEL"
            &&controller.Tick().Bindings["offline_bots_value"].Text=="1 BOTS"
            &&controller.Tick().Bindings["offline_mode_value"].Text=="Battle"
            &&Label("offline_start")=="START DRONE COMBAT PRACTICE"
            &&controller.Tick().Bindings["offline_character_notice"].Text.Contains("Zoom for a laser",StringComparison.Ordinal)
            &&services.Captured?.Character==OfflineCharacterMode.TrooperScoutDronePractice,
            "third real click selects one-opponent combat without replacing the delivered zero-bot drone capture");
        services.Pending=new(TaskCreationOptions.RunContinuationsAsynchronously);
        Click("offline_start",RmlUiIntentKind.OfflineLaunchMatch);
        Check(controller.Busy&&services.Validations==3&&services.Launches==2
            &&services.Captured is {Character:OfflineCharacterMode.TrooperScoutDroneCombatPractice,Hunter:Hunter.Weavel,Mode:GameMode.Battle,Bots:1},
            "combat validation captures exact mode3 actor/match/one-opponent request");
        Check(!controller.Dispatch(RmlUiIntentKind.OfflineChoice,16),"busy combat validation preserves its captured identity");
        services.Pending.SetResult("");Settle(200);services.Pending=null;
        Check(services.Validations==3&&services.Launches==3
            &&services.Captured is {Character:OfflineCharacterMode.TrooperScoutDroneCombatPractice,Hunter:Hunter.Weavel,Mode:GameMode.Battle,Bots:1},
            "combat completion delivers one exact captured request");
        Click("offline_character",RmlUiIntentKind.OfflineChoice,16);
        Check(Label("offline_character").Contains("TROOPER ARSENAL",StringComparison.Ordinal)
            &&Label("offline_start")=="START ARSENAL PRACTICE"
            &&controller.Tick().Bindings["offline_bots_value"].Text=="1 BOTS"
            &&controller.Tick().Bindings["offline_character_notice"].Text.Contains("Unsupported states use native fallback.",StringComparison.Ordinal)
            &&controller.Tick().Bindings["offline_character_notice"].Text.Contains("60 shared UA each life",StringComparison.Ordinal)
            &&services.Captured?.Character==OfflineCharacterMode.TrooperScoutDroneCombatPractice,
            "fourth real click selects Arsenal with exact notice while delivered combat capture remains detached");
        services.Pending=new(TaskCreationOptions.RunContinuationsAsynchronously);
        Click("offline_start",RmlUiIntentKind.OfflineLaunchMatch);
        Check(controller.Busy&&services.Validations==4&&services.Launches==3
            &&services.Captured is {Character:OfflineCharacterMode.TrooperArsenalPractice,Hunter:Hunter.Weavel,Mode:GameMode.Battle,Bots:1},
            "Arsenal async validation captures explicit mode4 actor/match/one-opponent identity");
        Check(!controller.Dispatch(RmlUiIntentKind.OfflineChoice,16),"busy Arsenal request cannot be replaced by later selection");
        services.Pending.SetResult("");Settle(200);services.Pending=null;
        Check(services.Validations==4&&services.Launches==4
            &&services.Captured is {Character:OfflineCharacterMode.TrooperArsenalPractice,Hunter:Hunter.Weavel,Mode:GameMode.Battle,Bots:1},
            "Arsenal validation completion delivers exact immutable request once");
        Click("offline_character",RmlUiIntentKind.OfflineChoice,16);
        Check(Label("offline_character").Contains("NATIVE",StringComparison.Ordinal)
            &&controller.Tick().Bindings["offline_character_notice"].Text==""
            &&services.Captured?.Character==OfflineCharacterMode.TrooperArsenalPractice,
            "fifth real click returns Native without mutating delivered Arsenal capture");
        Click("offline_character",RmlUiIntentKind.OfflineChoice,16);

        foreach(var mechanics in new[]{OfflineCharacterMode.TrooperWallClingPractice,OfflineCharacterMode.TrooperScoutDronePractice,OfflineCharacterMode.TrooperScoutDroneCombatPractice,OfflineCharacterMode.TrooperArsenalPractice})
        {
            Check(Label("offline_character").Contains(mechanics==OfflineCharacterMode.TrooperWallClingPractice?"TROOPER WALL-CLING":mechanics==OfflineCharacterMode.TrooperScoutDronePractice?"TROOPER SCOUT DRONE":mechanics==OfflineCharacterMode.TrooperScoutDroneCombatPractice?"DRONE COMBAT PRACTICE":"TROOPER ARSENAL",StringComparison.Ordinal),
                "conflict fixtures exercise each actual selected practice state");
            foreach(var conflict in new[]{
                new RmlUiOfflineRules("7","7:00","3:00","medium",1<<10),
                new RmlUiOfflineRules("7","7:00","3:00","medium",0){RiftlineM7Available=true},
                new RmlUiOfflineRules("7","7:00","3:00","medium",0){MovementModel="Momentum"},
                new RmlUiOfflineRules("7","7:00","3:00","medium",0){MovementModel="Quake3"},
                new RmlUiOfflineRules("7","7:00","3:00","medium",0){MovementModel="Traversal"},
                new RmlUiOfflineRules("7","7:00","3:00","medium",0){MovementModel="Parkour"},
                new RmlUiOfflineRules("7","7:00","3:00","medium",0){MovementModel="UnknownFixture"}})
            {
                services.Rules=conflict;int validations=services.Validations,launches=services.Launches;
                Click("offline_start",RmlUiIntentKind.OfflineLaunchMatch,expected:false);
                Check(services.Validations==validations&&services.Launches==launches,"conflicting current rule snapshot refuses before async map validation or launch service");
                string error=controller.Tick().Bindings["offline_error"].Text;
                string expectedReason=conflict.MovementModel=="UnknownFixture"
                    ? "MovementModel must be Native, Momentum, Quake3, Traversal or Parkour." : "native movement";
                Check(error.Contains(expectedReason,StringComparison.OrdinalIgnoreCase),"refusal provides the specific policy or unknown-model parse reason");
                Focus("offline_start");
                Check(NativeSemanticReadback.Read(host,offline).ContainsText(expectedReason),"actual native page exposes the same refusal text");
            }
            services.Rules=null;
            Click("offline_start",RmlUiIntentKind.OfflineLaunchMatch,expected:false);
            Check(services.Validations==4&&services.Launches==4
                &&controller.Tick().Bindings["offline_error"].Text.Contains("REOPEN OFFLINE SETUP",StringComparison.Ordinal),"missing current rule snapshot refuses actionably before a job");
            services.Rules=new("7","7:00","3:00","medium",0);
            if(mechanics!=OfflineCharacterMode.TrooperArsenalPractice)Click("offline_character",RmlUiIntentKind.OfflineChoice,16);
        }

        Click("offline_character",RmlUiIntentKind.OfflineChoice,16); // Arsenal -> Native
        Click("offline_character",RmlUiIntentKind.OfflineChoice,16); // Native -> wall
        Click("offline_character",RmlUiIntentKind.OfflineChoice,16); // wall -> drone

        // Only the genuine Aim Lab route may own training choices. Both panels
        // reuse the Offline document, so stale cross-route intents need policy
        // refusal in addition to native modal/document sequence fences.
        foreach(int trainingChoice in new[]{13,14,15})
            Check(!controller.Dispatch(RmlUiIntentKind.OfflineChoice,trainingChoice),
                "Battle refuses stale Aim Lab training choice"+trainingChoice);
        Check(!controller.Dispatch(RmlUiIntentKind.OfflineOpenTrainingOptions)
            &&controller.Tick().Bindings["offline_character_value"].Text=="TROOPER SCOUT DRONE",
            "Battle refuses training options without changing its captured drone draft");
        // Same authored options dialog:13 has an ID; native Tab moves to the
        // following no-ID14/15 buttons. No markup IDs or production hooks added.
        Check(controller.Dispatch(RmlUiIntentKind.Navigate,(int)RmlUiRouteArgument.Training),"genuine Aim Lab route opens");
        Settle(180);
        Check(controller.Tick().Bindings["offline_training_mode"].Boolean&&Label("offline_heading")=="AIM LAB",
            "actual native page switches to the genuine Aim Lab heading and route");
        Check(!controller.Dispatch(RmlUiIntentKind.OfflineChoice,16)
            &&!controller.Dispatch(RmlUiIntentKind.OfflineLaunchMatch)
            &&!controller.Dispatch(RmlUiIntentKind.OfflineOpenArena)
            &&controller.Tick().Bindings["offline_character_value"].Text=="TROOPER SCOUT DRONE",
            "Aim Lab refuses stale mechanics16 and Battle launch/arena intents without mutating the Battle draft");
        Click("offline_training_options",RmlUiIntentKind.OfflineOpenTrainingOptions);
        var modal=manager.Top;Check(modal!=offline&&manager.ModalCount==1,"actual native training options own the modal input lease");
        var before13=controller.Tick().Bindings["offline_training_difficulty_value"].Text;
        var before14=controller.Tick().Bindings["offline_training_distance_value"].Text;
        var before15=controller.Tick().Bindings["offline_training_scope_value"].Text;
        Click("offline_training_difficulty",RmlUiIntentKind.OfflineChoice,13);
        Check(controller.Tick().Bindings["offline_training_difficulty_value"].Text!=before13,"real choice13 still changes training difficulty");
        Focus("offline_training_difficulty");Key(1);Key(1);Key(2);Drain(RmlUiIntentKind.OfflineChoice,14,true,modal);
        Check(controller.Tick().Bindings["offline_training_distance_value"].Text!=before14,"real Tab/Enter choice14 still changes target distance");
        Key(1);Key(1);Key(2);Drain(RmlUiIntentKind.OfflineChoice,15,true,modal);
        Check(controller.Tick().Bindings["offline_training_scope_value"].Text!=before15,"real Tab/Enter choice15 still changes scope");
        Check(!layout.HandleIntent(selector with{Sequence=ulong.MaxValue-2},out var covered)||covered==default,"covered parent selector cannot pass the actual modal ownership boundary");
        Click("offline_training_done",RmlUiIntentKind.OfflineCloseTrainingOptions);
        Check(!host.IsAlive(modal),"closing training retires its actual document");
        Check(controller.Dispatch(RmlUiIntentKind.Navigate,(int)RmlUiRouteArgument.Offline),"return to Offline uses same controller draft");
        Settle(150);var closing=layout.Document;
        Check(!controller.Tick().Bindings["offline_training_mode"].Boolean&&Label("offline_heading")=="BOT LOBBY"
            &&Label("offline_character").Contains("TROOPER SCOUT DRONE",StringComparison.Ordinal),
            "returning to actual native Battle page restores its original explicit drone selection");
        Check(!controller.Dispatch(RmlUiIntentKind.OfflineChoice,13)
            &&!controller.Dispatch(RmlUiIntentKind.OfflineOpenTrainingOptions),
            "closed Aim Lab modal cannot restore training choices through the Battle route");
        Click("offline_back",RmlUiIntentKind.Navigate,(int)RmlUiRouteArgument.Home);
        Check(!layout.HandleIntent(new(RmlUiIntentKind.OfflineChoice,16,closing,ulong.MaxValue-1),out var retired)||retired==default,"retired Offline document cannot mutate the live selection");
        Check(services.Validations==4&&services.Launches==4&&services.Saves==0
            &&ReferenceEquals(GameState.Current.Owner,priorOwner)
            &&prefs==(LauncherPrefs.LastHunter,LauncherPrefs.LastColor,LauncherPrefs.Bots,LauncherPrefs.BotLevel,LauncherPrefs.LastKind),"all native paths preserve prefs/Scene and only four distinct permitted in-memory practice launch callbacks");
        Check(GL.GetError()==OpenTK.Graphics.OpenGL.ErrorCode.NoError,"native authored Offline acceptance finishes GL-clean");
        Console.WriteLine($"OFFLINE CHARACTER NATIVE GL PASS {checks} checks; {translated} translated intents; actual framebuffer {width}x{height}; synthetic services, isolated map preferences, no media/captures/Scene writes");
        return 0;

        string Label(string id)=>NativeSemanticReadback.Read(host,manager.Top).Label(id);
        void Frame()
        {
            var state=controller.Tick();
            if(state.Revision>presented){foreach(var b in state.Bindings){if(b.Value.Kind==RmlUiBindingKind.Text)layout.SetText(b.Key,b.Value.Text);else layout.SetBool(b.Key,b.Value.Boolean);}presented=state.Revision;}
            layout.Flush();host.Update();layout.AfterUpdate();GL.BindFramebuffer(FramebufferTarget.Framebuffer,0);GL.Viewport(0,0,width,height);
            GL.Disable(EnableCap.ScissorTest);GL.ClearColor(.025f,.05f,.08f,1);GL.Clear(ClearBufferMask.ColorBufferBit|ClearBufferMask.DepthBufferBit|ClearBufferMask.StencilBufferBit);RmlUiOpenGlFrame.Draw(host,width,height);
        }
        void Settle(int ms){var timer=Stopwatch.StartNew();do{Frame();Thread.Sleep(10);}while(timer.ElapsedMilliseconds<ms);Frame();}
        void Focus(string id)
        {
            Frame();Check(host.FocusDocument(manager.Top,id),"actual native focus/scroll reaches "+id);Settle(350);
            Check(host.TryGetElementBounds(manager.Top,id,out float x,out float y,out float w,out float h)
                &&w>0&&h>0&&x+w/2>=0&&x+w/2<width&&y+h/2>=0&&y+h/2<height,"actual native control is reachable within the framebuffer:"+id);
        }
        void Key(int key){host.Input.Key(key,true);host.Input.Key(key,false);host.Update();}
        RmlUiIntent Click(string id,RmlUiIntentKind kind,int argument=0,bool expected=true)
        {
            Focus(id);var doc=manager.Top;
            Check(host.TryGetElementBounds(doc,id,out float x,out float y,out float w,out float h),"actual native button bounds resolve:"+id);
            while(host.TryTakeIntent(out _)){}
            host.Input.PointerButton(0,x+w/2,y+h/2,true);host.Input.PointerButton(0,x+w/2,y+h/2,false);host.Update();
            return Drain(kind,argument,expected,doc);
        }
        RmlUiIntent Drain(RmlUiIntentKind kind,int argument,bool expected,RmlUiDocumentToken doc)
        {
            RmlUiIntent matched=default;int count=0;
            while(host.TryTakeIntent(out var raw))
            {
                if(!layout.HandleIntent(raw,out var forward)||forward==default)continue;
                bool accepted=controller.Dispatch(forward.Kind,forward.Argument);
                if(forward.Kind!=kind||forward.Argument!=argument)continue;
                Check(accepted==expected&&forward.Document==doc,"real native action reaches controller with the expected result:"+kind+"/"+argument);
                Check(layout.HandleIntent(raw,out var duplicate)&&duplicate==default,"actual foreground sequence cannot execute twice");
                matched=raw;count++;translated++;
            }
            Check(count==1,"real native input emits exactly one expected intent:"+kind+"/"+argument);Settle(150);return matched;
        }
    }

    private sealed class Services:IRmlUiMenuServices
    {
        public RmlUiOfflineRules? Rules=new("7","7:00","3:00","medium",0);
        public int Validations,Launches,Saves;
        public RmlUiOfflineRequest? Captured;
        public TaskCompletionSource<string>? Pending;
        public string Preview="";
        public HubSnapshot Hub()=>new("SYNTHETIC",Hunter.Samus,Hunter.Samus,0,true,"NO GAME FILES OR SCENE");
        public string BuildVersion()=>"offline-character-native-fixture";
        public RmlUiLobbyView Lobby()=>new(false,false,false,false,false,false,false,"","","","","",8,Array.Empty<RmlUiLobbyPlayer>());
        public IReadOnlyList<string> BuiltInArenaKeys()=>new[]{"MP1 SANCTORUS","MP11 BREAKTHROUGH"};
        public RmlUiArenaPreview[] ReadMapPickerPreviews(IReadOnlyList<string> rooms)
            =>rooms.Select(_=>new RmlUiArenaPreview(Preview,"SYNTHETIC PREVIEW")).ToArray();
        public RmlUiOfflineRules? ReadOfflineRules()=>Rules;
        public Task<string> ValidateOfflineAsync(RmlUiOfflineRequest request,CancellationToken token){Validations++;Captured=request;return Pending?.Task.WaitAsync(token)??Task.FromResult("");}
        public bool LaunchOffline(RmlUiOfflineRequest request){Launches++;Captured=request;return false;}
        public bool SaveOfflineRules(RmlUiOfflineRules expected,RmlUiOfflineRules next,out string error){Saves++;error="NO PERSISTENCE IN FIXTURE";return false;}
        public Task<RmlUiDirectory> Browse(CancellationToken token)=>Task.FromResult(new RmlUiDirectory(false,"",Array.Empty<RmlUiServerCandidate>()));
        public Task<RmlUiServerCandidate?> QuickMatch(CancellationToken token)=>Task.FromResult<RmlUiServerCandidate?>(null);
        public Task<RmlUiJoinResult> Join(string endpoint,string name,CancellationToken token)=>Task.FromResult(new RmlUiJoinResult(false,default,"FIXTURE"));
        public Task<RmlUiJoinResult> CreateLobby(LobbyCreationRequest request,CancellationToken token)=>Task.FromResult(new RmlUiJoinResult(false,default,"FIXTURE"));
        public bool AcceptJoin(LaunchPlan p)=>false;public bool OpenCreateLobby()=>false;public bool NavigateTo(RmlUiRouteArgument d)=>false;public bool OpenStudio()=>false;
        public bool SetReady()=>false;public bool StartMatch()=>false;public bool LeaveLobby()=>false;public bool NextHunter()=>false;public bool NextSuit()=>false;public bool OpenLobbyDetails()=>false;
        public RmlUiRuleDraft? ReadRules()=>null;public bool SubmitRules(RmlUiRuleDraft draft,out string error){error="FIXTURE";return false;}public void Quit(){}
    }
}
