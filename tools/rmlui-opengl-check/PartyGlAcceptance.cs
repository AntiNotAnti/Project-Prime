using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MphRead;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Social;
using MphRead.Mods.Launcher.RmlUi.Render;
using MphRead.Mods.Render;
using MphRead.Mods.Network;
using OpenTK.Graphics.OpenGL;

internal static class PartyGlAcceptance
{
    internal static string Id(int n) => $"PP-AAAA-BBBB-CCCC-DDDD-{n:X4}";
    internal static string Uuid(int n) => $"{n:X8}-1111-4111-8111-111111111111";
    internal static NativePartySnapshot Empty(long revision = 1) => new(Id(1), "", "", revision,
        Array.Empty<NativePartyMember>(), Array.Empty<NativePartyInvite>(), Array.Empty<NativePartyInvite>());
    private static NativePartySnapshot Members(long revision = 10) => new(Id(1), Uuid(1), Id(1), revision,
        new[] { new NativePartyMember(Id(1), "SYNTHETIC LEADER", NativePresence.Unknown),
            new NativePartyMember(Id(2), "SYNTHETIC MEMBER", NativePresence.Unknown) },
        Enumerable.Range(0, 18).Select(index => new NativePartyInvite(Uuid(100 + index), Uuid(200 + index),
            Id(20 + index), Id(1), DateTimeOffset.UtcNow.AddMinutes(10))).ToArray(),
        new[] { new NativePartyInvite(Uuid(300), Uuid(1), Id(1), Id(3), DateTimeOffset.UtcNow.AddMinutes(10)) });

