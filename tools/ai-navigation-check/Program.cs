using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using MphRead;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

const BindingFlags PrivateFields = BindingFlags.NonPublic | BindingFlags.Instance;
Type aiType = typeof(PlayerEntity.PlayerAiData);
if (args.Length > 0 && args[0] == "profile")
{
    FieldInfo? countField = aiType.GetField("_navigationQueryInvocations",
        BindingFlags.NonPublic | BindingFlags.Static);
    if (countField == null)
    {
        throw new InvalidOperationException("Profile mode requires the temporary query counter "
            + "described in README.md; use it only in an isolated diagnostic checkout.");
    }
    if (args.Length < 3 || !int.TryParse(args[2], out int bots) || bots is < 1 or > 8)
    {
        throw new ArgumentException("Usage: profile <assets directory> <1..8 bots> [room]");
    }
    Directory.SetCurrentDirectory(Path.GetFullPath(args[1]));
    Paths.UpdatePaths();
    Paths.ChooseMphPath();
    string room = args.Length > 3 ? args[3] : "MP1 SANCTORUS";
    var sim = new ServerSim();
    try
    {
        if (!sim.Start(room, GameMode.Battle, 8, _ => { }, () => { }))
        {
            throw new InvalidOperationException("Scene load failed");
        }
        NetSession.ApplyMatchState(new MatchStatePacket
        {
            MatchId = 1, AuthorityEpoch = 1, RoomKey = room, Mode = (byte)GameMode.Battle
        }, false);
        var roster = RosterPacket.Create();
        roster.MatchId = 1;
        roster.AuthorityEpoch = 1;
        roster.Revision = 1;
        roster.Count = (byte)bots;
        roster.ContainsBots = true;
        for (byte slot = 0; slot < bots; slot++)
        {
            roster.Slots[slot] = slot;
            roster.Generations[slot] = 1;
            roster.Teams[slot] = -1;
            roster.Hunters[slot] = (byte)(slot % 7);
            roster.Names[slot] = "BOT";
            roster.Flags[slot] = 1;
            roster.BotLevels[slot] = (byte)(slot % 4);
        }
        NetSession.ApplyRoster(roster);
        NetSlotManager.Sync();
        GameState.PointGoal = 10000;
        GameState.MatchTime = 3600;
        countField.SetValue(null, 0);
        for (int step = 0; step < 1800; step++) sim.Step();
        Console.WriteLine("AI_PROFILE " + JsonSerializer.Serialize(new
        {
            room, bots, steps = sim.Frames,
            engineMvid = typeof(PlayerEntity).Assembly.ManifestModule.ModuleVersionId,
            runtimeVersion = Environment.Version.ToString(),
            queryInvocations = countField.GetValue(null), sim.StepFailures,
            allocatedBytes = sim.StepAllocatedBytes, sim.StepSeconds
        }));
        if (sim.StepFailures != 0)
        {
            throw new InvalidOperationException("Headless simulation reported step failures");
        }
    }
    finally
    {
        sim.Stop();
        NetSession.Stop();
    }
    return;
}

Headless.Enter();
var scene = new Scene(new Vector2i(256, 192), SyntheticInput.CreateKeyboard(),
    SyntheticInput.CreateMouse(), _ => { }, () => { }, initializeRuntime: false);
PlayerEntity.PlayerAiData ai = scene.Players.Items[0].AiData;
var query = aiType.GetMethod("Func2138D28", PrivateFields)!
    .CreateDelegate<Func<Vector3, NodeData3>>(ai);
FieldInfo nodesField = aiType.GetField("_nodeList", PrivateFields)!;
var candidates = (NodeData3?[])aiType.GetField("_field4C", PrivateFields)!.GetValue(ai)!;
FieldInfo exclusionCountField = aiType.GetField("_field78", PrivateFields)!;
var exclusions = (ushort[])aiType.GetField("_field7A", PrivateFields)!.GetValue(ai)!;
FieldInfo positionField = typeof(NodeData3).GetField("<Position>k__BackingField", PrivateFields)!;
int assertions = 0;

