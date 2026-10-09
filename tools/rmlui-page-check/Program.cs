using System.Xml.Linq;
using MphRead.Mods.Launcher.RmlUi.Host;

int checks = 0;
void Check(bool result, string message) { checks++; if (!result) throw new Exception(message); }
RmlUiIntent Action(string text, RmlUiDocumentToken document, ulong sequence)
{
    if (!RmlUiIntentRegistry.TryParseLegacy(text, document, sequence, out var intent)) throw new Exception("Unknown action " + text);
    return intent;
}
var native = new FakeBridge();
using var host = new RmlUiHost(native);
Check(host.Initialize(1280,720,1,"."), "Host initializes");
host.FocusDocument(host.HomeDocument,"baseline_action");
using var manager = new RmlUiPageManager(host);
var first = manager.OpenPage(new("one","pages/one.rml","first_action"));
Check(!native.Visible[host.HomeDocument.DocumentId] && manager.Page==first, "The baseline is hidden behind the composed page");
Check(native.Focus==(first.DocumentId,"first_action"), "Initial page focus");
Check(manager.OpenPage(new("one","pages/one.rml","first_action"))==first, "Same page retains its draft and token");
var firstLifetime=manager.Lifetime(first);
var modal1=manager.OpenModal(new("modal-one","pages/modal-one.rml","confirm"));
Check(manager.Top==modal1 && manager.ModalCount==1, "Modal owns foreground");
Check(!manager.Accept(Action("quit",first,1)), "Page events cannot escape a modal");
Check(manager.Accept(Action("quit",modal1,2)), "Foreground modal accepts an event");
Check(!manager.Accept(Action("quit",modal1,2)) && !manager.Accept(Action("quit",modal1,1)), "Duplicate/reordered events are rejected");
var modal2=manager.OpenModal(new("modal-two","pages/modal-two.rml","cancel"));
var modalLifetime=manager.Lifetime(modal2);
Check(manager.CloseModal() && modalLifetime.IsCancellationRequested, "Closing modal cancels its work");
Check(native.Focus==(modal1.DocumentId,"confirm"), "Nested modal restores its parent focus");
Check(manager.Back() && native.Focus==(first.DocumentId,"first_action"), "Back restores page focus");
manager.SetVisible(false);
Check(!manager.Accept(Action("quit",first,3)), "Hidden pages cannot dispatch");
manager.SetVisible(true);
var second=manager.OpenPage(new("two","pages/two.rml","second_action"));
Check(firstLifetime.IsCancellationRequested && !host.IsAlive(first), "Page replacement retires the previous lifetime");
Check(!manager.Accept(Action("quit",first,4)), "Retired document event rejected");
Check(manager.Present(second,1,new Dictionary<string,RmlUiBindingValue>{{"label",RmlUiBindingValue.FromText("current")}}), "Current snapshot applies");
Check(!manager.Present(second,1,new Dictionary<string,RmlUiBindingValue>{{"label",RmlUiBindingValue.FromText("duplicate")}}), "Duplicate snapshot rejected");
Check(!manager.Present(first,2,new Dictionary<string,RmlUiBindingValue>{{"label",RmlUiBindingValue.FromText("stale")}}), "Retired snapshot rejected");
host.EnqueueSnapshot(new(first,100,new Dictionary<string,RmlUiBindingValue>{{"label",RmlUiBindingValue.FromText("worker")}}));
host.Update();
Check(native.Texts[(second.DocumentId,"label")]=="current", "Queued stale worker snapshot cannot touch replacement");
manager.OpenModal(new("reentrant","pages/reentrant.rml"));
manager.Lifetime(manager.Top).Register(()=>manager.ClosePage());
Check(manager.CloseModal() && host.LastCleanupError!=null && manager.Page==second, "Retirement callbacks cannot reenter document composition");
Check(manager.ClosePage() && native.Focus==(host.HomeDocument.DocumentId,"baseline_action"), "Closing page restores original baseline focus");
native.RejectOpen=true;
try {manager.OpenPage(new("broken","pages/broken.rml"));throw new Exception("Open failure was accepted");} catch(InvalidOperationException) { checks++; }
native.RejectOpen=false;
using var launcher=new RmlUiLauncherPages(host,manager);
launcher.SetText("player_name","Real Player");
var home=launcher.ShowBaseline();
Check(manager.PageKey=="home" && host.IsAlive(home), "Composed Home opens");
Check(native.Texts[(home.DocumentId,"profile")]=="Real Player", "Shared shell reflects publisher data");

