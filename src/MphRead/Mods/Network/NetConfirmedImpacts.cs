using System;
using System.Collections.Generic;
using MphRead.Entities;
using MphRead.Formats;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

/// <summary>Expendable live drawing only. No effect entities, audio, gameplay
/// callbacks, simulation clocks or replay records are owned by this class.</summary>
internal static class NetConfirmedImpacts
{
    internal static bool Enabled { get; set; } = Environment.GetEnvironmentVariable("PRIME_CONFIRMED_IMPACTS") == "1";
    internal const int Capacity = 512, RetentionFrames = 120, DrawFrames = 10;
    private struct Entry { internal bool Used; internal ConfirmedImpact Fact; internal uint Arrived; internal BeamProjectileEntity? Beam; }
    private static readonly Entry[] Entries = new Entry[Capacity];
    private static ulong _epoch;
    private static ushort _match;
    private static long _published, _accepted, _duplicates, _rejected, _overflow, _cueSubmissions, _projectileSubmissions, _evidenceOverflow;
    private static readonly List<string> PublishedFacts = new(), AcceptedFacts = new();
    // Diagnostic arms toggle only the draw seam, never author or accept a fact.
    internal static bool DrawCues = true, DrawProjectiles = true;
    internal static void Reset() { Array.Clear(Entries); _epoch = 0; _match = 0; }
    private static void Context()
    {
        if (_epoch == NetSession.AuthorityEpoch && _match == NetSession.CurrentMatchId) return;
        Reset(); _epoch = NetSession.AuthorityEpoch; _match = NetSession.CurrentMatchId;
    }
    private static void Evidence(List<string> rows, ConfirmedImpact fact)
    {
        if (!NetCampaignEvidence.Enabled) return;
        if (rows.Count >= 32768) { _evidenceOverflow++; return; }
        Span<byte> bytes = stackalloc byte[ConfirmedImpact.Size]; fact.Write(bytes);
        rows.Add(Convert.ToHexString(bytes));
    }
    internal static object Capture() => new { Enabled, Published = _published, Accepted = _accepted,
        Duplicates = _duplicates, Rejected = _rejected, Overflow = _overflow,
        CueDrawSubmissions = _cueSubmissions, ExactProjectileDrawSubmissions = _projectileSubmissions,
        EvidenceOverflow = _evidenceOverflow, PublishedFacts, AcceptedFacts,
        Visibility = "Draw submissions are not visible-pixel evidence." };

