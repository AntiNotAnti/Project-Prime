using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.Gui;

int checks = 0;
void Check(bool value, string description)
{
    if (!value) throw new InvalidOperationException(description);
    Console.WriteLine("ROUTER PASS " + description);
    checks++;
}

foreach (LauncherPage page in Enum.GetValues<LauncherPage>())
{
    var route = new LauncherRoute(page, "map/replay + 漢字 & α");
    Check(LauncherRouteCatalog.TryParse(LauncherRouteCatalog.DeepLink(route), out var parsed) && parsed == route,
        page + " canonical deep link preserves Unicode and reserved characters");
}
Check(LauncherRouteCatalog.TryParse("news", out var home) && home.Page == LauncherPage.Home,
    "News compatibility alias resolves Home");
Check(LauncherRouteCatalog.TryParse("hunterlicense", out var license) && license.Page == LauncherPage.License,
    "Hunter License compatibility alias resolves License");
Check(LauncherRouteCatalog.TryParse("forge", out var studio) && studio.Page == LauncherPage.StudioLaunch,
    "Forge compatibility alias resolves standalone Studio launch");
foreach (string invalid in new[] { "https://launcher/home", "prime://other/home", "prime://launcher:12/home",
    "prime://person@launcher/home", "prime://launcher/home#fragment", "prime://launcher/unknown",
    "prime://launcher/home?x=value", "prime://launcher/home?item=value&item=other", "prime://launcher/home?item=%00" })
    Check(!LauncherRouteCatalog.TryParse(invalid, out _), "Reject unsupported link " + invalid);

using (var router = new LauncherRouter())
{
    int events = 0;
    router.Changed += _ => events++;
    var homeLifetime = router.Lifetime;
    router.RememberFocus("home.play");
    Check(router.Navigate(new(LauncherPage.Play)) == LauncherNavigationOutcome.Changed
        && homeLifetime.Cancellation.IsCancellationRequested, "navigation cancels the old page lifetime");
    Check(!router.IsCurrent(homeLifetime.Stamp(20)), "old lifetime completion is rejected");
    var playLifetime = router.Lifetime;
    var gate = new UiSnapshotGate();
    gate.Bind(playLifetime);
    Check(gate.Accept(playLifetime.Stamp(0)) && gate.Accept(playLifetime.Stamp(2))
        && !gate.Accept(playLifetime.Stamp(1)) && !gate.Accept(playLifetime.Stamp(2)),
        "snapshot gate accepts increasing revisions and rejects repeats or reordering");
    Check(!gate.Accept(new(LauncherPage.Settings, playLifetime.Id, 3))
        && !gate.Accept(homeLifetime.Stamp(30)) && !gate.Accept(playLifetime.Stamp(-1)),
        "snapshot gate rejects another page, lifetime, or invalid revision");
    router.RememberFocus("play.create");
    var normal = router.OpenModal(LauncherModalKind.Dialog, initialFocus: "create.name");
    router.RememberFocus("create.confirm");
    var critical = router.OpenModal(LauncherModalKind.Busy, LauncherModalPriority.Critical,
        canDismiss: false, initialFocus: "download.cancel");
    Check(router.Navigate(new(LauncherPage.Settings)) == LauncherNavigationOutcome.Blocked
        && router.Current.Page == LauncherPage.Play, "modal prevents background route navigation");
    Check(router.CloseModal(normal.Id) == LauncherNavigationOutcome.Unchanged
        && router.Snapshot().TopModal?.Id == critical.Id, "background modal cannot steal close or focus");
    Check(router.Back() == LauncherNavigationOutcome.Blocked && router.Snapshot().TopModal?.Id == critical.Id,
        "Back cannot dismiss a required busy layer");
    Check(router.CloseModal(critical.Id, force: true) == LauncherNavigationOutcome.Changed
        && critical.Cancellation.IsCancellationRequested
        && router.Snapshot().Focus?.Target == "create.confirm", "foreground close cancels modal work and restores its parent focus");
    Check(router.Back() == LauncherNavigationOutcome.Changed && normal.Cancellation.IsCancellationRequested
        && router.Current.Page == LauncherPage.Play && router.Snapshot().Focus?.Target == "play.create",
        "Back closes modal before route history and restores page focus");
    Check(router.Back() == LauncherNavigationOutcome.Changed && router.Current.Page == LauncherPage.Home
        && playLifetime.Cancellation.IsCancellationRequested && router.Snapshot().Focus?.Target == "home.play"
        && router.Snapshot().Focus?.Lifetime == router.Lifetime.Id, "history restores focus using a fresh page lifetime");
    Check(!gate.Accept(playLifetime.Stamp(50)), "cancelled lifetime cannot accept snapshots before presenter rebind");
    Check(router.Back() == LauncherNavigationOutcome.ExitConfirmationRequired && !router.HasModal,
        "empty history requests app exit confirmation without silently exiting");
    Check(events == 9, "one changed event per navigation, focus, or modal transition");
}