launcher.SetText("home_feature_title","Community <News> & Updates");
launcher.SetBool("home_feature_available",true);
launcher.SetBool("home_social_rail_visible",true);
launcher.SetBool("home_social_alert_visible",true);
launcher.SetText("home_social_alert_text","2 INVITES // 1 REQUEST");
launcher.SetBool("home_social_empty_visible",true);
launcher.SetText("home_social_empty_text","NO FRIENDS ONLINE");
launcher.SetBool("home_friend0_visible",true);
launcher.SetBool("home_friend0_joinable",true);
launcher.SetText("home_friend0_name","Hunter Ω");
launcher.Flush();
Check(native.Texts[(home.DocumentId,"home_feature_title")]=="Community <News> & Updates", "Home dispatch uses safe text projection");
Check(native.Bools[(home.DocumentId,"visible:home_social_rail")] && native.Bools[(home.DocumentId,"visible:home_social_alert")], "Home social rail and inbox indicator project visible state");
Check(native.Bools[(home.DocumentId,"visible:home_friend0")] && native.Bools[(home.DocumentId,"class:home_friend0:joinable")], "Joinable friend preview state is projected without bypassing Social authority");
Check(native.Texts[(home.DocumentId,"home_friend0_name")]=="Hunter Ω", "Unicode friend identity is preserved");
Check(native.Bools[(home.DocumentId,"visible:home_social_empty")]
   && native.Texts[(home.DocumentId,"home_social_empty")]=="NO FRIENDS ONLINE",
   "An empty Home social account retains a useful, visible status card");
launcher.SetBool("home_social_rail_visible",false);
launcher.SetBool("home_friend0_visible",false);
launcher.Flush();
Check(!native.Bools[(home.DocumentId,"visible:home_social_rail")] && !native.Bools[(home.DocumentId,"visible:home_friend0")], "Empty or disconnected Home social content collapses");

// Notification inbox: real source projections, bounded slots, suppression and typed routes.
launcher.ObserveNewsNotice("PATCH NOTES", "A bundled update dispatch.");
launcher.ObserveSocialNotices(2,1,true,true);
launcher.ObserveRelease("v99.0.0");
launcher.Flush();
Check(native.Bools[(home.DocumentId,"class:build_button:available")]
    && native.Texts[(home.DocumentId,"notice_0_title")]=="UPDATE AVAILABLE"
    && native.Bools[(home.DocumentId,"visible:notice_badge")], "Published release and social activity display a live unread badge");
Check(!RmlUiIntentRegistry.IsValid(RmlUiIntentKind.NoticeAction,11)
    && RmlUiIntentRegistry.TryParseLegacy("notice:versions",home,1,out var typedVersion)
    && typedVersion.Kind==RmlUiIntentKind.NoticeAction && typedVersion.Argument==2,
    "New notification actions are ABI-validated and bounded");
Check(launcher.HandleIntent(Action("notice:toggle",home,1),out var openedNotice) && openedNotice.Kind==0
    && launcher.NoticeOpen, "Bell opens the notice drawer locally");
launcher.Flush();
Check(native.Bools[(home.DocumentId,"visible:notice_panel")]
    && !native.Bools[(home.DocumentId,"visible:notice_badge")], "Opening the drawer marks active notices seen");
Check(launcher.HandleIntent(Action("notice:open:0",home,2),out var reviewUpdate)
    && reviewUpdate.Kind==RmlUiIntentKind.NoticeAction && reviewUpdate.Argument==2
    && !launcher.NoticeOpen, "Update notice forwards to guarded Version Manager owner");
launcher.ObserveRelease(null);
launcher.Flush();
Check(!native.Bools[(home.DocumentId,"class:build_button:available")], "Update chip clears after release is no longer available");
launcher.HandleIntent(Action("notice:toggle",home,3),out _);
launcher.Flush();
Check(native.Texts[(home.DocumentId,"notice_0_title")]=="PARTY TRAVEL", "Real social travel appears ahead of older notices");
Check(launcher.HandleIntent(Action("notice:dismiss:0",home,4),out var dismissed) && dismissed.Kind==0, "Dismiss consumes only its own notice");
launcher.Flush();
Check(native.Texts[(home.DocumentId,"notice_0_title")]=="FRIEND REQUESTS", "Dismissing a notice reveals the next entry");
Check(launcher.Back(out _) && !launcher.NoticeOpen, "Back dismisses the overlay before changing routes");
launcher.ObserveSocialNotices(0,0,false,true);
launcher.ObserveNewsNotice(null,null);
launcher.ReportSystemNotice("Installer recovery requires attention.");
launcher.Flush();
Check(native.Texts[(home.DocumentId,"notice_0_title")]=="ACTION REQUIRED", "Authoritative error is retained as a system notice");
launcher.HandleIntent(Action("notice:toggle",home,5),out _);
launcher.HandleIntent(Action("notice:dismiss:0",home,6),out _);
launcher.Flush();
Check(native.Bools[(home.DocumentId,"visible:notice_empty")], "Dismissing the last alert restores an empty inbox");
Check(launcher.Back(out _) && !launcher.NoticeOpen, "Notice Back restores launcher navigation ownership");

