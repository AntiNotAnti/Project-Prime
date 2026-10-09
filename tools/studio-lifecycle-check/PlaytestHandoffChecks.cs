using ProjectPrime.Studio.Map;
using ProjectPrime.Studio.Protocol;

internal static partial class Program
{
    private static async Task CheckStudioPlaytestHandoffAsync()
    {
        Guid session = Guid.NewGuid();
        int admissionAttempts = 0;
        StudioGameResult accepted = await StudioPlaytestHandoff.WaitForAdmissionAsync(_ =>
        {
            admissionAttempts++;
            return Task.FromResult(admissionAttempts < 4
                ? StudioGameResult.Rejected("The game shell is still starting.", deferred: true)
                : new StudioGameResult(true, PlaytestId: session, PlaytestState: StudioPlaytestState.Accepted));
        }, CancellationToken.None, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(1));
        Check(admissionAttempts == 4 && accepted.PlaytestId == session,
            "cold game startup retries deferred admission and preserves the accepted session ID");

        int polls = 0;
        await StudioPlaytestHandoff.WaitForStartedAsync((id, _) =>
        {
            polls++;
            return Task.FromResult(new StudioGameResult(true, PlaytestId: id,
                PlaytestState: polls < 3 ? StudioPlaytestState.Accepted : StudioPlaytestState.Started));
        }, session, CancellationToken.None, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(1));
        Check(polls == 3, "accepted IPC handoff is not reported as a playable scene until Started");

        admissionAttempts = 0;
        Exception? permanent = await PlaytestFailureAsync(() => StudioPlaytestHandoff.WaitForAdmissionAsync(_ =>
        {
            admissionAttempts++;
            return Task.FromResult(StudioGameResult.Rejected("The package is invalid."));
        }, CancellationToken.None, TimeSpan.FromSeconds(1), TimeSpan.Zero));
        Check(permanent is IOException && permanent.Message.Contains("package is invalid") && admissionAttempts == 1,
            "permanent package rejection preserves its diagnostic without retry");

        Exception? missingId = await PlaytestFailureAsync(() => StudioPlaytestHandoff.WaitForAdmissionAsync(_ =>
            Task.FromResult(new StudioGameResult(true, PlaytestState: StudioPlaytestState.Accepted)),
            CancellationToken.None, TimeSpan.FromSeconds(1), TimeSpan.Zero));
        Check(missingId is IOException && missingId.Message.Contains("session ID"),
            "accepted response without a session ID never reports success");

        Exception? bootTimeout = await PlaytestFailureAsync(() => StudioPlaytestHandoff.WaitForAdmissionAsync(_ =>
            Task.FromResult(StudioGameResult.Rejected("Still starting.", deferred: true)),
            CancellationToken.None, TimeSpan.Zero, TimeSpan.Zero));
        Check(bootTimeout is TimeoutException && bootTimeout.Message.Contains("Still starting"),
            "endlessly deferred cold startup fails with a bounded actionable error");

        Exception? loadFailure = await PlaytestFailureAsync(() => StudioPlaytestHandoff.WaitForStartedAsync((id, _) =>
            Task.FromResult(new StudioGameResult(false, "Failed to prepare custom room", PlaytestId: id,
                PlaytestState: StudioPlaytestState.Rejected)),
            session, CancellationToken.None, TimeSpan.FromSeconds(1), TimeSpan.Zero));
        Check(loadFailure is IOException && loadFailure.Message.Contains("Failed to prepare custom room"),
            "game scene preparation failures surface in the editor instead of false success");

        Exception? ended = await PlaytestFailureAsync(() => StudioPlaytestHandoff.WaitForStartedAsync((id, _) =>
            Task.FromResult(new StudioGameResult(true, PlaytestId: id, PlaytestState: StudioPlaytestState.Ended)),
            session, CancellationToken.None, TimeSpan.FromSeconds(1), TimeSpan.Zero));
        Check(ended is IOException && ended.Message.Contains("ended before"),
            "playtest ending before a scene appears is not treated as started");

        Exception? stalled = await PlaytestFailureAsync(() => StudioPlaytestHandoff.WaitForStartedAsync((id, _) =>
            Task.FromResult(new StudioGameResult(true, PlaytestId: id, PlaytestState: StudioPlaytestState.Accepted)),
            session, CancellationToken.None, TimeSpan.Zero, TimeSpan.Zero));
        Check(stalled is TimeoutException && stalled.Message.Contains("no gameplay scene started"),
            "accepted but indefinitely queued gameplay has a bounded timeout");

        Exception? wrongSession = await PlaytestFailureAsync(() => StudioPlaytestHandoff.WaitForStartedAsync((_, _) =>
            Task.FromResult(new StudioGameResult(true, PlaytestId: Guid.NewGuid(), PlaytestState: StudioPlaytestState.Started)),
            session, CancellationToken.None, TimeSpan.FromSeconds(1), TimeSpan.Zero));
        Check(wrongSession is IOException && wrongSession.Message.Contains("different or missing"),
            "stale or mismatched game session cannot satisfy playtest confirmation");

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        admissionAttempts = 0;
        Exception? canceled = await PlaytestFailureAsync(() => StudioPlaytestHandoff.WaitForAdmissionAsync(_ =>
        {
            admissionAttempts++;
            return Task.FromResult(new StudioGameResult(true, PlaytestId: session));
        }, cancelled.Token, TimeSpan.FromSeconds(1), TimeSpan.Zero));
        Check(canceled is OperationCanceledException && admissionAttempts == 0,
            "cancelled playtest does not issue a late game launch request");
    }

    private static async Task<Exception?> PlaytestFailureAsync(Func<Task> work)
    {
        try { await work(); return null; }
        catch (Exception ex) { return ex; }
    }
}