    internal static ConfirmedImpact? Describe(PlayerEntity victim, PlayerEntity? attacker,
        BeamProjectileEntity? beam, BombEntity? bomb, ushort sequence, DamageFlags damageFlags)
    {
        if (beam?.ModCustomShot != null || !Enabled || !NetSession.IsAuthority || DemoPlayback.IsActive || sequence == 0 || attacker == null) return null;
        var claim = NetHitClaims.CurrentClaimKey;
        var burn = NetBurnSource.Current;
        ShotKey key = beam?.ModLaunchKey ?? claim ?? bomb?.ModOwnerKey ?? burn?.Key ?? ShotKey.For(attacker.SlotIndex, 0);
        uint component = beam?.ModComponentId ?? (claim.HasValue ? NetHitClaims.CurrentClaimComponent : 0);
        byte weapon = beam != null ? (byte)beam.Beam : claim.HasValue ? (byte)NetDamage.ClaimedBeam : (byte)255;
        var provenance = beam?.ModResolvedHitPointValid == true ? ImpactProvenance.NativePoint
            : claim.HasValue ? ImpactProvenance.AcceptedClaimPoint : ImpactProvenance.ConfirmedBodyAnchor;
        Vector3 point = provenance == ImpactProvenance.NativePoint ? beam!.ModResolvedHitPoint
            : provenance == ImpactProvenance.AcceptedClaimPoint ? NetHitClaims.CurrentClaimHitPoint : victim.Position;
        // An absent/nonfinite endpoint is explicitly a body anchor, never an
        // invented collision point or a reason to bend an unrelated projectile.
        if (!float.IsFinite(point.X) || !float.IsFinite(point.Y) || !float.IsFinite(point.Z))
        { point = victim.Position; provenance = ImpactProvenance.ConfirmedBodyAnchor; component = 0; }
        var flags = ReplayShotFactFlags.None;
        if (damageFlags.TestFlag(DamageFlags.Headshot)) flags |= ReplayShotFactFlags.Headshot;
        if (damageFlags.TestFlag(DamageFlags.Halfturret)) flags |= ReplayShotFactFlags.HalfturretTarget;
        if (beam?.EnhancedDirectHit == true || NetHitClaims.CurrentClaimDirect) flags |= ReplayShotFactFlags.Direct;
        bool continuous = beam is { Beam: BeamType.ShockCoil, ModHasSharedContinuousPhase: true } || NetHitClaims.CurrentClaimContinuous;
        if (continuous) flags |= ReplayShotFactFlags.Continuous;
        uint phase = !continuous ? 0 : beam != null ? (uint)beam.ModContinuousPhase : NetHitClaims.CurrentClaimPhase;
        var fact = new ConfirmedImpact(key, component, phase, (byte)victim.SlotIndex,
            NetPlayerLifecycle.Generation(victim.SlotIndex), NetPlayerLifecycle.Get(victim.SlotIndex), sequence,
            NetSession.NetFrame, weapon, flags, provenance, point);
        return fact.Valid ? fact : null;
    }
    internal static void Publish(in ConfirmedImpact fact)
    {
        if (!Enabled || !NetSession.IsAuthority || DemoPlayback.IsActive || !fact.Valid
            || !NetSession.MatchesStream(fact.Key.MatchId, fact.Key.AuthorityEpoch)) return;
        _published++; Evidence(PublishedFacts, fact);
        NetSession.ConfirmedImpactSink?.Invoke(fact);
    }
    internal static bool Accept(in ConfirmedImpact fact)
    {
        if (!Enabled || !NetSession.IsClient || NetSession.IsAuthority || DemoPlayback.IsActive || !fact.Valid
            || !NetSession.MatchesStream(fact.Key.MatchId, fact.Key.AuthorityEpoch)
            || NetPlayerLifecycle.Generation(fact.Key.ShooterSlot) != fact.Key.Generation
            || NetPlayerLifecycle.Generation(fact.Victim) != fact.VictimGeneration
            || NetPlayerLifecycle.Get(fact.Victim) != fact.VictimLife)
        { _rejected++; return false; }
        Context();
        uint now = NetSession.NetFrame;
        uint authorityNow = NetSession.LastSnapshotFrame;
        int age = unchecked((int)(authorityNow - fact.Frame));
        if (authorityNow == 0) { _rejected++; return false; }
        if (age > RetentionFrames || age < -60) { _rejected++; return false; }
        int free = -1;
        for (int i = 0; i < Entries.Length; i++)
        {
            ref var entry = ref Entries[i];
            if (entry.Used && unchecked((int)(authorityNow - entry.Fact.Frame)) > RetentionFrames) entry = default;
            if (!entry.Used) { if (free < 0) free = i; continue; }
            var old = entry.Fact;
            if (old.Key == fact.Key && old.Component == fact.Component && old.Phase == fact.Phase
                && old.Victim == fact.Victim && old.VictimGeneration == fact.VictimGeneration
                && old.VictimLife == fact.VictimLife && old.DamageEvent == fact.DamageEvent)
            { _duplicates++; return false; }
        }
        if (free < 0) { _overflow++; return false; }
        Entries[free] = new() { Used = true, Fact = fact, Arrived = now };
        _accepted++; Evidence(AcceptedFacts, fact); return true;
    }
    private static bool Live(Scene scene) => Enabled && NetSession.IsClient && !NetSession.IsAuthority
        && !DemoPlayback.IsActive && !scene.Services.IsReplica && !NetSession.ConnectionLost;
    private static bool Drawable(in Entry e) => e.Used && unchecked((int)(NetSession.NetFrame - e.Arrived)) is >= 0 and <= DrawFrames
        && NetPlayerLifecycle.Generation(e.Fact.Victim) == e.Fact.VictimGeneration
        && NetPlayerLifecycle.Get(e.Fact.Victim) == e.Fact.VictimLife
        && NetPlayerLifecycle.Generation(e.Fact.Key.ShooterSlot) == e.Fact.Key.Generation;
    internal static bool Matches(in ConfirmedImpact fact, BeamProjectileEntity beam)
        => beam.ModCustomShot == null && fact.Component != 0 && fact.Key.ShotId != 0 && fact.Weapon <= (byte)BeamType.OmegaCannon
            && fact.Provenance != ImpactProvenance.ConfirmedBodyAnchor
            && (fact.Flags & ReplayShotFactFlags.Direct) != 0
            && beam.Lifespan > 0 && !beam.Flags.TestFlag(BeamFlags.Collided)
            && beam.ModLaunchKey == fact.Key && beam.ModComponentId == fact.Component && (byte)beam.Beam == fact.Weapon
            && ((fact.Flags & ReplayShotFactFlags.Continuous) == 0
                || beam.ModHasSharedContinuousPhase && (uint)beam.ModContinuousPhase == fact.Phase);
    internal static void BeginDraw(Scene scene)
    {
        if (!Live(scene)) return;
        Context();
        foreach (ref var entry in Entries.AsSpan()) entry.Beam = null;
        if (!DrawProjectiles) return;
        foreach (ref var entry in Entries.AsSpan())
        {
            if (!Drawable(entry)) continue;
            BeamProjectileEntity? candidate = null; int matches = 0;
            foreach (var entity in scene.Entities)
                if (entity is BeamProjectileEntity beam && beam.ShouldDraw && Matches(entry.Fact, beam))
                { candidate = beam; matches++; }
            if (matches != 1 || candidate == null) continue;
            Vector3 delta = entry.Fact.Point - candidate.Position;
            if (delta.LengthSquared > .000001f && (candidate.Direction.LengthSquared < .000001f
                || Vector3.Dot(delta.Normalized(), candidate.Direction.Normalized()) < .5f)) continue;
            CollisionResult wall = default;
            if (CollisionDetection.CheckBetweenPoints(candidate.Position, entry.Fact.Point, TestFlags.Beams, scene, ref wall)) continue;
            entry.Beam = candidate;
        }
    }
    internal static bool TryAnchor(BeamProjectileEntity beam, out Vector3 point)
    {
        point = default;
        if (!DrawProjectiles || !Live(beam.OwningScene)) return false;
        // Multiple accepted impacts on one component (e.g. splash/pierce) are
        // ambiguous. Keep truthful endpoints instead of choosing a target.
        int count = 0;
        foreach (ref var entry in Entries.AsSpan())
            if (Drawable(entry) && ReferenceEquals(entry.Beam, beam)) { point = entry.Fact.Point; count++; }
        if (count != 1) return false;
        _projectileSubmissions++;
        NetCampaignEvidence.Timeline("render-submitted", beam.ModLaunchKey, beam.ModComponentId,
            phase: beam.ModHasSharedContinuousPhase ? (uint)beam.ModContinuousPhase : 0, weapon: (byte)beam.Beam, result: 1);
        return true;
    }
    internal static void Draw(Scene scene)
    {
        if (!Live(scene) || !DrawCues) return;
        foreach (ref var entry in Entries.AsSpan())
            if (Drawable(entry))
            {
                // One depth-tested particle per accepted fact; repeated draws
                // render the same transient cue, without spawning effects/audio.
                using var ncxOwner = scene.NcxSourceItems(new(entry.Fact.Key, entry.Fact.Component,
                    entry.Fact.Phase, 1, entry.Fact.Victim, entry.Fact.VictimGeneration, entry.Fact.VictimLife,
                    entry.Fact.DamageEvent, entry.Fact.Weapon, (byte)entry.Fact.Provenance));
                scene.AddSingleParticle(SingleType.Fuzzball, entry.Fact.Point,
                    new Vector3(1, .8f, .3f), alpha: .85f, scale: .22f);
                _cueSubmissions++;
                NetCampaignEvidence.Timeline("render-submitted", entry.Fact.Key, entry.Fact.Component,
                    entry.Fact.Phase, victim: entry.Fact.Victim, victimGeneration: entry.Fact.VictimGeneration,
                    victimLife: entry.Fact.VictimLife, damageEventId: entry.Fact.DamageEvent,
                    weapon: entry.Fact.Weapon, result: 0);
            }
    }
}
