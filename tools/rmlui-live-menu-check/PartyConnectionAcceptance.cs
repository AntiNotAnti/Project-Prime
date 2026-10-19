using System.Reflection;
using MphRead;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Launcher.RmlUi.Host;

internal static class PartyConnectionAcceptance
{
    public static void Run()
    {
        int checks = 0;
        void Check(bool value, string message)
        { if (!value) throw new InvalidOperationException(message); checks++; }
        NativePartyJoinTarget Target() => new("PP-AAAA-BBBB-CCCC-DDDD-0001",
            Guid.NewGuid().ToString(), 9, Guid.NewGuid().ToString(), 2,
            Guid.NewGuid().ToString(), "127.0.0.1", 27888, 57, 42,
            "MP1 SANCTORUS", DateTimeOffset.UtcNow.AddSeconds(30));
        RmlUiJoinResult Admitted() => new(true,
            new LaunchPlan { Kind = LaunchKind.Online, PlayerName = "Pilot", Hunter = Hunter.Samus }, "");
        void Drain(RmlUiMenuController controller)
        {
            for (int i = 0; i < 150 && controller.Joining; i++)
            { controller.Tick(); if (controller.Joining) Thread.Sleep(10); }
            Check(!controller.Joining, "Party admission completed through the existing owner queue");
        }

        var invalid = new Stub();
        using (var controller = new RmlUiMenuController(invalid))
        {
            var target = Target();
            Check(!controller.ConnectParty(target with { ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1) })
                && invalid.PartyJoins == 0, "expired Party hint never starts admission");
            invalid.LobbyView = Stub.EmptyLobby with { Active = true };
            Check(!controller.ConnectParty(target) && invalid.PartyJoins == 0,
                "an active lobby cannot be silently replaced by Party travel");
            invalid.LobbyView = Stub.EmptyLobby;
            controller.Dispatch(RmlUiIntentKind.PlayBrowse);
            Check(!controller.ConnectParty(target) && invalid.PartyJoins == 0,
                "Party admission cannot replace another pending native request");
        }

        var accepted = new Stub();
        using (var controller = new RmlUiMenuController(accepted))
        {
            var target = Target();
            Check(controller.ConnectParty(target, "Pilot") && ReferenceEquals(accepted.PartyTarget, target),
                "Party admission retains the exact immutable target");
            Check(!controller.ConnectParty(target) && accepted.PartyJoins == 1,
                "duplicate Party admission cannot create a second worker");
            accepted.PartyJoinPending.SetResult(Admitted());
            Drain(controller);
            Check(accepted.PartyEvents.SequenceEqual(new[] { "validate", "accept", "joined" }),
                "Party joined acknowledgment follows validation and successful existing-owner handoff");
        }

        var changed = new Stub { PartyMayAccept = false };
        using (var controller = new RmlUiMenuController(changed))
        {
            controller.ConnectParty(Target());
            changed.PartyJoinPending.SetResult(Admitted());
            Drain(controller);
            Check(changed.Accepted == 0 && changed.PartyDiscards == 1
                && !changed.PartyEvents.Contains("joined"),
                "changed account or destination discards the exact candidate before handoff");
        }

        var refused = new Stub { AcceptJoinResult = false };
        using (var controller = new RmlUiMenuController(refused))
        {
            controller.ConnectParty(Target());
            refused.PartyJoinPending.SetResult(Admitted());
            Drain(controller);
            Check(refused.PartyEvents.SequenceEqual(new[] { "validate", "accept", "discard" }),
                "refused owner handoff never reports Party joined");
        }

        var cancelled = new Stub();
        using (var controller = new RmlUiMenuController(cancelled))
        {
            controller.ConnectParty(Target());
            int before = cancelled.PartyCancellations;
            controller.Dispatch(RmlUiIntentKind.PlayCancel);
            Check(cancelled.PartyJoinToken.IsCancellationRequested && cancelled.PartyCancellations == before + 1,
                "cancel retires the registered candidate and cancels its work");
            cancelled.PartyJoinPending.SetResult(Admitted());
            Drain(controller);
            Check(cancelled.Accepted == 0 && cancelled.PartyDiscards == 1,
                "late successful admission after cancel cannot enter the lobby");
        }

        var retired = new Stub();
        var disposed = new RmlUiMenuController(retired);
        disposed.ConnectParty(Target());
        int beforeDispose = retired.PartyCancellations;
        disposed.Dispose();
        retired.PartyJoinPending.SetResult(Admitted());
        var queue = typeof(RmlUiMenuController).GetField("_completed", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(disposed)!;
        Check(retired.PartyCancellations == beforeDispose + 1 && retired.PartyJoinToken.IsCancellationRequested,
            "disposal retires the registered connection owner");
        Check((int)queue.GetType().GetProperty("Count")!.GetValue(queue)! == 0 && retired.Accepted == 0,
            "late disposed completion retains no private Party target or accepted result");
        Console.WriteLine($"Party connection owner acceptance passed: {checks} assertions.");
    }
}
