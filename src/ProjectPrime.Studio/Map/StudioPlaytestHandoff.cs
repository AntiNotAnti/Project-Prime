using System.Diagnostics;
using ProjectPrime.Studio.Protocol;

namespace ProjectPrime.Studio.Map;

/// <summary>
/// Turns an IPC acknowledgement into a confirmed external gameplay handoff.
/// Studio owns the wait and diagnostics; the game still owns map publication and scene startup.
/// </summary>
public static class StudioPlaytestHandoff
{
    private static readonly TimeSpan DefaultAdmissionTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan DefaultSceneTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(200);

    public static async Task<StudioGameResult> WaitForAdmissionAsync(
        Func<CancellationToken, Task<StudioGameResult>> request,
        CancellationToken cancellation,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        TimeSpan limit = timeout ?? DefaultAdmissionTimeout;
        TimeSpan interval = pollInterval ?? DefaultPollInterval;
        ValidateTiming(limit, interval);
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            StudioGameResult result = await request(cancellation).ConfigureAwait(false);
            if (result.Accepted)
            {
                if (result.PlaytestId == Guid.Empty)
                    throw new IOException("Project Prime accepted the playtest without returning a session ID.");
                return result;
            }
            if (!result.Deferred)
                throw new IOException(result.Error ?? "Project Prime rejected the map playtest.");
            if (elapsed.Elapsed >= limit)
                throw new TimeoutException("Project Prime opened but did not become ready to launch the map playtest. "
                    + (result.Error ?? "Return to the front screen and try again."));
            await Task.Delay(interval, cancellation).ConfigureAwait(false);
        }
    }

    public static async Task WaitForStartedAsync(
        Func<Guid, CancellationToken, Task<StudioGameResult>> status,
        Guid playtestId,
        CancellationToken cancellation,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null)
    {
        ArgumentNullException.ThrowIfNull(status);
        if (playtestId == Guid.Empty) throw new ArgumentException("A playtest ID is required.", nameof(playtestId));
        TimeSpan limit = timeout ?? DefaultSceneTimeout;
        TimeSpan interval = pollInterval ?? DefaultPollInterval;
        ValidateTiming(limit, interval);
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            StudioGameResult result = await status(playtestId, cancellation).ConfigureAwait(false);
            if (result.PlaytestState == StudioPlaytestState.Rejected)
                throw new IOException("The game could not load the map playtest: "
                    + (result.Error ?? "The game rejected the scene startup."));
            if (result.PlaytestId != playtestId)
                throw new IOException(result.Error ?? "The game returned a different or missing playtest session ID.");
            if (result.Accepted && result.PlaytestState == StudioPlaytestState.Started)
                return;
            if (result.PlaytestState == StudioPlaytestState.Ended)
                throw new IOException("The playtest ended before a playable map scene was confirmed.");
            if (!result.Accepted || result.PlaytestState != StudioPlaytestState.Accepted)
                throw new IOException(result.Error ?? "The game returned an invalid playtest startup status.");
            if (elapsed.Elapsed >= limit)
                throw new TimeoutException("Project Prime accepted the map, but no gameplay scene started. "
                    + "Check the game for a Settings confirmation, a map-loading error or stalled prewarming.");
            await Task.Delay(interval, cancellation).ConfigureAwait(false);
        }
    }

    private static void ValidateTiming(TimeSpan timeout, TimeSpan interval)
    {
        if (timeout < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        if (interval < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval));
    }
}
