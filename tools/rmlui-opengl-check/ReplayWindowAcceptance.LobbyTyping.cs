using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MphRead;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Render;
using OpenTK.Windowing.Common;

internal sealed partial class ReplayWindowAcceptance
{
    private readonly List<object> _nativeLobbyTypingChecks=new();
    private RmlUiMenuController? _nativeLobbyPriorController,_nativeLobbyController;
    private NativeLobbyTypingAuthority? _nativeLobbyAuthority;
    private int _nativeLobbyTextCallbacks,_nativeLobbyKeyCallbacks;
    private void CountLobbyText(TextInputEventArgs input)=>_nativeLobbyTextCallbacks++;
    private void CountLobbyKey(KeyboardKeyEventArgs input)=>_nativeLobbyKeyCallbacks++;
    private void PlanNativeLobbyTyping()
    {
        if(!OperatingSystem.IsMacOS())
        {Do("production AppKit lobby typing fixture is macOS-only",()=>_nativeLobbyTypingChecks.Add(new{Scope="AppKit route unavailable on this platform"}));return;}
        Wait("production lobby typing fixture starts at focused native Home",()=>HeaderWindowFocused()
            &&!_window!.HasScene&&ReadStatic<RmlUiLauncherPages>("_launcherPages") is{Suspended:false,Page:RmlUiMenuPage.Home});
        Do("substitute only idle lobby presentation authority in existing Shell",()=>
        {
            _nativeLobbyPriorController=ReadStatic<RmlUiMenuController>("_controller");
            _nativeLobbyAuthority=new();_nativeLobbyController=new(_nativeLobbyAuthority);
            typeof(RmlUiOpenGlSmoke).GetField("_controller",StaticPrivate)!.SetValue(null,_nativeLobbyController);
            typeof(RmlUiOpenGlSmoke).GetField("_lastPresentedRevision",StaticPrivate)!.SetValue(null,0L);
            _window!.TextInput+=CountLobbyText;_window.KeyDown+=CountLobbyKey;
        });
        Wait("actual Shell presents native Lobby with substituted local authority",()=>HeaderWindowFocused()
            &&ReadStatic<RmlUiLauncherPages>("_launcherPages") is{Suspended:false,Page:RmlUiMenuPage.Lobby}
            &&Host!.TryGetElementBounds(Host.CurrentInputDocument,"lobby_chat_input",out _,out _,out float w,out float h)&&w>0&&h>0);
        DoHeader("production pointer clicks actual lobby chat field",()=>PressHeader("lobby_chat_input",true));
        Wait("real native hit testing establishes chat text owner",()=>HeaderWindowFocused()
            &&Host!.FocusedElement()=="lobby_chat_input"&&Host.TryGetTextInputState(out var scope)&&!scope.Composing);
        DoHeader("AppKit queues authored a key for actual production NSWindow",()=>
        {
            ProductionCocoaKeyInput.BeginDiagnostics();
            _nativeLobbyTypingChecks.Add(new{Case="before first key",Cocoa=ProductionCocoaKeyInput.Inspect(),
                Focus=Host!.FocusedElement(),_nativeLobbyTextCallbacks,_nativeLobbyKeyCallbacks});
            ProductionCocoaKeyInput.Send("a",0);
        });
        Wait("Cocoa GLFW Renderer route inserts first lobby character",()=>LobbyTyped("a"),5000);
        DoHeader("AppKit queues authored b key for same production NSWindow",()=>ProductionCocoaKeyInput.Send("b",11));
        Wait("Cocoa GLFW Renderer route appends second lobby character",()=>LobbyTyped("ab"),5000);
        DoHeader("production pointer submits actual typed lobby message",()=>PressHeader("lobby_chat_send",true));
        Wait("actual Shell reads chat field and dispatches exactly one message",()=>
        {
            if(_nativeLobbyAuthority!.Sent!=1)return false;
            Require(_nativeLobbyAuthority.Message=="ab"&&Host!.ReadField(Host.CurrentInputDocument,"lobby_chat_input")=="",
                "actual controller received native Cocoa-typed message and cleared input");
            _nativeLobbyTypingChecks.Add(new{Case="submitted",Message=_nativeLobbyAuthority.Message,Count=_nativeLobbyAuthority.Sent,
                _nativeLobbyTextCallbacks,_nativeLobbyKeyCallbacks,Cocoa=ProductionCocoaKeyInput.Inspect(),BridgeDiagnostics=ProductionCocoaKeyInput.Diagnostics(),
                Scope="Owned programmatic NSEvent -> NSApplication dequeue -> NSWindow -> GLFW -> RenderWindow -> RmlUi -> actual Shell/controller. Physical keyboard and network transport not asserted."});
            return true;
        },5000);
        Do("restore exact original idle controller after production lobby typing fixture",RestoreNativeLobbyTyping);
        Wait("actual original Home owner resumes after native Cocoa lobby fixture",()=>HeaderWindowFocused()
            &&ReferenceEquals(ReadStatic<RmlUiMenuController>("_controller"),_nativeLobbyPriorController)
            &&ReadStatic<RmlUiLauncherPages>("_launcherPages") is{Suspended:false,Page:RmlUiMenuPage.Home});
    }
    private bool LobbyTyped(string expected)
    {
        if(!HeaderWindowFocused())return false;
        string text=Host!.ReadField(Host.CurrentInputDocument,"lobby_chat_input");
        bool scoped=Host.TryGetTextInputState(out var scope);
        if(text!=expected)
        {
            if(_elapsed.ElapsedMilliseconds-_stepStarted>3000)
                throw new InvalidOperationException($"Production Cocoa lobby typing failed: expected chars={expected.Length}, actual chars={text.Length}, GLFW text callbacks={_nativeLobbyTextCallbacks}, key callbacks={_nativeLobbyKeyCallbacks}, native focused={Host.FocusedElement()}, textscope={scoped}, composing={scope.Composing}, Cocoa={System.Text.Json.JsonSerializer.Serialize(ProductionCocoaKeyInput.Inspect())}, bridge={System.Text.Json.JsonSerializer.Serialize(ProductionCocoaKeyInput.Diagnostics())}");
            return false;
        }
        Require(_nativeLobbyTextCallbacks>=expected.Length&&_nativeLobbyKeyCallbacks>=expected.Length,
            "authored NSWindow keys traverse actual GLFW/OpenTK text and physical-key callbacks");
        _nativeLobbyTypingChecks.Add(new{Case="typed",Characters=text.Length,_nativeLobbyTextCallbacks,_nativeLobbyKeyCallbacks,
            Focus=Host.FocusedElement(),Scope=scoped,Composing=scope.Composing});return true;
    }
    private void RestoreNativeLobbyTyping()
    {
        if(_window!=null){_window.TextInput-=CountLobbyText;_window.KeyDown-=CountLobbyKey;}
        if(_nativeLobbyController==null)return;
        if(OperatingSystem.IsMacOS())ProductionCocoaKeyInput.EndDiagnostics();
        if(ReferenceEquals(ReadStatic<RmlUiMenuController>("_controller"),_nativeLobbyController))
        {
            typeof(RmlUiOpenGlSmoke).GetField("_controller",StaticPrivate)!.SetValue(null,_nativeLobbyPriorController);
            typeof(RmlUiOpenGlSmoke).GetField("_lastPresentedRevision",StaticPrivate)!.SetValue(null,0L);
        }
        _nativeLobbyController.Dispose();_nativeLobbyController=null;
    }
    private sealed class NativeLobbyTypingAuthority:IRmlUiMenuServices
    {
        public int Sent;public string Message="";
        public HubSnapshot Hub()=>new("Cocoa test",Hunter.Samus,Hunter.Samus,0,true,"Desktop");
        public string BuildVersion()=>"production-key-route-fixture";
        public RmlUiLobbyView Lobby()=>new(true,false,true,false,false,true,false,"PRIVATE KEY ROUTE","MP1 SANCTORUS","BATTLE","FFA","TYPE INTO THE EXISTING NATIVE FIELD",8,
            new[]{new RmlUiLobbyPlayer("Cocoa test","SAMUS",false,true,false)});
        public bool SendLobbyChat(string message){Message=message;Sent++;return true;}
        public Task<RmlUiDirectory> Browse(CancellationToken token)=>Task.FromResult(new RmlUiDirectory(true,"",Array.Empty<RmlUiServerCandidate>()));
        public Task<RmlUiServerCandidate?> QuickMatch(CancellationToken token)=>Task.FromResult<RmlUiServerCandidate?>(null);
        public Task<RmlUiJoinResult> Join(string endpoint,string playerName,CancellationToken token)=>Task.FromResult(new RmlUiJoinResult(false,default,"No network fixture"));
        public Task<RmlUiJoinResult> CreateLobby(LobbyCreationRequest request,CancellationToken token)=>Task.FromResult(new RmlUiJoinResult(false,default,"No network fixture"));
        public bool AcceptJoin(LaunchPlan plan)=>false;public bool OpenCreateLobby()=>false;
        public bool NavigateTo(RmlUiRouteArgument route)=>false;public bool OpenStudio()=>false;
        public bool SetReady()=>false;public bool StartMatch()=>false;public bool LeaveLobby()=>false;
        public bool NextHunter()=>false;public bool NextSuit()=>false;public bool OpenLobbyDetails()=>false;
        public RmlUiRuleDraft? ReadRules()=>null;public IReadOnlyList<string> BuiltInArenaKeys()=>Array.Empty<string>();
        public bool SubmitRules(RmlUiRuleDraft draft,out string error){error="No network fixture";return false;}
        public void Quit()=>throw new InvalidOperationException("Typing fixture cannot quit the owner.");
    }
}
