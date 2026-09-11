using System;
using System.Collections.Generic;
using MphRead.Entities;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

/// <summary>
/// Pure, bounded bookkeeping for the rendered WAN validation scenarios. The
/// scenario is a test concern: none of these values are sent to the Worker or
/// used to change authoritative gameplay.
/// </summary>
internal enum RenderedWanScenario
{
    General,
    Headshot
}

internal enum HeadshotScenarioVariant
{
    Vertical,
    Strafe
}

internal enum HeadshotScenarioRange
{
    Close,
    Long
}

internal static class RenderedWanScenarioParser
{
    internal static RenderedWanScenario Parse(string value) => value switch
    {
        "general" => RenderedWanScenario.General,
        "headshot" => RenderedWanScenario.Headshot,
        _ => throw new ArgumentException("Rendered WAN scenario must be general or headshot.")
    };

    internal static string Format(RenderedWanScenario value) => value switch
    {
        RenderedWanScenario.General => "general",
        RenderedWanScenario.Headshot => "headshot",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
}

internal readonly record struct HeadshotScenarioStage(
    HeadshotScenarioVariant Variant, HeadshotScenarioRange Range)
{
    internal static HeadshotScenarioStage At(uint tick, int totalSeconds)
    {
        // Four equal deterministic arms: close/long vertical followed by
        // close/long strafe. A stage is a fact about the harness timeline;
        // validity still requires the target observations to match it.
        int frames = Math.Max(1, totalSeconds * 60);
        int stage = Math.Clamp((int)((long)tick * 4 / frames), 0, 3);
        return stage switch
        {
            0 => new(HeadshotScenarioVariant.Vertical, HeadshotScenarioRange.Close),
            1 => new(HeadshotScenarioVariant.Vertical, HeadshotScenarioRange.Long),
            2 => new(HeadshotScenarioVariant.Strafe, HeadshotScenarioRange.Close),
            _ => new(HeadshotScenarioVariant.Strafe, HeadshotScenarioRange.Long)
        };
    }
}

/// <summary>One command/life fenced combat observation.</summary>
internal sealed class CorrelatedShotLedger
{
    private const int Capacity = 512;
    private readonly Entry[] _entries = new Entry[Capacity];
    private int _count;
    private int _cursor;

    internal int Count => _count;
    internal int LocalRootShots { get; private set; }
    internal int AuthoritativeRootShots { get; private set; }
    internal int CorrelatedRootShots { get; private set; }
    internal int PredictedContacts { get; private set; }
    internal int PredictedHeadshots { get; private set; }
    internal int AuthoritativeHits { get; private set; }
    internal int AuthoritativeHeadshots { get; private set; }

    internal void Reset()
    {
        Array.Clear(_entries);
        _count = 0;
        _cursor = 0;
        LocalRootShots = 0;
        AuthoritativeRootShots = 0;
        CorrelatedRootShots = 0;
        PredictedContacts = 0;
        PredictedHeadshots = 0;
        AuthoritativeHits = 0;
        AuthoritativeHeadshots = 0;
    }

    internal void ObserveLocalRoot(in CombatActor actor, uint commandSequence,
        byte weapon)
    {
        if (!actor.IsValid) return;
        Entry entry = GetOrCreate(actor, commandSequence);
        if (entry.LocalRoot || entry.LocalWeapon != byte.MaxValue && entry.LocalWeapon != weapon)
            return;
        entry.LocalRoot = true;
        entry.LocalWeapon = weapon;
        LocalRootShots++;
        if (entry.AuthoritativeRoot)
        {
            CorrelatedRootShots++;
        }
    }

    internal void ObserveAuthoritativeRoot(in CombatEvent value)
    {
        if (value.Kind != CombatEventKind.Shot || !value.Actor.IsValid) return;
        Entry entry = GetOrCreate(value.Actor, value.CommandSequence);
        if (entry.AuthoritativeRoot) return;
        entry.AuthoritativeRoot = true;
        entry.AuthoritativeWeapon = value.Weapon;
        AuthoritativeRootShots++;
        if (entry.LocalRoot)
        {
            CorrelatedRootShots++;
        }
    }

