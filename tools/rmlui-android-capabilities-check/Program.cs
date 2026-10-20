using System.Xml.Linq;
using MphRead.Droid;
using MphRead.Mods.Launcher.RmlUi.Host;

int checks = 0;
void Check(bool condition, string message)
{
    checks++;
    if (!condition) throw new InvalidOperationException(message);
}
void Throws(Action action, string message)
{
    try { action(); }
    catch (ArgumentException) { Check(true, message); return; }
    throw new InvalidOperationException(message);
}
bool AuthoredDisplayNone(XElement element)
{
    string? display = null;
    foreach (string declaration in (element.Attribute("style")?.Value ?? "").Split(';'))
    {
        int colon = declaration.IndexOf(':');
        if (colon >= 0 && declaration[..colon].Trim().Equals("display", StringComparison.OrdinalIgnoreCase))
            display = declaration[(colon + 1)..].Trim();
    }
    return String.Equals(display, "none", StringComparison.OrdinalIgnoreCase);
}
bool AuthoredHidden(XElement element, ISet<string> applied) => element.AncestorsAndSelf().Any(parent =>
    parent.Attribute("id") is { } id && applied.Contains(id.Value) || AuthoredDisplayNone(parent));
foreach (string style in new[] { "display:none", " DISPLAY : NONE ; ", "color:red; DiSpLaY: NoNe;" })
{
    var ancestor = new XElement("div", new XAttribute("style", style), new XElement("button"));
    Check(AuthoredHidden(ancestor.Element("button")!, new HashSet<string>()), "static hidden ancestor recognized: " + style);
}
foreach (string style in new[] { "", "display:block", "visibility:hidden", "color:none", "display:none;display:block" })
{
    var visible = new XElement("button", new XAttribute("style", style));
    Check(!AuthoredHidden(visible, new HashSet<string>()), "visible unsupported action still requires capability admission: " + style);
}
string root = Directory.GetCurrentDirectory();
while (!Directory.Exists(Path.Combine(root, "src/MphRead")))
    root = Directory.GetParent(root)?.FullName ?? throw new InvalidOperationException("Run within the repository.");
var local = RmlUiLauncherCapabilities.ConfiguredLocal;
var unrestricted = RmlUiLauncherCapabilities.Unrestricted;
var native = new FakeBridge();
using var host = new RmlUiHost(native);
Check(host.Initialize(1280, 720, 2, root, RmlUiRenderBackend.DrawList), "fake host initializes");

// The default constructor remains the desktop route with no visibility overrides.
using (var desktop = new RmlUiLauncherPages(host))
{
    var document = desktop.ShowBaseline();
    Check(desktop.SelectedActivityIndex == 1, "desktop retains lobby browser default");
    Check(!native.Bools.Any(v => v.Key.Document == document.DocumentId && v.Key.Name == "visible:nav_studio"),
        "desktop capability imposes no hidden controls");
    Check(!native.Strings.ContainsKey((document.DocumentId, "home_drawer_footer")),
        "unrestricted desktop capability does not override authored drawer footer");
    Check(RmlUiIntentRegistry.TryParseLegacy("stage:adventure", document, 1, out var adventure)
        && desktop.HandleIntent(adventure, out var forwarded) && forwarded == adventure,
        "desktop still accepts and forwards Adventure");
    Check(desktop.SelectedActivityIndex == 3, "desktop changes selected activity");
    desktop.ShowBaseline(RmlUiMenuPage.Play);
    Check(desktop.Page == RmlUiMenuPage.Play, "desktop still presents multiplayer");
}
using var pages = new RmlUiLauncherPages(host, capabilities: local);
var home = pages.ShowBaseline();
Check(pages.SelectedActivityIndex == 2 && pages.Page == RmlUiMenuPage.Home, "configured local defaults to Offline on Home");
ulong sequence = 0;
RmlUiIntent Intent(string action, RmlUiDocumentToken document = default)
{
    Check(RmlUiIntentRegistry.TryParseLegacy(action, document == default ? pages.Manager.Top : document,
        ++sequence, out var intent), "actual action decodes: " + action);
    return intent;
}
RmlUiIntent Dispatch(string action, RmlUiDocumentToken document = default)
{
    var intent = Intent(action, document);
    Check(pages.HandleIntent(intent, out var forwarded), "publisher owns: " + action);
    return forwarded;
}
void Hidden(RmlUiDocumentToken document, string id)
    => Check(native.Bools.TryGetValue((document.DocumentId, "visible:" + id), out int value) && value == 0,
        "capability hides actual control: " + id);

