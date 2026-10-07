using System.Collections.Immutable;
using System.Runtime.InteropServices;
using MphRead;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Presenters;
using MphRead.Mods.Network;
using MphRead.Mods.Cosmetics;

if (args.Length is not (1 or 3) || (args.Length == 3 && args[1] != "--assets"))
    throw new ArgumentException("Usage: rmlui-lobby-page-check <bridge> [--assets <directory>]");
string assets = args.Length == 3 ? Path.GetFullPath(args[2]) : Path.Combine(AppContext.BaseDirectory, "rmlui");
string root = Path.Combine(Path.GetTempPath(), "prime-rmlui-admin-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
foreach(string file in Directory.EnumerateFiles(assets,"*",SearchOption.AllDirectories)) {
    string target=Path.Combine(root,Path.GetRelativePath(assets,file));
    Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(file,target);
}
nint module=NativeLibrary.Load(Path.GetFullPath(args[0]));
NativeLibrary.SetDllImportResolver(typeof(RmlUiHost).Assembly,(name,_,_)=>name=="ProjectPrime.RmlUi.Native"?module:0);
int checks=0;
void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);checks++;}
try {
 using var host=new RmlUiHost();
 Check(host.Initialize(1280,720,1,root,RmlUiRenderBackend.DrawList),"Real native host initializes");
 var backend=new FakeLobby();
 backend.State=backend.State with { Players=backend.State.Players.Add(new(2,"Target",Hunter.Kanden,1,-1,false,12,false,false,1,0,MapAvailabilityState.Ready,7)).Add(new(3,"Bot",Hunter.Noxus,2,-1,false,0,false,true,2,10,MapAvailabilityState.Ready,8)) };
 using var controller=new LobbySessionController(backend);
 using var admin=new RmlLobbyAdminPresenter(host,controller);admin.Open();
 Check(admin.Active,"Native admin page opens");
 Check(host.TryGetElementBounds(admin.Document,"admin_slot0",out _,out _,out float rosterWidth,out float rosterHeight)&&rosterWidth>200&&rosterHeight>=40,"Admin roster occupies its responsive column");
 bool Activate(RmlUiDocumentToken document,string id,Func<RmlUiIntent,bool> handle) {
  Check(host.FocusDocument(document,id),"Real DOM focuses "+id);host.Input.Key(2,true);host.Input.Key(2,false);host.Update();
  Check(host.TryTakeIntent(out var action)&&action.Document==document,"Real typed action emitted "+id);
  return handle(action);
 }
 Check(Activate(admin.Document,"admin_slot2",admin.Handle),"Admin selects server slot rather than row index");
 Check(Activate(admin.Document,"admin_kick",admin.Handle)&&backend.Commands.Count==0,"Destructive action waits for confirmation");
 Check(!host.FocusDocument(admin.Document,"admin_slot0"),"Confirmation disables underlying controls");
 Check(admin.Back()&&admin.Active&&backend.Commands.Count==0&&host.FocusDocument(admin.Document,"admin_slot0"),"Back cancels confirmation before closing the admin document");
 Check(Activate(admin.Document,"admin_kick",admin.Handle),"Confirmation can reopen after Back cancellation");
 backend.State=backend.State with { RosterRevision=2,Players=backend.State.Players.SetItem(1,backend.State.Players[1] with {Name="Replacement",Generation=9}) };
 Check(Activate(admin.Document,"admin_confirm",admin.Handle)&&backend.Commands.Count==0,"Retained stale target confirmation cannot kick slot replacement");
 Check(Activate(admin.Document,"admin_slot2",admin.Handle),"Select replacement explicitly");
 Check(Activate(admin.Document,"admin_kick",admin.Handle),"Current player can request confirmation");
 Check(Activate(admin.Document,"admin_confirm",admin.Handle)&&backend.Commands.Count==1&&backend.Commands[0].TargetSlot==2&&backend.Commands[0].TargetGeneration==9&&backend.Commands[0].ExpectedRosterRevision==2,"Confirmed command retains current authority witness");
 backend.State=backend.State with {OwnerSlot=2,CommandPending=false};admin.Update();
 Check(!host.FocusDocument(admin.Document,"admin_kick")&&!host.FocusDocument(admin.Document,"admin_bot_add")&&!host.FocusDocument(admin.Document,"admin_close_lobby"),"Native DOM disables authority after ownership loss");
 host.SetField(admin.Document,"admin_chat_input","local draft");admin.Update();Check(host.ReadField(admin.Document,"admin_chat_input")=="local draft","Immutable snapshots preserve chat draft");
 admin.Dispose();Check(!admin.Active,"Admin document lifetime closes");
 var hunters=new FakeHunters();
 using var selection=new RmlHunterSelectionPresenter(host,backend:hunters);selection.Open();
 Check(selection.Active,"Native Hunter page opens");
 Check(host.TryGetElementBounds(selection.Document,"hunter_name",out _,out _,out float headerWidth,out float headerHeight)&&headerWidth>500&&headerHeight<100,"Hunter heading has a full-width block instead of vertical letter wrapping");
 Check(host.TryGetElementBounds(selection.Document,"hunter_preview_space",out float vx,out float vy,out float vw,out float vh)&&vw>0&&vh>0,"Native framebuffer preview bounds are available");
 Check(host.TryGetElementBounds(selection.Document,"hunter_apply",out _,out _,out float applyWidth,out float applyHeight)&&applyWidth>=200&&applyHeight<80,"Hunter apply action has readable full-width button geometry");
 Check(host.TryGetElementBounds(selection.Document,"hunter_cancel",out _,out _,out float cancelWidth,out float cancelHeight)&&cancelWidth>=200&&cancelHeight<80,"Hunter cancel action has readable full-width button geometry");
 int previews=0;selection.PreviewChanged+=_=>previews++;
 Check(Activate(selection.Document,"hunter_choice6",selection.Handle)&&selection.Snapshot.Hunter==Hunter.Weavel,"All real Hunter choices include Weavel");
 Check(Activate(selection.Document,"hunter_suit3",selection.Handle)&&selection.Snapshot.Color==3,"Full suit selection uses explicit draft");
 Check(Activate(selection.Document,"hunter_preview_turret",selection.Handle)&&selection.Snapshot.PreviewMode==SkinContext.Halfturret,"Weavel Halfturret context available");
 Check(Activate(selection.Document,"hunter_rotate_right",selection.Handle)&&selection.Snapshot.Yaw==15,"Native rotate command updates preview snapshot");
 Check(Activate(selection.Document,"hunter_zoom_in",selection.Handle)&&selection.Snapshot.Zoom>1,"Native zoom command updates preview snapshot");
 Check(Activate(selection.Document,"hunter_armor",selection.Handle)&&selection.Snapshot.Draft!=selection.Snapshot.Equipped,"Complete cosmetic catalog changes a local draft");
 Check(Activate(selection.Document,"hunter_cosmetics_save",selection.Handle)&&selection.Snapshot.Saving,"Equip invokes real asynchronous Core service seam");
 Check(!host.FocusDocument(selection.Document,"hunter_cosmetics_save"),"Pending cosmetic save disables repeated submit");
 hunters.Complete(true);SpinWait.SpinUntil(()=>{selection.Update();return !selection.Snapshot.Saving;},1000);
 Check(!selection.Snapshot.Saving&&selection.Snapshot.Draft==selection.Snapshot.Equipped,"Acknowledged equip state refreshes native controls");
 Check(Activate(selection.Document,"hunter_apply",selection.Handle)&&hunters.SavedHunter==Hunter.Weavel&&hunters.SavedColor==3,"Explicit apply persists chosen identity");
 Check(previews>0,"Renderer receives immutable preview snapshots");
 foreach(float density in new[]{1f,1.25f,1.5f,2f}) { host.Resize(1280,720,density);selection.Update();host.Update();host.Render(1280,720);Check(host.FocusDocument(selection.Document,"hunter_cancel"),"Scrollable responsive native Hunter controls remain focusable at density "+density); }
 selection.Dispose();Check(!host.TryGetElementBounds(selection.Document,"hunter_preview_space",out _,out _,out _,out _),"Retired preview bounds are rejected");Check(!selection.Active,"Hunter documents close and retire preview control");
 Console.WriteLine($"Native admin/Hunter presenter integration passed: {checks} assertions; actual DOM, target generation confirmation, authority loss, drafts, full identity/cosmetics/preview controls, async state, density and document lifetime.");
}finally{NativeLibrary.Free(module);Directory.Delete(root,true);}