    internal void ObservePredictedContact(in CombatShot shot, in CombatActor target,
        bool headshot)
    {
        if (!shot.Actor.IsValid || !target.IsValid)
            return;
        Entry entry = GetOrCreate(shot.Actor, shot.CommandSequence);
        PredictedContacts++;
        if (headshot) PredictedHeadshots++;
        entry.PredictedContacts++;
        if (headshot) entry.PredictedHeadshots++;
    }

    internal void ObserveAuthoritativeDamage(in CombatEvent value)
    {
        if (value.Kind != CombatEventKind.Damage || !value.Actor.IsValid
            || !value.Target.IsValid || value.Amount == 0)
            return;
        Entry entry = GetOrCreate(value.Actor, value.CommandSequence);
        AuthoritativeHits++;
        if ((value.Flags & CombatEventFlags.Headshot) != 0) AuthoritativeHeadshots++;
        entry.AuthoritativeHits++;
        if ((value.Flags & CombatEventFlags.Headshot) != 0) entry.AuthoritativeHeadshots++;
    }

    internal int CountLocalWeapon(byte weapon)
    {
        int total = 0;
        for (int i = 0; i < _count; i++)
            if (_entries[i].LocalRoot && _entries[i].LocalWeapon == weapon) total++;
        return total;
    }

    internal int CountAuthorityWeapon(byte weapon)
    {
        int total = 0;
        for (int i = 0; i < _count; i++)
            if (_entries[i].AuthoritativeRoot && _entries[i].AuthoritativeWeapon == weapon) total++;
        return total;
    }

    internal int CountCorrelatedWeapon(byte weapon)
    {
        int total = 0;
        for (int i = 0; i < _count; i++)
            if (_entries[i].LocalRoot && _entries[i].AuthoritativeRoot
                && _entries[i].LocalWeapon == weapon
                && _entries[i].AuthoritativeWeapon == weapon) total++;
        return total;
    }

    private Entry GetOrCreate(in CombatActor actor, uint commandSequence)
    {
        for (int i = 0; i < _count; i++)
        {
            ref Entry current = ref _entries[i];
            if (current.Actor == actor && current.CommandSequence == commandSequence)
                return current;
        }
        int index;
        if (_count < Capacity)
        {
            index = _count++;
        }
        else
        {
            index = _cursor;
            _cursor = (_cursor + 1) % Capacity;
        }
        _entries[index] = new Entry(actor, commandSequence);
        return _entries[index];
    }

    private sealed class Entry(CombatActor actor, uint commandSequence)
    {
        internal CombatActor Actor = actor;
        internal uint CommandSequence = commandSequence;
        internal byte LocalWeapon = byte.MaxValue;
        internal byte AuthoritativeWeapon = byte.MaxValue;
        internal bool LocalRoot;
        internal bool AuthoritativeRoot;
        internal int PredictedContacts;
        internal int PredictedHeadshots;
        internal int AuthoritativeHits;
        internal int AuthoritativeHeadshots;
    }
}

/// <summary>Runtime facts used by the explicit headshot validity gate.</summary>
internal sealed class HeadshotScenarioFacts
{
    internal int TriggerAttempts { get; private set; }
    internal int FramesObserved { get; private set; }
    internal int FramesOnTarget { get; private set; }
    internal int TargetAirborneFrames { get; private set; }
    internal int TargetMovedFrames { get; private set; }
    internal int VerticalFrames { get; private set; }
    internal int StrafeFrames { get; private set; }
    internal int CloseFrames { get; private set; }
    internal int LongFrames { get; private set; }
    internal double RangeSum { get; private set; }
    internal int RangeSamples { get; private set; }
    internal float MaximumVerticalSpeed { get; private set; }
    internal Vector3 InitialTargetPosition { get; private set; }
    internal bool HasInitialTargetPosition { get; private set; }
    internal CorrelatedShotLedger Shots { get; } = new();

