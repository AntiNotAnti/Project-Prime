using System.Collections.Concurrent;
using System.Reflection;
using MphRead;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Launcher.RmlUi.Host;

/// <summary>Actual menu-controller contract with fake admission/account owners; no UI, account or network.</summary>
internal static class SocialConnectionAcceptance
{
    internal static void Run()
    {
        int checks = 0;
        int owner = Environment.CurrentManagedThreadId;
        void Check(bool value, string label)
        { if (!value) throw new InvalidOperationException(label); checks++; }
        HomeProjection(Check);
        NativeSocialJoinTarget Target() => new("PP-AAAA-BBBB-CCCC-DDDD-0001", Guid.NewGuid().ToString(),
            "127.0.0.1", 27888, 57, 42, "MP1 SANCTORUS", "Synthetic Social lobby", DateTimeOffset.UtcNow.AddSeconds(30));
        LaunchPlan Plan() => new() { Kind = LaunchKind.Online, PlayerName = "Pilot", Hunter = Hunter.Samus,
            RoomKey = "", Port = 27888, Lobby = new LobbyContext("Synthetic Social lobby", "127.0.0.1:27888") };
        void Settle(RmlUiMenuController controller)
        {
            // Always Tick once: cancellation already sets Joining=false before a late result arrives.
            for (int i = 0; i < 200; i++)
            {
                controller.Tick();
                if (!controller.Joining && !controller.Busy && QueueCount(controller) == 0) return;
                Thread.Sleep(5);
            }
            throw new TimeoutException("Social result did not settle through the menu owner queue.");
        }
        void OnOwner(Services service, params Origin[] origins)
        {
            Check(service.CallThreads.All(thread => thread == owner)
                && origins.All(origin => origin.Threads.All(thread => thread == owner)),
                "Social validation, handoff, retirement, Alive and completion callbacks run only on the controller owner thread");
        }

        var invalid = new Services(); var invalidOrigin = new Origin(invalid.Trace);
        using (var controller = new RmlUiMenuController(invalid))
        {
            var target = Target();
            foreach (var rejected in new[] { target with { ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1) },
                target with { SelfPrimeId = "invalid" }, target with { LobbyId = Guid.Empty.ToString() },
                target with { Host = "localhost" }, target with { Port = 0 }, target with { Protocol = 0 },
                target with { AuthorityEpoch = 0 }, target with { RoomKey = "" } })
                Check(!controller.ConnectSocial(rejected, "Pilot", invalidOrigin.Alive, invalidOrigin.Completed)
                    && invalid.Joins == 0 && invalidOrigin.Results.IsEmpty,
                    "Invalid or expired Social hints cannot start admission or complete an origin page");
            invalidOrigin.Current = false;
            Check(!controller.ConnectSocial(target, "Pilot", invalidOrigin.Alive, invalidOrigin.Completed) && invalid.Joins == 0,
                "A retired Social page cannot start a connection");
            invalidOrigin.Current = true;
            invalid.Inner.LobbyView = Stub.EmptyLobby with { Active = true };
            Check(!controller.ConnectSocial(target, "Pilot", invalidOrigin.Alive, invalidOrigin.Completed) && invalid.Joins == 0,
                "Social admission cannot replace an active session");
            invalid.Inner.LobbyView = Stub.EmptyLobby;
            Check(controller.Dispatch(RmlUiIntentKind.PlayBrowse)
                && !controller.ConnectSocial(target, "Pilot", invalidOrigin.Alive, invalidOrigin.Completed) && invalid.Joins == 0,
                "Social admission cannot replace another busy native request");
        }
        OnOwner(invalid, invalidOrigin);

