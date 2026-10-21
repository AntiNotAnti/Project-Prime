using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.CustomWeapons.Packages;

namespace MphRead.Mods.Network;

public static partial class DemoPlayback
{
    private static readonly object CustomRequestGate = new();
    private static readonly SemaphoreSlim CustomPreparationGate = new(1, 1);
    private static PreparedCustomReplayArchive? _customPrepared;
    private static CancellationTokenSource? _customOpening;
    private static string? _customStore, _customOpenError;
    private static long _customRequestGeneration;
    private static int _playerThread;
    internal static bool IsCustomPlayback => Session.Metadata?.CustomWeapons != null;
    internal static int CustomSubjectSlot { get; private set; }

    private static bool TryJoinCustomArchive(string path, out bool opened, out long nativeAttempt)
    {
        opened = false; nativeAttempt = 0;
        if (Environment.GetEnvironmentVariable("PROJECT_PRIME_CUSTOM_WEAPON_PLAYBACK") != "1") return false;
        string fullPath = Path.GetFullPath(path);
        long attempt;
        lock (CustomRequestGate)
        {
            if (_customPrepared != null && _player == null && _customPrepared.Path == fullPath)
            { opened = true; return true; }
            attempt = ++_customRequestGeneration;
            nativeAttempt = attempt;
            _customOpening?.Cancel();
        }
        using (var reader = DemoReader.Open(fullPath, out _))
            if (reader?.Metadata?.CustomWeapons == null)
            {
                lock (CustomRequestGate) return attempt != _customRequestGeneration;
            }
        // Join is also a launcher worker preflight. It must never dispose a GL
        // owner from that worker; normal navigation closes playback first.
        string store = Path.GetFullPath(Environment.GetEnvironmentVariable("PROJECT_PRIME_WEAPON_STORE")
            is { Length: > 0 } configured ? configured : Path.Combine(Platform.AppPaths.UserDataDirectory, "weapons"));
        CancellationTokenSource cancellation;
        long generation;
        lock (CustomRequestGate)
        {
            if (attempt != _customRequestGeneration) return true;
            if (_player != null || _lab != null)
            { _customOpenError = "Close the current replay before opening a custom archive."; return true; }
            Stop();
            generation = ++_customRequestGeneration;
            cancellation = _customOpening = new();
            _prepared = new(new PassiveReplaySessionHost());
        }
        CancellationToken token = cancellation.Token;
        PreparedCustomReplayArchive? prepared = null;
        try
        {
            prepared = Task.Run(async () =>
            {
                await CustomPreparationGate.WaitAsync(token).ConfigureAwait(false);
                try { return PreparedCustomReplayArchive.Prepare(fullPath, new WeaponPackageStore(store), cancellation: token); }
                finally { CustomPreparationGate.Release(); }
            }, token).GetAwaiter().GetResult();
            lock (CustomRequestGate)
            {
                if (generation != _customRequestGeneration || token.IsCancellationRequested) return true;
                var request = prepared.CreateRequestSession();
                _prepared.Dispose(); _prepared = request;
                _customPrepared = prepared; prepared = null; _customStore = store;
                CustomSubjectSlot = 0; _customOpenError = null;
                ResetPlaybackPresentation();
                opened = true;
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        {
            lock (CustomRequestGate)
                if (generation == _customRequestGeneration)
                    _customOpenError = "Cannot open custom replay with its exact local package: " + error.Message;
        }
        finally
        {
            prepared?.Dispose();
            lock (CustomRequestGate) if (ReferenceEquals(_customOpening, cancellation)) _customOpening = null;
            cancellation.Dispose();
        }
        return true;
    }

    private static void RetireCustomArchiveRequest()
    {
        lock (CustomRequestGate)
        {
            _customRequestGeneration++;
            _customOpening?.Cancel(); _customOpening = null;
            _customPrepared?.Dispose(); _customPrepared = null;
            _customStore = null; _customOpenError = null;
        }
    }

    private static PassiveReplayPlayer AdoptCustomArchive(OpenTK.Mathematics.Vector2i size)
    {
        lock (CustomRequestGate)
        {
            var prepared = _customPrepared ?? throw new InvalidOperationException("Custom replay preparation was retired.");
            try { return PassiveReplayPlayer.OpenCustomArchive(prepared, _customStore!, size); }
            finally { prepared.Dispose(); _customPrepared = null; }
        }
    }

    private static void ResetPlaybackPresentation()
    {
        Replay.ReplayInput.CancelScrub(); Replay.ReplayCamera.ClearBookmarks(); Replay.ReplayCamera.Reset();
        Replay.ReplayHud.Reset(); Replay.ReplayStudio.ResetCache(); Replay.ReplayKillMessagePresenter.Reset();
    }

    internal static bool WatchCustomSubject(int slot)
    {
        if (!IsCustomPlayback || (uint)slot >= 8) return false;
        CustomSubjectSlot = slot;
        Replay.ReplayCamera.SetMode(Replay.ReplayCameraMode.FirstPerson);
        return true;
    }

    internal static void CycleCustomSubject(int direction)
    {
        if (!IsCustomPlayback || _player == null) return;
        for (int count = 1; count <= 8; count++)
        {
            int slot = (CustomSubjectSlot + direction * count + 8) % 8;
            if (_player.Current.State.TryGetPlayer(slot, out var state) && state.Health > 0)
            { WatchCustomSubject(slot); return; }
        }
    }
}
