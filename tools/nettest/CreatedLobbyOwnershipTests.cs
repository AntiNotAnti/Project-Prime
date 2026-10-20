using System;
using System.IO;
using System.Diagnostics;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System.Reflection;
using System.Threading;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Network;

namespace MphRead.NetTest;

// Actual created-lobby service methods plus NetSession's playback-only transport.
// Candidate setup is reflected so allocation, HTTP, game assets and GL stay closed.
// These checks do not claim real allocator, server-process or gameplay admission.
internal static class CreatedLobbyOwnershipTests
{
    internal static void Run(Action<bool,string> check)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        Type serviceType = typeof(NetSession).Assembly.GetType(
            "MphRead.Mods.Launcher.RmlUi.Render.RmlUiLiveMenuServices", true)!;
        Type candidateType = serviceType.GetNestedType("CreatedAdmission", BindingFlags.NonPublic)!;
        FieldInfo candidateField = serviceType.GetField("_createdJoin", flags)!;
        string directory = LauncherPrefs.Directory;
        string gameRoot = GameFiles.Root;
        string temporary = Path.Combine(Path.GetTempPath(), "prime-created-lobby-owner-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        var saved = (LauncherPrefs.PlayerName, LauncherPrefs.ServerAddress, LauncherPrefs.ServerPort,
            LauncherPrefs.LastHunter, LauncherPrefs.LastKind, LauncherPrefs.LastLobbyMode,
            LauncherPrefs.LastLobbyTimeLimitSeconds, LauncherPrefs.LastLobbyGoal);
        object? service = null;
        try
        {
            LauncherPrefs.Directory = GameFiles.Root = temporary;
            CheckLocalStartGate(check);
            service = Activator.CreateInstance(serviceType, flags, null, new object?[] { null }, null)!;
            var owner = (IRmlUiMenuServices)service;
            var request = new LobbyCreationRequest("Closed fixture", "CreatedPilot", "MP1 SANCTORUS",
                GameMode.Battle, MatchFormat.Auto, Hunter.Kanden, false);
            var plan = new LaunchPlan { Kind = LaunchKind.Host, PlayerName = "CreatedPilot",
                Hunter = Hunter.Kanden, Port = 27901, Lobby = new("Closed fixture", "198.51.100.23:27901") };
            var equalPlan = new LaunchPlan { Kind = LaunchKind.Host, PlayerName = "CreatedPilot",
                Hunter = Hunter.Kanden, Port = 27901, Lobby = new("Closed fixture", "198.51.100.23:27901") };
            string path = Path.Combine(temporary, "launcher.txt");
            byte[] Bytes() => File.Exists(path) ? File.ReadAllBytes(path) : Array.Empty<byte>();
            object Seed(bool ready, CancellationToken token = default)
            {
                object candidate = Activator.CreateInstance(candidateType, flags, null, new object[] { token }, null)!;
                candidateType.GetField("Plan", flags)!.SetValue(candidate, plan);
                candidateType.GetField("Preferences", flags)!.SetValue(candidate,
                    new LobbyCreationPreferences(request, "198.51.100.23", 27901, LaunchKind.Host, 240, 12));
                candidateType.GetField("Ready", flags)!.SetValue(candidate, ready);
                var ownership = (NetClientJoinOwnership)candidateType.GetField("Ownership", flags)!.GetValue(candidate)!;
                ownership.Capture();
                candidateField.SetValue(service, candidate);
                return candidate;
            }
            CancellationTokenSource Cancellation(object candidate) =>
                (CancellationTokenSource)candidateType.GetField("Cancel", flags)!.GetValue(candidate)!;

            NetSession.StartPlayback();
            object exact = Seed(true);
            check(plan.Equals(equalPlan) && !ReferenceEquals(plan.Lobby, equalPlan.Lobby),
                "created same-value plans retain distinct attempt witnesses");
            check(owner.CanAcceptCreatedLobby(plan) && !owner.CanAcceptCreatedLobby(equalPlan),
                "actual created service requires exact Lobby reference plus current transport");
            owner.DiscardCreatedLobby(equalPlan);
            owner.NoteCreatedLobby(equalPlan);
            check(ReferenceEquals(candidateField.GetValue(service), exact) && NetSession.Active && !File.Exists(path),
                "equal stale plan cannot discard, detach or persist the current created candidate");
            owner.CanAcceptCreatedLobby(plan);
            check(!File.Exists(path), "created preferences remain deferred before accepted Note");
            owner.NoteCreatedLobby(plan);
            check(candidateField.GetValue(service) == null && NetSession.Active
                && LauncherPrefs.PlayerName == "CreatedPilot" && LauncherPrefs.ServerAddress == "198.51.100.23"
                && LauncherPrefs.ServerPort == 27901 && LauncherPrefs.LastHunter == Hunter.Kanden
                && LauncherPrefs.LastKind == (int)LaunchKind.Host && LauncherPrefs.LastLobbyMode == GameMode.Battle
                && LauncherPrefs.LastLobbyTimeLimitSeconds == 240 && LauncherPrefs.LastLobbyGoal == 12
                && File.Exists(path), "current exact Note commits detached preferences and releases candidate ownership");
            byte[] committed = Bytes();
            LauncherPrefs.PlayerName = "after-accept-sentinel";
            owner.NoteCreatedLobby(plan);
            owner.CancelCreatedLobby();
            check(LauncherPrefs.PlayerName == "after-accept-sentinel" && Bytes().SequenceEqual(committed) && NetSession.Active,
                "repeat Note and later cancellation neither rewrite preferences nor stop accepted transport");

            NetSession.StartPlayback();
            Seed(true);
            NetSession.StartPlayback();
            var replacement = NetSession.EvidenceTransport;
            check(!owner.CanAcceptCreatedLobby(plan), "actual replacement transport invalidates old created acceptance");
            owner.DiscardCreatedLobby(plan);
            check(candidateField.GetValue(service) == null && NetSession.Active
                && ReferenceEquals(NetSession.EvidenceTransport, replacement),
                "old created discard preserves actual newer transport incarnation");

            NetSession.StartPlayback();
            Seed(true);
            NetSession.StartPlayback();
            replacement = NetSession.EvidenceTransport;
            byte[] beforeStale = Bytes();
            string nameBeforeStale = LauncherPrefs.PlayerName;
            owner.NoteCreatedLobby(plan);
            check(candidateField.GetValue(service) == null && LauncherPrefs.PlayerName == nameBeforeStale
                && Bytes().SequenceEqual(beforeStale) && ReferenceEquals(NetSession.EvidenceTransport, replacement),
                "stale created Note detaches without writes or replacement transport mutation");
            owner.CancelCreatedLobby();
            check(NetSession.Active && ReferenceEquals(NetSession.EvidenceTransport, replacement),
                "cancellation after stale Note cannot take replacement ownership");

            NetSession.StartPlayback();
            Seed(true);
            NetSession.StartServerAuthority(_ => { }, () => { });
            owner.CancelCreatedLobby();
            check(NetSession.IsServer && NetSession.IsAuthority && NetSession.EvidenceTransport == null,
                "old created cancellation preserves replacement dedicated authority role");

            NetSession.StartPlayback();
            object current = Seed(true);
            CancellationToken currentToken = Cancellation(current).Token;
            owner.CancelCreatedLobby();
            check(currentToken.IsCancellationRequested && candidateField.GetValue(service) == null && !NetSession.Active,
                "current created cancellation stops only its exact owned transport");
            owner.CancelCreatedLobby();
            check(!NetSession.Active, "created cancellation is idempotent");

            NetSession.StartPlayback();
            object waiting = Seed(false);
            var waitingCancel = Cancellation(waiting);
            owner.CancelCreatedLobby();
            check(waitingCancel.IsCancellationRequested && candidateField.GetValue(service) == null && !NetSession.Active,
                "in-flight created cancellation retires owned transport while its worker retains CTS disposal");
            waitingCancel.Dispose(); // No worker exists in this deliberately seeded boundary.

            NetSession.StartPlayback();
            object disposed = Seed(true);
            CancellationToken disposedToken = Cancellation(disposed).Token;
            ((IDisposable)service).Dispose();
            service = null;
            check(disposedToken.IsCancellationRequested && !NetSession.Active,
                "actual live service Dispose retires registered creation without a menu controller");
            var afterDispose = owner.CreateLobby(request, CancellationToken.None).GetAwaiter().GetResult();
            check(!afterDispose.Joined && candidateField.GetValue(owner) == null && !NetSession.Active,
                "disposed live service rejects a late creation before allocator or client work");
            check(Bytes().SequenceEqual(beforeStale), "created cancellation and disposal never persist preferences");
        }
        finally
        {
            if (service is IDisposable disposable) disposable.Dispose();
            NetSession.Stop();
            (LauncherPrefs.PlayerName, LauncherPrefs.ServerAddress, LauncherPrefs.ServerPort,
                LauncherPrefs.LastHunter, LauncherPrefs.LastKind, LauncherPrefs.LastLobbyMode,
                LauncherPrefs.LastLobbyTimeLimitSeconds, LauncherPrefs.LastLobbyGoal) = saved;
            LauncherPrefs.Directory = directory;
            GameFiles.Root = gameRoot;
            Directory.Delete(temporary, true);
        }
    }
    private sealed class UnreadableRotation : IReadOnlyList<(string RoomKey, GameMode Mode)>
    {
        public int Count => throw new InvalidOperationException("Pre-cancelled start touched rotation/source input.");
        public (string RoomKey, GameMode Mode) this[int index] => throw new InvalidOperationException("Pre-cancelled start indexed source input.");
        public IEnumerator<(string RoomKey, GameMode Mode)> GetEnumerator() => throw new InvalidOperationException("Pre-cancelled start enumerated source input.");
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private static void CheckLocalStartGate(Action<bool,string> check)
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        Type type = typeof(LocalServer);
        var running = type.GetProperty("Running", flags)!;
        var ownerToken = type.GetProperty("OwnerToken", flags)!;
        var hostedLibrary = type.GetProperty("HostedLibrary", flags)!;
        var lastError = type.GetProperty("LastError", flags)!;
        object? savedRunning = running.GetValue(null);
        object? savedOwner = ownerToken.GetValue(null);
        object? savedLibrary = hostedLibrary.GetValue(null);
        object? savedError = lastError.GetValue(null);
        using var sentinel = new Process(); // No process handle, launch or child to kill.
        Guid sentinelOwner = Guid.NewGuid();
        Task<int>? waiting = null;
        using var entered = new ManualResetEventSlim();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try
        {
            lock (LocalServer.StartLifecycleGate)
            {
                running.SetValue(null, sentinel);
                ownerToken.SetValue(null, sentinelOwner);
                hostedLibrary.SetValue(null, "closed-fixture-library");
                waiting = Task.Run(() =>
                {
                    entered.Set();
                    return LocalServer.Start("must-not-start", new UnreadableRotation(), 2, 60, 1,
                        "closed.invalid", 1, false, cancel: cancelled.Token, lobby: true);
                });
                check(entered.Wait(TimeSpan.FromSeconds(2)), "local-start contender reaches held lifecycle gate");
                check(!waiting.Wait(50), "pre-cancelled real LocalServer.Start waits for lifecycle gate before retiring");
                check(ReferenceEquals(LocalServer.Running, sentinel) && LocalServer.OwnerToken == sentinelOwner,
                    "blocked local start cannot replace an existing child witness or owner token");
            }
            check(waiting.Wait(TimeSpan.FromSeconds(2)) && waiting.Result == -1,
                "pre-cancelled real local start returns failure after gate release without launching");
            check(ReferenceEquals(LocalServer.Running, sentinel) && LocalServer.OwnerToken == sentinelOwner
                && Equals(hostedLibrary.GetValue(null), "closed-fixture-library")
                && LocalServer.LastError == "server startup was cancelled",
                "pre-cancelled local start preserves child/token/library and never reads source rotation");
        }
        finally
        {
            // Let the cancelled waiter leave before restoring the source-free sentinels.
            if (waiting != null) try { waiting.Wait(TimeSpan.FromSeconds(2)); } catch (AggregateException) { }
            lock (LocalServer.StartLifecycleGate)
            {
                running.SetValue(null, savedRunning);
                ownerToken.SetValue(null, savedOwner);
                hostedLibrary.SetValue(null, savedLibrary);
                lastError.SetValue(null, savedError);
            }
        }
    }

}