sealed class FakeLobby : ILobbySessionBackend
{
    public LobbySnapshot State = new()
    {
        Active = true, Persistent = true, Phase = SessionPhase.Lobby, SessionRevision = 1, RosterRevision = 1,
        MaxPlayers = 8, OwnerSlot = 0, LocalSlot = 0, LocalHunter = Hunter.Samus,
        PlayerName = "Owner", RequiredMapReady = true,
        Match = new MatchDefinition { RoomKey = "test_arena", Mode = GameMode.Battle, PointGoal = 7 },
        Players = ImmutableArray.Create(new LobbyPlayerSnapshot(0, "Owner", Hunter.Samus, 0, -1,
            false, 0, false, false, 1, 0, MapAvailabilityState.Ready, 1))
    };
    public int Pumps, Stops, AcceptedRules, Identifies, SpectatorRequests;
    public bool LoadMatch = false;
    public bool RejectStart = false;
    public Action? OnPump = null;
    public List<LobbyIntent> Commands { get; } = new();
    public double Clock { get; set; }
    public bool ShouldLoadMatch => LoadMatch;
    public bool Refused { get; set; }
    public bool TimedOut { get; set; }
    public string RefusedMessage => "Server refused the connection.";
    public LobbySnapshot Capture() => State with
    {
        Players = State.Players.ToArray().ToImmutableArray(),
        Chat = State.Chat.ToArray().ToImmutableArray()
    };
    public void Pump() { Pumps++; OnPump?.Invoke(); }
    public void Stop() { Stops++; State = State with { Active = false }; }
    public bool SendCommand(LobbyIntent intent)
    {
        Commands.Add(intent);
        if (RejectStart && intent.Kind == LobbyIntentKind.StartMatch) return false;
        State = State with { CommandPending = true, Message = "Waiting for server..." };
        return true;
    }
    public void Identify(Hunter hunter, byte color) { Identifies++; State = State with { LocalHunter = hunter, LocalColor = color }; }
    public void SetSpectator(bool spectator) { SpectatorRequests++; State = State with { PreferSpectator = spectator }; }
    public void SendChat(string text) => State = State with { Chat = State.Chat.Add(text) };
    public void RetryMap() { }
    public LobbyActionResult ValidateRules(MatchDefinition match) => LobbyActionResult.Ok;
    public void RulesAccepted(MatchDefinition match) => AcceptedRules++;
}

