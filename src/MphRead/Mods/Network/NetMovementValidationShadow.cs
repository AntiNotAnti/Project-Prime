using System;
using MphRead.Entities;
using MphRead.Formats;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

[Flags]
internal enum MovementValidationExemption : byte
{
    None = 0, Teleport = 1, Platform = 2, JumpPad = 4, Impulse = 8,
    FormTransition = 16, Climbing = 32, Collision = 64, Prime = 128
}
internal readonly record struct MovementValidationSample(ulong Epoch, ushort Match, int Slot,
    ushort Generation, ushort Life, Hunter Hunter, uint Frame, Vector3 Position,
    IntentButtons Buttons, MovementValidationExemption Exemptions, sbyte MoveX = 0, sbyte MoveY = 0);
internal readonly record struct MovementValidationRow(ulong Epoch, ushort Match, int Slot,
    ushort Generation, ushort Life, Hunter Hunter, uint Frame, uint ElapsedFrames,
    bool Alt, float HorizontalPerFrame, float UpwardPerFrame, float AccelerationPerFrame,
    bool HorizontalSignal, bool VerticalSignal, bool AccelerationSignal);
internal readonly record struct MovementValidationCounts(Hunter Hunter, bool Alt, long Baselines,
    long Evaluated, long Exempt, long Unsupported, long WouldRefuse, long DuplicateOrOld,
    long LongGaps, long InputAbsentWhileMoving, float MaximumHorizontal, float MaximumUpward);
internal sealed record MovementValidationReport(bool Enabled, int HardRejections,
    MovementValidationCounts[] Hunters, MovementValidationRow[] Rows, long Overflow, long Invalid);