using (var router = new LauncherRouter())
{
    router.RememberFocus("home.settings");
    var critical = router.OpenModal(LauncherModalKind.Error, LauncherModalPriority.Critical, initialFocus: "error.close");
    var queued = router.OpenModal(LauncherModalKind.Dialog, initialFocus: "dialog.close");
    Check(router.Snapshot().TopModal?.Id == critical.Id && router.Snapshot().Focus?.Target == "error.close",
        "lower priority modal cannot replace an existing critical modal");
    router.CloseModal(critical.Id);
    Check(router.Snapshot().TopModal?.Id == queued.Id && router.Snapshot().Focus?.Target == "dialog.close",
        "closing critical modal reveals the queued lower priority modal");
    router.CloseModal(queued.Id);
    Check(router.Snapshot().Focus?.Target == "home.settings", "queued modal dismissal restores original page focus");
    var first = router.OpenModal(LauncherModalKind.Dialog, initialFocus: "first.close");
    var second = router.OpenModal(LauncherModalKind.Dialog, initialFocus: "second.close");
    Check(router.Snapshot().TopModal?.Id == second.Id, "equal priority modals preserve last-opened precedence");
    router.RenewLifetime();
    Check(first.Cancellation.IsCancellationRequested && second.Cancellation.IsCancellationRequested
        && !router.HasModal && router.Snapshot().Focus?.Target == "home.settings",
        "document/device recreation cancels modal work and retains page focus identity");
}

using (var router = new LauncherRouter())
{
    router.Navigate(new(LauncherPage.Play));
    var before = router.Snapshot();
    router.CanNavigate = route => route.Page != LauncherPage.Home && route.Page != LauncherPage.Settings;
    Check(router.Back() == LauncherNavigationOutcome.Blocked && router.Previous?.Page == LauncherPage.Home
        && router.Navigate(new(LauncherPage.Settings)) == LauncherNavigationOutcome.Blocked
        && router.Lifetime.Id == before.Lifetime.Id, "guard rejection preserves history and active lifetime");
    router.CanNavigate = null;
    router.Navigate(new(LauncherPage.Lobby));
    router.Forget(LauncherPage.Play);
    Check(router.Previous?.Page == LauncherPage.Home, "Forget removes ended-session routes from history");
    var pending = router.Lifetime;
    var modal = router.OpenModal(LauncherModalKind.Dialog);
    router.Reset(new(LauncherPage.Play));
    Check(router.Current.Page == LauncherPage.Play && router.Previous == null && !router.HasModal
        && pending.Cancellation.IsCancellationRequested && modal.Cancellation.IsCancellationRequested,
        "authoritative reset clears route and modal work with a new session lifetime");
    router.Navigate(new(LauncherPage.Settings), LauncherNavigationMode.Replace);
    Check(router.Previous == null, "replace navigation does not preserve obsolete history");
}

using (var router = new LauncherRouter())
{
    for (int i = 0; i < 100; i++)
        router.Navigate(new(i % 2 == 0 ? LauncherPage.Play : LauncherPage.Home));
    Check(router.Snapshot().History.Count == 64, "route history stays bounded after repeated navigation");
    var snapshot = router.Snapshot();
    router.Reset(new(LauncherPage.Lobby));
    Check(snapshot.History.Count == 64 && snapshot.Current.Page == LauncherPage.Home,
        "published snapshots retain copied history after future mutation");
    router.Dispatch(new(LauncherNavigationAction.NextTab));
    Check(router.Current.Page == LauncherPage.License, "next tab from lobby uses Play as tab anchor");
    router.Dispatch(new(LauncherNavigationAction.PreviousTab));
    Check(router.Current.Page == LauncherPage.Play, "previous tab preserves actual shipped tab order");
}

using (var router = new LauncherRouter())
{
    Exception? workerError = null;
    var worker = new Thread(() =>
    {
        try { router.Navigate(new(LauncherPage.Play)); }
        catch (Exception error) { workerError = error; }
    });
    worker.Start(); worker.Join();
    Check(workerError is InvalidOperationException && router.Current.Page == LauncherPage.Home,
        "worker-thread navigation is rejected without mutating state");
    bool faultReported = false;
    router.CancellationFaulted += error => faultReported = error.InnerExceptions.Count == 1;
    router.Lifetime.Cancellation.Register(() => throw new InvalidOperationException("cancel callback failure"));
    Check(router.Navigate(new(LauncherPage.Play)) == LauncherNavigationOutcome.Changed && faultReported,
        "cancellation callback failure is reported without wedging route publication");
    var lifetime = router.Lifetime;
    router.Dispose(); router.Dispose();
    bool disposed = false;
    try { router.Back(); } catch (ObjectDisposedException) { disposed = true; }
    Check(disposed && lifetime.Cancellation.IsCancellationRequested && !router.IsCurrent(lifetime.Stamp(0)),
        "idempotent disposal cancels work and rejects late callbacks or mutations");
}

