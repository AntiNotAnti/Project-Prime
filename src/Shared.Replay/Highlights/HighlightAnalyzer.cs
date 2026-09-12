using System;
using System.Collections.Generic;
using System.Linq;

namespace MphRead.Mods.Network;

public enum HighlightKind : byte
{
    Kill = 1,
    Headshot,
    DoubleKill,
    TripleKill,
    FirstHunt,
    PrimeSlayer,
    ObjectiveCapture,
    NodeCapture,
    Defender,
    Interceptor,
    Assist,
    PrimeChange,
    MatchPoint,
    Overtime,
    MatchEnd
}

public enum ReplayHighlightSourceKind : byte
{
    Kill = 1,
    MatchAward,
    MatchSemantic
}

public readonly record struct ReplayHighlightEvent
{
    public ReplayHighlightEvent(uint recordingFrame, uint authoritativeTick,
        uint matchId, uint eventId, uint sequenceId, CombatActor focus,
        HighlightKind kind, ReplayMarker markers, ReplayHighlightSourceKind source)
    {
        if (matchId == 0 || eventId == 0 || (!focus.IsValid && !focus.IsNone)
            || !HighlightScoringPolicy.IsSupported(kind)
            || source is < ReplayHighlightSourceKind.Kill
                or > ReplayHighlightSourceKind.MatchSemantic
            || ((ushort)markers & ~(ushort)HighlightScoringPolicy.AllowedMarkers) != 0)
        {
            throw new ArgumentException("Replay highlight event is invalid.");
        }
        RecordingFrame = recordingFrame;
        AuthoritativeTick = authoritativeTick;
        MatchId = matchId;
        EventId = eventId;
        SequenceId = sequenceId;
        Focus = focus;
        Kind = kind;
        Markers = markers;
        Source = source;
    }

    public uint RecordingFrame { get; }
    public uint AuthoritativeTick { get; }
    public uint MatchId { get; }
    public uint EventId { get; }
    public uint SequenceId { get; }
    public CombatActor Focus { get; }
    public HighlightKind Kind { get; }
    public ReplayMarker Markers { get; }
    public ReplayHighlightSourceKind Source { get; }
}

public readonly record struct ReplayHighlightTimelineAnchor
{
    public ReplayHighlightTimelineAnchor(uint recordingFrame, uint authoritativeTick,
        uint matchId)
    {
        if (matchId == 0) throw new ArgumentOutOfRangeException(nameof(matchId));
        RecordingFrame = recordingFrame;
        AuthoritativeTick = authoritativeTick;
        MatchId = matchId;
    }

    public uint RecordingFrame { get; }
    public uint AuthoritativeTick { get; }
    public uint MatchId { get; }
}

public readonly record struct ReplayHighlight
{
    public ReplayHighlight(uint startFrame, uint focusFrame, uint endFrame,
        uint authoritativeTick, CombatActor focus, HighlightKind kind,
        int score, ReplayMarker markers, string label)
    {
        if (startFrame > focusFrame || focusFrame > endFrame
            || (!focus.IsValid && !focus.IsNone)
            || !HighlightScoringPolicy.IsSupported(kind)
            || score is <= 0 or > HighlightScoringPolicy.MaximumScore
            || ((ushort)markers & ~(ushort)HighlightScoringPolicy.AllowedMarkers) != 0
            || label != HighlightScoringPolicy.Label(kind, markers)
            || label.Length > HighlightScoringPolicy.MaximumLabelLength)
        {
            throw new ArgumentException("Replay highlight is invalid.");
        }
        StartFrame = startFrame;
        FocusFrame = focusFrame;
        EndFrame = endFrame;
        AuthoritativeTick = authoritativeTick;
        Focus = focus;
        Kind = kind;
        Score = score;
        Markers = markers;
        Label = label;
    }

    public uint StartFrame { get; }
    public uint FocusFrame { get; }
    public uint EndFrame { get; }
    public uint AuthoritativeTick { get; }
    public CombatActor Focus { get; }
    public HighlightKind Kind { get; }
    public int Score { get; }
    public ReplayMarker Markers { get; }
    public string Label { get; }
}