foreach (string id in new[] { "nav_community", "nav_studio", "profile", "header_settings", "drawer_browser",
    "drawer_training", "drawer_adventure", "home_feature_rail", "home_social", "home_social_rail",
    "home_drawer_multiplayer", "home_drawer_training", "home_drawer_adventure" }) Hidden(home, id);
Check(native.Strings.GetValueOrDefault((home.DocumentId, "home_drawer_footer"))
    == "CONFIGURED LOCAL PLAY // NO 3D PREVIEW IN THIS ROUTE"
    && XDocument.Load(Path.Combine(root, "src/MphRead/Mods/Launcher/RmlUi/Pages/home/home.rml"))
        .Descendants().Any(element => element.Attribute("id")?.Value == "home_drawer_footer"),
    "configured local footer publishes actual authored binding without offering a preview");
Check(Dispatch("home:drawer-open").Kind == 0, "drawer opens locally");
Check(Dispatch("stage:offline").Argument == 2 && pages.SelectedActivityIndex == 2, "offline stage remains selectable");
Check(Dispatch("home:deploy").Kind == RmlUiIntentKind.Navigate, "local deploy forwards a route");

string[] forbidden = { "route:play", "route:hunter", "route:forge", "route:theatre", "route:settings", "route:news",
    "route:adventure", "route:social", "route:training", "studio:open", "play:quick", "stage:quick", "stage:browser",
    "stage:adventure", "stage:training", "stage-preview:browser", "hunter:preview-mode:0", "hunter:rotate:0",
    "hunter:zoom:0", "hunter:spectator", "offline:arena-search", "offline:open-rules", "offline:apply-rules",
    "offline:rule:1", "offline:damage", "offline:launch-training", "offline:open-training-options", "offline:choice:7",
    "offline:choice:15" };
foreach (string action in forbidden)
{
    Check(Dispatch(action).Kind == 0, "forged unsupported action never forwards: " + action);
    Check(pages.SelectedActivityIndex == 2, "forged action never changes local activity");
    var stale = Intent(action, new(home.Generation + 100, home.DocumentId));
    Check(pages.HandleIntent(stale, out var forwarded) && forwarded.Kind == 0, "unsupported stale token is consumed");
    Check(unrestricted.Allows(stale), "unrestricted capability leaves existing dispatch authority intact");
}
Check(local.Allows(new(RmlUiIntentKind.OfflineChoice, 16, home, ++sequence)),
    "explicit local wall-cling practice selector is available through the configured offline owner");
foreach (var (kind, argument) in new[] { (RmlUiIntentKind.OfflineChoice, -1), (RmlUiIntentKind.OfflineChoice, 17), (RmlUiIntentKind.HunterSelect, 7),
    (RmlUiIntentKind.HunterSuit, 4), (RmlUiIntentKind.OfflineSelectArena, 16), (RmlUiIntentKind.OfflineArenaPage, 0),
    ((RmlUiIntentKind)9999, 0) })
    Check(!local.Allows(new(kind, argument, home, ++sequence)), "invalid local arguments are denied");
Throws(() => pages.ShowBaseline(RmlUiMenuPage.Play), "explicit unsupported publisher route rejects");

