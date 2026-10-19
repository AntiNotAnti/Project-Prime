using MphRead;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Launcher.RmlUi.Host;

internal static class PartyQueueAcceptance
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool value, string message)
        { if (!value) throw new InvalidOperationException(message); checks++; }
        NativePartyJoinTarget Target() => new("PP-AAAA-BBBB-CCCC-DDDD-0001", Guid.NewGuid().ToString(), 9,
            Guid.NewGuid().ToString(), 2, Guid.NewGuid().ToString(), "127.0.0.1", 27888, 57, 42,
            "MP1 SANCTORUS", DateTimeOffset.UtcNow.AddSeconds(30));
        var services = new Stub();
        using var controller = new RmlUiMenuController(services);
        services.PartyQueue = new(1, 1, NativePartyQueuePhase.Verifying, "VERIFYING CURRENT PARTY", 0, 0, 0, 0, false, false);
        Check(!controller.Tick().Bindings["party_queue_open"].Boolean,
            "a retained service snapshot cannot open a queue outside its Party join");
        Check(controller.ConnectParty(Target()), "the existing join owner starts Party admission");
        Check(controller.Tick().Bindings["party_queue_open"].Boolean,
            "the same join owner publishes its real verification phase");
        Check(!controller.Dispatch(RmlUiIntentKind.PlayQueueAction, 0)
            && !controller.Dispatch(RmlUiIntentKind.PlayQueueAction, 1) && services.PartyQueueCommands.Count == 0,
            "there is no solo fallback and unreserved seats cannot be accepted");
        var reserved = services.PartyQueue with { Revision = 2, Phase = NativePartyQueuePhase.Reserved,
            Status = "SERVER HOLDS TWO PARTY SEATS", ReservedCount = 2, RemainingSeconds = 10, CanAccept = true, CanDecline = true };
        services.PartyQueue = reserved;
        var shown = controller.Tick();
        Check(shown.Bindings["party_queue_can_accept"].Boolean && services.Accepted == 0
            && !services.PartyEvents.Contains("joined"), "Reserved supplies consent controls without claiming Joined");
        services.PartyQueue = reserved with { Revision = 3 };
        Check(!controller.Dispatch(RmlUiIntentKind.PlayQueueAction, 1) && services.PartyQueueCommands.Count == 0,
            "a stale displayed snapshot cannot accept a newer reservation revision");
        controller.Tick();
        var exact = services.PartyQueue;
        Check(controller.Dispatch(RmlUiIntentKind.PlayQueueAction, 1)
            && ReferenceEquals(services.PartyQueueCommands.Single().Snapshot, exact),
            "Accept transfers the exact displayed lease to the existing attempt owner");
        Check(!controller.Dispatch(RmlUiIntentKind.PlayQueueAction, 1) && services.PartyQueueCommands.Count == 1,
            "a second queued click cannot reuse a consumed server state");
        shown = controller.Tick();
        Check(controller.Joining && !shown.Bindings["party_queue_can_accept"].Boolean
            && services.Accepted == 0 && !services.PartyEvents.Contains("joined"),
            "Admitting remains within the original pending join and awaits actual Welcome");
        Check(!controller.Dispatch(RmlUiIntentKind.PlayQueueAction, 2),
            "a committed claim cannot use an unavailable self-decline capability");
        Check(controller.Dispatch(RmlUiIntentKind.PlayQueueAction, 3),
            "Leave requests cancellation through the exact current owner");
        Check(!controller.Tick().Bindings["party_queue_open"].Boolean && controller.Joining && services.Accepted == 0,
            "a terminal queue closes its card while the existing join settles");
        services.PartyJoinPending.SetResult(new(false, default, "PARTY QUEUE CANCELLED"));
        for (int i = 0; i < 150 && controller.Joining; i++) { controller.Tick(); if (controller.Joining) Thread.Sleep(10); }
        Check(!controller.Joining && services.Accepted == 0 && !controller.Dispatch(RmlUiIntentKind.PlayQueueAction, 1),
            "cancelled queue completion cannot admit a session or reactivate controls");
        services.PartyJoinPending = new();
        services.PartyQueue = reserved with { AttemptId = 2, Revision = 1 };
        Check(controller.ConnectParty(Target()), "a fresh Party attempt can be reviewed after cancellation");
        controller.Tick();
        controller.Dispatch(RmlUiIntentKind.PlayCancel);
        Check(!controller.Tick().Bindings["party_queue_open"].Boolean
            && !controller.Dispatch(RmlUiIntentKind.PlayQueueAction, 1),
            "generic join cancellation immediately retires the queue's presentation authority");
        services.PartyJoinPending.SetResult(new(true, new LaunchPlan { Kind = LaunchKind.Online, Hunter = Hunter.Samus }, ""));
        for (int i = 0; i < 150 && controller.Joining; i++) { controller.Tick(); if (controller.Joining) Thread.Sleep(10); }
        Check(services.Accepted == 0 && services.PartyDiscards == 1,
            "late actual admission after cancellation discards only the original candidate");
        Console.WriteLine($"Party queue owner acceptance passed: {checks} assertions.");
    }
}
