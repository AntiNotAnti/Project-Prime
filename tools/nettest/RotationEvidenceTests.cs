using System;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Content-free reflection fixtures exercise the actual observation gates only.
// A metadata-only Room and controlled confirmation fields are not loaded-world,
// real bootstrap, applied-client, UDP, archive or five-rotation acceptance.
internal static class RotationEvidenceTests
{
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static int _checks, _failures;
    private static void Check(bool value, string label)
    {
        _checks++;
        if (!value) _failures++;
        Console.WriteLine($"[rotation-evidence] {(value ? "PASS" : "FAIL")} {label}");
    }
    private static void Field(Type owner, string name, object? value) => owner.GetField(name, Static)!.SetValue(null, value);
    private static void Field(object owner, string name, object? value) => owner.GetType().GetField(name, Instance)!.SetValue(owner, value);
    private static void Property(Type owner, string name, object? value) => owner.GetProperty(name, Static)!.SetValue(null, value);
    private static void Property(object owner, string name, object? value) => owner.GetType().GetProperty(name, Instance)!.SetValue(owner, value);
    private static IList? Events => (IList?)typeof(NetCampaignEvidence).GetField("_rotationEvents", Static)!.GetValue(null);
    private static int Count => Events?.Count ?? 0;
    private static T Value<T>(int row, string name) => (T)Events![row]!.GetType().GetProperty(name, Instance)!.GetValue(Events[row])!;
    private static void ResetRows()
    {
        Field(typeof(NetCampaignEvidence), "_rotationEvents", null);
        Field(typeof(NetCampaignEvidence), "_rotationKeys", null);
        Field(typeof(NetCampaignEvidence), "_rotationOverflow", 0L);
    }
    private sealed class MetadataReplicaServices : ISceneServices
    {
        public bool IsReplica => true;
        public bool MayEndOnScore => false;
        public bool ShouldLeaveAfterMatch => false;
        public bool SuppressDamage => true;
        public bool AllowsPresentationSideEffects => false;
        public IPlayerReplicationHost PlayerReplication => LivePlayerReplicationHost.Instance;
        public bool DisablePowerups => true;
        public Mods.Multiplayer.MatchWorldProfile? NetworkWorldProfile => null;
        public bool ReplicatesHealthSpawns => false;
        public bool TryGetHealthSpawn(short id, out HealthSpawnState state) { state = default; return false; }
    }
    private static Scene MetadataScene(bool replica = false)
    {
        var scene = new Scene(new Vector2i(32, 32), SyntheticInput.CreateKeyboard(), SyntheticInput.CreateMouse(),
            _ => { }, () => { }, replica ? new MetadataReplicaServices() : null, initializeRuntime: false);
        var room = new RoomEntity(scene);
        Field(room, "_meta", Metadata.GetRoomByName("MP1 SANCTORUS").Item1!);
        Field(scene, "_room", room);
        Field(scene, "_frameCount", 120UL);
        return scene;
    }
    private static MatchStartIdentity Configure(bool authority)
    {
        NetSession.Stop();
        Field(typeof(NetSession), "_playback", false);
        Property(typeof(NetSession), "Role", authority ? NetRole.Server : NetRole.Client);
        Property(typeof(NetSession), "IsAuthority", authority);
        Property(typeof(NetSession), "LocalSlot", authority ? -1 : 0);
        Property(typeof(NetSession), "NetFrame", 120u);
        Field(typeof(NetSession), "_lastSnapshotFrame", 120u);
        var start = new MatchStartIdentity(52, 777, 7);
        var session = new SessionStatePacket
        {
            MatchId = start.MatchId, AuthorityEpoch = start.AuthorityEpoch, StartGeneration = start.StartGeneration,
            Revision = 1, Phase = SessionPhase.InMatch, Policy = ServerSessionPolicy.Continuous,
            Match = new MatchDefinition { RoomKey = "MP1 SANCTORUS", Mode = GameMode.Battle, PointGoal = 5, TimeLimitSeconds = 300 },
            MaxPlayers = 2, StartStage = StartStage.InMatch
        };
        Property(typeof(NetSession), "ServerSession", session);
        Property(typeof(NetSession), "ServerMatch", new MatchStatePacket
        {
            MatchId = start.MatchId, AuthorityEpoch = start.AuthorityEpoch, RoomKey = session.Match.RoomKey,
            Mode = (byte)GameMode.Battle, Flags = MatchStatePacket.FlagInProgress
        });
        Field(typeof(NetRoomChange), "_loadedMatch", start.MatchId);
        Field(typeof(NetRoomChange), "_loadedEpoch", start.AuthorityEpoch);
        Field(typeof(NetRoomChange), "_loadPending", false);
        if (!authority)
        {
            NetPlayerLifecycle.SetOccupant(0, 9);
            var baseline = new WorldBootstrapIdentity(start, 5, 9, 20);
            Field(typeof(NetSession), "_appliedBootstrap", baseline);
            Field(typeof(NetSession), "_confirmedBootstrap", baseline);
        }
        ResetRows();
        return start;
    }
    private static ServerSim MetadataSim(Scene scene)
    {
        var sim = new ServerSim();
        Field(sim, "_scene", scene);
        Property(sim, "Frames", 120L);
        return sim;
    }
    // Warm and measure the identical non-inlined loop. The first long loop can
    // trigger runtime OSR even after the observation methods have been warmed;
    // keep that cold result visible, outside the subsequent exact-zero gate.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long DisabledAllocationSample()
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 20000; i++)
        {
            NetCampaignEvidence.CaptureRotationLifecycle(null!);
            NetCampaignEvidence.CompleteAuthorityRotation(null, default, "disabled");
        }
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
    private static void Disabled()
    {
        for (int i = 0; i < 100; i++)
        {
            NetCampaignEvidence.CaptureRotationLifecycle(null!);
            NetCampaignEvidence.CompleteAuthorityRotation(null, default, "disabled");
        }
        long initial = DisabledAllocationSample();
        long subsequent = DisabledAllocationSample();
        long lastWarmup = 0;
        for (int i = 0; i < 64; i++) lastWarmup = DisabledAllocationSample();
        Console.WriteLine($"[rotation-evidence] default-runtime measurement helper warmup: initial={initial}, subsequent={subsequent}, final-of-64={lastWarmup}; each sample contains 40000 owner observations");
        // Fixed warmup count, no allocation tolerance or retry-until-zero.
        // All five subsequent measurements must independently allocate zero.
        for (int i = 0; i < 5; i++)
        {
            long allocated = DisabledAllocationSample();
            Check(allocated == 0, $"disabled warmed sample {i + 1}/5: 40000 owner observations allocate zero managed bytes (actual {allocated})");
        }
        Check(Events == null && typeof(NetCampaignEvidence).GetField("_rotationKeys", Static)!.GetValue(null) == null,
            "disabled owner observations construct no event list or dedup set");
    }
    private static void Enabled()
    {
        var start = Configure(authority: true);
        var scene = MetadataScene();
        var canonical = (scene.FrameCount, scene.Random.Rng1, scene.Random.Rng2, NetSession.NetFrame,
            NetSession.ServerMatch, NetSession.ServerSession, scene.Room, scene.GameState.PointGoal);
        for (int i = 0; i < 1000; i++) NetCampaignEvidence.CaptureRotationLifecycle(scene);
        Check(Count == 1 && Value<string>(0, "Stage") == "authority-started", "same-world authority observations deduplicate");
        Check(Value<ulong>(0, "Epoch") == start.AuthorityEpoch && Value<ushort>(0, "Match") == start.MatchId
            && Value<uint>(0, "StartGeneration") == start.StartGeneration && Value<string>(0, "Room") == "MP1 SANCTORUS",
            "metadata fixture reports exact current match/start/epoch/room identity");
        Check(Value<int>(0, "OwnerThread") == Environment.CurrentManagedThreadId,
            "observation records its actual invoking owner-thread identity");
        Check(canonical.Equals((scene.FrameCount, scene.Random.Rng1, scene.Random.Rng2, NetSession.NetFrame,
            NetSession.ServerMatch, NetSession.ServerSession, scene.Room, scene.GameState.PointGoal)),
            "observation leaves controlled frame, RNG, room, configuration and session state unchanged");
        var sim = MetadataSim(scene);
        NetCampaignEvidence.CompleteAuthorityRotation(sim, start with { StartGeneration = start.StartGeneration - 1 }, "stale start");
        Check(Count == 1, "authority completion refuses a stale start generation in the same room/match/epoch");
        NetCampaignEvidence.CompleteAuthorityRotation(sim, start with { MatchId = 51 }, "stale match");
        NetCampaignEvidence.CompleteAuthorityRotation(sim, start with { AuthorityEpoch = 776 }, "stale epoch");
        Check(Count == 1, "authority completion refuses retired match and epoch identity");
        Field(typeof(NetSession), "_playback", true);
        NetCampaignEvidence.CompleteAuthorityRotation(sim, start, "playback cannot complete");
        NetCampaignEvidence.CaptureRotationLifecycle(scene);
        Check(Count == 1, "playback cannot create authority starts or completions even with a controlled stale authority flag");
        Field(typeof(NetSession), "_playback", false);
        var session = NetSession.ServerSession!.Value;
        Property(typeof(NetSession), "ServerSession", session with { Match = session.Match with { RoomKey = "MP2 HARVESTER" } });
        NetCampaignEvidence.CaptureRotationLifecycle(scene);
        NetCampaignEvidence.CompleteAuthorityRotation(sim, start, "wrong room");
        Check(Count == 1, "current start cannot label a different metadata room as loaded");
        Property(typeof(NetSession), "ServerSession", session);
        NetCampaignEvidence.CompleteAuthorityRotation(sim, start, "explicit controlled completion");
        NetCampaignEvidence.CompleteAuthorityRotation(sim, start, "duplicate explicit completion");
        Check(Count == 2 && Value<string>(1, "Stage") == "authority-completed" && Value<bool>(1, "Completed")
            && Value<long>(1, "SimFrames") == 120, "only explicit completion produces one deduplicated completion event");
        ResetRows(); NetCampaignEvidence.CaptureRotationLifecycle(scene);
        Property(typeof(NetSession), "ServerSession", session with { Phase = SessionPhase.PostMatch });
        NetCampaignEvidence.CaptureRotationLifecycle(scene);
        // Execute the actual unstarted Stop path; the metadata-only Scene is not
        // a loaded owner and must not enter native cleanup under this fixture.
        new ServerSim().Stop();
        Check(Count == 1 && Value<string>(0, "Stage") == "authority-started",
            "postmatch phase and actual empty Stop cannot manufacture match completion");

        // Match the actual initial Continuous owner session: the lobby start
        // coordinator has not begun, so its real generation and stage are 0/None.
        // This is an observation-gate fixture, never a loaded-world claim.
        var initialContinuous = session with
        {
            StartGeneration = 0, StartStage = StartStage.None, Policy = ServerSessionPolicy.Continuous,
            Phase = SessionPhase.InMatch
        };
        var initialStart = start with { StartGeneration = 0 };
        Property(typeof(NetSession), "ServerSession", initialContinuous);
        ResetRows();
        NetCampaignEvidence.CaptureRotationLifecycle(scene);
        NetCampaignEvidence.CompleteAuthorityRotation(sim, initialStart, "controlled initial continuous completion");
        Check(Count == 2 && Value<uint>(0, "StartGeneration") == 0 && Value<uint>(1, "StartGeneration") == 0
            && Value<string>(0, "SessionPolicy") == "Continuous" && Value<string>(1, "SessionPolicy") == "Continuous"
            && Value<string>(0, "StartStage") == "None" && Value<string>(1, "StartStage") == "None",
            "initial Continuous generation zero preserves explicit actual session policy/stage on both observations");
        foreach (var invalidInitial in new[]
        {
            initialContinuous with { Policy = ServerSessionPolicy.Lobby },
            initialContinuous with { StartStage = StartStage.InMatch }
        })
        {
            ResetRows();
            Property(typeof(NetSession), "ServerSession", invalidInitial);
            NetCampaignEvidence.CaptureRotationLifecycle(scene);
            NetCampaignEvidence.CompleteAuthorityRotation(sim, initialStart, "invalid zero generation");
            Check(Count == 0, "zero generation requires both actual Continuous policy and None start stage");
        }

        start = Configure(authority: false); scene = MetadataScene();
        for (int i = 0; i < 1000; i++) NetCampaignEvidence.CaptureRotationLifecycle(scene);
        Check(Count == 1 && Value<string>(0, "Stage") == "client-applied-confirmed"
            && Value<int>(0, "Slot") == 0 && Value<ushort>(0, "Generation") == 9
            && Value<uint>(0, "BootstrapRevision") == 5 && Value<bool>(0, "WorldAdmissionConfirmed"),
            "controlled confirmed admission deduplicates with exact local incarnation/revision");
        Field(typeof(NetSession), "_confirmedBootstrap", null);
        NetCampaignEvidence.CaptureRotationLifecycle(scene);
        Check(Count == 1, "unconfirmed client baseline does not produce an applied witness");
        var baseline = new WorldBootstrapIdentity(start, 5, 9, 20);
        Field(typeof(NetSession), "_confirmedBootstrap", baseline);
        var ending = NetSession.ServerMatch!.Value; ending.Flags |= MatchStatePacket.FlagEnding;
        Property(typeof(NetSession), "ServerMatch", ending);
        NetCampaignEvidence.CaptureRotationLifecycle(scene);
        NetCampaignEvidence.CaptureRotationLifecycle(scene);
        Check(Count == 2 && Value<string>(1, "Stage") == "client-completed", "accepted controlled Ending deduplicates client completion");
        ResetRows(); Field(typeof(NetSession), "_playback", true);
        NetCampaignEvidence.CaptureRotationLifecycle(scene);
        Check(Count == 0, "client playback is not an applied-world witness");
        Field(typeof(NetSession), "_playback", false);
        var replica = MetadataScene(replica: true);
        NetCampaignEvidence.CaptureRotationLifecycle(replica);
        Property(typeof(NetSession), "IsAuthority", true);
        NetCampaignEvidence.CompleteAuthorityRotation(MetadataSim(replica), start, "replica cannot complete");
        Check(Count == 0, "replica scenes produce neither applied-client nor authoritative completion witnesses");

        start = Configure(authority: false); scene = MetadataScene();
        initialStart = start with { StartGeneration = 0 };
        initialContinuous = NetSession.ServerSession!.Value with
        {
            StartGeneration = 0, StartStage = StartStage.None, Policy = ServerSessionPolicy.Continuous
        };
        Property(typeof(NetSession), "ServerSession", initialContinuous);
        baseline = new WorldBootstrapIdentity(initialStart, 5, 9, 20);
        Field(typeof(NetSession), "_appliedBootstrap", baseline);
        Field(typeof(NetSession), "_confirmedBootstrap", baseline);
        NetCampaignEvidence.CaptureRotationLifecycle(scene);
        ending = NetSession.ServerMatch!.Value; ending.Flags |= MatchStatePacket.FlagEnding;
        Property(typeof(NetSession), "ServerMatch", ending);
        NetCampaignEvidence.CaptureRotationLifecycle(scene);
        Check(Count == 2 && Value<string>(0, "Stage") == "client-applied-confirmed"
            && Value<string>(1, "Stage") == "client-completed"
            && Value<uint>(0, "StartGeneration") == 0 && Value<uint>(1, "StartGeneration") == 0
            && Value<string>(0, "SessionPolicy") == "Continuous" && Value<string>(1, "SessionPolicy") == "Continuous"
            && Value<string>(0, "StartStage") == "None" && Value<string>(1, "StartStage") == "None"
            && Value<ushort>(0, "Generation") == 9 && Value<uint>(0, "BootstrapRevision") == 5,
            "controlled confirmed initial Continuous client preserves actual zero identity through completion");
        foreach (var invalidInitial in new[]
        {
            initialContinuous with { Policy = ServerSessionPolicy.Lobby },
            initialContinuous with { StartStage = StartStage.InMatch }
        })
        {
            ResetRows();
            Property(typeof(NetSession), "ServerSession", invalidInitial);
            NetCampaignEvidence.CaptureRotationLifecycle(scene);
            Check(Count == 0, "confirmed client zero generation requires actual Continuous policy and None start stage");
        }
    }
    public static int Run()
    {
        var current = typeof(GameState).GetProperty("Current", Static)!;
        object? previous = current.GetValue(null);
        try
        {
            if (NetCampaignEvidence.Enabled) Enabled(); else Disabled();
            Console.WriteLine($"[rotation-evidence] {_checks} checks; failures={_failures}; mode={(NetCampaignEvidence.Enabled ? "enabled" : "disabled")}. Content-free gate fixtures only; no loaded-world acceptance.");
            return _failures == 0 ? 0 : 1;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { ResetRows(); NetSession.Stop(); current.SetValue(null, previous); }
    }
}