// Inspect the real authored actions and their ancestors. An unsupported action must
// be hidden at its real DOM ID or by a hidden parent, not merely denied in code.
string assets = Path.Combine(root, "src/MphRead/Mods/Launcher/RmlUi");
var appliedIds = new HashSet<string>();
foreach (var (path, surface) in new[] { ("pages/home/home.rml", RmlUiLocalDocumentSurface.Home),
    ("pages/offline/setup.rml", RmlUiLocalDocumentSurface.Offline), ("pages/offline/arena.rml", RmlUiLocalDocumentSurface.OfflineArena) })
{
    var probe = host.OpenDocument(path, RmlUiDocumentLayer.Page);
    local.Apply(host, probe, surface);
    appliedIds.UnionWith(native.Bools.Where(pair => pair.Key.Document == probe.DocumentId && pair.Value == 0)
        .Select(pair => pair.Key.Name["visible:".Length..]));
    host.CloseDocument(probe);
}
Check(RmlUiIntentRegistry.TryParseLegacy("studio:open", home, 1, out var visibleStudio)
    && !local.Allows(visibleStudio)
    && !AuthoredHidden(new XElement("button", new XAttribute("data-action", "studio:open")), appliedIds),
    "visible unsupported Studio action remains refused");
Check(local.Allows(new(RmlUiIntentKind.HunterOpen, 0, home, ++sequence)),
    "local inline HunterOpen remains available despite hidden unsupported Hunter workspace route");
var auditedIds = new HashSet<string>();
foreach (string relative in new[] { "Components/app-shell.rml", "Pages/home/home.rml", "Pages/offline/setup.rml", "Pages/offline/arena.rml" })
{
    var xml = XDocument.Load(Path.Combine(assets, relative));
    foreach (var element in xml.Descendants())
    {
        string? id = element.Attribute("id")?.Value;
        if (id != null && appliedIds.Contains(id)) auditedIds.Add(id);
        foreach (string attribute in new[] { "data-action", "data-preview" })
        {
            string? action = element.Attribute(attribute)?.Value;
            if (action == null) continue;
            Check(RmlUiIntentRegistry.TryParseLegacy(action, home, 1, out var intent), "authored action decodes: " + action);
            // The shared shell includes a permanently hidden Studio-home action.
            // Account for authored display:none as well as platform visibility overrides.
            bool hidden = AuthoredHidden(element, appliedIds);
            Check(local.Allows(intent) || hidden, "unsupported authored action is hidden: " + action);
        }
    }
}
Check(auditedIds.SetEquals(appliedIds), "every capability visibility override targets an actual authored DOM ID");
Check(auditedIds.Count == 23, "authored capability DOM inventory includes original controls, unsupported headings and five added workspace/social actions");

var offline = pages.ShowBaseline(RmlUiMenuPage.Offline);
Hidden(offline, "offline_training_panel"); Hidden(offline, "offline_rules_open");
pages.SetBool("offline_training_open", true); pages.SetBool("offline_rules_open", true); pages.Flush();
Check(pages.Manager.ModalCount == 0, "forged publisher flags cannot open unsupported modals");
Check(Dispatch("offline:open-arena").Kind == RmlUiIntentKind.OfflineOpenArena, "arena request forwards");
pages.SetBool("offline_arena_open", true); pages.SetBool("offline_arena_row0_present", true); pages.Flush(); pages.AfterUpdate();
var arena = pages.Manager.Top;
Check(arena != offline && pages.Manager.ModalCount == 1, "publisher opens exactly one arena modal");
Hidden(arena, "offline_arena_search"); Hidden(arena, "offline_arena_search_apply");
Check(!native.Focuses.Any(item => item.Document == arena.DocumentId && item.Element == "offline_arena_search"),
    "local arena never focuses hidden text search");
Check(Dispatch("offline:choice:1", offline).Kind == 0, "background page cannot dispatch through arena modal");
Check(Dispatch("offline:arena-page:next").Kind == RmlUiIntentKind.OfflineArenaPage, "arena paging remains available");
Check(Dispatch("offline:arena:0").Kind == RmlUiIntentKind.OfflineSelectArena, "arena selection remains available");
Check(pages.Back(out var back) && back.Kind == RmlUiIntentKind.OfflineCancelArena && back.Document == arena,
    "Back targets the foreground arena cancel");
