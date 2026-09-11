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
    internal static int ArmAt(uint tick, int totalSeconds)
    {
        int frames = Math.Max(1, totalSeconds * 60);
        int frame = (int)(tick % (uint)frames);
        // Keep a full close/long arm for both motion variants. Long arms are
        // intentionally weighted to two sixths of the run so the fixture has
        // enough time to reach and hold the far band instead of merely
        // crossing it at a stage boundary.
        return frame < frames / 6 ? 0
            : frame < frames / 2 ? 1
            : frame < frames * 2 / 3 ? 2 : 3;
    }

    internal static HeadshotScenarioStage At(uint tick, int totalSeconds)
    {
        int stage = ArmAt(tick, totalSeconds);
        return stage switch
        {
            0 => new(HeadshotScenarioVariant.Vertical, HeadshotScenarioRange.Close),
            1 => new(HeadshotScenarioVariant.Vertical, HeadshotScenarioRange.Long),
            2 => new(HeadshotScenarioVariant.Strafe, HeadshotScenarioRange.Close),
            _ => new(HeadshotScenarioVariant.Strafe, HeadshotScenarioRange.Long)
        };
    }
}

/// <summary>
/// The headshot band is the same geometry used by the production lag
/// compensation/projectile paths: the top of the presented hunter minus half
/// of the 0.3-unit head band. Keeping this in one helper prevents the fixture
/// aim and its FramesOnTarget evidence from silently using different points.
/// </summary>
internal static class HeadshotScenarioGeometry
{
    private const float HeadBandMidpointOffset = 0.15f;

    internal static bool TryGetHeadHeight(Hunter hunter, out float height,
        PlayerEntity? presentedEntity = null)
    {
        if (presentedEntity is { ModIsInPlay: true })
        {
            height = Fixed.ToFloat(presentedEntity.Values.MaxPickupHeight)
                - HeadBandMidpointOffset;
            return float.IsFinite(height);
        }

        int index = (int)hunter;
        if ((uint)index >= (uint)Metadata.PlayerValues.Count)
        {
            height = 0;
            return false;
        }
        height = Fixed.ToFloat(Metadata.PlayerValues[index].MaxPickupHeight)
            - HeadBandMidpointOffset;
        return float.IsFinite(height);
    }

    internal static bool TryGetHeadPoint(in SnapshotPlayer target,
        Vector3 presentedPosition, out Vector3 point, PlayerEntity? presentedEntity = null)
    {
        if (!TryGetHeadHeight(target.Hunter, out float height, presentedEntity))
        {
            point = default;
            return false;
        }
        point = presentedPosition + new Vector3(0, height, 0);
        return true;
    }
}

internal static class HeadshotScenarioAim
{
    internal static bool IsBodyCalibration(uint targetLife, uint trackedLife,
        uint lifeStartFrame, bool lifeKnown, uint scenarioTick)
        => lifeKnown && targetLife != 0 && targetLife == trackedLife
            && scenarioTick >= lifeStartFrame
            && scenarioTick - lifeStartFrame
                < HeadshotScenarioThresholds.StationaryBodyAimFrames;
}

/// <summary>Initial N2 evidence thresholds for a genuine headshot run.</summary>
internal static class HeadshotScenarioThresholds
{
    internal const int MinimumTriggerAttempts = 8;
    internal const int MinimumRootShots = 4;
    internal const int MinimumCorrelatedRootShots = 30;
    internal const int MinimumPredictedContacts = 20;
    internal const int MinimumAuthoritativeHits = 20;
    internal const int MinimumHeadshotCases = 10;
    internal const int MinimumStationaryBodyFrames = 20;
    internal const int MinimumStationaryHeadFrames = 20;
    internal const int StationaryBodyAimFrames = 90;
    internal const int StationaryCalibrationFrames = 180;
    internal const int MinimumMotionFrames = 30;
    internal const float MinimumReasonableHitRate = 0.5f;
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
    internal int ConfirmedHeadshots => CountEntries(static entry
        => entry.PredictedHeadshots > 0 && entry.AuthoritativeHeadshots > 0);
    internal int DowngradedHeadshots => CountEntries(static entry
        => entry.PredictedHeadshots > 0 && entry.AuthoritativeHits > 0
            && entry.AuthoritativeHeadshots == 0);
    internal int PromotedHeadshots => CountEntries(static entry
        => entry.AuthoritativeHeadshots > 0 && entry.PredictedHeadshots == 0);
    internal int DeniedHeadshots => CountEntries(static entry
        => entry.PredictedHeadshots > 0 && entry.AuthoritativeHits == 0);

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

