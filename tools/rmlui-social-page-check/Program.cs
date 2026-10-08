using System.Collections.Immutable;
using System.Runtime.InteropServices;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Social;

int checks = 0;
void Check(bool value, string message) { checks++; if (!value) throw new InvalidOperationException(message); }
var backend = new FakeSocialBackend();
using (var controller = new SocialController(backend))
{
    Check(controller.Snapshot.VisibleRows.Length == 8 && controller.Snapshot.PageCount == 2, "Social page bound lost copied rows");
    controller.SelectRow(0);
    Check(controller.Snapshot.AvailableCommands.Contains(SocialCommand.RemoveFriend), "Selected player permissions were not retained");
    controller.Dispatch(controller.Intent(SocialCommand.RemoveFriend));
    Check(controller.Snapshot.PendingConfirmation != null && backend.Executed.Count == 0, "Destructive action bypassed confirmation");
    backend.Change(backend.Data with { Rows = backend.Data.Rows.RemoveAt(0) });
    Check(!controller.Confirm().Success && backend.Executed.Count == 0, "Confirmation accepted a retired player witness");
    controller.Refresh(); controller.Pump();
    controller.SetTab(SocialTab.Players); controller.SelectRow(0);
    Check(controller.Dispatch(controller.Intent(SocialCommand.JoinFriend)).Success, "Verified join could not be dispatched");
    controller.Pump();
    Check(controller.TryTakeJoin(out var request) && request!.Host == "127.0.0.1" && request.RoomKey == "123456", "Owner did not receive transferable join request");
    using (request) { var admission = request!.TakeAdmission(); Check(admission != null, "Admission was discarded before handoff"); admission!.Dispose(); }
}
if (args.Length == 2 && args[0] == "--native")
{
    nint module = NativeLibrary.Load(Path.GetFullPath(args[1]));
    NativeLibrary.SetDllImportResolver(typeof(RmlUiHost).Assembly,(name,_,_)=>name=="ProjectPrime.RmlUi.Native"?module:0);
    try
    {
        foreach (var size in new[] {(W:1280,H:720,D:1f),(W:2560,H:1440,D:2f),(W:2560,H:1440,D:1f)})
        {
            using var host = new RmlUiHost();
            Check(host.Initialize(size.W,size.H,size.D,Path.Combine(AppContext.BaseDirectory,"rmlui"),RmlUiRenderBackend.DrawList), "Native Social host initialization failed");
            using var pages = new RmlUiPageManager(host);
            backend = new();
            using var presenter = new SocialPagePresenter(host,pages,new(backend));
            presenter.Open(); host.Update(); host.Render(size.W,size.H);
            Check(pages.Page == presenter.Document && pages.PageKey == "social" && !host.IsVisible(host.HomeDocument), "Social presentation created a second shell owner");
            RmlUiIntent Click(string id, bool pointer = false)
            {
                var document = pages.Top;
                Check(host.FocusDocument(document,id), "Native Social focus failed: " + id);
                host.Update(); host.Render(size.W,size.H);
                Check(host.TryGetElementBounds(document,id,out float x,out float y,out float w,out float h) && w>0 && h>0,
                    "Social action has no usable real bounds: " + id);
                if(pointer)
                {
                    host.Input.PointerMoved(x+w/2,y+h/2);
                    host.Input.PointerButton(0,x+w/2,y+h/2,true); host.Input.PointerButton(0,x+w/2,y+h/2,false);
                }
                else { host.Input.Key(2,true);host.Input.Key(2,false); }
                host.Update();
                bool received = host.TryTakeIntent(out var intent);
                Check(received && presenter.Handle(intent), $"Native Social intent was not handled: {id}; received={received}; kind={intent.Kind}; hover={host.HoveredElement()}; focus={host.FocusedElement()}; bounds={x},{y},{w},{h}");
                host.Update();host.Render(size.W,size.H);
                return intent;
            }
            Check(host.TryGetElementBounds(presenter.Document,"social_counts",out _,out _,out float contentWidth,out _)
                && contentWidth > size.W * .7f, "Social scrollbar consumed its content viewport width");
            Click("social_friends",true);
            Click("social_row_0");
            var command = Click("social_command_25");
            Check(pages.ModalCount == 1 && presenter.Controller.Snapshot.PendingConfirmation != null && backend.Executed.Count == 0,
                "Native Remove Friend did not acquire confirmation modal ownership");
            Check(presenter.Handle(command) && backend.Executed.Count == 0, "Duplicate background action bypassed modal witness");
            Click("social_confirm_cancel");
            Check(pages.ModalCount == 0 && host.FocusedElement()=="social_command_25", "Social modal dismissal did not restore focus");
            Click("social_command_25");
            backend.Change(backend.Data with { Rows = backend.Data.Rows.RemoveAt(0) });
            Click("social_confirm_apply");
            Check(backend.Executed.Count == 0 && presenter.Controller.Snapshot.CommandError.Contains("changed") && pages.ModalCount == 0,
                "Native confirmation sent an action against changed Social state");
            Click("social_row_0"); Click("social_command_25"); Click("social_confirm_apply"); presenter.Refresh();
            Check(backend.Executed.SequenceEqual(new[] {SocialCommand.RemoveFriend}), "Confirmed native action was not sent exactly once");
            host.SetField(presenter.Document,"social_filter","Player 01");
            host.SetField(presenter.Document,"social_lookup_id","PP-1234-5678-9ABC-DEF0-1234");
            presenter.Refresh();
            Check(host.ReadField(presenter.Document,"social_filter")=="Player 01" && host.ReadField(presenter.Document,"social_lookup_id").Length==27,
                "Social refresh rewrote editable drafts");
            Click("social_search");
            Check(presenter.Controller.Snapshot.Filter=="Player 01" && presenter.Controller.Snapshot.VisibleRows.Length==1,"Native search did not use actual edit field");
            host.SetField(presenter.Document,"social_filter",""); Click("social_search");
            foreach(string tab in new[] {"players","requests","invites","party","recent","blocked","friends"}) Click("social_"+tab);
            Click("social_next_page");
            Check(presenter.Controller.Snapshot.PageIndex == 1 && presenter.Controller.Snapshot.VisibleRows.Length == 1,"Native Social paging used an invalid visible row slice");
            Click("social_previous_page");
            for(int cycle=0;cycle<3;cycle++) Click("social_presence");
            Click("social_activity"); Click("social_invites_privacy");
            host.TryGetElementBounds(presenter.Document,"social_page",out float pageX,out float pageY,out float pageW,out float pageH);
            host.Input.PointerMoved(pageX+pageW*.5f,pageY+pageH*.5f);
            host.Input.PointerWheel(-1000);
            for(int frame=0;frame<15;frame++) { Thread.Sleep(20);host.Update(); }
            host.Render(size.W,size.H);
            Check(host.TryGetElementBounds(presenter.Document,"social_dnd",out _,out float privacyY,out _,out float privacyH)
                && privacyY >= pageY && privacyY+privacyH < pageY+pageH, "Social privacy actions could not scroll into the usable viewport");
            Click("social_dnd",true);
            Check(backend.PrivacyWrites == 6 && presenter.Controller.Snapshot.Privacy==new SocialPrivacy(0,2,2,true),"Native privacy options did not persist each selected value");
            Click("social_lookup");presenter.Refresh();
            Check(presenter.Controller.Snapshot.Tab==SocialTab.Players && presenter.Controller.Snapshot.SelectedRow?.PrimeId=="PP-1234-5678-9ABC-DEF0-1234", "Native Prime ID lookup lost selected copied identity");
            Click("social_command_29"); presenter.Refresh();
            Check(presenter.Controller.TryTakeJoin(out var join) && join!.Host=="127.0.0.1", "Presenter consumed join ownership instead of leaving it to Shell");
            join!.Dispose();
            backend.DeferLoad=true;Click("social_refresh");
            Check(presenter.Controller.Snapshot.CanCancel,"Refresh request did not expose cancellation");
            Click("social_cancel_refresh");backend.PendingLoads[^1].SetResult(backend.Data);presenter.Refresh();
            Check(!presenter.Controller.Snapshot.Busy,"Late cancelled Social load revived an operation");
            Click("social_refresh");var old=presenter.Document;
            pages.OpenPage(new("replacement","pages/home/home.rml","deploy"));
            Check(!host.IsAlive(old) && backend.Disposed && backend.LoadTokens[^1].IsCancellationRequested,"Document retirement kept subscriptions or async request ownership");
            backend.PendingLoads[^1].SetResult(backend.Data);presenter.Refresh();
            Check(pages.PageKey=="replacement" && !presenter.Handle(new(RmlUiIntentKind.SocialAction,7,old,999)),"Retired Social presenter revived its route");
        }
    }
    finally { NativeLibrary.Free(module); }
}
Console.WriteLine($"PASS: {checks} Social controller/native-page checks.");