pages.SetBool("offline_arena_open", false); pages.Flush();
Check(pages.Manager.ModalCount == 0 && !host.IsAlive(arena), "arena closes on publisher acknowledgement");
Check(!pages.HandleIntent(Intent("offline:arena:0", arena), out var retired) && retired.Kind == 0,
    "retired supported modal event cannot dispatch");
for (int choice = 0; choice <= 6; choice++)
    Check(Dispatch("offline:choice:" + choice).Kind == RmlUiIntentKind.OfflineChoice, "supported local option forwards");
foreach (string action in new[] { "hunter:open", "hunter:select:6", "hunter:suit:3", "hunter:apply", "hunter:cancel", "hunter:reset-preview", "quit", "offline:launch-match" })
    Check(Dispatch(action).Kind != 0, "supported local action forwards: " + action);
var duplicate = Intent("offline:choice:1");
Check(pages.HandleIntent(duplicate, out var first) && first == duplicate
    && pages.HandleIntent(duplicate, out var second) && second.Kind == 0, "sequence replay cannot repeat local choices");
Check(pages.Back(out back) && back.Kind == RmlUiIntentKind.Navigate && back.Argument == (int)RmlUiRouteArgument.Home,
    "offline Back preserves Home route");
pages.ShowBaseline(RmlUiMenuPage.Home);
pages.SetBool("home_social_rail_visible", true); pages.SetBool("home_friends_visible", true); pages.Flush();
Hidden(pages.Document, "home_social_rail");
host.SetBool(pages.Document, "visible:nav_studio", true); pages.Flush();
Hidden(pages.Document, "nav_studio");
host.SetBool(pages.Document, "visible:profile", true); pages.PresentChrome(pages.Document);
Hidden(pages.Document, "profile");
pages.SetBool("multiplayer_mode", true); pages.SetBool("adventure_mode", true); pages.Flush();
Check(pages.Page == RmlUiMenuPage.Home, "unsupported publisher route flags cannot escape local Home");

// Exercise the actual UI/EGL admission boundary with copied value types.
var admission = new RmlUiAndroidLauncherAdmission<Request, Plan>();
Check(admission.TryRequest(new(3), out var witness) && admission.IsCurrent(witness), "owner submits copied request");
Check(!admission.TryRequest(new(4), out _), "awaiting admission rejects second submission");
Check(!admission.TryTakePlan(out _), "plan is absent before acknowledgement");
Check(Task.Run(() =>
{
    try { admission.TryRequest(new(5), out _); return false; }
    catch (InvalidOperationException) { return true; }
}).GetAwaiter().GetResult(), "wrong owner cannot submit");
Check(Task.Run(() =>
{
    try { admission.TryTakePlan(out _); return false; }
    catch (InvalidOperationException) { return true; }
}).GetAwaiter().GetResult(), "wrong owner cannot take plan");
var other = new RmlUiAndroidLauncherAdmission<Request, Plan>();
Check(other.TryRequest(new(3), out var otherWitness) && !admission.TryAccept(otherWitness, new(9)),
    "equal request from another admission lifetime cannot acknowledge");
Check(!admission.TryAccept(witness with { Generation = witness.Generation + 1 }, new(9))
    && !admission.TryAccept(witness with { Request = new(4) }, new(9)), "generation and copied request must match acknowledgement");
var plan = new Plan(7);
Check(Task.Run(() => admission.IsCurrent(witness) && admission.TryAccept(witness, plan)).GetAwaiter().GetResult(),
    "UI can acknowledge matching witness without native work");
plan = new(99);
Check(!admission.IsCurrent(witness) && !admission.TryAccept(witness, new(8))
    && !admission.TryRequest(new(4), out _), "accepted plan rejects duplicate acknowledgements and submissions");
Check(admission.TryTakePlan(out var copied) && copied.Arena == 7 && !admission.TryTakePlan(out _),
    "owner consumes immutable copied plan exactly once");