    private int CountEntries(Func<Entry, bool> predicate)
    {
        int total = 0;
        for (int i = 0; i < _count; i++)
            if (predicate(_entries[i])) total++;
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
    internal int StationaryBodyFrames { get; private set; }
    internal int StationaryHeadFrames { get; private set; }
    internal double RangeSum { get; private set; }
    internal int RangeSamples { get; private set; }
    internal float MaximumVerticalSpeed { get; private set; }
    internal Vector3 InitialTargetPosition { get; private set; }
    internal bool HasInitialTargetPosition { get; private set; }
    private Vector3 _previousTargetPosition;
    private bool _hasPreviousTargetPosition;
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
        StationaryBodyFrames = 0;
        StationaryHeadFrames = 0;
        RangeSum = 0;
        RangeSamples = 0;
        MaximumVerticalSpeed = 0;
        InitialTargetPosition = Vector3.Zero;
        HasInitialTargetPosition = false;
        _previousTargetPosition = Vector3.Zero;
        _hasPreviousTargetPosition = false;
        Shots.Reset();
    }

    internal void Trigger()
    {
        TriggerAttempts++;
    }

    internal void ObserveTarget(in SnapshotPlayer target, in SnapshotPlayer shooter,
        in HeadshotScenarioStage stage, bool bodyCalibrationAim)
    {
        if (!HasInitialTargetPosition)
        {
            InitialTargetPosition = target.Position;
            HasInitialTargetPosition = true;
        }
        FramesObserved++;
        float frameDisplacement = _hasPreviousTargetPosition
            ? (target.Position - _previousTargetPosition).Length : 0;
        _previousTargetPosition = target.Position;
        _hasPreviousTargetPosition = true;
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

        // Stationary evidence is based on the observed pose, not an assumed
        // client-frame window or the run's original spawn position. A target
        // may settle under gravity before the first rendered aim sample; its
        // current zero-speed, zero-step pose is still a genuine stationary
        // sample. Admission, interpolation, and reconnect can delay that
        // sample, so fencing it to frames 1..60 loses real evidence.
        bool stationary = target.Speed.LengthSquared <= 0.0025f
            && frameDisplacement < 0.02f;
        Vector3 body = target.Position - shooter.Position;
        if (bodyCalibrationAim && stationary && body.LengthSquared > 0.001f
            && Vector3.Dot(shooter.Aim, body.Normalized()) >= 0.996f)
        {
            StationaryBodyFrames++;
        }

        if (!HeadshotScenarioGeometry.TryGetHeadPoint(target, target.Position,
                out Vector3 head))
            return;
        Vector3 toHead = head - shooter.Position;
        bool aimedAtHead = toHead.LengthSquared > 0.001f
            && Vector3.Dot(shooter.Aim, toHead.Normalized()) >= 0.996f;
        if (!bodyCalibrationAim && stationary && aimedAtHead)
            StationaryHeadFrames++;
        if (aimedAtHead)
            FramesOnTarget++;
    }
}