        var success = new Services(); var successOrigin = new Origin(success.Trace);
        using (var controller = new RmlUiMenuController(success))
        {
            var target = Target();
            Check(controller.ConnectSocial(target, " Pilot ", successOrigin.Alive, successOrigin.Completed)
                && ReferenceEquals(success.Attempts[0].Target, target) && success.Attempts[0].Name == " Pilot ",
                "Social admission preserves the exact target and caller name for its established service");
            Check(!controller.ConnectSocial(target, "Pilot", successOrigin.Alive, successOrigin.Completed) && success.Joins == 1,
                "Duplicate Social clicks cannot start a second worker");
            var attempt = success.Prepare(Plan()); var work = Work(controller);
            CompleteOnWorker(attempt, new(true, attempt.Plan, ""), work);
            Check(success.Accepted == 0 && successOrigin.Results.IsEmpty && QueueCount(controller) == 1,
                "Worker completion queues immutable Social data without applying UI callbacks or accepting a session");
            Settle(controller);
            Check(success.Trace.SequenceEqual(new[] { "validate", "accept", "joined", "callback" })
                && success.Accepted == 1 && success.Notes == 1 && success.DiscardCalls == 0
                && successOrigin.Results.Single() == (true, ""),
                "Social joined note and successful origin callback follow CanAccept and successful existing-owner handoff exactly once");
            controller.Tick();
            Check(success.Accepted == 1 && success.Notes == 1 && successOrigin.Results.Count == 1,
                "Further owner ticks cannot repeat Social acceptance, note or completion");
            OnOwner(success, successOrigin);
        }

        var refused = new Services { AcceptResult = false }; var refusedOrigin = new Origin(refused.Trace);
        using (var controller = new RmlUiMenuController(refused))
        {
            controller.ConnectSocial(Target(), "Pilot", refusedOrigin.Alive, refusedOrigin.Completed);
            var attempt = refused.Prepare(Plan());
            CompleteOnWorker(attempt, new(true, attempt.Plan, ""), Work(controller)); Settle(controller);
            Check(refused.Trace.SequenceEqual(new[] { "validate", "accept", "discard", "callback" })
                && refused.Notes == 0 && refused.Retired == 1 && !refusedOrigin.Results.Single().Accepted,
                "Refused existing-owner handoff discards only the candidate and never notes Social joined");
            OnOwner(refused, refusedOrigin);
        }

        var staleLease = new Services(); var leaseOrigin = new Origin(staleLease.Trace);
        using (var controller = new RmlUiMenuController(staleLease))
        {
            var target = Target(); controller.ConnectSocial(target, "Pilot", leaseOrigin.Alive, leaseOrigin.Completed);
            var attempt = staleLease.Prepare(Plan());
            staleLease.CurrentAccountLease = new object(); // Same public Prime ID, different opaque account lifetime.
            CompleteOnWorker(attempt, new(true, attempt.Plan, ""), Work(controller)); Settle(controller);
            Check(staleLease.Trace.SequenceEqual(new[] { "validate", "discard", "callback" })
                && target.SelfPrimeId == attempt.Target.SelfPrimeId && staleLease.Accepted == 0 && staleLease.Notes == 0
                && !leaseOrigin.Results.Single().Accepted && staleLease.Retired == 1,
                "CanAccept rejects a retired same-Prime-ID account lease before AcceptJoin or joined note");
            OnOwner(staleLease, leaseOrigin);
        }

        var gone = new Services(); var oldPage = new Origin(gone.Trace); var newPage = new Origin(gone.Trace);
        using (var controller = new RmlUiMenuController(gone))
        {
            controller.ConnectSocial(Target(), "Pilot", oldPage.Alive, oldPage.Completed);
            var attempt = gone.Prepare(Plan()); oldPage.Current = false;
            CompleteOnWorker(attempt, new(true, attempt.Plan, ""), Work(controller)); Settle(controller);
            Check(gone.Trace.SequenceEqual(new[] { "discard" }) && gone.Accepted == 0 && gone.Notes == 0
                && oldPage.Results.IsEmpty && newPage.Results.IsEmpty,
                "A retired origin cannot accept or deliver its callback to itself or a replacement page");
            OnOwner(gone, oldPage, newPage);
        }