    internal double AverageRange => RangeSamples == 0 ? 0 : RangeSum / RangeSamples;
    internal bool TargetMoved => TargetMovedFrames > 0;

    internal void Reset()
    {
        TriggerAttempts = 0;
        FramesObserved = 0;
        FramesOnTarget = 0;
        TargetAirborneFrames = 0;
        TargetMovedFrames = 0;
        VerticalFrames = 0;
        StrafeFrames = 0;
        CloseFrames = 0;
        LongFrames = 0;
        RangeSum = 0;
        RangeSamples = 0;
        MaximumVerticalSpeed = 0;
        InitialTargetPosition = Vector3.Zero;
        HasInitialTargetPosition = false;
        Shots.Reset();
    }

    internal void Trigger()
    {
        TriggerAttempts++;
    }

    internal void ObserveTarget(in SnapshotPlayer target, in SnapshotPlayer shooter,
        in HeadshotScenarioStage stage, float headHeight = 1.1f)
    {
        if (!HasInitialTargetPosition)
        {
            InitialTargetPosition = target.Position;
            HasInitialTargetPosition = true;
        }
        FramesObserved++;
        float displacement = (target.Position - InitialTargetPosition).Length;
        if (displacement >= 0.1f) TargetMovedFrames++;
        if ((target.Flags & SnapshotPlayerFlags.Grounded) == 0)
            TargetAirborneFrames++;
        float verticalSpeed = MathF.Abs(target.Speed.Y);
        MaximumVerticalSpeed = MathF.Max(MaximumVerticalSpeed, verticalSpeed);
        float range = (target.Position - shooter.Position).Length;
        if (Single.IsFinite(range))
        {
            RangeSum += range;
            RangeSamples++;
            if (range <= 8f) CloseFrames++; else if (range >= 16f) LongFrames++;
        }
        // Stage names are only the deterministic schedule. Count coverage
        // from observed target motion so a label cannot manufacture a valid
        // vertical/strafe run.
        float horizontalSpeed = MathF.Abs(target.Speed.X) + MathF.Abs(target.Speed.Z);
        bool airborne = (target.Flags & SnapshotPlayerFlags.Grounded) == 0;
        if (stage.Variant == HeadshotScenarioVariant.Vertical
            && (airborne || verticalSpeed >= 0.05f))
            VerticalFrames++;
        else if (stage.Variant == HeadshotScenarioVariant.Strafe && horizontalSpeed >= 0.05f)
            StrafeFrames++;

        Vector3 head = target.Position + new Vector3(0, headHeight, 0);
        Vector3 toHead = head - shooter.Position;
        if (toHead.LengthSquared > 0.001f && Vector3.Dot(shooter.Aim, toHead.Normalized()) >= 0.996f)
            FramesOnTarget++;
    }
}

internal sealed record ScenarioGateResult(bool Valid, string[] FailureReasons)
{
    internal static ScenarioGateResult ValidateHeadshot(HeadshotScenarioFacts facts,
        bool correctWeapon, bool authorityWeapon, bool zoomed, bool ammoAvailable, bool minimumDuration,
        bool validParticipant, bool authoritativeWorker, bool deterministicTarget,
        bool cleanLink, int expectedMinimumTriggers = 6,
        int expectedMinimumRootShots = 4)
    {
        var failures = new List<string>(8);
        if (!correctWeapon) failures.Add("weapon-not-imperialist");
        if (!authorityWeapon) failures.Add("authoritative-weapon-not-imperialist");
        if (!zoomed) failures.Add("zoom-not-observed");
        if (!ammoAvailable) failures.Add("ammo-not-available");
        if (!minimumDuration) failures.Add("minimum-duration-not-reached");
        if (facts.TriggerAttempts < expectedMinimumTriggers)
            failures.Add("trigger-attempts-below-minimum");
        if (facts.Shots.LocalRootShots == 0)
            failures.Add("no-local-root-shots");
        else if (facts.Shots.LocalRootShots < expectedMinimumRootShots)
            failures.Add("local-root-shots-below-minimum");
        if (facts.Shots.AuthoritativeRootShots == 0)
            failures.Add("no-authoritative-root-shots");
        else if (facts.Shots.AuthoritativeRootShots < expectedMinimumRootShots)
            failures.Add("authoritative-root-shots-below-minimum");
        if (facts.Shots.CorrelatedRootShots == 0)
            failures.Add("no-correlated-root-shots");
        if (cleanLink && facts.Shots.LocalRootShots > 0
            && Math.Abs(facts.Shots.LocalRootShots - facts.Shots.AuthoritativeRootShots)
                > Math.Max(1, (int)Math.Ceiling(
                    Math.Max(facts.Shots.LocalRootShots, facts.Shots.AuthoritativeRootShots) * 0.05)))
            failures.Add("clean-link-shot-ratio-out-of-tolerance");
        if (!validParticipant) failures.Add("client-participant-invalid");
        if (!authoritativeWorker) failures.Add("worker-authority-invalid");
        if (!deterministicTarget) failures.Add("target-trajectory-not-deterministic");
        return new(failures.Count == 0, failures.ToArray());
    }
}

internal sealed record RenderedWanScenarioReport(
    bool ScenarioValid, string[] InvalidReasons, string VariantCoverage,
    string RangeCoverage, bool CorrectWeapon, bool AuthoritativeWeapon,
    bool ZoomObserved, bool AmmoAvailable, bool MinimumDurationReached,
    int TriggerAttempts,
    int LocalRootShots, int AuthoritativeRootShots, int CorrelatedRootShots,
    int LocalImperialistShots, int AuthoritativeImperialistShots,
    int PredictedContacts, int PredictedHeadshots, int AuthoritativeHits,
    int AuthoritativeHeadshots, double LocalAuthorityShotRatio,
    int FramesOnTarget, int TargetAirborneFrames, int TargetMovedFrames,
    double AverageRange, float MaximumVerticalSpeed, int VerticalFrames,
    int StrafeFrames, int CloseFrames, int LongFrames)
{
    internal static RenderedWanScenarioReport Create(HeadshotScenarioFacts facts,
        ScenarioGateResult gate, bool correctWeapon, bool authoritativeWeapon,
        bool zoomObserved, bool ammoAvailable, bool minimumDurationReached)
    {
        int local = facts.Shots.LocalRootShots;
        int authority = facts.Shots.AuthoritativeRootShots;
        return new(gate.Valid, gate.FailureReasons,
            facts.VerticalFrames > 0 && facts.StrafeFrames > 0 ? "vertical+strafe" :
                facts.VerticalFrames > 0 ? "vertical" : facts.StrafeFrames > 0 ? "strafe" : "none",
            facts.CloseFrames > 0 && facts.LongFrames > 0 ? "close+long" :
                facts.CloseFrames > 0 ? "close" : facts.LongFrames > 0 ? "long" : "none",
            correctWeapon, authoritativeWeapon, zoomObserved, ammoAvailable,
            minimumDurationReached, facts.TriggerAttempts, local, authority,
            facts.Shots.CorrelatedRootShots,
            facts.Shots.CountLocalWeapon((byte)BeamType.Imperialist),
            facts.Shots.CountAuthorityWeapon((byte)BeamType.Imperialist),
            facts.Shots.PredictedContacts, facts.Shots.PredictedHeadshots,
            facts.Shots.AuthoritativeHits, facts.Shots.AuthoritativeHeadshots,
            authority == 0 ? 0 : local / (double)authority,
            facts.FramesOnTarget, facts.TargetAirborneFrames, facts.TargetMovedFrames,
            facts.AverageRange, facts.MaximumVerticalSpeed, facts.VerticalFrames,
            facts.StrafeFrames, facts.CloseFrames, facts.LongFrames);
    }
}