internal sealed record ScenarioGateResult(
    bool ChoreographyValid, bool ShotCorrelationValid, bool CombatCoverageValid,
    bool HeadshotEvidenceValid, string[] FailureReasons)
{
    internal bool Valid => ChoreographyValid && ShotCorrelationValid
        && CombatCoverageValid && HeadshotEvidenceValid;

    internal static ScenarioGateResult ValidateHeadshot(HeadshotScenarioFacts facts,
        bool correctWeapon, bool authorityWeapon, bool zoomed, bool ammoAvailable, bool minimumDuration,
        bool validParticipant, bool authoritativeWorker, bool deterministicTarget,
        bool cleanLink, int expectedMinimumTriggers = HeadshotScenarioThresholds.MinimumTriggerAttempts,
        int expectedMinimumRootShots = HeadshotScenarioThresholds.MinimumRootShots)
    {
        var failures = new List<string>(16);
        var choreographyFailures = new List<string>(8);
        var correlationFailures = new List<string>(8);
        var coverageFailures = new List<string>(8);
        var evidenceFailures = new List<string>(4);

        if (!minimumDuration) choreographyFailures.Add("minimum-duration-not-reached");
        if (!validParticipant) choreographyFailures.Add("client-participant-invalid");
        if (!authoritativeWorker) choreographyFailures.Add("worker-authority-invalid");
        if (!deterministicTarget) choreographyFailures.Add("target-trajectory-not-deterministic");
        if (facts.StationaryBodyFrames < HeadshotScenarioThresholds.MinimumStationaryBodyFrames)
            choreographyFailures.Add("stationary-body-aim-below-minimum");
        if (facts.StationaryHeadFrames < HeadshotScenarioThresholds.MinimumStationaryHeadFrames)
            choreographyFailures.Add("stationary-head-aim-below-minimum");

        if (!correctWeapon) correlationFailures.Add("weapon-not-imperialist");
        if (!authorityWeapon) correlationFailures.Add("authoritative-weapon-not-imperialist");
        if (!zoomed) correlationFailures.Add("zoom-not-observed");
        if (!ammoAvailable) correlationFailures.Add("ammo-not-available");
        if (facts.TriggerAttempts < expectedMinimumTriggers)
            correlationFailures.Add("trigger-attempts-below-minimum");
        if (facts.Shots.LocalRootShots == 0)
            correlationFailures.Add("no-local-root-shots");
        else if (facts.Shots.LocalRootShots < expectedMinimumRootShots)
            correlationFailures.Add("local-root-shots-below-minimum");
        if (facts.Shots.AuthoritativeRootShots == 0)
            correlationFailures.Add("no-authoritative-root-shots");
        else if (facts.Shots.AuthoritativeRootShots < expectedMinimumRootShots)
            correlationFailures.Add("authoritative-root-shots-below-minimum");
        if (facts.Shots.CorrelatedRootShots == 0)
            correlationFailures.Add("no-correlated-root-shots");
        if (facts.Shots.CorrelatedRootShots
            < HeadshotScenarioThresholds.MinimumCorrelatedRootShots)
            correlationFailures.Add("correlated-root-shots-below-minimum");
        if (cleanLink && facts.Shots.LocalRootShots > 0
            && Math.Abs(facts.Shots.LocalRootShots - facts.Shots.AuthoritativeRootShots)
                > Math.Max(1, (int)Math.Ceiling(
                    Math.Max(facts.Shots.LocalRootShots, facts.Shots.AuthoritativeRootShots) * 0.05)))
            correlationFailures.Add("clean-link-shot-ratio-out-of-tolerance");

        if (facts.Shots.PredictedContacts < HeadshotScenarioThresholds.MinimumPredictedContacts)
            coverageFailures.Add("predicted-contacts-below-minimum");
        if (facts.Shots.AuthoritativeHits < HeadshotScenarioThresholds.MinimumAuthoritativeHits)
            coverageFailures.Add("authoritative-hits-below-minimum");
        if (facts.Shots.CorrelatedRootShots > 0)
        {
            double predictedRate = facts.Shots.PredictedContacts
                / (double)facts.Shots.CorrelatedRootShots;
            double authorityRate = facts.Shots.AuthoritativeHits
                / (double)facts.Shots.CorrelatedRootShots;
            if (predictedRate < HeadshotScenarioThresholds.MinimumReasonableHitRate)
                coverageFailures.Add("predicted-contact-rate-too-low");
            if (authorityRate < HeadshotScenarioThresholds.MinimumReasonableHitRate)
                coverageFailures.Add("authoritative-hit-rate-too-low");
        }

        int headshotCases = Math.Max(facts.Shots.PredictedHeadshots,
            facts.Shots.AuthoritativeHeadshots);
        if (headshotCases < HeadshotScenarioThresholds.MinimumHeadshotCases)
            evidenceFailures.Add("headshot-evidence-below-minimum");

        failures.AddRange(choreographyFailures);
        failures.AddRange(correlationFailures);
        failures.AddRange(coverageFailures);
        failures.AddRange(evidenceFailures);
        return new(choreographyFailures.Count == 0, correlationFailures.Count == 0,
            coverageFailures.Count == 0, evidenceFailures.Count == 0,
            failures.ToArray());
    }
}