        var cancelled = new Services(); var cancelOrigin = new Origin(cancelled.Trace);
        using (var controller = new RmlUiMenuController(cancelled))
        {
            controller.ConnectSocial(Target(), "Pilot", cancelOrigin.Alive, cancelOrigin.Completed);
            var attempt = cancelled.Prepare(Plan()); var work = Work(controller); int before = cancelled.Cancellations;
            controller.CancelSocialConnection();
            Check(attempt.Token.IsCancellationRequested && cancelled.Cancellations == before + 1 && cancelled.Retired == 1,
                "Explicit Social cancel retires its service candidate and cancels the registered worker");
            CompleteOnWorker(attempt, new(true, attempt.Plan, ""), work); Settle(controller);
            Check(cancelled.Accepted == 0 && cancelled.Notes == 0 && cancelled.DiscardCalls == 1
                && cancelOrigin.Results.Count == 1 && !cancelOrigin.Results.Single().Accepted
                && cancelOrigin.Results.Single().Message.Contains("CANCELLED", StringComparison.Ordinal),
                "Late success after cancel cannot join and reports one cancellation to the still-current origin");
            OnOwner(cancelled, cancelOrigin);
        }

        var replaced = new Services(); var firstOrigin = new Origin(replaced.Trace); var secondOrigin = new Origin(replaced.Trace);
        using (var controller = new RmlUiMenuController(replaced))
        {
            var target = Target(); controller.ConnectSocial(target, "Pilot", firstOrigin.Alive, firstOrigin.Completed);
            var first = replaced.Prepare(Plan()); var firstWork = Work(controller);
            controller.CancelSocialConnection(); firstOrigin.Current = false;
            Check(controller.ConnectSocial(target, "Pilot", secondOrigin.Alive, secondOrigin.Completed),
                "A fresh Social attempt can reuse a valid immutable destination after explicit cancellation");
            var second = replaced.Prepare(Plan()); var secondWork = Work(controller);
            Check(first.Plan.Equals(second.Plan) && !ReferenceEquals(first.Plan.Lobby, second.Plan.Lobby),
                "Fixture uses value-equal launch plans with distinct exact Lobby owner references");
            CompleteOnWorker(first, new(true, first.Plan, ""), firstWork); controller.Tick();
            Check(controller.Joining && controller.Busy && replaced.Accepted == 0 && replaced.Notes == 0
                && ReferenceEquals(replaced.Candidate, second) && !second.Stopped && firstOrigin.Results.IsEmpty,
                "Late equal old plan cannot clear or stop a newer Social owner; discard delegates exact-reference semantics to the service");
            CompleteOnWorker(second, new(true, second.Plan, ""), secondWork); Settle(controller);
            Check(replaced.Accepted == 1 && replaced.Notes == 1 && secondOrigin.Results.Single().Accepted
                && firstOrigin.Results.IsEmpty && replaced.Retired == 1,
                "Only the fresh Social candidate transfers ownership and completes its own origin");
            OnOwner(replaced, firstOrigin, secondOrigin);
        }

        var failure = new Services(); var failureOrigin = new Origin(failure.Trace);
        using (var controller = new RmlUiMenuController(failure))
        {
            controller.ConnectSocial(Target(), "Pilot", failureOrigin.Alive, failureOrigin.Completed);
            var attempt = failure.Attempts.Single();
            CompleteOnWorker(attempt, new(false, default, "SYNTHETIC REFUSAL"), Work(controller)); Settle(controller);
            Check(failure.Accepted == 0 && failure.Notes == 0 && failure.DiscardCalls == 1
                && failureOrigin.Results.Single() == (false, "SYNTHETIC REFUSAL"),
                "An ordinary refused Social result completes false without validation, acceptance or joined note");
            OnOwner(failure, failureOrigin);
        }

        foreach (bool taskCancelled in new[] { false, true })
        {
            var failedWorker = new Services(); var workerOrigin = new Origin(failedWorker.Trace);
            using var controller = new RmlUiMenuController(failedWorker);
            controller.ConnectSocial(Target(), "Pilot", workerOrigin.Alive, workerOrigin.Completed);
            var work = Work(controller); var attempt = failedWorker.Attempts.Single();
            Task.Run(() => { if (taskCancelled) attempt.Pending.SetCanceled(); else attempt.Pending.SetException(new InvalidOperationException("SYNTHETIC ADMISSION FAULT")); }).GetAwaiter().GetResult();
            WaitRetired(work); Settle(controller);
            Check(failedWorker.Accepted == 0 && failedWorker.Notes == 0 && workerOrigin.Results.Count == 1
                && !workerOrigin.Results.Single().Accepted,
                "A faulted or cancelled Social task releases the originating page with one false owner-thread completion");
            OnOwner(failedWorker, workerOrigin);
        }