Check(admission.Current.State == RmlUiAndroidLauncherAdmissionState.Retired, "consumption retires admission");
other.Retire();
Check(!other.IsCurrent(otherWitness) && !Task.Run(() => other.TryAccept(otherWitness, new(1))).GetAwaiter().GetResult()
    && !other.TryTakePlan(out _) && !other.TryRequest(new(1), out _), "retirement rejects late UI and owner work");
var canceled = new RmlUiAndroidLauncherAdmission<Request, Plan>();
Check(canceled.TryRequest(new(2), out var canceledWitness) && canceled.TryAccept(canceledWitness, new(2)), "accepted cancellation setup");
Task.Run(canceled.Retire).GetAwaiter().GetResult();
Check(!canceled.TryTakePlan(out _), "retirement discards an already accepted plan before owner consumption");
Check(checks == 488, "capability/admission and current authored-action inventory checks remain complete");
HunterAcceptance.Run(root, Check);
Check(checks == 664, "existing Hunter checks and current authored-action inventory remain complete");
Guid previewOwner = Guid.NewGuid(), replacementOwner = Guid.NewGuid();
Check(!RmlUiAndroidLauncherPreviewBarrier.Blocked && !RmlUiAndroidLauncherPreviewBarrier.IsOwned(previewOwner),
    "preview barrier begins unowned");
Check(RmlUiAndroidLauncherPreviewBarrier.TryAcquire(previewOwner)
    && RmlUiAndroidLauncherPreviewBarrier.Blocked && RmlUiAndroidLauncherPreviewBarrier.IsOwned(previewOwner),
    "native launcher acquires actual production preview barrier");
Check(!RmlUiAndroidLauncherPreviewBarrier.TryAcquire(previewOwner)
    && !RmlUiAndroidLauncherPreviewBarrier.TryAcquire(replacementOwner), "held barrier rejects duplicate and different owner");
Task.Run(() => RmlUiAndroidLauncherPreviewBarrier.Release(replacementOwner)).GetAwaiter().GetResult();
Check(RmlUiAndroidLauncherPreviewBarrier.Blocked && RmlUiAndroidLauncherPreviewBarrier.IsOwned(previewOwner)
    && !RmlUiAndroidLauncherPreviewBarrier.TryAcquire(replacementOwner), "wrong-owner release cannot unlock preview barrier");
Task.Run(() => RmlUiAndroidLauncherPreviewBarrier.Release(previewOwner)).GetAwaiter().GetResult();
Check(!RmlUiAndroidLauncherPreviewBarrier.Blocked && !RmlUiAndroidLauncherPreviewBarrier.IsOwned(previewOwner),
    "exact owner release unlocks preview barrier");
Check(RmlUiAndroidLauncherPreviewBarrier.TryAcquire(replacementOwner), "new Activity owner acquires released barrier");
RmlUiAndroidLauncherPreviewBarrier.Release(previewOwner);
Check(RmlUiAndroidLauncherPreviewBarrier.Blocked && RmlUiAndroidLauncherPreviewBarrier.IsOwned(replacementOwner)
    && !RmlUiAndroidLauncherPreviewBarrier.TryAcquire(previewOwner), "stale old-owner release cannot unlock replacement owner");
RmlUiAndroidLauncherPreviewBarrier.Release(replacementOwner);
Check(!RmlUiAndroidLauncherPreviewBarrier.Blocked, "barrier fixture leaves production static gate released");
Check(checks == 673, "existing Hunter/barrier checks and current authored-action inventory remain complete");

// Actual admission overload keeps the acknowledged plan private until native
// launcher owner retirement succeeds. Callback failure consumes it permanently.
var absentPlan = new RmlUiAndroidLauncherAdmission<Request, Plan>();
int retirementCalls = 0;
bool RetireAbsent() { retirementCalls++; return true; }
Check(!absentPlan.TryTakePlan(RetireAbsent, out var absent) && absent == default && retirementCalls == 0,
    "open admission with no plan never invokes retirement callback");