internal sealed record RenderedWanScenarioReport(
    bool ScenarioValid, bool ChoreographyValid, bool ShotCorrelationValid,
    bool CombatCoverageValid, bool HeadshotEvidenceValid, string[] InvalidReasons,
    string VariantCoverage,
    string RangeCoverage, bool CorrectWeapon, bool AuthoritativeWeapon,
    bool ZoomObserved, bool AmmoAvailable, bool MinimumDurationReached,
    int TriggerAttempts,
    int LocalRootShots, int AuthoritativeRootShots, int CorrelatedRootShots,
    int LocalImperialistShots, int AuthoritativeImperialistShots,
    int PredictedContacts, int PredictedHeadshots, int AuthoritativeHits,
    int AuthoritativeHeadshots, int ConfirmedHeadshots, int DowngradedHeadshots,
    int PromotedHeadshots, int DeniedHeadshots, double LocalAuthorityShotRatio,
    double? HeadshotAgreementRate,
    int FramesOnTarget, int TargetAirborneFrames, int TargetMovedFrames,
    double AverageRange, float MaximumVerticalSpeed, int VerticalFrames,
    int StrafeFrames, int CloseFrames, int LongFrames,
    int StationaryBodyFrames, int StationaryHeadFrames)
{
    internal static RenderedWanScenarioReport Create(HeadshotScenarioFacts facts,
        ScenarioGateResult gate, bool correctWeapon, bool authoritativeWeapon,
        bool zoomObserved, bool ammoAvailable, bool minimumDurationReached)
    {
        int local = facts.Shots.LocalRootShots;
        int authority = facts.Shots.AuthoritativeRootShots;
        double? headshotAgreementRate = gate.Valid
            ? (facts.Shots.PredictedHeadshots == 0
                ? 0d
                : facts.Shots.ConfirmedHeadshots
                    / (double)facts.Shots.PredictedHeadshots)
            : null;
        return new(gate.Valid, gate.ChoreographyValid, gate.ShotCorrelationValid,
            gate.CombatCoverageValid, gate.HeadshotEvidenceValid, gate.FailureReasons,
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
            facts.Shots.ConfirmedHeadshots, facts.Shots.DowngradedHeadshots,
            facts.Shots.PromotedHeadshots, facts.Shots.DeniedHeadshots,
            authority == 0 ? 0 : local / (double)authority,
            headshotAgreementRate,
            facts.FramesOnTarget, facts.TargetAirborneFrames, facts.TargetMovedFrames,
            facts.AverageRange, facts.MaximumVerticalSpeed, facts.VerticalFrames,
            facts.StrafeFrames, facts.CloseFrames, facts.LongFrames,
            facts.StationaryBodyFrames, facts.StationaryHeadFrames);
    }
}
