using System;
using MphRead.Formats;
using OpenTK.Mathematics;

namespace MphRead.Entities;

/// <summary>
/// Small per-opponent, per-bot model built only from physically visible frames.
/// It is not shared between bots, persisted between lives, or used while hidden.
/// </summary>
internal struct BotOpponentModel
{
    public int ObservedFrames { get; private set; }
    public float ReversalHabit { get; private set; }
    public float AirborneHabit { get; private set; }
    public float RushHabit { get; private set; }

    private int _lastStrafeSign;
    private int _framesWithoutStrafe;
    private int _framesUnseen;

    internal void Observe(Vector3 botPosition, Vector3 opponentPosition,
        Vector3 opponentSpeed, bool grounded)
    {
        if (!IsFinite(botPosition) || !IsFinite(opponentPosition)
            || !IsFinite(opponentSpeed))
        {
            return;
        }

        Vector3 towardBot = new(botPosition.X - opponentPosition.X,
            0, botPosition.Z - opponentPosition.Z);
        if (towardBot.LengthSquared < .01f)
        {
            return;
        }
        towardBot.Normalize();

        // The observed native Speed uses twice the 60-Hz displacement.
        Vector3 perTick = opponentSpeed / 2f;
        Vector3 right = new(towardBot.Z, 0, -towardBot.X);
        float lateral = Vector3.Dot(perTick, right);
        int side = lateral > .025f ? 1 : lateral < -.025f ? -1 : 0;
        if (_framesUnseen > 5)
        {
            // No inferred strafe reversal may bridge a period behind cover.
            _lastStrafeSign = 0;
            _framesWithoutStrafe = 0;
        }
        if (side != 0 && _lastStrafeSign != 0 && side != _lastStrafeSign
            && _framesWithoutStrafe <= 16)
        {
            ReversalHabit = Math.Min(1, ReversalHabit * .94f + .23f);
        }
        else
        {
            ReversalHabit *= .993f;
        }
        if (side != 0)
        {
            _lastStrafeSign = side;
            _framesWithoutStrafe = 0;
        }
        else if (++_framesWithoutStrafe > 16)
        {
            _lastStrafeSign = 0;
        }

        AirborneHabit = Math.Clamp(AirborneHabit * .966f + (grounded ? 0 : .034f), 0, 1);
        float closing = Vector3.Dot(perTick, towardBot);
        RushHabit = Math.Clamp(RushHabit * .959f + (closing > .085f ? .041f : 0), 0, 1);

        _framesUnseen = 0;
        ObservedFrames = Math.Min(1800, ObservedFrames + 1);
    }

    internal void DecayWithoutSight()
    {
        _framesUnseen = Math.Min(1800, _framesUnseen + 1);
        ReversalHabit *= .975f;
        AirborneHabit *= .983f;
        RushHabit *= .983f;
        if (_framesUnseen > 5)
        {
            _lastStrafeSign = 0;
            _framesWithoutStrafe = 0;
        }
    }

    private static bool IsFinite(Vector3 v)
        => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}

/// <summary>Bounded, explainable tactical responses to observed opponent habits.</summary>
internal static class BotOpponentTactics
{
    internal static float LeadScale(BotOpponentModel model)
    {
        if (model.ObservedFrames < 24) return 1;
        return Math.Clamp(1f - model.ReversalHabit * .32f, .68f, 1f);
    }

    internal static float WeaponCounterBonus(BeamType beam, BotOpponentModel model,
        float distance, int botLevel)
    {
        if (model.ObservedFrames < 30 || botLevel < 2
            || !float.IsFinite(distance))
            return 0;
        float score = 0;
        float airborne = model.AirborneHabit;
        float reversals = model.ReversalHabit;
        float rush = model.RushHabit;

        if (airborne > .28f)
        {
            if (beam is BeamType.Imperialist or BeamType.VoltDriver) score += airborne * 9f;
            if (beam is BeamType.Battlehammer or BeamType.Magmaul) score -= airborne * 6f;
        }
        if (reversals > .20f && distance <= 15)
        {
            if (beam == BeamType.ShockCoil) score += reversals * 12f;
            if (beam == BeamType.Imperialist) score -= reversals * 3f;
        }
        if (rush > .25f)
        {
            if (beam == BeamType.ShockCoil && distance <= 18) score += rush * 10f;
            if (beam == BeamType.Judicator) score += rush * 5f;
        }
        return Math.Clamp(score * (botLevel >= 3 ? 1f : .65f), -8f, 14f);
    }

    internal static float RangeAdjustment(BeamType beam, BotOpponentModel model)
    {
        if (model.ObservedFrames < 24 || model.RushHabit <= .3f)
            return 0;
        // An observed rusher makes long-range weapons prefer more breathing room.
        return beam is BeamType.Imperialist or BeamType.VoltDriver
            ? model.RushHabit * 4.5f : 0;
    }
}