    internal static void Run(RmlUiHost host, int width, int height)
    {
        PartyAdapterAcceptance.Run();
        string output = Path.GetFullPath(Path.Combine("artifacts", "rmlui-party-rml45"));
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "README.txt"),
            "Actual native RmlUi OpenGL framebuffer captures with a deterministic SYNTHETIC INativePartyAuthority. No Supabase request, account, friend, live Party or gameplay session is created or changed. A socket-free transport with explicitly injected accepted-client session/control state exercises leader UI guards only; actual playback and unadmitted roles are rejected first. This establishes injected native presentation and pointer routing, not real group admission.\n");
        var authority = new FixtureAuthority();
        using var pages = new RmlUiPageManager(host);
        using var page = new NativePartyPage(host, pages, authority);
        host.Input.SetFramebufferScale(1, 1);
        page.Open();
        RmlUiDocumentToken document = page.Document;
        Ready();
        Check(host.FocusDocument(document, "party_invite"), "verified empty party enables first invitation");
        Capture("empty");
        host.SetField(document, "party_invite_id", Id(2).ToLowerInvariant());
        Click("party_invite", 33);
        Ready();
        var first = authority.Commands.Last();
        Check(first.Action == NativePartyAction.Invite && first.PartyId == "" && first.ExpectedRevision == 1
            && first.ExpectedSelfPrimeId == Id(1) && first.TargetPrimeId == Id(2),
            "native first invite sends exact verified empty-party self/revision witness to fake authority");
        Capture("first-invite");
        Click("party_outgoing", 4);
        Click("party_row_0", 13);
        Click("party_command_36", 36);
        Ready();
        Check(authority.Commands.Last().Action == NativePartyAction.CancelInvite
            && authority.Commands.Last().PartyId == Uuid(1), "native outgoing cancellation carries its exact current party");

        authority.Set(Members());
        Click("party_refresh", 7); Ready();
        Click("party_incoming", 3);
        Capture("incoming");
        Click("party_next", 12);
        Click("party_row_0", 13);
        Click("party_command_35", 35); Ready();
        Check(authority.Commands.Last().Action == NativePartyAction.DeclineInvite
            && authority.Commands.Last().InviteId == Uuid(108) && authority.Commands.Last().PartyId == Uuid(208),
            "native paged selection declines the invitation's party rather than current membership party");
        Click("party_previous", 11);
        Click("party_row_0", 13);
        Click("party_command_34", 34); Ready();
        Check(authority.Commands.Last().Action == NativePartyAction.AcceptInvite
            && authority.Commands.Last().InviteId == Uuid(100) && authority.Commands.Last().PartyId == Uuid(200),
            "native incoming accept captures exact selected invitation identity");

        authority.Set(Members(30));
        Click("party_refresh", 7); Ready();
        Click("party_members", 0);
        Click("party_row_1", 14);
        int commandsBefore = authority.Commands.Count;
        Click("party_command_39", 39);
        RmlUiDocumentToken retiredModal = pages.Top;
        Check(retiredModal != document && authority.Commands.Count == commandsBefore,
            "native member removal waits for foreground confirmation");
        Check(!page.Handle(new(RmlUiIntentKind.SocialAction, 46, document, ulong.MaxValue))
            && !page.Handle(new(RmlUiIntentKind.SocialAction, 46, host.HomeDocument, ulong.MaxValue)),
            "background and wrong documents cannot answer Party confirmation");
        Capture("remove-confirmation", atOrigin: false);
        Click("social_confirm_cancel", 47);
        Check(!host.IsAlive(retiredModal) && authority.Commands.Count == commandsBefore,
            "native confirmation cancellation retires modal without issuing fake command");
        Check(!page.Handle(new(RmlUiIntentKind.SocialAction, 46, retiredModal, ulong.MaxValue)),
            "retired native confirmation cannot execute member removal");
        Click("party_command_40", 40);
        Click("social_confirm_apply", 46); Ready();
        Check(authority.Commands.Last().Action == NativePartyAction.Promote
            && authority.Commands.Last().TargetPrimeId == Id(2) && authority.Commands.Last().ExpectedSelfPrimeId == Id(1),
            "confirmed native leadership transfer sends exact captured fake member and account");
        Check(!host.FocusDocument(document, "party_command_38"), "follower readback disables Disband");
        Capture("follower");
        Click("party_command_37", 37);
        Click("social_confirm_apply", 46); Ready();
        Check(authority.Commands.Last().Action == NativePartyAction.Leave && authority.Snapshot.PartyId == "",
            "confirmed native Leave waits for authoritative empty readback");

        authority.Set(Members(50));
        Click("party_refresh", 7); Ready();
        Click("party_command_38", 38);
        Click("social_confirm_apply", 46); Ready();
        Check(authority.Commands.Last().Action == NativePartyAction.Disband && authority.Snapshot.PartyId == "",
            "confirmed native Disband reaches fake authority once");
        authority.Set(Members(60));
        Click("party_refresh", 7); Ready();
        authority.HoldReads = true;
        Click("party_refresh", 7);
        Wait(() => host.FocusDocument(document, "party_cancel"), "pending native refresh enables cancellation");
        Capture("pending-at-actions", atOrigin: false);
        Click("party_cancel", 8);
        Check(!host.FocusDocument(document, "party_command_37"), "cancelled native read clears membership commands");
        authority.HoldReads = false;
        while (authority.Pending.TryDequeue(out var completion)) completion.TrySetResult(Members(999));
        Click("party_refresh", 7); Ready();
        Capture("verified-members");

        var travel = new NativePartyTravel(Uuid(800), 3, Id(2), Uuid(801), "SYNTHETIC ROOM",
            "SYNTHETIC PARTY LOBBY", 100, DateTimeOffset.UtcNow.AddSeconds(90), false, "pending");
        authority.Set(Members(75) with { LeaderPrimeId = Id(2), Travel = travel });
        Click("party_refresh", 7); Ready();
        Check(!host.FocusDocument(document, "party_publish"), "native Publish remains disabled without actual current leader lobby");
        Click("party_follow", 42); Ready();
        Check(authority.Travels.Last() is { Action: NativePartyTravelAction.Follow, ExpectedRevision: 75,
            TravelRevision: 3 } sentTravel && sentTravel.ExpectedSelfPrimeId == Id(1)
            && sentTravel.PartyId == Uuid(1) && sentTravel.TravelId == Uuid(800),
            "native Follow sends exact verified account/party/travel witnesses");
        Check(page.TryTakeJoin(out var target) && target.SelfPrimeId == Id(1) && target.PartyRevision == 75
            && target.TravelId == Uuid(800) && target.AuthorityEpoch == 100 && target.RoomKey == "SYNTHETIC ROOM",
            "native Follow emits one captured locator without starting a session");
        Check(!page.TryTakeJoin(out _), "native travel locator drains once");
        page.ReportJoinRefused("SYNTHETIC OWNER REFUSAL // NO SESSION WAS CREATED");
        Capture("travel-follow-refused", atOrigin: false);
        Click("party_decline_travel", 44); Ready();
        Check(authority.Travels.Last().Action == NativePartyTravelAction.Decline
            && authority.Snapshot.Travel?.SelfStatus == "declined" && !page.TryTakeJoin(out _),
            "native Decline waits for authoritative response and emits no join locator");
        Click("party_join_leader", 43); Ready();
        Check(authority.Travels.Last().Action == NativePartyTravelAction.JoinLeader && page.TryTakeJoin(out _)
            && authority.Snapshot.Travel?.SelfStatus == "following",
            "native Join Leader emits locator while retaining Following rather than claiming admission");
        Capture("travel-following", atOrigin: false);
        authority.Set(authority.Snapshot with { Travel = travel with { ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1) } });
        Click("party_refresh", 7); Ready();
        Check(!host.FocusDocument(document, "party_follow") && !host.FocusDocument(document, "party_join_leader")
            && !host.FocusDocument(document, "party_decline_travel"), "native expired travel disables all destination actions");

        object syntheticLease = new();
        try
        {
            NetSession.StartPlayback(); // actual socket-free lifecycle, no gameplay pump or admission
            typeof(NetSession).GetProperty(nameof(NetSession.ServerSession))!.SetValue(null,
                new SessionStatePacket { Policy = ServerSessionPolicy.Lobby, Phase = SessionPhase.Lobby,
                    MatchId = 42, AuthorityEpoch = 100,
                    Match = new MatchDefinition { RoomKey = "SYNTHETIC ROOM", Mode = GameMode.Battle } });
            var leaderTravel = travel with { LeaderPrimeId = Id(1), IsLeader = true,
                ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(90), SelfStatus = "pending" };
            authority.Set(Members(80) with { Travel = leaderTravel, AccountWitness = syntheticLease });
            Click("party_refresh", 7); Ready();
            Check(!host.FocusDocument(document, "party_reserve") && !host.FocusDocument(document, "party_publish"),
                "native true playback role cannot Reserve or Publish a live leader lobby");
            SetStatic("_playback", false);
            Click("party_refresh", 7); Ready();
            Check(!host.FocusDocument(document, "party_reserve") && !host.FocusDocument(document, "party_publish"),
                "native unadmitted client role cannot Reserve or Publish a live leader lobby");
            typeof(NetSession).GetProperty(nameof(NetSession.LocalSlot))!.SetValue(null, 0);
            Click("party_refresh", 7); Ready();
            Click("party_reserve", 53); Ready();
            Check(authority.Travels.Last() is { Action: NativePartyTravelAction.Reserve,
                LocalLobby.MatchId: 42, LocalLobby.AuthorityEpoch: 100 } reserve
                && reserve.ExpectedSelfPrimeId == Id(1) && reserve.ExpectedRevision == 80
                && ReferenceEquals(reserve.AccountWitness, syntheticLease),
                "native Reserve captures exact verified Party and current local lobby without a real request");
            Check(authority.Snapshot.Reservation is { Status: "pending", RequestedCount: 1 }
                && !page.TryTakeJoin(out _) && !host.FocusDocument(document, "party_reserve"),
                "backend intent alone emits no join locator and cannot renew through repeated Reserve");
            Capture("reservation-intent", atOrigin: false);

            var owned = new NetClientJoinOwnership(); owned.Capture();
            var live = new PartyReservationStatePacket(1, 77, 1, Uuid(900), Uuid(901), 80,
                leaderTravel.TravelId, (uint)leaderTravel.Revision, 42, 100,
                PartyReservationWireState.Reserved, 255, 1, 900, 0, 1, 1, 1);
            SetStatic("_partyReservationOwner", owned);
            SetStatic("_partyReservationAccountWitness", new WeakReference<object>(syntheticLease));
            SetStatic("_partyReservationState", live);
            SetStatic("_partyReservationExpires", NetSession.Clock + 15);
            Check(NetSession.CaptureNativePartyReservation(Uuid(900), syntheticLease)?.ReservedCount == 1
                && NetSession.CaptureNativePartyReservation(Uuid(900), new object()) == null,
                "actual memory-only reservation projection requires exact owned transport and account witness");
            Capture("reservation-live-injected", atOrigin: false);
            Click("party_cancel_reservation", 45); Ready();
            Check(authority.Travels.Last().Action == NativePartyTravelAction.CancelReservation
                && authority.Travels.Last().Reservation?.RequestId == Uuid(900)
                && authority.Snapshot.Reservation?.Status == "cancelled" && !page.TryTakeJoin(out _),
                "native Cancel sends exact intent and waits for authority without claiming Joined");
            Capture("reservation-cancelled", atOrigin: false);
        }
        finally { NetSession.Stop(); }

        var pendingGroup = new NativePartyReservation(Uuid(900), 1, 80, travel.TravelId, travel.Revision,
            42, travel.AuthorityEpoch, DateTimeOffset.UtcNow.AddSeconds(60), "pending", 1, false);
        authority.Set(Members(85) with { LeaderPrimeId = Id(2),
            Travel = travel with { ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(90) }, Reservation = pendingGroup });
        Click("party_refresh", 7); Ready();
        Check(!host.FocusDocument(document, "party_reserve") && !host.FocusDocument(document, "party_cancel_reservation"),
            "native follower cannot create a group or mutate the leader-owned Pending cohort");
        Click("party_follow", 42); Ready();
        Check(page.TryTakeJoin(out var groupTarget) && groupTarget.PartyRevision == 85
            && groupTarget.Reservation is { RequestId: var requestId, PartyRevision: 80 } && requestId == Uuid(900),
            "native Follow preserves distinct member account revision and captured leader group intent");
        Capture("reservation-follow-intent", atOrigin: false);

        RmlUiDocumentToken external = pages.OpenModal(new("synthetic-party-fence", "pages/social/confirm.rml", "social_confirm_cancel"));
        Frame();
        Check(!page.Handle(new(RmlUiIntentKind.SocialAction, 37, document, ulong.MaxValue)),
            "unrelated foreground modal fences background native Party commands");
        pages.CloseModal();
        Check(!host.IsAlive(external), "synthetic foreground modal retired");
        page.Dispose();
        Check(!host.IsAlive(document) && !page.Handle(new(RmlUiIntentKind.SocialAction, 7, document, ulong.MaxValue)),
            "retired native Party page rejects delayed refresh");
        Check(GL.GetError() == ErrorCode.NoError, "native Party fixture leaves no OpenGL error");
        Console.WriteLine("RML PARTY GL synthetic-only screenshots: " + output);

        void Frame()
        {
            page.Refresh(); host.Update();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GL.Viewport(0, 0, width, height); GL.Disable(EnableCap.ScissorTest);
            GL.ClearColor(0.025f, 0.05f, 0.08f, 1);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
            RmlUiOpenGlFrame.Draw(host, width, height);
        }
        void Settle(int milliseconds)
        {
            var watch = Stopwatch.StartNew();
            do { Frame(); Thread.Sleep(10); } while (watch.ElapsedMilliseconds < milliseconds);
            Frame();
        }
        void Wait(Func<bool> predicate, string label)
        {
            var watch = Stopwatch.StartNew();
            do { Frame(); if (predicate()) { Check(true, label); return; } Thread.Sleep(5); }
            while (watch.Elapsed < TimeSpan.FromSeconds(3));
            throw new TimeoutException(label);
        }
        void Ready() => Wait(() => host.FocusDocument(document, "party_refresh"), "native Party settles its verified fake authority read");
        void SetStatic(string name, object value) => typeof(NetSession).GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, value);
        void Click(string id, int argument)
        {
            Frame(); var target = pages.Top;
            Check(host.FocusDocument(target, id), "native focus reaches " + id);
            Settle(450);
            Check(host.TryGetElementBounds(target, id, out float x, out float y, out float w, out float h), "native bounds resolve " + id);
            double px = x + w / 2, py = y + h / 2;
            Check(px >= 0 && px < width && py >= 0 && py < height, "native pointer center is inside framebuffer: " + id);
            host.Input.PointerButton(0, px, py, true); host.Input.PointerButton(0, px, py, false); host.Update();
            bool received = false;
            while (host.TryTakeIntent(out var intent))
            {
                bool handled = page.Handle(intent);
                if (intent.Kind != RmlUiIntentKind.SocialAction || intent.Argument != argument) continue;
                Check(handled && intent.Document == target, "actual native pointer dispatches " + id);
                Check(!page.Handle(intent), "duplicate native sequence rejected: " + id); received = true;
            }
            if (!received) Console.WriteLine($"PARTY POINTER MISS {id} bounds={x},{y},{w},{h} hovered={host.HoveredElement()} focused={host.FocusedElement()}");
            Check(received, "native button emits expected Party action: " + id);
            Settle(180);
        }
        void Capture(string name, bool atOrigin = true)
        {
            if (atOrigin) { host.Input.PointerMoved(width / 2, height / 2); host.Input.PointerWheel(1000); Settle(450); }
            else Settle(180);
            byte[] pixels = FinalCompositeCapture.Read(width, height);
            string path = Path.Combine(output, $"synthetic-{name}-{width}x{height}.ppm");
            using (var stream = File.Create(path))
            {
                stream.Write(Encoding.ASCII.GetBytes($"P6\n# SYNTHETIC PARTY AUTHORITY - ACTUAL NATIVE GL FRAMEBUFFER\n{width} {height}\n255\n"));
                for (int row = height - 1; row >= 0; row--) stream.Write(pixels, row * width * 3, width * 3);
            }
            FinalCompositeCapture.WriteEvidence(path, width, height, "Actual native GL Party page with synthetic authority; no network/account/real Party mutation.");
        }
    }

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        Console.WriteLine("RML PARTY GL PASS " + label);
    }
    private sealed class FixtureAuthority : INativePartyTravelAuthority
    {
        private readonly object _gate = new();
        private NativePartySnapshot _snapshot = Empty();
        internal readonly ConcurrentQueue<NativePartyCommand> Commands = new();
        internal readonly ConcurrentQueue<NativePartyTravelCommand> Travels = new();
        internal readonly ConcurrentQueue<TaskCompletionSource<NativePartySnapshot>> Pending = new();
        internal volatile bool HoldReads;
        internal NativePartySnapshot Snapshot { get { lock (_gate) return _snapshot; } }
        internal void Set(NativePartySnapshot value) { lock (_gate) _snapshot = value; }
        public Task<NativePartySnapshot> ReadAsync(CancellationToken cancellation)
        {
            if (!HoldReads) return Task.FromResult(Snapshot);
            var completion = new TaskCompletionSource<NativePartySnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
            Pending.Enqueue(completion); return completion.Task.WaitAsync(cancellation);
        }
        public Task<NativePartyTravelResult> TravelAsync(NativePartyTravelCommand command, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (_snapshot.SelfPrimeId != command.ExpectedSelfPrimeId || _snapshot.PartyId != command.PartyId
                    || _snapshot.ServerRevision != command.ExpectedRevision || _snapshot.Travel is not { } travel
                    || travel.TravelId != command.TravelId || travel.Revision != command.TravelRevision)
                    throw new InvalidOperationException("Synthetic Party travel witness mismatch.");
                Travels.Enqueue(command);
                if (command.Action is NativePartyTravelAction.Reserve or NativePartyTravelAction.CancelReservation)
                {
                    _snapshot = _snapshot with { Reservation = command.Action == NativePartyTravelAction.Reserve
                        ? new NativePartyReservation(Uuid(900), 1, _snapshot.ServerRevision, travel.TravelId, travel.Revision,
                            42, travel.AuthorityEpoch, DateTimeOffset.UtcNow.AddSeconds(60), "pending", 1, false)
                        : _snapshot.Reservation! with { Status = "cancelled" } };
                    return Task.FromResult(new NativePartyTravelResult(_snapshot, null));
                }
                _snapshot = _snapshot with { Travel = travel with { SelfStatus = command.Action == NativePartyTravelAction.Decline ? "declined" : "following" } };
                NativePartyJoinTarget? target = command.Action is NativePartyTravelAction.Follow or NativePartyTravelAction.JoinLeader
                    ? new(_snapshot.SelfPrimeId, _snapshot.PartyId, _snapshot.ServerRevision, travel.TravelId,
                        travel.Revision, travel.LobbyId, "203.0.113.7", 27015, 57, travel.AuthorityEpoch,
                        travel.RoomKey, travel.ExpiresAt) { Reservation = _snapshot.Reservation,
                            AccountWitness = _snapshot.AccountWitness } : null;
                return Task.FromResult(new NativePartyTravelResult(_snapshot, target));
            }
        }
        public Task<bool> ExecuteAsync(NativePartyCommand command, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            lock (_gate)
            {
                var state = _snapshot;
                if (state.ServerRevision != command.ExpectedRevision || state.SelfPrimeId != command.ExpectedSelfPrimeId) return Task.FromResult(false);
                Commands.Enqueue(command);
                state = command.Action switch
                {
                    NativePartyAction.Invite => state with { PartyId = Uuid(1), LeaderPrimeId = Id(1),
                        Members = state.Members.Length == 0 ? new[] { new NativePartyMember(Id(1), "SYNTHETIC LEADER", NativePresence.Unknown) } : state.Members,
                        Outgoing = state.Outgoing.Append(new(Uuid(400), Uuid(1), Id(1), command.TargetPrimeId, DateTimeOffset.UtcNow.AddMinutes(10))).ToArray() },
                    NativePartyAction.CancelInvite => state with { Outgoing = state.Outgoing.Where(invite => invite.InviteId != command.InviteId).ToArray() },
                    NativePartyAction.DeclineInvite => state with { Incoming = state.Incoming.Where(invite => invite.InviteId != command.InviteId).ToArray() },
                    NativePartyAction.AcceptInvite => state with { PartyId = command.PartyId, LeaderPrimeId = state.Incoming.Single(invite => invite.InviteId == command.InviteId).FromPrimeId,
                        Members = new[] { new NativePartyMember(Id(1), "SYNTHETIC SELF", NativePresence.Unknown),
                            new NativePartyMember(state.Incoming.Single(invite => invite.InviteId == command.InviteId).FromPrimeId, "SYNTHETIC OTHER LEADER", NativePresence.Unknown) },
                        Incoming = Array.Empty<NativePartyInvite>(), Outgoing = Array.Empty<NativePartyInvite>() },
                    NativePartyAction.Promote => state with { LeaderPrimeId = command.TargetPrimeId },
                    NativePartyAction.Kick => state with { Members = state.Members.Where(member => member.PrimeId != command.TargetPrimeId).ToArray() },
                    NativePartyAction.Leave or NativePartyAction.Disband => Empty(state.ServerRevision),
                    _ => state
                };
                _snapshot = state with { ServerRevision = state.ServerRevision + 1 };
                return Task.FromResult(true);
            }
        }
    }
}
