using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using MphRead.Mods.Network;

namespace MphRead.Mods.Replay;

/// <summary>Consumes a checkpoint and oracle exported by the actual v0.1.34
/// assembly. Does not generate old bytes using the current writer.</summary>
internal static class ReplayV134Check
{
    internal static int Run(string directory)
    {
        Headless.Enter();
        string source = Path.Combine(directory, "v134-source.ppdemo");
        byte[] bytes = File.ReadAllBytes(Path.Combine(directory, "v134-world.ppwc"));
        using var oracle = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "v134-expected.json")));
        using var checkpoint = ReplayWorldCheckpoint.FromBytes(bytes);
        using var world = new PassiveReplayScene(source, new(256, 192));
        // Record real constructor defaults; do not assume newly-added arrays
        // should be null or numeric sentinel fields should be zero.
        int nextToken = world.Scene.GameState.NextTokenId;
        int hardpoint = world.Scene.GameState.ActiveHardpointId;
        var chamberField = typeof(Entities.PlayerEntity).GetField("_chamberShots", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var chambers = world.Scene.Players.Items.Select(p => chamberField.GetValue(p)).ToArray();
        checkpoint.Restore(world);
        void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
        Require(world.Session.CurrentFrame == oracle.RootElement.GetProperty("frame").GetUInt32(), "Historical frame lost.");
        foreach (var expected in oracle.RootElement.GetProperty("players").EnumerateArray())
        {
            int slot = expected.GetProperty("slot").GetInt32();
            var player = world.Scene.Players.Items[slot];
            float[] Vector(string key) => expected.GetProperty(key).EnumerateArray().Select(v => v.GetSingle()).ToArray();
            Require(Vector("position").SequenceEqual(new[] { player.Position.X, player.Position.Y, player.Position.Z }), $"Historical position p{slot}.");
            Require(Vector("speed").SequenceEqual(new[] { player.Speed.X, player.Speed.Y, player.Speed.Z }), $"Historical velocity p{slot}.");
            Require(Vector("facing").SequenceEqual(new[] { player.FacingVector.X, player.FacingVector.Y, player.FacingVector.Z }), $"Historical facing p{slot}.");
            Require(player.Health == expected.GetProperty("health").GetUInt32(), $"Historical health p{slot}.");
            Require((uint)player.Flags1 == expected.GetProperty("flags1").GetUInt32() && (uint)player.Flags2 == expected.GetProperty("flags2").GetUInt32(), $"Historical flags p{slot}.");
            Require(ReferenceEquals(chambers[slot], chamberField.GetValue(player)) && ((uint[])chambers[slot]!).All(v => v == 0), $"New chamber state lost constructor defaults p{slot}.");
        }
        Require(world.Scene.GameState.Points.SequenceEqual(oracle.RootElement.GetProperty("points").EnumerateArray().Select(v => v.GetInt32())), "Historical scores lost.");
        Require(world.Scene.GameState.NextTokenId == nextToken && world.Scene.GameState.ActiveHardpointId == hardpoint,
            "New game-mode state lost constructor defaults.");
        for (int i = 0; i < 10; i++) Require(world.Step(), "Restored historical world cannot continue.");
        Require(!ReplayWorldCheckpoint.SupportsContract(new string('0', 64), "0.1.34"), "Unknown contract accepted.");
        Console.WriteLine("[replay-v134] PASS: actual historical checkpoint decoded with frozen type IDs/fields; all eight player states and scores match old-producer oracle; new fields retain constructor defaults; continuation succeeds.");
        return 0;
    }
}