NodeData3 Node(Vector3 position)
{
    var node = new NodeData3(default(Vector3Fx));
    // Fixture-only positions include nonfinite values the fixed-point asset format cannot express.
    positionField.SetValue(node, position);
    return node;
}

void Verify(Vector3[] positions, int[] expected, ushort[]? excluded = null)
{
    NodeData3[] sample = positions.Select(Node).ToArray();
    nodesField.SetValue(ai, sample);
    Array.Clear(candidates);
    Array.Clear(exclusions);
    excluded?.CopyTo(exclusions, 0);
    exclusionCountField.SetValue(ai, (ushort)(excluded?.Length ?? 0));
    PlayerEntity.PlayerAiData.InitializeGlobals();
    ai.Flags2 = 0;
    NodeData3 result = query(Vector3.Zero);
    if (!ReferenceEquals(result, sample[expected[0]]))
    {
        throw new InvalidOperationException("Chosen node mismatch");
    }
    assertions++;
    for (int index = 1; index < expected.Length; index++)
    {
        if (!ReferenceEquals(candidates[index], sample[expected[index]]))
        {
            throw new InvalidOperationException("Candidate ordering mismatch at " + index);
        }
        assertions++;
    }
}

Verify([new(1, 0, 0)], [0, 0]);
// Strict equal-distance ties retain traversal order.
Verify([new(1, 0, 0), new(-1, 0, 0)], [0, 0]);
// Ten candidates keep exact insertion order at capacity.
Verify(Enumerable.Range(1, 20).Reverse().Select(index => new Vector3(index, 0, 0)).ToArray(),
    [19, 19, 18, 17, 16, 15, 14, 13, 12, 11, 10]);
// Excluded nearest node cannot enter candidates.
Verify([new(1, 0, 0), new(2, 0, 0), new(3, 0, 0)], [1, 1], [0]);
Verify([new(float.NaN, 0, 0), new(2, 0, 0)], [1, 1]);
Verify([new(float.PositiveInfinity, 0, 0), new(2, 0, 0)], [1, 1]);
// Distance overflow retains the existing comparison semantics.
Verify([new(float.MaxValue, 0, 0), new(2, 0, 0)], [1, 1]);
Verify([new(-0.0f, 0, 0), new(0, 0, 0)], [0, 0]);
// No accepted candidate, or entirely excluded input, preserves fallback.
Verify([new(1000, 0, 0), new(2000, 0, 0)], [0]);
Verify([new(1, 0, 0)], [0], [0]);
Console.WriteLine("AI_CHARACTERIZATION " + assertions + " assertions PASS");

exclusionCountField.SetValue(ai, (ushort)0);
NodeData3[] nodes = Enumerable.Range(0, 256).Select(index => Node(new Vector3(
    (index * 37) % 71 - 35, (index * 11) % 9, (index * 13) % 83 - 41))).ToArray();
nodesField.SetValue(ai, nodes);
PlayerEntity.PlayerAiData.InitializeGlobals();
for (int index = 0; index < 20000; index++)
{
    _ = Array.IndexOf(nodes, query(new Vector3(index % 7, 0, index % 11)));
}
var results = new List<object>();
const int Iterations = 100000;
for (int repeat = 0; repeat < 5; repeat++)
{
    long before = GC.GetAllocatedBytesForCurrentThread();
    long start = Stopwatch.GetTimestamp();
    int checksum = 0;
    for (int index = 0; index < Iterations; index++)
    {
        checksum += Array.IndexOf(nodes, query(new Vector3(index % 7, 0, index % 11)));
    }
    TimeSpan elapsed = Stopwatch.GetElapsedTime(start);
    long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    results.Add(new
    {
        repeat, iterations = Iterations, allocatedBytes = allocated,
        bytesPerQuery = allocated / (double)Iterations,
        nanosecondsPerQuery = elapsed.TotalNanoseconds / Iterations, checksum
    });
}
Console.WriteLine("AI_MICRO " + JsonSerializer.Serialize(new
{
    engineMvid = typeof(PlayerEntity).Assembly.ManifestModule.ModuleVersionId,
    runtimeVersion = Environment.Version.ToString(), nodeCount = nodes.Length, results
}));