public static class HighlightScoringPolicy
{
    public const int MaximumScore = 4096;
    public const int MaximumLabelLength = 32;
    public const ReplayMarker AllowedMarkers = ReplayMarker.Kill
        | ReplayMarker.Headshot | ReplayMarker.MultiKill | ReplayMarker.FlagCapture
        | ReplayMarker.NodeCapture | ReplayMarker.PrimeChange
        | ReplayMarker.MatchPoint | ReplayMarker.Overtime | ReplayMarker.MatchEnd
        | ReplayMarker.Award;

    public static bool IsSupported(HighlightKind kind)
        => kind is >= HighlightKind.Kill and <= HighlightKind.MatchEnd;

    public static int BaseScore(HighlightKind kind) => kind switch
    {
        HighlightKind.TripleKill => 100,
        HighlightKind.MatchEnd => 95,
        HighlightKind.DoubleKill => 80,
        HighlightKind.PrimeSlayer => 75,
        HighlightKind.ObjectiveCapture => 70,
        HighlightKind.NodeCapture => 65,
        HighlightKind.Overtime => 65,
        HighlightKind.MatchPoint => 60,
        HighlightKind.Defender or HighlightKind.Interceptor => 50,
        HighlightKind.Headshot => 40,
        HighlightKind.FirstHunt => 35,
        HighlightKind.PrimeChange => 30,
        HighlightKind.Kill => 20,
        HighlightKind.Assist => 10,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    public static string Label(HighlightKind kind, ReplayMarker markers)
    {
        bool headshot = (markers & ReplayMarker.Headshot) != 0;
        if (kind == HighlightKind.ObjectiveCapture
            && (markers & ReplayMarker.Overtime) != 0) return "OVERTIME CAPTURE";
        if (kind == HighlightKind.TripleKill
            && (markers & (ReplayMarker.FlagCapture
                | ReplayMarker.NodeCapture)) != 0) return "TRIPLE KILL + OBJECTIVE";
        if (kind == HighlightKind.PrimeSlayer
            && (markers & ReplayMarker.MatchEnd) != 0) return "PRIME SLAYER FINALE";
        if (kind is HighlightKind.Kill or HighlightKind.Headshot
            && (markers & ReplayMarker.MatchPoint) != 0) return "MATCH POINT KILL";
        if (kind == HighlightKind.DoubleKill && headshot)
            return "DOUBLE KILL HEADSHOT";
        return kind switch
        {
            HighlightKind.Kill => "KILL",
            HighlightKind.Headshot => "HEADSHOT",
            HighlightKind.DoubleKill => "DOUBLE KILL",
            HighlightKind.TripleKill => "TRIPLE KILL",
            HighlightKind.FirstHunt => "FIRST HUNT",
            HighlightKind.PrimeSlayer => "PRIME SLAYER",
            HighlightKind.ObjectiveCapture => "OBJECTIVE CAPTURE",
            HighlightKind.NodeCapture => "NODE CAPTURE",
            HighlightKind.Defender => "DEFENDER",
            HighlightKind.Interceptor => "INTERCEPTOR",
            HighlightKind.Assist => "ASSIST",
            HighlightKind.PrimeChange => "PRIME CHANGE",
            HighlightKind.MatchPoint => "MATCH POINT",
            HighlightKind.Overtime => "OVERTIME",
            HighlightKind.MatchEnd => "MATCH END",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    internal static int ContextBonus(ReplayMarker markers, HighlightKind kind)
    {
        int bonus = 0;
        bool kill = (markers & ReplayMarker.Kill) != 0;
        bool headshot = (markers & ReplayMarker.Headshot) != 0;
        bool objective = (markers & (ReplayMarker.FlagCapture
            | ReplayMarker.NodeCapture)) != 0;
        if (kill && headshot) bonus += 10;
        if (kind == HighlightKind.DoubleKill && headshot) bonus += 10;
        if (kind == HighlightKind.TripleKill && objective) bonus += 20;
        if (kill && (markers & ReplayMarker.MatchPoint) != 0) bonus += 10;
        if (objective && (markers & ReplayMarker.Overtime) != 0) bonus += 15;
        if (kind == HighlightKind.PrimeSlayer
            && (markers & ReplayMarker.MatchEnd) != 0) bonus += 15;
        return bonus;
    }
}

public sealed class HighlightAnalyzer
{
    public const int Version = 2;
    public const int MaximumHighlights = 8;
    public const int MaximumEvents = 65536;
    public const int MaximumTimelinePoints = 131072;
    public const uint DefaultLeadFrames = 240;
    public const uint DefaultTailFrames = 120;
    private const uint NearbyHighlightFrames = DefaultLeadFrames
        + DefaultTailFrames;

    private enum Family : byte { Combat, Objective, Match, Prime }

    private sealed class Group
    {
        public required uint MatchId;
        public required uint Start;
        public required uint FocusFrame;
        public required uint End;
        public required ReplayHighlightEvent Featured;
        public required Family Family;
        public required ReplayMarker Markers;
        public required int Score;
        public required uint LastAuthoritativeTick;
        public readonly HashSet<(ReplayHighlightSourceKind Source, uint EventId)> Sources = new();
        public readonly HashSet<uint> Sequences = new();
    }

    public IReadOnlyList<ReplayHighlight> Analyze(
        IReadOnlyList<ReplayHighlightEvent> events,
        IReadOnlyList<ReplayHighlightTimelineAnchor> timeline,
        uint durationFrames)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(timeline);
        if (events.Count > MaximumEvents)
            throw new ArgumentOutOfRangeException(nameof(events));
        if (timeline.Count > MaximumTimelinePoints)
            throw new ArgumentOutOfRangeException(nameof(timeline));

        Dictionary<uint, ReplayHighlightTimelineAnchor[]> timelineByMatch = timeline
            .GroupBy(point => point.MatchId)
            .ToDictionary(group => group.Key, group => group
                .OrderBy(point => point.RecordingFrame)
                .ThenBy(point => point.AuthoritativeTick).ToArray());

        var candidates = new List<Group>(events.Count);
        var seen = new HashSet<(uint MatchId, ReplayHighlightSourceKind Source,
            uint EventId)>();
        foreach (ReplayHighlightEvent value in events)
        {
            if (!seen.Add((value.MatchId, value.Source, value.EventId))) continue;
            uint focus = MapFrame(value, timelineByMatch, durationFrames);
            uint start = focus > DefaultLeadFrames ? focus - DefaultLeadFrames : 0;
            uint end = Math.Min(durationFrames,
                focus > uint.MaxValue - DefaultTailFrames
                    ? uint.MaxValue : focus + DefaultTailFrames);
            var group = new Group
            {
                MatchId = value.MatchId,
                Start = start,
                FocusFrame = focus,
                End = end,
                Featured = value,
                Family = GetFamily(value.Kind),
                Markers = value.Markers,
                Score = HighlightScoringPolicy.BaseScore(value.Kind),
                LastAuthoritativeTick = value.AuthoritativeTick
            };
            group.Sources.Add((value.Source, value.EventId));
            if (value.SequenceId != 0) group.Sequences.Add(value.SequenceId);
            candidates.Add(group);
        }

        candidates.Sort(Chronological);
        var merged = new List<Group>(candidates.Count);
        foreach (Group candidate in candidates)
        {
            Group? target = null;
            for (int i = merged.Count - 1; i >= 0; i--)
            {
                Group existing = merged[i];
                if (existing.End < candidate.Start) break;
                if (CanMerge(existing, candidate)) { target = existing; break; }
            }
            if (target is null) merged.Add(candidate);
            else Merge(target, candidate);
        }

        Group[] ranked = merged.OrderByDescending(value => FinalScore(value))
            .ThenBy(value => value.FocusFrame)
            .ThenBy(value => value.Featured.Kind)
            .ThenBy(value => value.Featured.Focus.Slot)
            .ThenBy(value => value.Featured.Focus.ConnectionId)
            .ThenBy(value => value.Featured.Focus.Life)
            .ThenBy(value => value.Start)
            .ThenBy(value => value.End)
            .ThenBy(value => value.Featured.AuthoritativeTick)
            .ThenBy(value => value.Markers)
            .ThenBy(value => value.Featured.MatchId)
            .ThenBy(value => value.Featured.Source)
            .ThenBy(value => value.Featured.EventId).ToArray();
        IReadOnlyList<Group> composed = ComposeReel(ranked);
        var result = new List<ReplayHighlight>(composed.Count);
        foreach (Group group in composed)
        {
            int score = FinalScore(group);
            ReplayHighlightEvent featured = group.Featured;
            result.Add(new ReplayHighlight(group.Start, group.FocusFrame,
                group.End, featured.AuthoritativeTick, featured.Focus, featured.Kind,
                score, group.Markers,
                HighlightScoringPolicy.Label(featured.Kind, group.Markers)));
        }
        return result.AsReadOnly();
    }

    /// <summary>
    /// Applies a deterministic diversity pass after authoritative candidates
    /// have been merged and scored. Score remains the dominant signal; close
    /// alternatives gain small, bounded preferences for a new event family,
    /// exact actor, objective kind, and a distinct moment in the match.
    /// </summary>
    private static IReadOnlyList<Group> ComposeReel(IReadOnlyList<Group> ranked)
    {
        int count = Math.Min(MaximumHighlights, ranked.Count);
        var selected = new List<Group>(count);
        if (count == 0) return selected;

        var remaining = new List<Group>(ranked.Count);
        for (int i = 0; i < ranked.Count; i++) remaining.Add(ranked[i]);
        while (selected.Count < count)
        {
            int bestIndex = 0;
            int bestScore = CompositionScore(remaining[0], selected);
            for (int i = 1; i < remaining.Count; i++)
            {
                int score = CompositionScore(remaining[i], selected);
                // The input is already in the complete stable ranking order,
                // so retaining the first candidate is the final tie-breaker.
                if (score > bestScore)
                {
                    bestIndex = i;
                    bestScore = score;
                }
            }
            selected.Add(remaining[bestIndex]);
            remaining.RemoveAt(bestIndex);
        }
        // Selection is greedy, but the public/cache order remains the complete
        // stable score order. Theatre sorts the chosen set chronologically for
        // playback, while metadata readers can validate one durable contract.
        var chosen = new HashSet<Group>(selected);
        var ordered = new List<Group>(selected.Count);
        for (int i = 0; i < ranked.Count; i++)
        {
            if (chosen.Contains(ranked[i])) ordered.Add(ranked[i]);
        }
        return ordered;
    }

    private static int CompositionScore(Group candidate,
        IReadOnlyList<Group> selected)
    {
        int sameActor = 0;
        int sameFamily = 0;
        int sameKind = 0;
        int sameObjectiveKind = 0;
        uint nearest = uint.MaxValue;
        for (int i = 0; i < selected.Count; i++)
        {
            Group prior = selected[i];
            if (candidate.Featured.Focus.IsValid
                && prior.Featured.Focus == candidate.Featured.Focus) sameActor++;
            if (prior.Family == candidate.Family) sameFamily++;
            if (prior.Featured.Kind == candidate.Featured.Kind) sameKind++;
            if (IsObjectiveStory(candidate.Featured.Kind)
                && prior.Featured.Kind == candidate.Featured.Kind)
            {
                sameObjectiveKind++;
            }
            uint distance = candidate.FocusFrame >= prior.FocusFrame
                ? candidate.FocusFrame - prior.FocusFrame
                : prior.FocusFrame - candidate.FocusFrame;
            nearest = Math.Min(nearest, distance);
        }

        int score = FinalScore(candidate) * 32;
        if (selected.Count > 0)
        {
            if (candidate.Featured.Focus.IsValid && sameActor == 0) score += 48;
            if (sameFamily == 0) score += 32;
            if (sameKind == 0) score += 16;
            if (IsObjectiveStory(candidate.Featured.Kind)
                && sameObjectiveKind == 0) score += 24;
            score -= sameActor * 12 + sameFamily * 4 + sameKind * 4;
            if (nearest < NearbyHighlightFrames)
            {
                score -= (int)((NearbyHighlightFrames - nearest) * 20
                    / NearbyHighlightFrames);
            }
        }
        return score;
    }

    private static bool IsObjectiveStory(HighlightKind kind)
        => kind is HighlightKind.ObjectiveCapture or HighlightKind.NodeCapture
            or HighlightKind.Defender or HighlightKind.Interceptor
            or HighlightKind.PrimeChange;

    private static uint MapFrame(ReplayHighlightEvent value,
        IReadOnlyDictionary<uint, ReplayHighlightTimelineAnchor[]> timeline,
        uint duration)
    {
        if (!timeline.TryGetValue(value.MatchId,
                out ReplayHighlightTimelineAnchor[]? points)
            || points.Length == 0)
            return Math.Min(value.RecordingFrame, duration);
        int low = 0;
        int high = points.Length;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (points[middle].RecordingFrame < value.RecordingFrame) low = middle + 1;
            else high = middle;
        }
        ReplayHighlightTimelineAnchor closest = low == points.Length ? points[^1]
            : low == 0 ? points[0]
            : value.RecordingFrame - points[low - 1].RecordingFrame
                <= points[low].RecordingFrame - value.RecordingFrame
                    ? points[low - 1] : points[low];
        long tickDistance = Math.Abs((long)unchecked((int)(
            value.AuthoritativeTick - closest.AuthoritativeTick)));
        if (tickDistance > ReplayArchive.MaximumFrame)
            return Math.Min(value.RecordingFrame, duration);
        long mapped = (long)closest.RecordingFrame
            + unchecked((int)(value.AuthoritativeTick
                - closest.AuthoritativeTick));
        return (uint)Math.Clamp(mapped, 0, duration);
    }

    private static int Chronological(Group left, Group right)
    {
        int value = left.Start.CompareTo(right.Start);
        if (value != 0) return value;
        value = left.FocusFrame.CompareTo(right.FocusFrame);
        if (value != 0) return value;
        value = left.MatchId.CompareTo(right.MatchId);
        if (value != 0) return value;
        value = left.Featured.Source.CompareTo(right.Featured.Source);
        return value != 0 ? value : left.Featured.EventId.CompareTo(right.Featured.EventId);
    }

    private static bool CanMerge(Group left, Group right)
    {
        if (left.MatchId != right.MatchId || right.Start > left.End) return false;
        ReplayHighlightEvent a = left.Featured;
        ReplayHighlightEvent b = right.Featured;
        bool sameFocus = a.Focus == b.Focus;
        bool linked = b.SequenceId != 0 && left.Sequences.Contains(b.SequenceId);
        if (linked && (sameFocus || a.Focus.IsNone || b.Focus.IsNone)) return true;
        if (left.Family == Family.Combat && right.Family == Family.Combat
            && sameFocus && a.Focus.IsValid)
        {
            uint age = unchecked(b.AuthoritativeTick - left.LastAuthoritativeTick);
            return age <= AwardPolicy.KillWindowTicks;
        }
        if (sameFocus && a.Focus.IsValid
            && IsTripleObjectivePair(a.Kind, b.Kind)) return true;
        if ((a.Focus.IsNone || b.Focus.IsNone)
            && IsGlobalContextPair(a.Kind, b.Kind)) return true;
        return false;
    }

    private static bool IsTripleObjectivePair(HighlightKind left,
        HighlightKind right)
        => left == HighlightKind.TripleKill && IsObjective(right)
            || right == HighlightKind.TripleKill && IsObjective(left);

    private static bool IsGlobalContextPair(HighlightKind left,
        HighlightKind right)
        => IsObjective(left) && right == HighlightKind.Overtime
            || IsObjective(right) && left == HighlightKind.Overtime
            || IsKill(left) && right == HighlightKind.MatchPoint
            || IsKill(right) && left == HighlightKind.MatchPoint
            || left == HighlightKind.PrimeSlayer && right == HighlightKind.MatchEnd
            || right == HighlightKind.PrimeSlayer && left == HighlightKind.MatchEnd;

    private static void Merge(Group target, Group candidate)
    {
        target.Start = Math.Min(target.Start, candidate.Start);
        target.End = Math.Max(target.End, candidate.End);
        target.Markers |= candidate.Markers;
        target.LastAuthoritativeTick = candidate.LastAuthoritativeTick;
        foreach (uint sequence in candidate.Sequences) target.Sequences.Add(sequence);
        bool added = false;
        foreach ((ReplayHighlightSourceKind source, uint eventId) in candidate.Sources)
        {
            added |= target.Sources.Add((source, eventId));
        }
        if (added) target.Score = Math.Min(HighlightScoringPolicy.MaximumScore,
            target.Score + candidate.Score);
        if (IsBetterFeature(candidate.Featured, target.Featured))
        {
            target.Featured = candidate.Featured;
            target.FocusFrame = candidate.FocusFrame;
            target.Family = candidate.Family;
        }
    }

    private static bool IsBetterFeature(ReplayHighlightEvent candidate,
        ReplayHighlightEvent current)
    {
        if (candidate.Focus.IsValid != current.Focus.IsValid)
            return candidate.Focus.IsValid;
        if (candidate.Source == ReplayHighlightSourceKind.MatchSemantic
            && current.Source == ReplayHighlightSourceKind.MatchAward
            && IsObjective(candidate.Kind) && IsObjective(current.Kind)) return true;
        int score = HighlightScoringPolicy.BaseScore(candidate.Kind)
            .CompareTo(HighlightScoringPolicy.BaseScore(current.Kind));
        if (score != 0) return score > 0;
        if (candidate.RecordingFrame != current.RecordingFrame)
            return candidate.RecordingFrame < current.RecordingFrame;
        if (candidate.Kind != current.Kind) return candidate.Kind < current.Kind;
        return candidate.EventId < current.EventId;
    }

    private static int FinalScore(Group group) => Math.Min(
        HighlightScoringPolicy.MaximumScore, group.Score
            + HighlightScoringPolicy.ContextBonus(group.Markers,
                group.Featured.Kind));

    private static Family GetFamily(HighlightKind kind) => kind switch
    {
        HighlightKind.Kill or HighlightKind.Headshot or HighlightKind.DoubleKill
            or HighlightKind.TripleKill or HighlightKind.FirstHunt
            or HighlightKind.PrimeSlayer or HighlightKind.Interceptor
            or HighlightKind.Assist => Family.Combat,
        HighlightKind.ObjectiveCapture or HighlightKind.NodeCapture
            or HighlightKind.Defender => Family.Objective,
        HighlightKind.MatchPoint or HighlightKind.Overtime
            or HighlightKind.MatchEnd => Family.Match,
        HighlightKind.PrimeChange => Family.Prime,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static bool IsObjective(HighlightKind kind)
        => kind is HighlightKind.ObjectiveCapture or HighlightKind.NodeCapture;

    private static bool IsKill(HighlightKind kind)
        => kind is HighlightKind.Kill or HighlightKind.Headshot
            or HighlightKind.DoubleKill or HighlightKind.TripleKill
            or HighlightKind.FirstHunt or HighlightKind.PrimeSlayer;
}