sealed class FakeHunters : IHunterSelectionBackend
{
    private readonly Dictionary<Hunter, CosmeticLoadout> _equipped = new();
    private readonly HashSet<Hunter> _pending = new();
    private TaskCompletionSource<HunterEquipResult>? _work;
    private Hunter _savingHunter;
    public Hunter PreferredHunter { get; set; } = Hunter.Samus;
    public byte PreferredColor => 0;
    public Hunter SavedHunter;
    public byte SavedColor;
    public string Blocked = "";
    public void SaveIdentity(Hunter hunter, byte color) { SavedHunter = hunter; SavedColor = color; }
    public HunterSelectionProfile Capture(Hunter hunter) => new(
        _equipped.GetValueOrDefault(hunter, CosmeticLoadout.Default), _pending.Contains(hunter),
        ImmutableArray.Create(SkinContext.Biped, SkinContext.ViewModel), true, "Installed pack enabled.", "Effects visible.");
    public bool IsUnlocked(Hunter hunter, CosmeticDefinition definition, out string reason)
    { reason = definition.Key == Blocked ? "This cosmetic is locked by the test authority." : ""; return reason.Length == 0; }
    public Task<HunterEquipResult> EquipAsync(Hunter hunter, CosmeticLoadout loadout, CancellationToken cancellationToken)
    {
        _equipped[hunter] = loadout; _pending.Add(hunter); _savingHunter = hunter;
        _work = new(); return _work.Task;
    }
    public void Complete(bool synced)
    {
        if (synced) _pending.Remove(_savingHunter);
        _work!.SetResult(new(synced, synced ? "EQUIPPED / SYNCED" : "LOCAL / NOT SYNCED — server unavailable"));
    }
}