using (var legacy = new PrimeRouter())
{
    Check(legacy.Current == PrimeRoute.News && !legacy.Back(), "legacy facade preserves initial route and root Back result");
    int events = 0;
    legacy.Changed += _ => events++;
    Check(legacy.Navigate(PrimeRoute.Play) && legacy.Navigate(PrimeRoute.Play) && events == 1,
        "legacy same-route navigation has no duplicate event");
    legacy.CanNavigate = route => route != PrimeRoute.News;
    Check(legacy.Back() && legacy.Current == PrimeRoute.Play && legacy.Previous == PrimeRoute.News,
        "legacy blocked Back remains consumed and retains history");
    legacy.CanNavigate = null;
    legacy.Navigate(PrimeRoute.Lobby);
    legacy.NextRoute();
    Check(legacy.Current == PrimeRoute.HunterLicense, "legacy Lobby next tab retains original policy");
    legacy.CanNavigate = route =>
    {
        if (route != PrimeRoute.Play) return true;
        legacy.Navigate(PrimeRoute.Lobby);
        return false;
    };
    Check(!legacy.Navigate(PrimeRoute.Play) && legacy.Current == PrimeRoute.Lobby,
        "legacy active-session guard may redirect Play to Lobby without corrupting history");
    foreach (PrimeRoute route in Enum.GetValues<PrimeRoute>())
        Check(PrimeRouter.FromCore(PrimeRouter.ToCore(route)) == route, "legacy " + route + " mapping round trips");
}

using (var shared = new LauncherRouter())
{
    using (var legacy = new PrimeRouter(shared))
    {
        bool legacyVisible = false;
        legacy.CanNavigate = _ => false;
        legacy.NavigationGuardEnabled = () => legacyVisible;
        Check(shared.Navigate(new(LauncherPage.Play)) == LauncherNavigationOutcome.Changed
            && legacy.Current == PrimeRoute.Play, "hidden legacy guard cannot block native navigation on the shared router");
        legacyVisible = true;
        Check(shared.Navigate(new(LauncherPage.Settings)) == LauncherNavigationOutcome.Blocked,
            "visible legacy draft guard protects the shared router");
        legacyVisible = false;
        shared.Navigate(new(LauncherPage.Adventure));
        Check(legacy.Current == PrimeRoute.Offline, "Adventure fallback maps the existing offline/save workspace");
        shared.Navigate(new(LauncherPage.Hunters));
        Check(legacy.Current == PrimeRoute.HunterLicense, "Hunter fallback maps existing License customization");
        shared.Navigate(new(LauncherPage.Community));
        Check(legacy.Current == PrimeRoute.Forge, "Community route can preserve explicit legacy fallback without throwing");
        Check(shared.Snapshot().History.Count == 4, "native and legacy presenters observe one route history");
    }
    Check(shared.Navigate(new(LauncherPage.Settings)) == LauncherNavigationOutcome.Changed
        && shared.CanNavigate == null && !shared.Lifetime.Cancellation.IsCancellationRequested,
        "disposing a borrowed legacy presenter detaches its guard and retains engine router ownership");
}

using (var router = new LauncherRouter())
{
    bool invalidCallbackReported = false;
    router.CancellationFaulted += _ => invalidCallbackReported = true;
    router.Lifetime.Cancellation.Register(() => router.Navigate(new(LauncherPage.Settings)));
    Check(router.Navigate(new(LauncherPage.Play)) == LauncherNavigationOutcome.Changed
        && router.Current.Page == LauncherPage.Play && invalidCallbackReported,
        "retired-page cancellation callback cannot navigate over the requested destination");
    router.CanNavigate = _ => { router.Forget(LauncherPage.Home); return true; };
    Check(router.Back() == LauncherNavigationOutcome.Blocked && router.Current.Page == LauncherPage.Play
        && router.Previous == null, "guard mutation of history cannot pop an invalid or different history entry");
}

using (var shared = new LauncherRouter())
{
    shared.Changed += snapshot =>
    {
        if (snapshot.Current.Page == LauncherPage.Play)
            shared.Navigate(new(LauncherPage.Settings));
    };
    using var legacy = new PrimeRouter(shared);
    int notifications = 0;
    PrimeRoute? presented = null;
    legacy.Changed += route => { notifications++; presented = route; };
    shared.Navigate(new(LauncherPage.Play));
    Check(shared.Current.Page == LauncherPage.Settings && legacy.Current == PrimeRoute.Settings
        && notifications == 1 && presented == PrimeRoute.Settings,
        "reentrant event redirect cannot deliver a retired route to the legacy presenter");
}

Console.WriteLine($"ROUTER {checks} checks PASS");