sealed class FakeSocialBackend : ISocialBackend
{
    public event Action? Changed;
    public SocialData Data = CreateData();
    public List<SocialCommand> Executed { get; } = new();
    public List<TaskCompletionSource<SocialData>> PendingLoads { get; } = new();
    public List<CancellationToken> LoadTokens { get; } = new();
    public bool DeferLoad, Disposed;
    public int PrivacyWrites;
    public SocialData Current() => Data;
    public void Change(SocialData data) { Data=data;Changed?.Invoke(); }
    public Task<SocialData> LoadAsync(CancellationToken token)
    {
        LoadTokens.Add(token);
        if(!DeferLoad) return Task.FromResult(Data);
        var source = new TaskCompletionSource<SocialData>();PendingLoads.Add(source);return source.Task;
    }
    public Task<SocialRow?> LookupAsync(string primeId,CancellationToken token) => Task.FromResult<SocialRow?>(
        new("lookup",SocialTab.Players,primeId,"Lookup Player","ONLINE","IN LOBBY","Verified destination",AllowedCommands:ImmutableArray.Create(SocialCommand.JoinFriend)));
    public Task<SocialBackendResult> ExecuteAsync(SocialIntent intent,SocialRow? target,CancellationToken token)
    {
        Executed.Add(intent.Command);
        var join = intent.Command==SocialCommand.JoinFriend ? new SocialJoinRequest("127.0.0.1",12345,"Fixture Server","123456",new(),42) : null;
        return Task.FromResult(new SocialBackendResult(true,"ACTION COMPLETE",Data,join));
    }
    public void SavePrivacy(SocialPrivacy privacy) { PrivacyWrites++;Data=Data with { Privacy=privacy };Changed?.Invoke(); }
    public void Dispose() => Disposed=true;
    private static SocialData CreateData() => new()
    {
        Connected=true,SelfPrimeId="PP-1111-2222-3333-4444-5555",Status="CONNECTED",OnlineCount=42,FriendsOnline=12,IncomingRequests=2,IncomingInvites=3,
        CanInviteToLobby=true,Rows=Enum.GetValues<SocialTab>().SelectMany(tab=>Enumerable.Range(0,tab==SocialTab.Friends?10:1).Select(index=>
            new SocialRow(tab+"-"+index,tab,"PP-1234-5678-9ABC-DEF0-1234",$"Player {index:00}","FRIEND","IN LOBBY","Copied row detail",AllowedCommands:Enum.GetValues<SocialCommand>().ToImmutableArray()))).ToImmutableArray(),
        Party=new("party-private-key","PP-1111-2222-3333-4444-5555",true,2),
        Travel=new("travel-private-key",2,"party","Party Leader","123456","Fixture Server",42,DateTimeOffset.UtcNow.AddMinutes(5),true,"pending",ImmutableArray.Create(new SocialTravelMember("other","Player 01",false,false,"pending"))),
        Reservation=new("reservation-private-key",42,true,2,"ready",DateTimeOffset.UtcNow.AddMinutes(5),ImmutableArray.Create(new SocialReservedMember("other","Player 01",false,1,"reserved")))
    };
}
