using System;
using System.Buffers.Binary;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Threading;

namespace MphRead.Mods.Network;

/// <summary>Normal lifecycle policies plus real cooperative/stuck owned subprocesses.</summary>
public static class NetworkLifecycleCheck
{
    private const string FixtureVariable = "PROJECT_PRIME_LIFECYCLE_FIXTURE";
    private const string FinalizedVariable = "PROJECT_PRIME_LIFECYCLE_FINALIZED";
    public static int Run()
    {
        if (Environment.GetEnvironmentVariable(FixtureVariable) is string fixture)
            return Child(fixture);
        string directory = Path.Combine(Path.GetTempPath(), "prime-lifecycle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        int checks = 0;
        void Require(bool condition, string message)
        { if (!condition) throw new InvalidDataException(message); checks++; }
        void Reject(Action action, string message)
        {
            try { action(); }
            catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException) { checks++; return; }
            throw new InvalidDataException(message);
        }
        try
        {
            Require(Launcher.GameFiles.CoreRootProblem(directory) != null, "Empty extraction root was accepted.");
            string model = Path.Combine(directory, "model.bin");
            File.WriteAllBytes(model, new byte[99]);
            Reject(() => ServerAssetPreflight.CheckModelFile(model), "Truncated model was accepted.");
            byte[] header = new byte[100];
            Reject(() => ServerAssetPreflight.ValidateModelHeader(header, 100), "Zero/corrupt room model was accepted.");
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(72), 1); // material count
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(74), 1); // node count
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(96), 1); // mesh count
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(16), 100); // material
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(20), 232); // display list
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(24), 264); // node
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(36), 504); // mesh
            ServerAssetPreflight.ValidateModelHeader(header, 508); checks++;
            Reject(() => ServerAssetPreflight.ValidateModelHeader(header, 507), "Out-of-file model table was accepted.");
            string rotation = Path.Combine(directory, "rotation.txt");
            foreach (string invalid in new[] { "Bogus", "99999", "None", "SinglePlayer" })
            {
                File.WriteAllText(rotation, "MP1 SANCTORUS | " + invalid);
                Reject(() => MapRotation.Load(rotation), "Invalid mode was silently defaulted: " + invalid);
            }
            foreach (string invalid in new[] { "NaN", "Infinity", "-1", "garbage", "2000" })
            {
                File.WriteAllText(rotation, "MP1 SANCTORUS | Battle | " + invalid);
                Reject(() => MapRotation.Load(rotation), "Invalid match length was accepted: " + invalid);
            }
            File.WriteAllText(rotation, "MP1 SANCTORUS | Battle | 7 | -1");
            Reject(() => MapRotation.Load(rotation), "Negative goal was accepted.");
            File.WriteAllText(rotation, "MP1 SANCTORUS\nMP3 PROVING GROUND | Battle | 0 | 0");
            Require(MapRotation.Load(rotation).Entries.Count == 2, "Valid optional/default and unlimited rotation failed.");
            var lease = new BoundedLoadingLease();
            lease.Begin(1, 10);
            Require(lease.Active(189), "Healthy long load lost its bounded lease.");
            lease.Begin(1, 180); lease.Begin(2, 181);
            Require(!lease.Active(190), "Repeated/new markers extended an unfinished load indefinitely.");
            lease.Complete(1, 190);
            Require(!lease.ProtectsResponsiveness(190), "Stale completion renewed the lease.");
            lease.Complete(2, 190);
            Require(lease.ProtectsResponsiveness(209) && !lease.ProtectsResponsiveness(210), "Post-load probe grace is not bounded.");
            lease.Complete(2, 205);
            Require(!lease.ProtectsResponsiveness(211), "Duplicate completion renewed post-load grace.");
            lease.Begin(3, 212);
            Require(lease.Active(213), "A subsequent match could not obtain a new loading lease.");
            Require(!DedicatedSceneServices.Instance.AllowsPresentationSideEffects
                && !DedicatedSceneServices.Instance.IsReplica, "Authority scene permits presentation side effects.");
            int signals = 0;
            using (var shutdown = new ShutdownSignals())
            {
                shutdown.OnShutdown(() => signals++);
                MethodInfo fire = typeof(ShutdownSignals).GetMethod("Fire", BindingFlags.NonPublic | BindingFlags.Instance)!;
                Require((bool)fire.Invoke(shutdown, null)! && !(bool)fire.Invoke(shutdown, null)! && signals == 1,
                    "Second shutdown request was claimed instead of allowing default termination.");
            }
            CheckBootstrapRefresh(Require);
            CheckChild("cooperative", directory, Require);
            CheckChild("stuck", directory, Require);
            CheckChild("eof", directory, Require);
            Console.WriteLine($"[network-lifecycle] PASS: {checks} asset/rotation/headless/lease/signal and owned subprocess assertions");
            return 0;
        }
        catch (Exception ex) { Console.WriteLine("[network-lifecycle] FAIL: " + ex); return 1; }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void CheckBootstrapRefresh(Action<bool, string> require)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(DedicatedServer);
        using var transport = new NetTransport(0, playbackOnly: true);
        var server = new DedicatedServer(0, 2, MapRotation.SingleMatch("MP1 SANCTORUS", GameMode.Battle, 0, 0))
            { SessionPolicy = ServerSessionPolicy.Continuous };
        type.GetField("_controlPlaneOnlyForTests", flags)!.SetValue(server, true);
        type.GetField("_transport", flags)!.SetValue(server, transport);
        var endpoint = new IPEndPoint(IPAddress.Loopback, 45001);
        void Deliver(string method, PacketType packetType, byte[] payload)
        {
            byte[] wire = new byte[1 + payload.Length]; wire[0] = (byte)packetType; payload.CopyTo(wire, 1);
            var packet = new ReceivedPacket(endpoint, wire, wire.Length);
            if (method == "HandleHello") type.GetMethod(method, flags)!.Invoke(server, new object[] { packet, 1d, -1 });
            else type.GetMethod(method, flags)!.Invoke(server, new object[] { packet, 1d });
        }
        byte[] hello = new byte[6]; hello[0] = NetConfig.ProtocolVersion; hello[1] = 255;
        BinaryPrimitives.WriteUInt32LittleEndian(hello.AsSpan(2), 7001);
        Deliver("HandleHello", PacketType.Hello, hello);
        var peers = (IList)type.GetField("_peers", flags)!.GetValue(server)!;
        require(peers.Count == 1, "Refresh fixture did not exercise ordinary server admission.");
        object peer = peers[0]!; var peerType = peer.GetType();
        bool Ready() => (bool)peerType.GetField("MatchReady")!.GetValue(peer)!;
        WorldBootstrapIdentity Baseline() => (WorldBootstrapIdentity)peerType.GetField("BootstrapIdentity")!.GetValue(peer)!;
        var start = (MatchStartIdentity)type.GetProperty("CurrentStartIdentity", flags)!.GetValue(server)!;
        byte[] snapshot = (byte[])type.GetField("_lastSnapshot", flags)!.GetValue(server)!;
        int length = SnapshotHeader.Size + NetMatchTimeSync.Size + NetHealthSync.HeaderSize;
        new SnapshotHeader { MatchId = start.MatchId, AuthorityEpoch = start.AuthorityEpoch, Frame = 100 }.Write(snapshot);
        snapshot.AsSpan(SnapshotHeader.Size, length - SnapshotHeader.Size).Clear();
        BinaryPrimitives.WriteUInt16LittleEndian(snapshot.AsSpan(length - NetHealthSync.HeaderSize), start.MatchId);
        type.GetField("_lastSnapshotLength", flags)!.SetValue(server, length);
        void Loaded(bool refresh = false)
        {
            byte[] bytes = new byte[MatchLoadedRolePacket.Size];
            new MatchLoadedRolePacket(start.MatchId, start.AuthorityEpoch, start.StartGeneration, false, refresh).Write(bytes);
            Deliver("HandleMatchLoaded", PacketType.MatchLoaded, bytes);
        }
        void ReadyAt(WorldBootstrapIdentity identity)
        {
            byte[] bytes = new byte[WorldBootstrapIdentity.Size]; identity.Write(bytes);
            Deliver("HandleWorldReady", PacketType.WorldReady, bytes);
        }
        Loaded(); var first = Baseline(); ReadyAt(first);
        require(Ready(), "Ordinary bootstrap did not reach WorldReady.");
        Loaded();
        require(Ready() && Baseline() == first, "An ordinary duplicate restarted a ready peer bootstrap.");
        Loaded(refresh: true); var second = Baseline();
        require(!Ready() && NetLifecycleTracker.Newer(second.Revision, first.Revision)
            && second.SlotGeneration == first.SlotGeneration && second.Start == first.Start,
            "Resume did not capture a fresh bootstrap for the same admission.");
        ReadyAt(first);
        require(!Ready(), "Stale WorldReady released a refreshed peer.");
        Loaded(refresh: true); var third = Baseline();
        require(!Ready() && NetLifecycleTracker.Newer(third.Revision, second.Revision),
            "Resume during synchronization retained the obsolete baseline.");
        ReadyAt(third);
        require(Ready(), "Current refreshed WorldReady did not restore gameplay readiness.");
        byte[] role = new byte[MatchLoadedRolePacket.Size];
        new MatchLoadedRolePacket(1, 1, 1, true, true).Write(role);
        require(MatchLoadedRolePacket.TryRead(role, out var decoded) && decoded.Spectating && decoded.RefreshBootstrap,
            "Refresh and spectator role flags did not round trip independently.");
        role[^1] = 4;
        require(!MatchLoadedRolePacket.TryRead(role, out _), "Unknown load role flag was accepted.");
        // A loaded scene may beat SessionState. Preserve exactly one refresh
        // across that deferral, then consume it when the same start is known.
        const BindingFlags clientFlags = BindingFlags.Static | BindingFlags.NonPublic;
        using var clientTransport = new NetTransport(0, playbackOnly: true);
        try
        {
            typeof(NetSession).GetField("_transport", clientFlags)!.SetValue(null, clientTransport);
            typeof(NetSession).GetField("_hostEndPoint", clientFlags)!.SetValue(null, endpoint);
            typeof(NetSession).GetProperty(nameof(NetSession.ServerMatch))!.SetValue(null,
                new MatchStatePacket { MatchId = 1, AuthorityEpoch = 1, RoomKey = "MP1 SANCTORUS", Mode = (byte)GameMode.Battle });
            typeof(NetSession).GetProperty(nameof(NetSession.ServerSession))!.SetValue(null, null);
            NetSession.MarkMatchLoaded(refreshBootstrap: true);
            require((bool)typeof(NetSession).GetField("_pendingBootstrapRefresh", clientFlags)!.GetValue(null)!,
                "A refresh was lost while awaiting SessionState.");
            typeof(NetSession).GetProperty(nameof(NetSession.ServerSession))!.SetValue(null,
                new SessionStatePacket { MatchId = 1, AuthorityEpoch = 1, StartGeneration = 1,
                    Phase = SessionPhase.InMatch, Match = new MatchDefinition { RoomKey = "MP1 SANCTORUS", Mode = GameMode.Battle } });
            NetSession.MarkMatchLoaded();
            byte[] sent = (byte[])typeof(NetSession).GetField("_scratch", clientFlags)!.GetValue(null)!;
            require(MatchLoadedRolePacket.TryRead(sent.AsSpan(0, MatchLoadedRolePacket.Size), out var deferred)
                && deferred.RefreshBootstrap
                && !(bool)typeof(NetSession).GetField("_pendingBootstrapRefresh", clientFlags)!.GetValue(null)!,
                "Deferred refresh was not sent once and consumed for its matching start.");
        }
        finally { NetSession.Stop(); }
    }

    private static void CheckChild(string fixture, string directory, Action<bool, string> require)
    {
        string executable = Environment.ProcessPath ?? throw new InvalidOperationException("No executable for child fixture.");
        var start = new ProcessStartInfo(executable);
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Assembly.GetEntryAssembly()!.Location);
        start.ArgumentList.Add("--network-lifecycle");
        start.Environment[FixtureVariable] = fixture;
        string finalized = Path.Combine(directory, fixture + ".finalized");
        start.Environment[FinalizedVariable] = finalized;
        string token = OwnedServerControl.Configure(start);
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Child fixture did not start.");
        OwnedServerControl.Attach(process, token);
        try
        {
            var wait = Stopwatch.StartNew();
            while (!OwnedServerControl.Loading(process) && !process.HasExited && wait.ElapsedMilliseconds < 5000)
                Thread.Sleep(10);
            require(OwnedServerControl.Loading(process), "Fixture loading marker was not consumed: " + fixture);
            if (fixture == "eof")
            {
                process.StandardInput.Close();
                Thread.Sleep(100);
                require(!process.HasExited, "EOF was treated as an owned shutdown command.");
            }
            var stop = Stopwatch.StartNew();
            require(OwnedServerControl.Stop(process, fixture == "cooperative" ? 2000 : 200), "Owned fixture did not exit: " + fixture);
            require(stop.ElapsedMilliseconds < 4500, "Owned stop exceeded its finite bound: " + fixture);
            require(File.Exists(finalized) == (fixture == "cooperative"), "Graceful cleanup/force-fallback result is wrong: " + fixture);
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); process.WaitForExit(2000); }
        }
    }

    private static int Child(string fixture)
    {
        using var cancel = new CancellationTokenSource();
        using var control = OwnedServerControl.Listen(() => { if (fixture == "cooperative") cancel.Cancel(); });
        using var loading = OwnedServerControl.LoadingScope();
        while (!cancel.IsCancellationRequested) Thread.Sleep(10);
        File.WriteAllText(Environment.GetEnvironmentVariable(FinalizedVariable)!, "finalized");
        return 0;
    }
}