/// <summary>Opt-in source-time envelope observations. This is not a collision reference,
/// cheat verdict, or movement authority. It has no path to write player state or reject packets.
/// All envelopes are deliberately broad; collision/alt parity remains unproved.</summary>
internal static class NetMovementValidationShadow
{
    internal static bool Enabled { get; set; } = Environment.GetEnvironmentVariable("PRIME_MOVEMENT_VALIDATION_SHADOW") == "1";
    private struct History
    {
        internal bool Seen, VelocitySeen;
        internal MovementValidationSample Last;
        internal Vector3 Velocity;
        internal uint Elapsed;
    }
    private sealed class Counts
    {
        internal long Baselines, Evaluated, Exempt, Unsupported, WouldRefuse, DuplicateOrOld, LongGaps, InputAbsent;
        internal float MaxHorizontal, MaxUpward;
        internal void Clear() { Baselines = Evaluated = Exempt = Unsupported = WouldRefuse = DuplicateOrOld = LongGaps = InputAbsent = 0; MaxHorizontal = MaxUpward = 0; }
    }
    private static readonly History[] Slots = new History[8];
    private static readonly Counts[] Counters = Create();
    private static readonly MovementValidationRow[] Rows = new MovementValidationRow[128];
    private static int _rows;
    private static long _overflow, _invalid;
    private static Counts[] Create()
    {
        var result = new Counts[14]; for (int i = 0; i < result.Length; i++) result[i] = new(); return result;
    }
    private static bool Alt(in MovementValidationSample value) => (value.Buttons & IntentButtons.AltFormState) != 0;
    private static bool Context(in MovementValidationSample a, in MovementValidationSample b) =>
        a.Epoch == b.Epoch && a.Match == b.Match && a.Generation == b.Generation && a.Life == b.Life && a.Hunter == b.Hunter;
    internal static void ObserveAccepted(PlayerEntity player, in IntentPacket intent)
    {
        if (!Enabled || !NetSession.IsAuthority || player.SceneServices.IsReplica || !intent.HasState
            || !NetSession.MatchesStream(intent.MatchId, intent.AuthorityEpoch)
            || !NetPlayerLifecycle.Matches(player.SlotIndex, intent.SlotGeneration, intent.LifeId)) return;
        Observe(new(intent.AuthorityEpoch, intent.MatchId, player.SlotIndex, intent.SlotGeneration, intent.LifeId,
            player.Hunter, intent.Frame, intent.Position, intent.Buttons, player.ModMovementValidationExemptions, intent.MoveX, intent.MoveY));
    }
    internal static void Observe(in MovementValidationSample sample)
    {
        if (!Enabled) return;
        if ((uint)sample.Slot >= Slots.Length || (uint)sample.Hunter >= 7 || sample.Epoch == 0
            || sample.Match == 0 || sample.Generation == 0 || sample.Life == 0
            || !float.IsFinite(sample.Position.X) || !float.IsFinite(sample.Position.Y) || !float.IsFinite(sample.Position.Z)
            || Math.Abs(sample.Position.X) >= 100000 || Math.Abs(sample.Position.Y) >= 100000 || Math.Abs(sample.Position.Z) >= 100000)
        { _invalid++; return; }
        bool alt = Alt(sample);
        Counts count = Counters[(int)sample.Hunter * 2 + (alt ? 1 : 0)];
        ref History history = ref Slots[sample.Slot];
        if (!history.Seen || !Context(sample, history.Last))
        { count.Baselines++; history = new() { Seen = true, Last = sample }; return; }
        if (!NetLifecycleTracker.Newer(sample.Frame, history.Last.Frame)) { count.DuplicateOrOld++; return; }
        uint elapsed = unchecked(sample.Frame - history.Last.Frame);
        if (elapsed > 60)
        { count.LongGaps++; count.Baselines++; history = new() { Seen = true, Last = sample }; return; }
        if (sample.Exemptions != 0 || history.Last.Exemptions != 0 || alt != Alt(history.Last))
        { count.Exempt++; history = new() { Seen = true, Last = sample }; return; }
        Vector3 velocity = (sample.Position - history.Last.Position) / elapsed;
        float horizontal = new Vector2(velocity.X, velocity.Z).Length;
        float upward = Math.Max(0, velocity.Y);
        count.MaxHorizontal = Math.Max(count.MaxHorizontal, horizontal);
        count.MaxUpward = Math.Max(count.MaxUpward, upward);
        var values = Metadata.PlayerValues[(int)sample.Hunter];
        if (alt)
        {
            // Alt collision, boosts, Stinglarva chains and climbing are not
            // independently modeled. Measure their deltas but never label them
            // a biped-envelope refusal.
            count.Unsupported++; history = new() { Seen = true, Last = sample }; return;
        }
        count.Evaluated++;
        // Raw native fixed-point constants supply conservative upper envelopes,
        // with margin for diagonal input and the 30/60 Hz conversion. They are
        // observations only, not exact accepted-movement limits.
        float speedLimit = 2 * Fixed.ToFloat(Math.Max(values.WalkSpeedCap, values.StrafeSpeedCap)) + .1f;
        float verticalLimit = 2 * Fixed.ToFloat(Math.Max(values.JumpSpeed, values.BombJumpSpeed)) + .1f;
        float acceleration = history.VelocitySeen && elapsed <= 2 && history.Elapsed <= 2
            ? new Vector2(velocity.X - history.Velocity.X, velocity.Z - history.Velocity.Z).Length / ((history.Elapsed + elapsed) * .5f) : 0;
        float accelerationLimit = 4 * Fixed.ToFloat(Math.Max(values.WalkBipedTraction, values.StrafeBipedTraction)) + .2f;
        bool speedSignal = horizontal > speedLimit, upSignal = upward > verticalLimit, accelerationSignal = acceleration > accelerationLimit;
        const IntentButtons movement = IntentButtons.MoveUp | IntentButtons.MoveDown | IntentButtons.MoveLeft | IntentButtons.MoveRight;
        if ((sample.Buttons & movement) == 0 && sample.MoveX == 0 && sample.MoveY == 0 && horizontal > .05f) count.InputAbsent++;
        if (speedSignal || upSignal || accelerationSignal)
        {
            count.WouldRefuse++;
            if (_rows < Rows.Length) Rows[_rows++] = new(sample.Epoch, sample.Match, sample.Slot,
                sample.Generation, sample.Life, sample.Hunter, sample.Frame, elapsed, alt,
                horizontal, upward, acceleration, speedSignal, upSignal, accelerationSignal);
            else _overflow++;
        }
        history = new() { Seen = true, VelocitySeen = true, Last = sample, Velocity = velocity, Elapsed = elapsed };
    }
    internal static void ResetSlot(int slot) { if ((uint)slot < Slots.Length) Slots[slot] = default; }
    internal static void ResetHistory() => Array.Clear(Slots);
    internal static void Reset()
    { Array.Clear(Slots); Array.Clear(Rows); _rows = 0; _overflow = _invalid = 0; foreach (var count in Counters) count.Clear(); }
    internal static MovementValidationReport Capture()
    {
        var counts = new MovementValidationCounts[Counters.Length];
        for (int i = 0; i < counts.Length; i++)
        {
            var c = Counters[i]; counts[i] = new((Hunter)(i / 2), (i & 1) != 0,
                c.Baselines, c.Evaluated, c.Exempt, c.Unsupported, c.WouldRefuse, c.DuplicateOrOld,
                c.LongGaps, c.InputAbsent, c.MaxHorizontal, c.MaxUpward);
        }
        return new(Enabled, 0, counts, Rows.AsSpan(0, _rows).ToArray(), _overflow, _invalid);
    }
}
