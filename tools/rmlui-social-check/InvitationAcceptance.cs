using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using MphRead.Mods.Launcher;

internal static class InvitationAcceptance
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
    private static string Id(int value) => $"PP-AAAA-BBBB-CCCC-DDDD-{value:X4}";
    private static string Uuid(int value) => $"{value:x8}-1111-4111-8111-111111111111";
    private static T WithWitness<T>(T value, object witness) where T : class
    {
        typeof(T).GetProperty("AccountWitness", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(value, witness);
        return value;
    }
    private static object? Witness<T>(T value) where T : class
        => typeof(T).GetProperty("AccountWitness", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value);
    private static NativeSocialDirectory CurrentDirectory(NativeSocialFriendsController controller)
        => (NativeSocialDirectory)typeof(NativeSocialFriendsController)
            .GetField("_directory", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controller)!;
    private static NativeSocialInvitation Invite(int value, bool incoming = true, string status = "pending")
        => new(Uuid(value), Id(2), "Same Friend", incoming, Uuid(100 + value),
            "ARENA " + value, "Verified Server", status, Now.AddMinutes(-1).AddTicks(value), Now.AddMinutes(1));
    private static NativeSocialDirectory Directory(object witness, params NativeSocialInvitation[] invitations)
        => WithWitness(new NativeSocialDirectory(Id(1), new[] {
            new NativeSocialPerson(Id(2), "Same Friend", NativeSocialRelation.Friend) })
        { InvitationsAvailable = true, Invitations = invitations }, witness);
    private static NativeSocialJoinTarget Target(object witness)
        => WithWitness(new NativeSocialJoinTarget(Id(1), Uuid(101), "203.0.113.7", 27888,
            57, 42, "ARENA 1", "Verified Server", Now.AddSeconds(30)), witness);
    private static NativePresenceDirectory Presence(object witness, DateTimeOffset seen,
        bool joinable = true, string self = "", string activity = "lobby")
        => WithWitness(new NativePresenceDirectory(self.Length == 0 ? Id(1) : self,
            new("friends", "friends", "friends", false), new[] {
                new NativePresencePerson(Id(2), "Same Friend", activity, "ARENA 1",
                    activity == "lobby" ? Uuid(101) : "", joinable, seen) }), witness);
    private static NativeSocialView Wait(NativeSocialFriendsController controller,
        Func<NativeSocialView, bool> ready)
    {
        for (int i = 0; i < 400; ++i)
        {
            var state = controller.Tick();
            if (ready(state)) return state;
            Thread.Sleep(5);
        }
        throw new TimeoutException("Invitation fixture did not complete.");
    }
    private static void Drain(NativeSocialFriendsController controller)
    {
        // Cancellation-ignoring fake services exercise generation/post-await
        // fences rather than relying on a cancelled Task.WaitAsync wrapper.
        for (int i = 0; i < 20; ++i) { controller.Tick(); Thread.Sleep(2); }
    }
    private static void Load(NativeSocialFriendsController controller, Service service,
        NativeSocialDirectory directory)
    {
        controller.Refresh();
        service.TakeRead().Reply.SetResult(directory);
        Wait(controller, s => !s.Busy && s.Connected);
    }
    private static bool SelectInvite(NativeSocialFriendsController controller, string invite)
    {
        var rows = controller.Current().InvitationRows;
        int index = Array.FindIndex(rows, i => i.InviteId == invite);
        return index >= 0 && controller.Select(index);
    }

    public static void Run()
    {
        int checks = 0;
        var failures = new List<string>();
        void Check(bool value, string label)
        {
            checks++;
            if (!value) failures.Add(label);
        }
        void Case(string name, Action body)
        {
            try { body(); }
            catch (Exception error) { failures.Add(name + ": " + error.GetType().Name + " " + error.Message); }
        }

        Case("selection and server readback", () =>
        {
            var service = new Service(); var clock = new Clock();
            using var controller = new NativeSocialFriendsController(service, clock);
            var incoming = Invite(1); var outgoing = Invite(2, false);
            Load(controller, service, Directory(service.Account, incoming, outgoing));
            Check(controller.SetTab(3) && controller.Current().Total == 2,
                "incoming/outgoing invitations from one friend remain separate rows");
            Check(SelectInvite(controller, incoming.InviteId)
                && controller.Current().SelectedInvitation == incoming
                && controller.CanLobby(NativeSocialLobbyAction.Accept)
                && controller.CanLobby(NativeSocialLobbyAction.Decline)
                && !controller.CanLobby(NativeSocialLobbyAction.Cancel),
                "incoming invitation UUID owns Accept/Decline, never outgoing cancellation");
            Check(SelectInvite(controller, outgoing.InviteId)
                && controller.Current().SelectedInvitation == outgoing
                && controller.CanLobby(NativeSocialLobbyAction.Cancel)
                && !controller.CanLobby(NativeSocialLobbyAction.Accept)
                && !controller.CanLobby(NativeSocialLobbyAction.Decline),
                "selecting another UUID for the same friend cannot reuse incoming authority");
            var exactDirectory = CurrentDirectory(controller);
            Check(controller.LobbyCommand(NativeSocialLobbyAction.Cancel), "current outgoing invite starts one command");
            var call = service.TakeLobby();
            Check(ReferenceEquals(call.Command.Directory, exactDirectory)
                && ReferenceEquals(Witness(call.Command.Directory), service.Account)
                && ReferenceEquals(call.Command.Invitation, outgoing)
                && call.Command.Friend == null && call.Command.Action == NativeSocialLobbyAction.Cancel,
                "command captures the exact sanitized directory, opaque lease and selected UUID row");
            Check(controller.Current().InvitationRows.Length == 2
                && !controller.CanLobby(NativeSocialLobbyAction.Cancel)
                && !controller.LobbyCommand(NativeSocialLobbyAction.Cancel),
                "pending command neither removes invitation optimistically nor admits a duplicate action");
            call.Reply.SetResult(new(Directory(service.Account, incoming), null));
            var reread = Wait(controller, s => !s.Busy);
            Check(reread.InvitationRows.Length == 1 && reread.InvitationRows[0] == incoming
                && reread.SelectedInvitation == null,
                "only the server reread removes the outgoing invite and retires its selected UUID");
        });

        Case("status, direction and expiry", () =>
        {
            var service = new Service(); var clock = new Clock();
            using var controller = new NativeSocialFriendsController(service, clock);
            var accepted = Invite(1, status: "accepted");
            var outgoingAccepted = Invite(9, false, "accepted");
            Load(controller, service, Directory(service.Account, accepted, outgoingAccepted,
                Invite(2) with { ExpiresAt = Now }, Invite(3) with { CreatedAt = Now.AddSeconds(1) },
                Invite(4, status: "declined"), Invite(5, status: "cancelled"), Invite(6, status: "expired")));
            controller.SetTab(3);
            Check(controller.Current().Total == 2 && SelectInvite(controller, accepted.InviteId)
                && controller.CanLobby(NativeSocialLobbyAction.Accept)
                && !controller.CanLobby(NativeSocialLobbyAction.Decline),
                "accepted incoming invites permit retrying join; terminal, future-created and exactly expired rows are not actionable");
            Check(SelectInvite(controller, outgoingAccepted.InviteId)
                && !controller.CanLobby(NativeSocialLobbyAction.Accept)
                && !controller.CanLobby(NativeSocialLobbyAction.Decline)
                && !controller.CanLobby(NativeSocialLobbyAction.Cancel),
                "accepted outgoing history cannot borrow incoming join authority or pending cancellation");
            SelectInvite(controller, accepted.InviteId);
            clock.Utc = accepted.ExpiresAt;
            Check(controller.Current().Total == 0 && controller.Current().SelectedInvitation == null
                && !controller.CanLobby(NativeSocialLobbyAction.Accept)
                && !controller.LobbyCommand(NativeSocialLobbyAction.Accept) && service.Lobby.IsEmpty,
                "expiry at the exact boundary retires selected invitation authority before worker dispatch");
        });

        Case("friend command capture", () =>
        {
            var service = new Service(); using var controller = new NativeSocialFriendsController(service, new Clock());
            Load(controller, service, Directory(service.Account)); controller.Select(0);
            var directory = CurrentDirectory(controller);
            var selected = controller.Current().Selected;
            var local = new NativePartyLocalLobby(7, 42, "ARENA 1", "Verified Server");
            Check(!controller.CanLobby(NativeSocialLobbyAction.Invite)
                && controller.LobbyCommand(NativeSocialLobbyAction.Invite, local),
                "sending a game invitation requires the current local lobby context");
            var call = service.TakeLobby();
            Check(ReferenceEquals(call.Command.Directory, directory)
                && ReferenceEquals(call.Command.Friend, selected)
                && ReferenceEquals(call.Command.LocalLobby, local)
                && ReferenceEquals(Witness(call.Command.Directory), service.Account)
                && call.Command.Invitation == null,
                "friend invitation carries exact directory, selected relationship, local lobby and opaque account witness");
            call.Reply.SetResult(new(Directory(service.Account), null));
            Wait(controller, s => !s.Busy);
        });

        Case("hidden invitation selection", () =>
        {
            var service = new Service(); using var controller = new NativeSocialFriendsController(service, new Clock());
            var invitations = Enumerable.Range(1, 10).Select(i => Invite(i) with { DisplayName = "Invitation " + i }).ToArray();
            Load(controller, service, Directory(service.Account, invitations)); controller.SetTab(3);
            Check(controller.Select(0) && controller.CanLobby(NativeSocialLobbyAction.Accept), "visible invitation can be selected");
            Check(controller.Page(1) && controller.Current().SelectedInvitation == null
                && !controller.LobbyCommand(NativeSocialLobbyAction.Accept),
                "paging selected invite off screen cannot dispatch its hidden UUID");
            controller.Page(-1); controller.Select(0);
            Check(controller.Search("NO MATCH") && controller.Current().SelectedInvitation == null
                && !controller.CanLobby(NativeSocialLobbyAction.Accept)
                && !controller.LobbyCommand(NativeSocialLobbyAction.Accept) && service.Lobby.IsEmpty,
                "search hiding the selected invite retires action authority");
        });

        Case("hidden friend selection and presence", () =>
        {
            var service = new Service(); var clock = new Clock();
            using var controller = new NativeSocialFriendsController(service, clock);
            var friends = Enumerable.Range(2, 10).Select(i => new NativeSocialPerson(Id(i), "Friend " + i.ToString("00"), NativeSocialRelation.Friend)).ToArray();
            Load(controller, service, WithWitness(new NativeSocialDirectory(Id(1), friends)
                { InvitationsAvailable = true }, service.Account));
            controller.Select(0);
            var local = new NativePartyLocalLobby(7, 42, "ARENA 1", "Verified Server");
            var present = Presence(service.Account, Now);
            Check(controller.CanLobby(NativeSocialLobbyAction.Invite, local)
                && controller.CanLobby(NativeSocialLobbyAction.JoinFriend, presence: present),
                "friend actions require a visible verified relationship and fresh shared joinable presence");
            Check(!controller.CanLobby(NativeSocialLobbyAction.JoinFriend)
                && !controller.CanLobby(NativeSocialLobbyAction.JoinFriend, presence: Presence(new object(), Now))
                && !controller.CanLobby(NativeSocialLobbyAction.JoinFriend, presence: Presence(service.Account, Now, self: Id(9)))
                && !controller.CanLobby(NativeSocialLobbyAction.JoinFriend, presence: Presence(service.Account, Now.AddSeconds(-45)))
                && !controller.CanLobby(NativeSocialLobbyAction.JoinFriend, presence: Presence(service.Account, Now.AddTicks(1)))
                && !controller.CanLobby(NativeSocialLobbyAction.JoinFriend, presence: Presence(service.Account, Now, false))
                && !controller.CanLobby(NativeSocialLobbyAction.JoinFriend, presence: Presence(service.Account, Now, false, activity: "menu")),
                "missing/private/expired/future/wrong-account presence never grants Join Friend");
            Check(controller.Page(1) && !controller.CanLobby(NativeSocialLobbyAction.Invite, local)
                && !controller.CanLobby(NativeSocialLobbyAction.JoinFriend, presence: present)
                && !controller.LobbyCommand(NativeSocialLobbyAction.Invite, local),
                "paging selected friend off screen cannot dispatch a hidden invitation target");
            controller.Page(-1); controller.Select(0); controller.Search("NO MATCH");
            Check(!controller.CanLobby(NativeSocialLobbyAction.Invite, local)
                && !controller.CanLobby(NativeSocialLobbyAction.JoinFriend, presence: present)
                && !controller.LobbyCommand(NativeSocialLobbyAction.JoinFriend, presence: present)
                && service.Lobby.IsEmpty,
                "search-hidden friend cannot retain invitation or join action authority");
        });

        Case("sanitation", () =>
        {
            var service = new Service(); using var controller = new NativeSocialFriendsController(service, new Clock());
            var valid = Invite(0xabc);
            Load(controller, service, Directory(service.Account, valid,
                valid with { InviteId = valid.InviteId.ToUpperInvariant() },
                null!, Invite(2) with { InviteId = "malformed" }, Invite(3) with { LobbyId = Guid.Empty.ToString() },
                Invite(4) with { PrimeId = "not an ID" }, Invite(5) with { PrimeId = Id(1) },
                Invite(6) with { DisplayName = new string('x', 129) }, Invite(7) with { Status = "arbitrary" },
                Invite(8) with { ExpiresAt = Now.AddMinutes(-2) }));
            controller.SetTab(3);
            Check(controller.Current().Total == 1 && controller.Current().InvitationRows.Single() == valid,
                "duplicate UUID, malformed identities/shape, self invitations and invalid time ranges do not become rows");
        });

        foreach (string retirement in new[] { "cancel", "refresh", "account" })
            Case("late " + retirement, () =>
            {
                var service = new Service(); using var controller = new NativeSocialFriendsController(service, new Clock());
                var incoming = Invite(1); var oldAccount = service.Account;
                Load(controller, service, Directory(oldAccount, incoming)); controller.SetTab(3); controller.Select(0);
                Check(controller.LobbyCommand(NativeSocialLobbyAction.Accept), retirement + " fixture starts accepted invitation work");
                var call = service.TakeLobby();
                if (retirement == "cancel") controller.Cancel();
                else if (retirement == "refresh") Load(controller, service, Directory(service.Account, Invite(2)));
                else
                {
                    service.Account = new object();
                    Check(!controller.Current().Connected && controller.Current().InvitationRows.Length == 0,
                        "same Prime ID with replaced opaque lease immediately clears invitation data");
                    Load(controller, service, Directory(service.Account, Invite(2)));
                }
                Check(call.Token.IsCancellationRequested, retirement + " cancels exact pending invocation");
                call.Reply.SetResult(new(Directory(oldAccount), Target(oldAccount)));
                Drain(controller);
                Check(!controller.TryTakeJoin(out _), "late success after " + retirement + " cannot publish a join target");
                if (retirement == "cancel")
                    Check(controller.SetTab(3) && controller.Current().InvitationRows.Single() == incoming,
                        "cancelled result cannot optimistically remove the original invitation");
                else
                    Check(controller.SetTab(3) && controller.Current().InvitationRows.Single().InviteId == Uuid(2),
                        "late old result after " + retirement + " cannot replace fresh private rows");
            });

        Case("target transfer and retirement", () =>
        {
            var service = new Service(); using var controller = new NativeSocialFriendsController(service, new Clock());
            Load(controller, service, Directory(service.Account, Invite(1))); controller.SetTab(3); controller.Select(0);
            controller.LobbyCommand(NativeSocialLobbyAction.Accept);
            var target = Target(service.Account);
            service.TakeLobby().Reply.SetResult(new(Directory(service.Account, Invite(1, status: "accepted")), target));
            Wait(controller, s => !s.Busy);
            Check(controller.TryTakeJoin(out var transferred) && ReferenceEquals(transferred, target)
                && !controller.TryTakeJoin(out _), "exact leased target transfers once, without reconstructing authority from displayed endpoint text");
            controller.Select(0); controller.LobbyCommand(NativeSocialLobbyAction.Accept);
            service.TakeLobby().Reply.SetResult(new(Directory(service.Account), Target(service.Account)));
            Wait(controller, s => !s.Busy); controller.Cancel();
            Check(!controller.TryTakeJoin(out _), "cancellation also retires an already-published but unconsumed join target");
        });

        foreach (string mismatch in new[] { "directory lease", "directory self", "target lease", "target self" })
            Case("mismatched " + mismatch, () =>
            {
                var service = new Service(); using var controller = new NativeSocialFriendsController(service, new Clock());
                Load(controller, service, Directory(service.Account, Invite(1))); controller.SetTab(3); controller.Select(0);
                controller.LobbyCommand(NativeSocialLobbyAction.Accept);
                var directory = Directory(service.Account, Invite(1, status: "accepted"));
                var target = Target(service.Account);
                if (mismatch == "directory lease") directory = WithWitness(directory, new object());
                if (mismatch == "directory self") directory = directory with { SelfPrimeId = Id(9) };
                if (mismatch == "target lease") target = WithWitness(target, new object());
                if (mismatch == "target self") target = target with { SelfPrimeId = Id(9) };
                service.TakeLobby().Reply.SetResult(new(directory, target));
                var result = Wait(controller, s => !s.Busy);
                Check(!controller.TryTakeJoin(out _), "result with mismatched " + mismatch + " cannot acquire join authority");
                if (mismatch.StartsWith("directory", StringComparison.Ordinal))
                    Check(!result.Connected && result.Rows.Length == 0 && result.InvitationRows.Length == 0,
                        "wrong response directory cannot publish private rows under the originating account");
            });

        Case("target validation", () =>
        {
            var good = Target(new object());
            Check(NativeSocialInvitationValidation.ValidTarget(good, Now), "canonical IPv4, current protocol shape and unexpired target are valid");
            foreach (var bad in new[] { good with { SelfPrimeId = "bad" }, good with { LobbyId = "bad" },
                good with { Host = "localhost" }, good with { Host = "127.1" }, good with { Host = "::1" },
                good with { Host = "203.0.113.007" }, good with { Host = "203.0.113.256" },
                good with { Port = 0 }, good with { Port = 65536 }, good with { Protocol = 0 },
                good with { AuthorityEpoch = 0 }, good with { ExpiresAt = Now }, good with { RoomKey = "" } })
                Check(!NativeSocialInvitationValidation.ValidTarget(bad, Now), "malformed/incompatible/expired target rejected: " + bad);
            var epochMethod = typeof(NativeSocialInvitationValidation).GetMethod("Epoch", BindingFlags.Static | BindingFlags.NonPublic)!;
            bool Epoch(string text, out ulong parsed)
            {
                object[] args = { text, 0UL };
                bool accepted = (bool)epochMethod.Invoke(null, args)!; parsed = (ulong)args[1]; return accepted;
            }
            Check(Epoch(ulong.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture), out ulong max)
                && max == ulong.MaxValue, "authority epoch preserves exact full-width ulong identity");
            foreach (string bad in new[] { "", "0", "042", "+42", " 42", "42 ", "42.0", "4.2e1", "18446744073709551616" })
                Check(!Epoch(bad, out _), "noncanonical authority epoch rejected: " + bad);
            Check(!JsonSerializer.Serialize(good).Contains("AccountWitness", StringComparison.Ordinal),
                "opaque account witness is not serialized with a join target");
        });

        if (failures.Count != 0)
            throw new InvalidOperationException("Invitation acceptance failed (" + failures.Count + "/" + checks + "):\n - "
                + String.Join("\n - ", failures));
        Console.WriteLine($"Native Social invitation acceptance passed: {checks} assertions.");
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Utc = Now;
        public override DateTimeOffset GetUtcNow() => Utc;
    }
    private sealed class Service : INativeSocialFriendsService
    {
        public object Account = new();
        public sealed record Read(CancellationToken Token, TaskCompletionSource<NativeSocialDirectory> Reply);
        public sealed record Call(NativeSocialLobbyCommand Command, CancellationToken Token,
            TaskCompletionSource<NativeSocialLobbyResult> Reply);
        public readonly ConcurrentQueue<Read> Reads = new();
        public readonly ConcurrentQueue<Call> Lobby = new();
        public bool IsCurrent(NativeSocialDirectory directory) => ReferenceEquals(Witness(directory), Account);
        public Task<NativeSocialDirectory> ReadAsync(CancellationToken token)
        {
            var reply = new TaskCompletionSource<NativeSocialDirectory>(TaskCreationOptions.RunContinuationsAsynchronously);
            Reads.Enqueue(new(token, reply)); return reply.Task;
        }
        public Task<NativeSocialLobbyResult> ExecuteLobbyAsync(NativeSocialLobbyCommand command, CancellationToken token)
        {
            var reply = new TaskCompletionSource<NativeSocialLobbyResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            Lobby.Enqueue(new(command, token, reply)); return reply.Task;
        }
        public Task<NativeSocialPerson?> LookupAsync(string primeId, CancellationToken token)
            => throw new InvalidOperationException("Invitation test must not start an unrelated lookup.");
        public Task<NativeSocialMutation> ExecuteAsync(NativeSocialFriendAction action, string primeId, CancellationToken token)
            => throw new InvalidOperationException("Invitation test must not use an unleased friend mutation.");
        public Read TakeRead() => Take(Reads);
        public Call TakeLobby() => Take(Lobby);
        private static T Take<T>(ConcurrentQueue<T> queue)
        {
            for (int i = 0; i < 400; ++i)
            {
                if (queue.TryDequeue(out var item)) return item;
                Thread.Sleep(5);
            }
            throw new TimeoutException("Invitation fake worker did not arrive.");
        }
    }
}