Check(launcher.HandleIntent(Action("home:drawer-open",home,10),out var consumed) && consumed.Kind==0, "Drawer opens locally");
launcher.AfterUpdate();
Check(native.Focus.Element=="drawer_browser", "Drawer focus follows selected activity");
Check(native.Bools[(home.DocumentId,"class:home_feature_rail:hidden")], "Featured rail hides while the activity drawer is open");
Check(launcher.HandleIntent(Action("stage:browser",home,11),out var selected) && selected.Kind==RmlUiIntentKind.StageSelect, "Activity select preserves typed stage event");
Check(native.Texts[(home.DocumentId,"text_activity_title_1")]=="LOBBY BROWSER", "Activity selection preserves authored labels");
launcher.SetField("play_player_name","Real Player");
launcher.SetField("play_join_address","example.test:12345");
Check(launcher.HandleIntent(Action("home:deploy",home,12),out var deploy), "Deploy recognized");
var play=launcher.Document;
Check(!host.IsAlive(home) && deploy.Document==play && deploy.Kind==RmlUiIntentKind.PlayBrowse, "Deploy command is moved to the new live Play document");
Check(launcher.ReadField("play_join_address")=="example.test:12345", "Explicit draft seeded when Play opens");
launcher.HandleIntent(Action("play:create-open",play,13),out var create);
launcher.SetField("play_create_name","My actual lobby");
host.SetField(play,"play_create_name","Unsaved draft");
launcher.SetText("play_status","REAL DIRECTORY STATUS");
launcher.SetText("play_create_mode","BOUNTY");
launcher.Flush();
Check(launcher.ReadField("play_create_name")=="Unsaved draft", "Periodic presentation preserves edited create fields");
launcher.HandleIntent(Action("play:cancel",play,14),out var browse);
Check(browse.Kind==RmlUiIntentKind.PlayBrowse && browse.Document==play && launcher.Page==RmlUiMenuPage.Play, "Create Back returns to browser while preserving page lifetime");
launcher.HandleIntent(Action("play:cancel",play,15),out var back);
Check(back.Kind==RmlUiIntentKind.PlayCancel && back.Document==launcher.Document && launcher.Page==RmlUiMenuPage.Home, "Browser Back returns Home with live dispatch token");
launcher.ShowBaseline(RmlUiMenuPage.Play);
Check(launcher.ReadField("play_create_name")=="Unsaved draft", "Draft preserved through document recreation");
launcher.SetBool("lobby_mode",true);
launcher.SetBool("lobby_require_ready",false);
launcher.SetBool("lobby_can_start",false);
launcher.SetText("lobby_brief_title","READY CHECK IN PROGRESS");
launcher.SetText("lobby_brief_count","4 / 8 HUNTERS");
launcher.SetText("lobby_brief_detail","2 / 4 COMBATANTS READY");
launcher.SetText("lobby_field_caption","ARENA VERIFIED // READY");
launcher.SetBool("slot0_occupied",true);
launcher.SetBool("slot0_ready",true);
launcher.SetBool("slot0_local",true);
launcher.Flush();
Check(launcher.Page==RmlUiMenuPage.Lobby && manager.PageKey=="lobby", "Lobby controller switches composed document");
Check(native.Texts[(launcher.Document.DocumentId,"lobby_brief_title")]=="READY CHECK IN PROGRESS"
    && native.Texts[(launcher.Document.DocumentId,"lobby_field_caption")]=="ARENA VERIFIED // READY",
    "Live lobby operational readouts project to authored controls");
Check(native.Bools[(launcher.Document.DocumentId,"class:lobby_brief_slot0:ready")]
    && native.Bools[(launcher.Document.DocumentId,"class:lobby_brief_slot0:local")]
    && native.Bools[(launcher.Document.DocumentId,"class:lobby_slot0:ready")],
    "Lobby readiness and local identity color the formation and nameplate");
