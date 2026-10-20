using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MphRead;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Network;

// Actual production controls and callbacks; session facts are explicitly staged.
// No game GL, transport, match launch, user-data write or physical input claim.
Headless.Enter();
string? isolated=Environment.GetEnvironmentVariable("PROJECT_PRIME_USER_DATA");
if(string.IsNullOrWhiteSpace(isolated))throw new InvalidOperationException("Set isolated PROJECT_PRIME_USER_DATA.");
string output=Path.GetFullPath(args.Length>0?args[0]:"artifacts/custom-loadout-ui-check");
Directory.CreateDirectory(output); Directory.CreateDirectory(isolated);
Directory.SetCurrentDirectory(isolated); LauncherPrefs.Directory=isolated;
if(!GuiLauncher.EnsureSetup(requireDisplay:false))return 2;
const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
var sessionProperty=typeof(NetSession).GetProperty(nameof(NetSession.ServerSession))!;
var slotProperty=typeof(NetSession).GetProperty(nameof(NetSession.LocalSlot))!;
var oldSession=NetSession.ServerSession;int oldSlot=NetSession.LocalSlot;
bool oldPhone=Deck.Phone,oldStill=Deck.Still;
var scene=new Scene(default,null!,null!,_=>{},()=>{},initializeRuntime:false);
GameState.Current=scene.GameState;PlayerEntity.LegacyRegistry=scene.Players;
scene.GameState.Mode=GameMode.Battle;
string product=typeof(Scene).Assembly.Location;
string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
string productHash=Hash(product);
var evidence=new List<object>();int checks=0;
void Check(bool pass,string label)
{if(!pass)throw new InvalidOperationException(label);checks++;Console.WriteLine("LOADOUT UI PASS "+label);}
object Field(object value,string name)=>value.GetType().GetField(name,Hidden)!.GetValue(value)!;
void Click(HubNavButton button)=>((EventHandler)typeof(HubNavButton).GetField("Click",Hidden)!.GetValue(button)!).Invoke(button,EventArgs.Empty);
void Refresh(LobbyScreen lobby)=>typeof(LobbyScreen).GetMethod("Refresh",Hidden)!.Invoke(lobby,null);
string Preferences()
{
    var files=Directory.GetFiles(isolated,"*",SearchOption.AllDirectories)
        .Where(path=>Path.GetFileName(path) is "launcher.txt" or "settings.json" or "controls.txt");
    return string.Join("\n",files.Order().Select(path=>Path.GetRelativePath(isolated,path)+":"+Hash(path)));
}
object Geometry(Control control,Control root,Size size)
{
    Point start=control.TranslatePoint(default,root)??new(double.NaN,double.NaN);
    Point end=control.TranslatePoint(new(control.Bounds.Width,control.Bounds.Height),root)??new(double.NaN,double.NaN);
    bool contained=double.IsFinite(start.X)&&double.IsFinite(start.Y)&&start.X>=0&&start.Y>=0
        &&end.X<=size.Width+.1&&end.Y<=size.Height+.1&&end.X>start.X&&end.Y>start.Y;
    return new {x=start.X,y=start.Y,width=end.X-start.X,height=end.Y-start.Y,contained};
}
void Capture(PrimeShell shell,Control target,Control help,string name,Size size,bool available)
{
    string path=Path.Combine(output,name+".png");
    Check(UiCapture.Capture(shell,path,size),"actual production layout captured "+name);
    evidence.Add(new {name,available,width=size.Width,height=size.Height,path,sha256=Hash(path),
        target=Geometry(target,shell,size),help=Geometry(help,shell,size)});
}
try
{
    Deck.Still=true;
    Dispatcher.UIThread.Invoke(()=>
    {
        foreach(var (device,size) in new[]{("desktop",new Size(940,528)),("phone",new Size(360,800))})
        foreach(bool available in new[]{false,true})
        {
            Deck.Phone=device=="phone";
            sessionProperty.SetValue(null,null);
            var settings=new MenuSettings {RoomKey="MP1 SANCTORUS",RiftlineM7Available=available};
            OfflineWorkspace? offline=null;
            PrimeShell? shell=null;
            shell=new PrimeShell(route=>route==PrimeRoute.Offline
                ?offline=new OfflineWorkspace(settings,new[]{"MP1 SANCTORUS"},shell!.Overlays)
                :new TextBlock {Text="SYNTHETIC CAPTURE OWNER"},()=>{});
            using(shell)
            {
                shell.Start();shell.Router.Navigate(PrimeRoute.Offline);
                Click(offline!.GetLogicalDescendants().OfType<PrimeButton>().Single(b=>b.Label=="ADVANCED MATCH RULES"));
                var toggle=shell.Overlays.GetLogicalDescendants().OfType<ToggleRow>().Single(t=>t.Label=="Riftline M7 available");
                var help=shell.Overlays.GetLogicalDescendants().OfType<TextBlock>().Single(t=>t.Text?.StartsWith("Battle (free-for-all) only.")==true);
                Check(toggle.On==available&&shell.Overlays.IsOpen,"offline rules display exact captured availability");
                Capture(shell,toggle,help,$"offline-rules-{device}-{(available?"on":"off")}",size,available);
            }
            slotProperty.SetValue(null,0);
            sessionProperty.SetValue(null,new SessionStatePacket {AuthorityEpoch=7,MatchId=1,Revision=1,OwnerSlot=0,
                Phase=SessionPhase.Lobby,Policy=ServerSessionPolicy.Lobby,MaxPlayers=8,
                WorldProfile=MphRead.Mods.Multiplayer.MatchWorldProfile.Resolve(8),
                Match=new MatchDefinition {RoomKey="MP1 SANCTORUS",Mode=GameMode.Battle,RiftlineM7Available=available}});
            LobbyScreen? lobby=null;shell=null;
            shell=new PrimeShell(route=>route==PrimeRoute.Lobby
                ?lobby=new LobbyScreen(new[]{"MP1 SANCTORUS"}){Overlays=shell!.Overlays}
                :new TextBlock {Text="SYNTHETIC CAPTURE OWNER"},()=>{});
            using(shell)
            {
                shell.Start();shell.Router.Navigate(PrimeRoute.Lobby);Refresh(lobby!);
                Click(lobby!.GetLogicalDescendants().OfType<PrimeButton>().Single(b=>b.Label=="ADVANCED RULES"));
                var toggle=(ButtonToggleRow)Field(lobby!,"_riftlineM7");
                var help=shell.Overlays.GetLogicalDescendants().OfType<TextBlock>().Single(t=>t.Text?.StartsWith("Battle (free-for-all), human players only.")==true);
                Check(toggle.On==available&&shell.Overlays.IsOpen,"lobby rules display exact staged authoritative availability");
                Capture(shell,toggle,help,$"lobby-rules-{device}-{(available?"on":"off")}",size,available);
            }
        }
        // Exercise the real start callback with a formerly saved opt-in and a
        // newly selected unsupported mode. No map generation or launch occurs.
        sessionProperty.SetValue(null,null);Deck.Phone=false;
        var refusalSettings=new MenuSettings {RoomKey="MP1 SANCTORUS",RiftlineM7Available=true};
        OfflineWorkspace? refused=null;PrimeShell? refusalShell=null;
        refusalShell=new PrimeShell(route=>route==PrimeRoute.Offline
            ?refused=new OfflineWorkspace(refusalSettings,new[]{"MP1 SANCTORUS"},refusalShell!.Overlays)
            :new TextBlock(),()=>{});
        using(refusalShell)
        {
            refusalShell.Start();refusalShell.Router.Navigate(PrimeRoute.Offline);
            int launches=0;refused!.Launched+=(_,_)=>launches++;
            ((ChoiceRow)Field(refused,"_mode")).Index=MatchTypeCatalog.BaseModeIndex(GameMode.Survival);
            string before=Preferences();
            var prefs=(LauncherPrefs.LastKind,LauncherPrefs.LastHunter,LauncherPrefs.LastColor,LauncherPrefs.Bots,LauncherPrefs.BotLevel);
            Click((PrimeButton)Field(refused,"_start"));
            Check(launches==0&&Preferences()==before
                &&prefs==(LauncherPrefs.LastKind,LauncherPrefs.LastHunter,LauncherPrefs.LastColor,LauncherPrefs.Bots,LauncherPrefs.BotLevel),
                "actual offline start callback refuses saved M7 plus Survival before launch or preference writes");
            var reason=refusalShell.Overlays.GetLogicalDescendants().OfType<TextBlock>()
                .Single(t=>t.Text?.Contains("Riftline M7 requires Battle (free-for-all).")==true);
            Check(refusalShell.Overlays.IsOpen,"actual refused start keeps its specific Battle requirement visible in modal");
            string path=Path.Combine(output,"offline-start-refused-desktop.png");
            Check(UiCapture.Capture(refusalShell,path,new Size(940,528)),"actual unsupported-mode refusal modal captured");
            evidence.Add(new {name="offline-start-refused-desktop",path,sha256=Hash(path),reason=reason.Text});
        }
    });
    Check(Hash(product)==productHash,"capture does not change accepted product binary");
    return 0;
}
finally
{
    File.WriteAllText(Path.Combine(output,"loadout-ui-evidence.json"),JsonSerializer.Serialize(new {
        checks,product,productHash,productMvid=typeof(Scene).Assembly.ManifestModule.ModuleVersionId,evidence,
        scope="Actual production Avalonia Rules controls, synthetic immutable settings/session facts, synthetic open/start callbacks, real UiCapture Skia PNG plus bounds JSON at940x528 and360x800. Refusal checks isolated preference bytes/in-memory values and no launch event. No network, native RmlUi rendering, physical input or Android device qualification. Target geometry is recorded for clipping inspection."
    },new JsonSerializerOptions {WriteIndented=true,NumberHandling=System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals}));
    sessionProperty.SetValue(null,oldSession);slotProperty.SetValue(null,oldSlot);Deck.Phone=oldPhone;Deck.Still=oldStill;
    try{scene.DoCleanup();}finally{scene.UnloadGl();}
}
