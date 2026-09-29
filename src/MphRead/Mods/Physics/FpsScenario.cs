using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MphRead.Mods.Physics;

public sealed record ScenarioInput
{
    public required int Tick { get; init; }
    public int MoveX { get; init; }
    public int MoveY { get; init; }
    // Edges are events; continuous directional state is held until the next row.
    public bool Jump { get; init; }
    public bool Fire { get; init; } // Held until the next input row.
}

public sealed record ScenarioImpulse
{
    public required float[] Velocity { get; init; }
    public required float[] Acceleration { get; init; }
    public required int NativeTicks { get; init; }
}

public sealed record FpsScenario
{
    public int Version { get; init; } = 1;
    public required string Name { get; init; }
    public required string Room { get; init; }
    public string Hunter { get; init; } = "Samus";
    public string Form { get; init; } = "Biped";
    public required float[] Spawn { get; init; }
    public required float[] Facing { get; init; }
    public required int NativeTicks { get; init; }
    public int SettleNativeTicks { get; init; } = 30;
    public uint Seed1 { get; init; } = 12345;
    public uint Seed2 { get; init; } = 67890;
    public ScenarioImpulse? InitialImpulse { get; init; }
    public int InitialFreezeNativeTicks { get; init; }
    public string? PlacementRequired { get; init; }
    public required ScenarioInput[] Inputs { get; init; }

    public static FpsScenario Load(string path)
    {
        var scenario = JsonSerializer.Deserialize<FpsScenario>(File.ReadAllText(path), new JsonSerializerOptions
        { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow })
            ?? throw new InvalidDataException("Empty scenario.");
        scenario.Validate();
        return scenario;
    }
    public void Validate()
    {
        if (Version != 1 || Form != "Biped" || Hunter != "Samus") throw new InvalidDataException("Version 1 scenarios support Samus biped only; alt forms remain gated.");
        if (string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(Room)) throw new InvalidDataException("Scenario name and room are required.");
        if (Spawn == null || Facing == null || Spawn.Length != 3 || Facing.Length != 3 || Spawn.Concat(Facing).Any(v => !float.IsFinite(v))) throw new InvalidDataException("Spawn/facing must be finite 3-vectors.");
        double length = Math.Sqrt(Facing.Sum(v => (double)v * v));
        if (Math.Abs(length - 1) > 1.0 / 4096 || Math.Abs(Facing[1]) > 1e-6) throw new InvalidDataException("Biped facing must be a horizontal unit vector within one native fixed-point unit.");
        if (NativeTicks is < 1 or > 18000 || SettleNativeTicks is < 0 or > 300) throw new InvalidDataException("Invalid scenario duration.");
        if (InitialFreezeNativeTicks < 0 || InitialFreezeNativeTicks > NativeTicks)
            throw new InvalidDataException("Initial freeze duration must fit the fixture.");
        if (Inputs == null || Inputs.Length == 0 || Inputs[0] == null || Inputs[0].Tick != 0) throw new InvalidDataException("Input script must start at native tick zero.");
        if (InitialImpulse is { } impulse)
        {
            if (impulse.Velocity == null || impulse.Acceleration == null || impulse.Velocity.Length != 3
                || impulse.Acceleration.Length != 3 || impulse.NativeTicks < 0 || impulse.NativeTicks > NativeTicks
                || impulse.Velocity.Concat(impulse.Acceleration).Any(v => !float.IsFinite(v) || Math.Abs(v) > 4
                    || v * 4096 != MathF.Truncate(v * 4096)))
                throw new InvalidDataException("Impulse must use finite native fixed-point vectors and an in-range duration.");
        }
        int previous = -1, previousJump = -2;
        foreach (var input in Inputs)
        {
            if (input == null || input.Tick <= previous || input.Tick >= NativeTicks || Math.Abs((long)input.MoveX) > 1 || Math.Abs((long)input.MoveY) > 1)
                throw new InvalidDataException("Inputs must have strictly increasing in-range ticks and digital axes -1/0/1.");
            if (input.Jump && input.Tick - previousJump < 2) throw new InvalidDataException("Native jump edges require an intervening release tick.");
            if (input.Jump) previousJump = input.Tick;
            previous = input.Tick;
        }
    }
    public ScenarioInput AtPrimeTick(int frame)
    {
        if (frame < 0 || frame >= NativeTicks * 2) throw new ArgumentOutOfRangeException(nameof(frame));
        int native = frame / 2;
        var input = Inputs.Last(row => row.Tick <= native);
        return input with { Jump = input.Jump && input.Tick == native && frame % 2 == 0 };
    }
}