Check(native.Bools[(launcher.Document.DocumentId,"disabled:lobby_start")], "Start guard rendered from authoritative capability");
launcher.SetBool("lobby_rules_open",true);
launcher.SetBool("rules_owner",true);
launcher.SetField("rules_time","7:00");
launcher.SetField("rules_goal","7");
launcher.Flush();
var rules=manager.Top;
launcher.SetBool("lobby_mode",true);launcher.Flush();
Check(manager.Top==rules,"Repeated lobby snapshots preserve the open rules draft");
Check(manager.ModalCount==1 && rules!=launcher.Document, "Rules is an independent native modal");
host.SetField(rules,"rules_time","13:22");
launcher.SetText("rules_status","UNSAVED RULES");launcher.Flush();
Check(launcher.ReadField("rules_time")=="13:22", "Rules snapshot preserves edited text");
var rulesLifetime=manager.Lifetime(rules);
Check(launcher.Back(out var closeRules) && closeRules.Kind==RmlUiIntentKind.LobbyRulesClose && rulesLifetime.IsCancellationRequested, "Rules Back cancels modal work and informs editor");
launcher.Suspend();
var external=manager.OpenPage(new("external","pages/external.rml"));
launcher.SetText("player_name","Updated real player");launcher.Flush();
Check(manager.Page==external, "Background baseline publishers cannot steal another route");
launcher.PresentChrome(external);
Check(native.Texts[(external.DocumentId,"profile")]=="Updated real player", "Shared chrome is hydrated without presenter revision interference");
launcher.Resume();
Check(manager.PageKey=="lobby" && !host.IsAlive(external), "Resume explicitly restores baseline route");
Check(host.Reinitialize(), "Host generation renews");
launcher.ShowBaseline();
Check(manager.Page.Generation==host.HomeDocument.Generation && manager.ModalCount==0, "Reload invalidates retired docs and recreates current route");

var root=new DirectoryInfo(Directory.GetCurrentDirectory());
while(root!=null && !Directory.Exists(Path.Combine(root.FullName,"src/MphRead/Mods/Launcher/RmlUi/Assets")))root=root.Parent;
if(root==null)throw new Exception("Run in the repository");
string assets=Path.Combine(root.FullName,"src/MphRead/Mods/Launcher/RmlUi");
var shell=XDocument.Load(Path.Combine(assets,"Components/app-shell.rml"));
var original=XDocument.Load(Path.Combine(assets,"Assets/prime_home.rml"));
var model=new Dictionary<string,RmlUiBindingValue>{{"activity_index",RmlUiBindingValue.FromText("1")},{"activity_selector_open",RmlUiBindingValue.FromBoolean(true)},{"player_name",RmlUiBindingValue.FromText("<actual & safe>")}};
var authoredActionIds=new HashSet<string>();
foreach(var page in Enum.GetValues<RmlUiMenuPage>())
{
    var spec=RmlUiMenuPages.Spec(page);
    var doc=XDocument.Load(Path.Combine(assets,"Pages",spec.Path[6..]));
    var nodes=doc.Descendants().Concat(page==RmlUiMenuPage.Rules?Array.Empty<XElement>():shell.Descendants()).ToArray();
    var ids=nodes.Where(n=>n.Attribute("id")!=null).Select(n=>(string)n.Attribute("id")!).ToArray();
    Check(ids.Distinct().Count()==ids.Length,$"{page} has unique composed IDs");
    var projected=RmlUiMenuPageBindings.Project(page,model);
    foreach(var node in nodes)
    {
        var id=(string?)node.Attribute("id");
        foreach(var attr in node.Attributes().Where(a=>a.Name.LocalName.StartsWith("data-prime-")))
        {
            string target=attr.Name.LocalName switch
            {
                "data-prime-text"=>id!,"data-prime-visible"=>"visible:"+id,"data-prime-disabled"=>"disabled:"+id,
                var key when key.StartsWith("data-prime-class-")=>"class:"+id+":"+key[17..],
                _=>throw new Exception("Unknown authored binding")
            };
            Check(projected.ContainsKey(target),$"{page} binding {target} has a production projector");
        }
        if(node.Attribute("data-action") is { } action)
        {
            Check(RmlUiIntentRegistry.TryParseLegacy(action.Value,new(1,1),1,out _),$"{page} action {action.Value} is registered");
            if(id!=null)authoredActionIds.Add(id);
        }
        Check(!node.Attributes().Any(a=>a.Name.LocalName is "data-if" or "data-model" || a.Name.LocalName.StartsWith("data-event-")),"Independent page does not depend on legacy model callbacks");
    }
    Check(projected.Keys.All(k=>ids.Contains(k.StartsWith("class:")?k.Split(':')[1]:k.Contains(':')?k[(k.IndexOf(':')+1)..]:k)),$"{page} projector cannot target missing IDs");
}
foreach(var node in original.Descendants().Where(n=>n.Attribute("data-event-click")!=null && n.Attribute("id")!=null))
    if (!new[]{"drawer_quick","play_next_map","play_next_mode","lobby_rules"}.Contains((string)node.Attribute("id")!))
    Check(authoredActionIds.Contains((string)node.Attribute("id")!),"Existing action ID preserved: "+node.Attribute("id")!.Value);
Console.WriteLine($"RmlUi page checks passed: {checks} assertions; independent composition, modal/lifetime/revision gates, real binding/action parity, typed baseline transitions, and editable drafts.");

if (args.Length != 0)
{
    if (args.Length != 2 || args[0] != "--native") throw new ArgumentException("Usage: rmlui-page-check [--native <bridge>]");
    NativePageCheck.Run(args[1], Path.Combine(AppContext.BaseDirectory,"rmlui"));
}
