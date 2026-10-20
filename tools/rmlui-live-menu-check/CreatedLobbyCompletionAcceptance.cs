using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Launcher.RmlUi.Host;

// Acceptance fixture for the four created-lobby lifecycle hooks. This checks the
// production menu owner with held committed-allocation results. It does not
// prove actual NetClientJoinOwnership/NetLaunch transport behavior by itself.
internal static class CreatedLobbyCompletionAcceptance
{
    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException(label);
        Console.WriteLine("CREATED LOBBY PASS " + label);
    }
    private static RmlUiJoinResult Admitted(string endpoint) => new(true,
        new LaunchPlan { Kind = LaunchKind.Host, PlayerName = "Pilot", Hunter = Hunter.Samus,
            Port = 27888, Lobby = new LobbyContext(endpoint, endpoint) }, "");
    private static void Open(RmlUiMenuController controller)
    {
        Check(controller.Dispatch(RmlUiIntentKind.PlayCreateOpen), "create form opens");
        Check(controller.Dispatch(RmlUiIntentKind.PlayCreate, playerName: "Pilot", lobbyName: "Fixture"),
            "one committed allocation begins");
    }
    private static void Pump(RmlUiMenuController controller, Func<bool> done)
    {
        for (int i = 0; i < 200; i++)
        {
            controller.Tick();
            if (done()) return;
            Thread.Sleep(1);
        }
        throw new TimeoutException("Created-lobby owner fixture did not settle.");
    }

    internal static void Run()
    {
        var accepted = new Stub();
        using (var controller = new RmlUiMenuController(accepted))
        {
            Open(controller);
            accepted.CreateEvents.Clear();
            accepted.CreatePending.SetResult(Admitted("accepted:27888"));
            Pump(controller, () => !controller.Creating);
            Check(accepted.CreateEvents.SequenceEqual(new[] { "validate", "accept", "note" })
                && accepted.CreatedNotes == 1 && accepted.CreatedDiscards == 0,
                "exact current candidate commits preferences only after owner acceptance");
            controller.Tick();
            Check(accepted.CreatedNotes == 1, "accepted completion is consumed once");
        }

        var replaced = new Stub { CreatedMayAccept = false };
        using (var controller = new RmlUiMenuController(replaced))
        {
            Open(controller);
            replaced.CreateEvents.Clear();
            replaced.CreatePending.SetResult(Admitted("replaced:27888"));
            Pump(controller, () => !controller.Creating);
            Check(replaced.CreateEvents.SequenceEqual(new[] { "validate", "discard" })
                && replaced.Accepted == 0 && replaced.CreatedNotes == 0,
                "a replaced client rejects the old create before accepting or saving");
        }

        var refused = new Stub { AcceptJoinResult = false };
        using (var controller = new RmlUiMenuController(refused))
        {
            Open(controller);
            refused.CreateEvents.Clear();
            refused.CreatePending.SetResult(Admitted("refused:27888"));
            Pump(controller, () => !controller.Creating);
            Check(refused.CreateEvents.SequenceEqual(new[] { "validate", "accept", "discard" })
                && refused.CreatedNotes == 0,
                "failed owner handoff retires the exact candidate and never saves");
        }

        // An already-complete Task queues its success synchronously inside Begin,
        // before the owning controller has accepted the result in Tick.
        var queued = new Stub();
        queued.CreatePending.SetResult(Admitted("queued:27888"));
        var queuedController = new RmlUiMenuController(queued);
        Open(queuedController);
        int queuedCancels = queued.CreatedCancellations;
        queuedController.Dispose();
        Check(queued.Accepted == 0 && queued.CreatedNotes == 0 && queued.CreatedDiscards == 1
            && queued.CreatedCancellations == queuedCancels + 1,
            "disposal retires a queued successful create and cancels the registered candidate");

        var late = new Stub();
        var oldController = new RmlUiMenuController(late);
        Open(oldController);
        TaskCompletionSource<RmlUiJoinResult> oldAllocation = late.CreatePending;
        CancellationToken oldToken = late.CreateToken;
        oldController.Dispose();
        Check(oldToken.IsCancellationRequested, "disposal cancels the old attempt while allocation is committed");
        late.CreatePending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using (var replacement = new RmlUiMenuController(late))
        {
            Open(replacement);
            oldAllocation.SetResult(Admitted("old:27888"));
            // Releasing an uncancellable old allocation must not unlock, accept,
            // save or retire the replacement controller's pending request.
            for (int i = 0; i < 20; i++) { replacement.Tick(); Thread.Sleep(1); }
            Check(replacement.Creating && late.Accepted == 0 && late.CreatedNotes == 0,
                "late retired allocation cannot adopt or retire a replacement attempt");
            late.CreatePending.SetResult(Admitted("new:27888"));
            Pump(replacement, () => !replacement.Creating);
            Check(late.Accepted == 1 && late.CreatedNotes == 1,
                "replacement candidate remains independently admissible");
        }
        var retry = new Stub();
        using (var controller = new RmlUiMenuController(retry))
        {
            Open(controller);
            var cancelledAllocation = retry.CreatePending;
            CancellationToken cancelledToken = retry.CreateToken;
            int cancellations = retry.CreatedCancellations;
            Check(controller.Dispatch(RmlUiIntentKind.PlayCancel) && cancelledToken.IsCancellationRequested
                && !controller.Creating && !controller.Joining && !controller.Busy
                && retry.CreatedCancellations == cancellations + 1,
                "real PlayCancel retires the committed create without waiting for its allocation reply");
            retry.CreatePending = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Open(controller);
            cancelledAllocation.SetResult(Admitted("cancelled-old:27888"));
            Pump(controller, () => retry.CreatedDiscards == 1);
            Check(controller.Creating && controller.Joining && controller.Busy
                && retry.Accepted == 0 && retry.CreatedNotes == 0,
                "old success after immediate retry is discarded without unlocking the replacement create");
            retry.CreatePending.SetResult(Admitted("retry-current:27888"));
            Pump(controller, () => !controller.Creating);
            Check(retry.Accepted == 1 && retry.CreatedNotes == 1,
                "immediate retry still accepts and notes its own successful admission");
        }

        var queuedCancel = new Stub();
        queuedCancel.CreatePending.SetResult(Admitted("cancel-queued:27888"));
        using (var controller = new RmlUiMenuController(queuedCancel))
        {
            Open(controller);
            Check(controller.Dispatch(RmlUiIntentKind.PlayCancel), "PlayCancel can retire an already queued create success");
            controller.Tick();
            Check(queuedCancel.Accepted == 0 && queuedCancel.CreatedNotes == 0 && queuedCancel.CreatedDiscards == 1,
                "queued success cannot be adopted or saved after actual PlayCancel");
        }
        Console.WriteLine("Created-lobby menu lifecycle fixture passed; real session ownership remains separate acceptance.");
    }
}
