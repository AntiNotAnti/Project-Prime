using System;
using System.IO;
using System.Text.Json;
using OpenTK.Mathematics;

namespace MphRead.Mods.Movement;

// Private offline experiment configuration; never a network/replay profile identity.
// Rates describe independently implemented horizontal math. Native jumps/collision stay native.
internal sealed record MomentumPracticeTuning
{
    public float GroundSpeed { get; init; } = MomentumProfileV1.GroundSpeed;
    public float GroundAcceleration { get; init; } = 10;
    public float GroundFriction { get; init; } = 6;
    public float StopSpeedFraction { get; init; } = 100f / 320;
    public float AirAcceleration { get; init; } = 1;
    public float AirWishSpeedFraction { get; init; } = 1;

    public static MomentumPracticeTuning Quake3Default { get; } = new();

    // Admission/Load validates once. Existing MomentumMath validation remains on each actual step.
    public MomentumTuning Tuning => new(GroundAcceleration, GroundFriction,
        GroundSpeed * StopSpeedFraction, AirAcceleration, GroundSpeed * AirWishSpeedFraction);

    public void Validate()
    {
        Nonnegative(GroundSpeed, nameof(GroundSpeed));
        if (GroundSpeed == 0) throw new ArgumentOutOfRangeException(nameof(GroundSpeed), "Ground speed must be positive.");
        Nonnegative(GroundAcceleration, nameof(GroundAcceleration));
        Nonnegative(GroundFriction, nameof(GroundFriction));
        Nonnegative(StopSpeedFraction, nameof(StopSpeedFraction));
        Nonnegative(AirAcceleration, nameof(AirAcceleration));
        Nonnegative(AirWishSpeedFraction, nameof(AirWishSpeedFraction));
        Product(GroundSpeed, StopSpeedFraction, nameof(StopSpeedFraction));
        Product(GroundSpeed, AirWishSpeedFraction, nameof(AirWishSpeedFraction));
        Product(GroundSpeed, GroundAcceleration, nameof(GroundAcceleration));
        Product(GroundSpeed, AirAcceleration, nameof(AirAcceleration));
        Product(GroundSpeed, GroundFriction, nameof(GroundFriction));
        Product(GroundSpeed * StopSpeedFraction, GroundFriction, nameof(StopSpeedFraction));
    }

    // Configuration owns full-deflection speed; signed axes, native contact, basis and dt remain input.
    // There is deliberately no total-speed retention clamp in this offline draft.
    public Vector3 Step(Vector3 velocity, MomentumInput input)
        => MomentumMath.Step(velocity, input with { DesiredSpeed = GroundSpeed }, Tuning);

    public static MomentumPracticeTuning Load(string path)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new JsonException("Practice tuning must be a JSON object.");
        MomentumPracticeTuning value = Quake3Default;
        int seen = 0;
        foreach (JsonProperty property in document.RootElement.EnumerateObject())
        {
            int bit = property.Name switch
            {
                "groundSpeed" => 1,
                "groundAcceleration" => 2,
                "groundFriction" => 4,
                "stopSpeedFraction" => 8,
                "airAcceleration" => 16,
                "airWishSpeedFraction" => 32,
                _ => throw new JsonException("Unknown practice tuning property: " + property.Name)
            };
            if ((seen & bit) != 0) throw new JsonException("Duplicate practice tuning property: " + property.Name);
            seen |= bit;
            if (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetSingle(out float number))
                throw new JsonException("Practice tuning values must be float numbers: " + property.Name);
            value = bit switch
            {
                1 => value with { GroundSpeed = number },
                2 => value with { GroundAcceleration = number },
                4 => value with { GroundFriction = number },
                8 => value with { StopSpeedFraction = number },
                16 => value with { AirAcceleration = number },
                32 => value with { AirWishSpeedFraction = number },
                _ => value
            };
        }
        value.Validate();
        return value;
    }

    private static void Nonnegative(float value, string name)
    {
        if (!float.IsFinite(value) || value < 0)
            throw new ArgumentOutOfRangeException(name, "Practice coefficients must be finite and nonnegative.");
    }

    private static void Product(float first, float second, string name)
    {
        if ((double)first * second > float.MaxValue)
            throw new ArgumentOutOfRangeException(name, "Derived practice coefficient must fit a finite float.");
    }
}