Check(absentPlan.TryRequest(new(51), out var absentWitness), "awaiting retirement setup");
Check(!absentPlan.TryTakePlan(RetireAbsent, out absent) && absent == default && retirementCalls == 0,
    "awaiting admission with no accepted plan never invokes retirement callback");
Check(absentPlan.TryAccept(absentWitness, new(51)), "retirement success setup accepts copied plan");
Plan retiredOutput = default;
bool successfulRetirement = false;
Check(absentPlan.TryTakePlan(() =>
{
    retirementCalls++;
    Check(retiredOutput == default, "acknowledged plan is not exposed during owner retirement");
    Check(absentPlan.Current.State == RmlUiAndroidLauncherAdmissionState.Retired && !absentPlan.TryTakePlan(out _),
        "admission retires before callback and cannot reentrantly expose a second plan");
    successfulRetirement = true;
    return true;
}, out retiredOutput) && successfulRetirement && retiredOutput == new Plan(51) && retirementCalls == 1,
    "exact copied plan reaches caller only after successful owner retirement");
Check(!absentPlan.TryTakePlan(RetireAbsent, out absent) && absent == default && retirementCalls == 1,
    "consumed admission never repeats retirement callback");

var failedRetirement = new RmlUiAndroidLauncherAdmission<Request, Plan>();
Check(failedRetirement.TryRequest(new(61), out var failedWitness) && failedRetirement.TryAccept(failedWitness, new(61)),
    "failed retirement setup accepts copied plan");
Plan failedOutput = new(999);
int failedCalls = 0;
Check(!failedRetirement.TryTakePlan(() => { failedCalls++; return false; }, out failedOutput)
    && failedOutput == default && failedCalls == 1, "failed retirement returns false and clears caller plan");
Check(failedRetirement.Current.State == RmlUiAndroidLauncherAdmissionState.Retired
    && !failedRetirement.TryAccept(failedWitness, new(62)) && !failedRetirement.TryRequest(new(62), out _)
    && !failedRetirement.TryTakePlan(() => { failedCalls++; return true; }, out failedOutput)
    && failedOutput == default && failedCalls == 1,
    "failed retirement permanently consumes admission without retry, duplicate acknowledgement, or callback");

var throwingRetirement = new RmlUiAndroidLauncherAdmission<Request, Plan>();
Check(throwingRetirement.TryRequest(new(71), out var throwingWitness) && throwingRetirement.TryAccept(throwingWitness, new(71)),
    "throwing retirement setup accepts copied plan");
Plan throwingOutput = default;
int throwingCalls = 0;
bool sawRetirementFailure = false;
try
{
    throwingRetirement.TryTakePlan(() =>
    {
        throwingCalls++;
        Check(throwingOutput == default, "throwing retirement has not exposed its accepted plan");
        throw new IOException("Fixture retirement failure");
    }, out throwingOutput);
}
catch (IOException error) when (error.Message == "Fixture retirement failure") { sawRetirementFailure = true; }
Check(sawRetirementFailure && throwingOutput == default && throwingCalls == 1
    && throwingRetirement.Current.State == RmlUiAndroidLauncherAdmissionState.Retired,
    "retirement exception exposes no accepted plan and leaves admission retired");
Check(!throwingRetirement.TryAccept(throwingWitness, new(72)) && !throwingRetirement.TryRequest(new(72), out _)
    && !throwingRetirement.TryTakePlan(() => { throwingCalls++; return true; }, out throwingOutput)
    && throwingOutput == default && throwingCalls == 1,
    "throwing retirement cannot retry or duplicate admission");
AdventureAcceptance.Run(root, Check);
DocumentCapabilityAcceptance.Run(root, Check);
Console.WriteLine($"PASS: {checks} configured-local capability, authored DOM, publisher, modal, admission, local Hunter presenter, and preview barrier checks (source-only; native/engine boundaries faked).");

readonly record struct Request(int Arena);
readonly record struct Plan(int Arena);