        var queued = new Services(); var queuedOrigin = new Origin(queued.Trace);
        var queuedController = new RmlUiMenuController(queued);
        queuedController.ConnectSocial(Target(), "Pilot", queuedOrigin.Alive, queuedOrigin.Completed);
        var queuedAttempt = queued.Prepare(Plan());
        CompleteOnWorker(queuedAttempt, new(true, queuedAttempt.Plan, ""), Work(queuedController));
        Check(QueueCount(queuedController) == 1 && queuedOrigin.Results.IsEmpty,
            "A completed worker can remain queued before owner application");
        int cancellations = queued.Cancellations; queuedController.Dispose();
        Check(queued.Cancellations == cancellations + 1 && queued.DiscardCalls == 1
            && queued.Accepted == 0 && queued.Notes == 0 && queuedOrigin.Results.IsEmpty && QueueCount(queuedController) == 0,
            "Disposal drains and discards queued Social data without invoking a retired controller's page callback");
        OnOwner(queued, queuedOrigin);

        var late = new Services(); var lateOrigin = new Origin(late.Trace);
        var disposed = new RmlUiMenuController(late);
        disposed.ConnectSocial(Target(), "Pilot", lateOrigin.Alive, lateOrigin.Completed);
        var lateAttempt = late.Prepare(Plan()); var lateWork = Work(disposed);
        disposed.Dispose(); CompleteOnWorker(lateAttempt, new(true, lateAttempt.Plan, ""), lateWork);
        Check(lateAttempt.Token.IsCancellationRequested && late.Retired == 1 && QueueCount(disposed) == 0
            && late.Accepted == 0 && late.Notes == 0 && lateOrigin.Results.IsEmpty,
            "Late success after disposal retains no queued Social target/callback and cannot accept or stop another owner");
        OnOwner(late, lateOrigin);
        Console.WriteLine($"Social connection owner acceptance passed: {checks} assertions; fake admission/account services only.");
    }

    private static T WithAccount<T>(T value, object lease) where T : class
    {
        // Populate the existing opaque witness as fixture data; never use a public ID
        // as a substitute for the production account-lifetime reference.
        typeof(T).GetProperty("AccountWitness", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(value, lease);
        return value;
    }

    private static void HomeProjection(Action<bool, string> check)
    {
        string Id(int value) => $"PP-AAAA-BBBB-CCCC-DDDD-{value:X4}";
        var now = new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
        object lease = new();
        string lobby = Guid.NewGuid().ToString();
        NativePresencePerson Joinable(int id, string name, DateTimeOffset seen)
            => new(Id(id), name, "lobby", "MP1 SANCTORUS", lobby, true, seen);
        var friends = WithAccount(new NativeSocialDirectory(Id(1), new[]
        {
            new NativeSocialPerson(Id(2), "Fresh friend", NativeSocialRelation.Friend),
            new NativeSocialPerson(Id(3), "Exact-expiry friend", NativeSocialRelation.Friend),
            new NativeSocialPerson(Id(4), "Private friend", NativeSocialRelation.Friend),
            new NativeSocialPerson(Id(5), "One-tick-fresh friend", NativeSocialRelation.Friend),
            new NativeSocialPerson(Id(6), "Future friend", NativeSocialRelation.Friend),
            new NativeSocialPerson(Id(7), "Not a friend", NativeSocialRelation.Player),
            new NativeSocialPerson(Id(8), "Blocked player", NativeSocialRelation.Blocked)
        }), lease);
        var presence = WithAccount(new NativePresenceDirectory(Id(1), new("friends", "friends", "friends", false), new[]
        {
            Joinable(2, "Fresh friend", now),
            Joinable(3, "Exact-expiry friend", now.AddSeconds(-45)),
            Joinable(5, "One-tick-fresh friend", now.AddSeconds(-45).AddTicks(1)),
            Joinable(6, "Future friend", now.AddTicks(1)),
            Joinable(7, "Not a friend", now),
            Joinable(8, "Blocked player", now)
        }), lease);
        check(NativePresenceController.ValidDirectory(presence),
            "Home freshness fixture uses valid server-shaped lobby presence, including exact time boundaries");
        var projected = RmlUiSocialHomeState.Project(friends, presence, 0, now);
        bool JoinableName(string name) => projected.FriendJoinable[Array.IndexOf(projected.Friends, name)];
        string Activity(string name) => projected.FriendActivities[Array.IndexOf(projected.Friends, name)];
        check(projected.Connected && projected.PresenceVerified && projected.SharingPresence == 2
            && JoinableName("Fresh friend") && JoinableName("One-tick-fresh friend"),
            "Home exposes joinable friend hints only from the same account's currently fresh presence");
        check(!JoinableName("Exact-expiry friend") && !JoinableName("Future friend")
            && Activity("Exact-expiry friend") == "PRESENCE NOT SHARED"
            && Activity("Future friend") == "PRESENCE NOT SHARED",
            "Home rejects presence at exactly 45 seconds and before its LastSeen time without rounded grace");
        check(projected.Friends.Length == 5 && !projected.Friends.Contains("Not a friend")
            && !projected.Friends.Contains("Blocked player") && !JoinableName("Private friend")
            && Activity("Private friend") == "PRESENCE NOT SHARED",
            "Unshared friends remain private, while nonfriends and blocked players never become Home join targets");

        var oneFriend = WithAccount(new NativeSocialDirectory(Id(1), new[]
            { new NativeSocialPerson(Id(2), "Fresh friend", NativeSocialRelation.Friend) }), lease);
        var onePresence = presence with { People = new[] { presence.People[0] } };
        var fresh = RmlUiSocialHomeState.Project(oneFriend, onePresence, 0, now);
        var justBeforeExpiry = RmlUiSocialHomeState.Project(oneFriend, onePresence, 0, now.AddSeconds(45).AddTicks(-1));
        var exactExpiry = RmlUiSocialHomeState.Project(oneFriend, onePresence, 0, now.AddSeconds(45));
        check(justBeforeExpiry.FriendJoinable.Single() && justBeforeExpiry.SharingPresence == 1
            && !exactExpiry.FriendJoinable.Single() && exactExpiry.SharingPresence == 0,
            "The same Home friend hint expires at the exact source-owned 45-second boundary");
        var replacedLease = RmlUiSocialHomeState.Project(oneFriend,
            WithAccount(onePresence with { People = onePresence.People.ToArray() }, new object()), 0, now);
        check(oneFriend.SelfPrimeId == onePresence.SelfPrimeId && !replacedLease.PresenceVerified
            && replacedLease.SharingPresence == 0 && !replacedLease.FriendJoinable.Single()
            && replacedLease.FriendActivities.Single() == "PRESENCE UNAVAILABLE",
            "A same-Prime-ID presence snapshot from another opaque account lifetime cannot populate Home activity or joinability");

        var service = new Stub { SocialOverride = fresh };
        using var controller = new RmlUiMenuController(service);
        static string Text(RmlUiMenuSnapshot view, string key)
            => view.Bindings.TryGetValue(key, out var value) ? value.Text : "";
        static bool Flag(RmlUiMenuSnapshot view, string key)
            => view.Bindings.TryGetValue(key, out var value) && value.Kind == RmlUiBindingKind.Boolean && value.Boolean;
        RmlUiMenuSnapshot NextHome(RmlUiSocialHomeState state)
        {
            long revision = controller.Snapshot.Revision;
            service.SocialOverride = state;
            // Honor the actual owner's 8 Hz polling boundary instead of assuming
            // a fake service change invalidates its presentation snapshot.
            for (int i = 0; i < 200; i++)
            {
                var next = controller.Tick();
                if (next.Revision != revision) return next;
                Thread.Sleep(5);
            }
            throw new TimeoutException("Home did not project a changed Social service snapshot.");
        }
        var home = controller.Tick();
        check(Text(home, "home_friends_online") == "1" && Flag(home, "home_friend0_joinable")
            && Text(home, "home_friend0_name") == "Fresh friend"
            && Text(home, "home_friend0_status") == "JOINABLE // OPEN SOCIAL",
            "The actual menu owner binds a fresh joinable hint to Open Social, without admitting a network session");
        check(Text(home, "home_invites_count") == "NOT SYNCED" && !Flag(home, "home_social_alert_visible"),
            "Unknown invitations remain not synced and cannot fabricate an invitation count or alert");
        home = NextHome(fresh with { InvitationsVerified = true, IncomingInvitations = 0 });
        check(Text(home, "home_invites_count") == "0" && !Flag(home, "home_social_alert_visible"),
            "A verified empty invitation snapshot projects an actual zero without an alert");
        home = NextHome(fresh with { InvitationsVerified = true, IncomingInvitations = 3 });
        check(Text(home, "home_invites_count") == "3" && Flag(home, "home_social_alert_visible")
            && Text(home, "home_social_alert_text") == "PENDING GAME INVITATIONS",
            "Verified incoming invitations populate the native Home count and invitation alert");
        home = NextHome(fresh with { InvitationsVerified = false, IncomingInvitations = 3 });
        check(Text(home, "home_invites_count") == "NOT SYNCED" && !Flag(home, "home_social_alert_visible"),
            "An unverified stale invite count cannot retain a native Home invitation alert");
        home = NextHome(service.SocialOverride with { IncomingRequests = 2 });
        check(Text(home, "home_requests_count") == "2" && Text(home, "home_invites_count") == "NOT SYNCED"
            && Flag(home, "home_social_alert_visible")
            && Text(home, "home_social_alert_text") == "PENDING FRIEND REQUESTS",
            "Unverified invitations cannot override a verified friend-request alert");
        home = NextHome(fresh with { Connected = false, InvitationsVerified = true, IncomingInvitations = 3 });
        check(Flag(home, "home_social_rail_visible") && Flag(home, "home_social_empty_visible") && !Flag(home, "home_social_alert_visible")
            && !Flag(home, "home_friend0_joinable") && Text(home, "home_invites_count") == "NOT SYNCED",
            "A disconnected Home retains Friends panel while clearing prior invitation counts, alerts and join hints");
    }

    private static CancellationTokenSource Work(RmlUiMenuController controller)
        => (CancellationTokenSource)typeof(RmlUiMenuController).GetField("_work", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controller)!;
    private static int QueueCount(RmlUiMenuController controller)
    {
        object queue = typeof(RmlUiMenuController).GetField("_completed", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controller)!;
        return (int)queue.GetType().GetProperty("Count")!.GetValue(queue)!;
    }
    private static void WaitRetired(CancellationTokenSource work)
    {
        // Run's finally disposes this exact CTS after publication. This avoids timing guesses
        // when observing a deliberately late completion after cancel or controller disposal.
        for (int i = 0; i < 400; i++)
        {
            try { _ = work.Token; }
            catch (ObjectDisposedException) { return; }
            Thread.Sleep(5);
        }
        throw new TimeoutException("Social worker did not retire its exact CTS after publication.");
    }
    private static void CompleteOnWorker(Attempt attempt, RmlUiJoinResult result, CancellationTokenSource work)
    { Task.Run(() => attempt.Pending.SetResult(result)).GetAwaiter().GetResult(); WaitRetired(work); }

    private sealed class Origin(ConcurrentQueue<string> trace)
    {
        internal bool Current = true;
        internal readonly ConcurrentQueue<int> Threads = new();
        internal readonly ConcurrentQueue<(bool Accepted, string Message)> Results = new();
        internal bool Alive() { Threads.Enqueue(Environment.CurrentManagedThreadId); return Current; }
        internal void Completed(bool accepted, string message)
        { Threads.Enqueue(Environment.CurrentManagedThreadId); Results.Enqueue((accepted, message)); trace.Enqueue("callback"); }
    }
    private sealed class Attempt(NativeSocialJoinTarget target, string name, CancellationToken token, object lease)
    {
        internal readonly NativeSocialJoinTarget Target = target;
        internal readonly string Name = name;
        internal readonly CancellationToken Token = token;
        internal readonly object AccountLease = lease;
        internal readonly TaskCompletionSource<RmlUiJoinResult> Pending = new();
        internal LaunchPlan Plan;
        internal bool Ready, Stopped;
    }
    private sealed class Services : IRmlUiMenuServices
    {
        internal readonly Stub Inner = new();
        internal readonly List<Attempt> Attempts = new();
        internal readonly ConcurrentQueue<string> Trace = new();
        internal readonly ConcurrentQueue<int> CallThreads = new();
        internal object CurrentAccountLease = new();
        internal Attempt? Candidate;
        internal bool AcceptResult = true;
        internal int Joins, Accepted, Notes, DiscardCalls, Cancellations, Retired;
        private void Call(string? trace = null)
        { CallThreads.Enqueue(Environment.CurrentManagedThreadId); if (trace != null) Trace.Enqueue(trace); }
        public Task<RmlUiJoinResult> JoinSocial(NativeSocialJoinTarget target, string name, CancellationToken token)
        {
            Call(); Joins++; var attempt = new Attempt(target, name, token, CurrentAccountLease);
            Attempts.Add(attempt); Candidate = attempt; return attempt.Pending.Task;
        }
        internal Attempt Prepare(LaunchPlan plan)
        { var attempt = Attempts.Last(); attempt.Plan = plan; attempt.Ready = true; return attempt; }
        public bool CanAcceptSocialJoin(NativeSocialJoinTarget target, LaunchPlan plan)
        {
            Call("validate"); return Candidate is { Ready: true, Stopped: false } candidate
                && ReferenceEquals(candidate.Target, target) && ReferenceEquals(candidate.AccountLease, CurrentAccountLease)
                && candidate.Plan.Equals(plan) && plan.Lobby != null && ReferenceEquals(candidate.Plan.Lobby, plan.Lobby)
                && !candidate.Token.IsCancellationRequested;
        }
        public bool AcceptJoin(LaunchPlan plan) { Call("accept"); if (AcceptResult) Accepted++; return AcceptResult; }
        public void NoteSocialJoined(NativeSocialJoinTarget target)
        { Call("joined"); Notes++; if (Candidate is { } candidate && ReferenceEquals(candidate.Target, target)) Candidate = null; }
        public void DiscardSocialJoin(NativeSocialJoinTarget target, LaunchPlan plan)
        {
            Call("discard"); DiscardCalls++;
            if (Candidate is not { Ready: true } candidate || !ReferenceEquals(candidate.Target, target)
                || plan.Lobby == null || !ReferenceEquals(candidate.Plan.Lobby, plan.Lobby)) return;
            Stop(candidate); Candidate = null;
        }
        public void CancelSocialJoin()
        { Call(); Cancellations++; if (Candidate is { } candidate) { Stop(candidate); Candidate = null; } }
        private void Stop(Attempt candidate) { if (!candidate.Stopped) { candidate.Stopped = true; Retired++; } }
        public HubSnapshot Hub() => Inner.Hub();
        public string BuildVersion() => Inner.BuildVersion();
        public RmlUiLobbyView Lobby() => Inner.Lobby();
        public Task<RmlUiDirectory> Browse(CancellationToken token) => Inner.Browse(token);
        public Task<RmlUiServerCandidate?> QuickMatch(CancellationToken token) => Inner.QuickMatch(token);
        public Task<RmlUiJoinResult> Join(string endpoint, string name, CancellationToken token) => Inner.Join(endpoint, name, token);
        public Task<RmlUiJoinResult> CreateLobby(LobbyCreationRequest request, CancellationToken token) => Inner.CreateLobby(request, token);
        public bool OpenCreateLobby() => Inner.OpenCreateLobby();
        public bool NavigateTo(RmlUiRouteArgument route) => Inner.NavigateTo(route);
        public bool OpenStudio() => Inner.OpenStudio();
        public bool SetReady() => Inner.SetReady();
        public bool StartMatch() => Inner.StartMatch();
        public bool LeaveLobby() => Inner.LeaveLobby();
        public bool NextHunter() => Inner.NextHunter();
        public bool NextSuit() => Inner.NextSuit();
        public bool OpenLobbyDetails() => Inner.OpenLobbyDetails();
        public RmlUiRuleDraft? ReadRules() => Inner.ReadRules();
        public IReadOnlyList<string> BuiltInArenaKeys() => Inner.BuiltInArenaKeys();
        public bool SubmitRules(RmlUiRuleDraft draft, out string error) => Inner.SubmitRules(draft, out error);
        public void Quit() => Inner.Quit();
    }
}
