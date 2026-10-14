using System;
using System.Collections.Generic;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods.MatchEvents;

namespace MphRead.Mods.Network;

/// <summary>N0 opt-in, process-owned evidence. Never feeds a gameplay decision.
/// Bounded buffers fail closed on overflow; only the opt-in headless drain publishes live status.
/// Wire/replay formats and normal play are unchanged.</summary>
internal static class NetCampaignEvidence
{
    private static readonly string? Path = Environment.GetEnvironmentVariable("PRIME_NET2_EVIDENCE");
    internal static bool Enabled => !String.IsNullOrWhiteSpace(Path);
    // A 30-minute eight-slot campaign can retain two rows per continuous shot.
    // This is an opt-in evidence bound, independent of gameplay/transport queues.
    internal static readonly int EvidenceRowLimit = ReadRowLimit();
    // Preserve the historical key-set bound unless the campaign explicitly
    // opts into the validated larger diagnostic budget (without soak rules).
    internal static readonly int EvidenceShotKeyLimit = Enabled
        && Environment.GetEnvironmentVariable("PRIME_NET2_EVIDENCE_ROW_LIMIT") != null ? EvidenceRowLimit : 16384;
    private static int RowLimit => EvidenceRowLimit;
    private static int ReadRowLimit()
    {
        int fallback = Environment.GetEnvironmentVariable("PRIME_NET2_SOAK") == "1" ? 1048576 : 262144;
        string? value = Environment.GetEnvironmentVariable("PRIME_NET2_EVIDENCE_ROW_LIMIT");
        if (!Enabled || value == null) return fallback;
        if (int.TryParse(value, out int parsed) && parsed is >= 262144 and <= 1048576) return parsed;
        throw new ArgumentException("PRIME_NET2_EVIDENCE_ROW_LIMIT must be between 262144 and 1048576.");
    }
    private static readonly List<Shot> Shots = new();
    private static readonly List<object> TurretOutcomes = new();
    private static readonly List<object> TimedGrants = new();
    private static readonly List<object> TimedGrantStates = new(), TimedGrantViews = new();
    private static readonly (ShotKey Life, ulong Sequence, uint Prediction, int Bucket, bool Confirmed)[,] GrantViewKeys =
        new (ShotKey, ulong, uint, int, bool)[8, 3];
    internal static void TimedGrantState(TimedGrantFact fact, string stage)
    {
        if (!Enabled) return;
        Span<byte> bytes = stackalloc byte[TimedGrantFact.Size]; fact.Write(bytes);
        Add(TimedGrantStates, new { Stage = stage, fact.Life, fact.Room, fact.Revision, Type = fact.Type.ToString(),
            fact.Sequence, fact.Spawn, fact.ContactFrame, fact.EndFrame, Reason = fact.Reason.ToString(),
            fact.AuthorityFrame, fact.Resume, ObservedFrame = NetSession.NetFrame, Payload = Convert.ToHexString(bytes) });
    }
    internal static void TimedGrantView(PlayerEntity player, TimedGrantPresentation presentation, ShotKey life, uint frame)
    {
        if (!Enabled || player.OwningScene.Services.IsReplica) return;
        for (int type = 0; type < 3; type++)
        {
            var view = player.Health > 0 && player.ModIsInPlay ? presentation.View(life, (TimedPowerUp)type, frame) : default;
            var key = (life, view.GrantSequence, view.PredictionSequence, (view.Remaining + 59) / 60, view.ConfirmedRemaining > 0);
            if (GrantViewKeys[player.SlotIndex, type] == key) continue;
            GrantViewKeys[player.SlotIndex, type] = key;
            Add(TimedGrantViews, new { Life = life, Type = ((TimedPowerUp)type).ToString(), ObservedFrame = frame,
                AuthorityFrame = presentation.AuthorityNow(frame), Owner = player.SlotIndex == NetSession.LocalSlot,
                view.Covered, view.Remaining, view.ConfirmedRemaining, view.GrantSequence, view.PredictionSequence,
                NativeRemaining = player.ModTimedVisualRemaining((TimedPowerUp)type), Alive = player.Health > 0 });
        }
    }
    private static readonly List<object> PickupClaims = new(), PickupVerdicts = new(), PickupItems = new(), PickupProofs = new();
    internal static void PickupProof(PickupContactProof proof)
    {
        if (!Enabled) return;
        var pose = proof.Pose; var claim = proof.Claim; var item = proof.Item; var verdict = proof.Verdict;
        Add(PickupProofs, new { claim.Key, claim.Spawn, Type = claim.Type.ToString(), claim.SourceFrame,
            CapturedSourceFrame = pose.SourceFrame, proof.NativeReservation,
            ClaimPosition = new[] { claim.Position.X, claim.Position.Y, claim.Position.Z }, pose.Life,
            pose.AuthorityFrame, Position = new[] { pose.Position.X, pose.Position.Y, pose.Position.Z },
            SpherePosition = new[] { pose.SpherePosition.X, pose.SpherePosition.Y, pose.SpherePosition.Z },
            pose.Radius, pose.MinHeight, pose.MaxHeight, pose.Alt, pose.Alive, pose.CanPickup,
            ItemPosition = new[] { item.Position.X, item.Position.Y, item.Position.Z }, item.Available,
            ItemAuthorityFrame = item.AuthorityFrame, proof.Window,
            verdict.DecisionFrame, verdict.ProofFrame, verdict.GrantSequence, verdict.GrantFrame,
            verdict.ExpiresAtFrame });
    }
    internal static void PickupClaim(PickupClaim claim, string stage)
    {
        if (!Enabled) return;
        Span<byte> bytes = stackalloc byte[global::MphRead.Mods.Network.PickupClaim.Size]; claim.Write(bytes);
        Add(PickupClaims, new { Stage = stage, claim.Key, claim.Spawn, Type = claim.Type.ToString(),
            claim.SourceFrame, ObservedFrame = NetSession.NetFrame, Payload = Convert.ToHexString(bytes) });
    }
    internal static void PickupVerdict(PickupVerdict verdict, string stage)
    {
        if (!Enabled) return;
        Span<byte> bytes = stackalloc byte[global::MphRead.Mods.Network.PickupVerdict.Size]; verdict.Write(bytes);
        Add(PickupVerdicts, new { Stage = stage, verdict.Claim.Key, verdict.Claim.Spawn,
            Result = verdict.Result.ToString(), verdict.DecisionFrame, verdict.ProofFrame,
            verdict.GrantSequence, verdict.GrantFrame, verdict.ExpiresAtFrame,
            ObservedFrame = NetSession.NetFrame, Payload = Convert.ToHexString(bytes) });
    }
    internal static void PickupItem(PickupItemFact fact, string stage)
    {
        if (!Enabled) return;
        Span<byte> bytes = stackalloc byte[PickupItemFact.Size]; fact.Write(bytes);
        Add(PickupItems, new { Stage = stage, fact.Spawn, Type = fact.Type.ToString(),
            fact.AuthorityFrame, fact.Available, ObservedFrame = NetSession.NetFrame, Payload = Convert.ToHexString(bytes) });
    }
    internal static void TimedGrant(TimedGrantRecord grant)
    {
        if (!Enabled) return;
        // Copy each transition now: the retained authority record later closes.
        // Process ownership preserves these facts through empty-server teardown.
        Add(TimedGrants, new { grant.Sequence, grant.Spawn, grant.Life,
            Type = grant.Type.ToString(), grant.ContactFrame, grant.SourceContactFrame,
            grant.EffectiveFrame, grant.SourceEffectiveFrame, grant.ExpiresAtFrame,
            grant.EndFrame, EndReason = grant.EndReason.ToString() });
    }
    internal static void TurretOutcome(in TurretFireOutcome fact, string stage, int cancelled = 0)
    {
        if (!Enabled) return;
        Span<byte> bytes = stackalloc byte[TurretFireOutcome.Size]; fact.Write(bytes);
        Add(TurretOutcomes, new { Stage = stage, fact.Key, fact.AuthorityFrame,
            Status = fact.Status.ToString(), Reason = fact.Reason.ToString(),
            ObservedFrame = NetSession.NetFrame, CancelledFlights = cancelled, Payload = Convert.ToHexString(bytes) });
    }
    private static readonly List<PickupObservation> Pickups = new();
    private static readonly List<Health> States = new();
    private static readonly List<FormObservation> Forms = new();
    private static readonly List<SlowState> SlowStates = new();
    private static readonly List<Fact> Facts = new();
    private static readonly List<Window> Windows = new();
    private static readonly List<object> ServerSegments = new();
    private static readonly List<NetTransportBudgetSnapshot> TransportWindows = new();
    private static readonly List<object> Readiness = new();
    private static readonly HashSet<ShotKey> Offered = new();
    private static readonly HashSet<ShotKey> Accepted = new();
    private static readonly long[] AliveFrames = new long[8];
    private static long _start, _lastWindow, _frames, _overflow, _offeredCarriers, _acceptedCarriers;
    private static bool _connectionLost, _semanticOverrun;
    private static string? _lastError;
    private static double _lastStopCheck;
    // Process-lifetime observations survive normal empty-server world teardown.
    // NetHitClaims' diagnostic counters reset when that world is disposed.
    private static long _claimReceipts, _claimsApplied;
    internal static void ClaimReceived() { if (Enabled) _claimReceipts++; }
    internal static void ClaimApplied() { if (Enabled) _claimsApplied++; }
    internal static object CaptureClaims() => new { Received = _claimReceipts, Applied = _claimsApplied,
        CountingUnit = "Process-lifetime authority claim entries received including retries; applied claim decisions" };
    private static readonly long AllocatedAtStart = GC.GetTotalAllocatedBytes();
    internal static bool RigHasEvidence => _frames >= 60 && States.Count > 0
        && (SpectatorMode.IsSpectating || !HitRig.IsSniper
            || (HitRig.Mode is HitRig.RigMode.BombSamus or HitRig.RigMode.BombKanden or HitRig.RigMode.BombLockjaw
                ? NetBombEvidence.RequestsAuthored > 0
                : Offered.Any(key => key.ShooterSlot == NetSession.LocalSlot)));
    private sealed record PickupObservation(string Stage, ulong Epoch, ushort Match, uint Frame,
        byte Slot, ushort Generation, ushort Life, int ItemId, int? SpawnerId, ushort? SpawnCount,
        ItemType Type, bool IsBot, float X, float Y, float Z);

    internal static void Pickup(PlayerEntity player, ItemInstanceEntity item)
    {
        if (!Enabled || !NetSession.Active || player.SceneServices.IsReplica
            || (!NetSession.IsAuthority && player.SlotIndex != NetSession.LocalSlot)) return;
        var position = player.Position;
        Add(Pickups, new(NetSession.IsAuthority ? "authority-native-consumption" : "owner-native-consumption",
            NetSession.AuthorityEpoch, NetSession.CurrentMatchId, NetSession.NetFrame, (byte)player.SlotIndex,
            NetPlayerLifecycle.Generation(player.SlotIndex), NetPlayerLifecycle.Get(player.SlotIndex),
            item.Id, item.Owner?.Id, item.Owner?.ModHealthState.SpawnCount, item.ItemType, player.IsBot,
            position.X, position.Y, position.Z));
    }

    private sealed record Shot(string Stage, ShotKey Key, uint SourceFrame, uint ObservedFrame,
        byte Weapon, byte Charge, FireEventKind Kind, uint ContinuousPhase, string? Reason = null,
        int OwnerHealth = -1, ushort CurrentOwnerLife = 0, string? RetiredFormSource = null, uint? AcceptedObservedFrame = null, string? AcceptedFormSource = null,
        bool? TurretEverAvailable = null, int? TurretRecoveryFrames = null);
    private sealed record FormObservation(string Stage, ulong Epoch, ushort Match, byte Slot, ushort Generation,
        ushort Life, string Source, uint ObservedFrame, string Phase, int BodyHp, int TurretHp,
        bool TurretActive, float X, float Y, float Z, float BodyX, float BodyY, float BodyZ, ushort ObservedDamageEvent, int BodyPredictionDebit, string? BodyAckSource, int? LocalSlot, bool? Spectating, bool? AuthoritySpectating);
    internal static void Form(in PlayerState state, PlayerEntity? player = null, bool local = false)
    {
        if (!Enabled || state.Form.Phase == FormPhase.Unknown) return;
        Span<byte> bytes = stackalloc byte[PlayerFormState.Size]; state.Form.Write(bytes);
        var observed = player?.ModCaptureForm() ?? state.Form;
        var body = player?.Position ?? state.Position;
        ushort sequence = player == null ? state.Form.DamageEventId
            : NetHitPrediction.TurretEvidenceSequenceFor(player.SlotIndex, state.Form.DamageEventId);
        Add(Forms, new(player == null ? "authority" : local ? "owner" : "observer",
            NetSession.AuthorityEpoch, NetSession.CurrentMatchId, state.SlotIndex, state.SlotGeneration, state.LifeId,
            Convert.ToHexString(bytes), NetSession.NetFrame, observed.Phase.ToString(), player?.Health ?? state.Health,
            player?.Halfturret.Health ?? state.Form.TurretHealth, observed.TurretActive,
            observed.TurretPosition.X, observed.TurretPosition.Y, observed.TurretPosition.Z, body.X, body.Y, body.Z, sequence, player == null ? 0 : NetHitPrediction.FormPredictionDebit(player.SlotIndex),
            player == null ? null : NetHitPrediction.FormAckSource(player.SlotIndex, state.SlotGeneration, state.LifeId),
            player == null ? null : NetSession.LocalSlot,
            player == null ? null : player.Flags2.TestFlag(PlayerFlags2.Spectating),
            player == null ? null : (state.Flags & PlayerState.FlagSpectating) != 0));
    }

    private sealed record Health(ulong Epoch, ushort Match, uint Frame, byte Slot, ushort Generation,
        ushort Life, ushort Hp, ushort TurretHp, ushort Kills, ushort Deaths, uint DamageEvent);
    private sealed record SlowState(ulong Epoch, ushort Match, uint Revision, uint Frame, ulong Context, string Payload);
    private sealed record Fact(byte Type, ulong Epoch, ushort Match, uint Id, uint Tick,
        bool Baseline, bool Award, uint AwardFrontier, string Payload);
    private sealed record Window(double Seconds, ulong Epoch, ushort Match, uint Frame, int LocalSlot,
        long Frames, long[] AliveFrames, NetTelemetrySnapshot Network, NetIntentSnapshot[] Intents,
        NetInputAgeSnapshot[] InputAges, NetSlotPresentationSnapshot[] ObserverPoses,
        uint SemanticLatest, uint SemanticPresented, uint AwardLatest, uint AwardPresented,
        string Lifecycle, string Room, int BotMask, bool Spectating);

    private static void Add<T>(List<T> rows, T row)
    {
        if (rows.Count < RowLimit) rows.Add(row); else _overflow++;
    }

    internal static void Intent(int slot, in IntentPacket intent, bool offered)
    {
        if (!Enabled) return;
        if (offered) _offeredCarriers++; else _acceptedCarriers++;
        var seen = offered ? Offered : Accepted;
        Span<byte> source = stackalloc byte[PlayerFormState.Size];
        for (int i = 0; i < intent.FireEventCount; i++)
        {
            var fire = intent.FireEvents[i];
            var key = new ShotKey(intent.AuthorityEpoch, intent.MatchId, slot,
                intent.SlotGeneration, fire.SourceLife != 0 ? fire.SourceLife : intent.LifeId, fire.ShotId);
            if (seen.Contains(key)) continue;
            if (seen.Count >= RowLimit) { _overflow++; continue; }
            seen.Add(key);
            NetCampaignDrain.Shot(key, offered);
            string? acceptedFormSource = null; uint? acceptedObserved = null; bool? everAvailable = null;
            if (!offered && fire.Kind == FireEventKind.TurretFire && NetSession.IsAuthority
                && NetFireEvents.TryTurretRecovery(key, out uint acceptedAt, out var acceptedForm, out bool available))
            {
                acceptedForm.Write(source);
                acceptedFormSource = Convert.ToHexString(source); acceptedObserved = acceptedAt; everAvailable = available;
                var owner = PlayerEntity.Players[slot];
                Form(new PlayerState { SlotIndex = (byte)slot, SlotGeneration = key.Generation, LifeId = key.LifeId,
                    Health = (ushort)owner.Health, Position = owner.Position, Form = acceptedForm });
            }
            Add(Shots, new(offered ? "offered" : "accepted", key, fire.SourceFrame,
                NetSession.NetFrame, fire.Weapon, fire.Charge, fire.Kind, fire.ContinuousPhase,
                AcceptedObservedFrame: acceptedObserved, AcceptedFormSource: acceptedFormSource,
                TurretEverAvailable: everAvailable, TurretRecoveryFrames: acceptedObserved.HasValue ? NetIncomingFireHistory.RecoveryFrames : null));
        }
    }

    internal static void SkippedContinuousPhase(PlayerEntity player, in FireEvent fire)
    {
        if (!Enabled || player.SceneServices.IsReplica) return;
        Add(Shots, new("phase-skipped", ShotKey.For(player.SlotIndex, fire.ShotId), fire.SourceFrame,
            NetSession.NetFrame, fire.Weapon, fire.Charge, fire.Kind, fire.ContinuousPhase));
    }

    internal static void Committed(PlayerEntity player, in FireEvent fire)
    {
        if (!Enabled || fire.ShotId == 0 || player.SceneServices.IsReplica) return;
        Committed(NetFireEvents.TryAcceptedKey(player, out var accepted) ? accepted
            : ShotKey.For(player.SlotIndex, fire.ShotId), fire);
    }
    internal static void Committed(ShotKey key, in FireEvent fire)
    {
        if (!Enabled || fire.ShotId == 0) return;
        Add(Shots, new("committed", key, fire.SourceFrame,
            NetSession.NetFrame, fire.Weapon, fire.Charge, fire.Kind, fire.ContinuousPhase));
    }

    internal static void Disposition(AcceptedShot shot, ShotDisposition disposition, string reason)
    {
        if (!Enabled) return;
        Add(Shots, new(disposition.ToString().ToLowerInvariant(), shot.Key, shot.Fire.SourceFrame,
            NetSession.NetFrame, shot.Fire.Weapon, shot.Fire.Charge, shot.Fire.Kind, shot.Fire.ContinuousPhase, reason));
    }
    internal static void ContinuousUnavailable(PlayerEntity owner, ShotKey key, FireEvent fire)
    {
        if (!Enabled) return;
        ushort life = NetPlayerLifecycle.Get(owner.SlotIndex);
        Add(Shots, new("rejected", key, fire.SourceFrame, NetSession.NetFrame, fire.Weapon, fire.Charge,
            fire.Kind, fire.ContinuousPhase, life != key.LifeId ? "continuous owner life retired" : "continuous owner dead",
            owner.Health, life));
    }
    internal static void TurretUnavailable(PlayerEntity owner, ShotKey key, FireEvent fire)
    {
        if (!Enabled || !NetSession.IsAuthority) return;
        var form = owner.ModCaptureForm();
        form.DamageEventId = NetDamage.Sequence(owner.SlotIndex);
        Span<byte> source = stackalloc byte[PlayerFormState.Size]; form.Write(source);
        Form(new PlayerState { SlotIndex = (byte)owner.SlotIndex, SlotGeneration = NetPlayerLifecycle.Generation(owner.SlotIndex),
            LifeId = NetPlayerLifecycle.Get(owner.SlotIndex), Health = (ushort)owner.Health, Position = owner.Position, Form = form });
        Add(Shots, new("rejected", key, fire.SourceFrame, NetSession.NetFrame, fire.Weapon, fire.Charge,
            fire.Kind, fire.ContinuousPhase, "turret source retired before native emission", owner.Health,
            NetPlayerLifecycle.Get(owner.SlotIndex), Convert.ToHexString(source)));
    }
    internal static void TurretRecoveryUnavailable(PlayerEntity owner, ShotKey key, FireEvent fire,
        uint acceptedAt, PlayerFormState acceptedForm, bool everAvailable)
    {
        if (!Enabled || !NetSession.IsAuthority) return;
        var form = owner.ModCaptureForm(); form.DamageEventId = NetDamage.Sequence(owner.SlotIndex);
        Form(new PlayerState { SlotIndex = (byte)owner.SlotIndex, SlotGeneration = key.Generation, LifeId = key.LifeId,
            Health = (ushort)owner.Health, Position = owner.Position, Form = form });
        Span<byte> terminal = stackalloc byte[PlayerFormState.Size]; form.Write(terminal);
        Span<byte> accepted = stackalloc byte[PlayerFormState.Size]; acceptedForm.Write(accepted);
        Add(Shots, new("rejected", key, fire.SourceFrame, NetSession.NetFrame, fire.Weapon, fire.Charge,
            fire.Kind, fire.ContinuousPhase, "turret unavailable throughout native recovery window", owner.Health,
            NetPlayerLifecycle.Get(owner.SlotIndex), Convert.ToHexString(terminal), acceptedAt,
            Convert.ToHexString(accepted), everAvailable, NetIncomingFireHistory.RecoveryFrames));
    }
    internal static void AdmissionRejected(ShotKey key, FireEvent fire, string reason)
    {
        if (!Enabled) return;
        Add(Shots, new("admission-rejected", key, fire.SourceFrame, NetSession.NetFrame,
            fire.Weapon, fire.Charge, fire.Kind, fire.ContinuousPhase, reason));
    }

    internal static void State(in PlayerState state, uint frame)
    {
        if (!Enabled || frame % 6 != 0) return;
        Add(States, new(NetSession.AuthorityEpoch, NetSession.CurrentMatchId, frame, state.SlotIndex,
            state.SlotGeneration, state.LifeId, state.Health, state.HalfturretHealth,
            state.Kills, state.Deaths, state.DamageEventId));
    }

    // A fast snapshot embeds cached scoreboard fields. Keep their actual slow-lane
    // identity instead of asserting that they came from the fast snapshot's frame.
    // Frame is source time at the sender and local arrival time at the receiver;
    // only epoch/match/revision joins establish the receiver's authority provenance.
    internal static void SlowSnapshot(ReadOnlySpan<byte> payload, uint frame, ulong context = 0)
    {
        if (!Enabled) return;
        Add(SlowStates, new(BinaryPrimitives.ReadUInt64LittleEndian(payload[2..]),
            BinaryPrimitives.ReadUInt16LittleEndian(payload),
            BinaryPrimitives.ReadUInt32LittleEndian(payload[10..]), frame, context, Convert.ToHexString(payload)));
    }

    internal static void Semantic(PacketType type, ReadOnlySpan<byte> payload)
    {
        if (!Enabled) return;
        // Exact, already-validated authority bytes retain event, actor and victim identities.
        if (Facts.Count >= RowLimit) { _overflow++; return; }
        if (type == PacketType.MatchSemanticEvent && MatchSemanticEventPacket.TryRead(payload, out var fact))
        {
            Facts.Add(new((byte)type, fact.AuthorityEpoch, fact.MatchId, fact.EventId, fact.Tick,
                fact.IsBaseline, false, fact.AwardFrontier, Convert.ToHexString(payload)));
            NetCampaignDrain.Fact(fact.AuthorityEpoch, fact.MatchId, fact.EventId, false, fact.IsBaseline);
        }
        else if (type == PacketType.MatchAward && MatchAwardPacket.TryRead(payload, out var award))
        {
            Facts.Add(new((byte)type, award.AuthorityEpoch, award.MatchId, award.AwardId, award.Tick,
                false, true, 0, Convert.ToHexString(payload)));
            NetCampaignDrain.Fact(award.AuthorityEpoch, award.MatchId, award.AwardId, true, false);
        }
    }

    internal static void Tick(Scene scene)
    {
        if (!Enabled || scene.Services.IsReplica) return;
        if (NetSession.IsAuthority) NetCampaignDrain.Tick(scene);
        long now = Stopwatch.GetTimestamp();
        if (_start == 0) _start = _lastWindow = now;
        _frames++;
        foreach (var player in scene.Players.Items)
            if ((uint)player.SlotIndex < 8 && player.ModIsInPlay && player.Health > 0)
                AliveFrames[player.SlotIndex]++;
        _connectionLost |= NetSession.ConnectionLost;
        _semanticOverrun |= NetSession.SemanticReceived.PresentationOverrun;
        _lastError ??= NetSession.LastError;
        if (Stopwatch.GetElapsedTime(_lastWindow, now).TotalSeconds >= 1)
        {
            CaptureWindow(now);
            _lastWindow = now;
        }
    }

    private static void CaptureWindow(long now)
    {
        Transport(NetSession.EvidenceTransport);
        var semantic = NetSession.SemanticReceived;
        Add(Windows, new(Stopwatch.GetElapsedTime(_start, now).TotalSeconds,
            NetSession.AuthorityEpoch, NetSession.CurrentMatchId, NetSession.NetFrame,
            NetSession.LocalSlot, _frames, (long[])AliveFrames.Clone(), NetSession.CaptureTelemetry(),
            NetTelemetry.Session.Select(peer => peer.Capture()).ToArray(),
            NetTelemetry.Session.Select(peer => peer.CaptureInputAge()).ToArray(),
            NetPresentationDiagnostics.Capture(),
            semantic.LatestEvent, semantic.PresentedEvent, semantic.LatestAward, semantic.PresentedAward,
            NetPlayerLifecycle.Describe(), NetSession.ServerMatch?.RoomKey ?? "",
            Enumerable.Range(0, 8).Aggregate(0, (mask, slot) => mask | (NetSession.SlotIsBot[slot] ? 1 << slot : 0)),
            SpectatorMode.IsSpectating));
    }

    internal static void Transport(NetTransport? transport)
    {
        if (!Enabled || transport == null) return;
        NetCampaignDrain.Transport(transport);
        // One report/second; supports the existing 30-minute maximum campaign.
        // No per-packet history and no file I/O until process shutdown.
        if (TransportWindows.Count < 2048) TransportWindows.Add(transport.CaptureBudget());
        else _overflow++;
    }

    internal static void Ready(int slot, ushort generation, bool rejoining, bool lateJoin,
        bool admitted, double admissionMs, double loadMs, double bootstrapMs,
        uint? authorityFrame = null, uint? baselineAuthorityFrame = null, uint? bootstrapRevision = null)
    {
        if (!Enabled) return;
        Add(Readiness, new { NetSession.AuthorityEpoch, NetSession.CurrentMatchId, Slot = slot,
            Generation = generation, Rejoining = rejoining, LateJoin = lateJoin,
            InitialAdmission = !admitted, AdmissionMilliseconds = admitted ? (double?)null : admissionMs,
            LoadMilliseconds = loadMs, BootstrapMilliseconds = bootstrapMs < 0 ? (double?)null : bootstrapMs,
            AuthorityFrame = authorityFrame, BaselineAuthorityFrame = baselineAuthorityFrame,
            BootstrapRevision = bootstrapRevision });
    }

    internal static void ServerSegment(ServerSim sim, string room)
    {
        if (!Enabled || sim.Frames == 0) return;
        Add(ServerSegments, new { Room = room, NetSession.AuthorityEpoch, NetSession.CurrentMatchId,
            sim.Frames, sim.StepFailures, sim.DroppedSteps, sim.Stalls, sim.StepAllocatedBytes,
            MeanMs = sim.StepSeconds * 1000 / sim.Frames, P50Ms = sim.StepPercentile(.5),
            P95Ms = sim.StepPercentile(.95), P99Ms = sim.StepPercentile(.99),
            P999Ms = sim.StepPercentile(.999), WorstMs = sim.WorstStepSeconds * 1000 });
    }

    // Cross-platform graceful shutdown of this isolated test server. The runner owns
    // the marker and never needs to signal a user's unrelated game/server process.
    internal static bool StopRequested(double seconds)
    {
        if (!Enabled || seconds - _lastStopCheck < 1) return false;
        _lastStopCheck = seconds;
        return File.Exists(Path + ".stop");
    }

    internal static void Write(string role, NetTransportSnapshot? transport = null, long semanticDisconnects = 0)
    {
        if (!Enabled) return;
        try
        {
            if (_start != 0) CaptureWindow(Stopwatch.GetTimestamp());
            var assembly = typeof(NetCampaignEvidence).Assembly;
            var report = new { Schema = 2, Role = role, Protocol = NetConfig.ProtocolVersion,
                AcceptedShotPlayback = NetFireEvents.AcceptedPlaybackEnabled,
                Version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
                Platform = RuntimeInformation.OSDescription, Runtime = RuntimeInformation.FrameworkDescription,
                Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                Frames = _frames, AliveFrames, Overflow = _overflow, EvidenceRowLimit,
                ConnectionLost = _connectionLost || NetSession.ConnectionLost,
                SemanticOverrun = _semanticOverrun || NetSession.SemanticReceived.PresentationOverrun,
                SemanticDisconnects = semanticDisconnects, LastError = _lastError ?? NetSession.LastError,
                OfferedCarriers = _offeredCarriers, AcceptedCarriers = _acceptedCarriers,
                AllocatedBytes = GC.GetTotalAllocatedBytes() - AllocatedAtStart,
                HeapBytes = GC.GetTotalMemory(false), WorkingSet = Environment.WorkingSet,
                Transport = transport ?? NetSession.CaptureTelemetry().Transport,
                AdmissionBudgets = NetTransport.AdmissionBudgets,
                IntentHistory = NetFireEvents.CaptureHistory(),
                ShotProof = NetShotProof.Capture(),
                TimedModifiers = NetTimedShotModifiers.Capture(),
                TimedSourceCapabilities = NetTimedSourceCapabilities.Capture(),
                TimedCaseJoins = NetTimedCampaignJoin.Capture(),
                CombatReconciliation = NetHitPrediction.Capture(),
                CombatPayments = NetCombatPayments.Capture(),
                AiClient = NetAiClient.Capture(),
                TurretOutcomes,
                TimedGrants, TimedGrantStates, TimedGrantViews, PickupClaims, PickupVerdicts, PickupItems, PickupProofs,
                CampaignClaims = CaptureClaims(),
                HitClaims = new { NetHitClaims.Declared, NetHitClaims.Applied, NetHitClaims.Duplicate,
                    NetHitClaims.RefusedDeadShooter, NetHitClaims.RefusedDeadVictim, NetHitClaims.RefusedOther,
                    NetHitClaims.Unanswered, NetHitClaims.Resends, NetHitClaims.Received, NetHitClaims.AppliedHere,
                    NetHitClaims.DuplicateHere, NetHitClaims.VoidedDeadShooter, NetHitClaims.VoidedDeadVictim,
                    NetHitClaims.RefusedHere, NetHitClaims.TooOldHere, NetHitClaims.RepeatsHere },
                Pickups,
                MovementValidation = NetMovementValidationShadow.Capture(),
                ConfirmedImpacts = NetConfirmedImpacts.Capture(),
                SelfSplash = NetSelfSplash.Capture(),
                ContinuousPayments = NetContinuousPayments.Capture(),
                Bombs = NetBombEvidence.Capture(),
                Drain = NetCampaignDrain.Capture(),
                TransportWindows, Readiness, Windows, ServerSegments, Shots, States, SlowStates, Facts, Forms };
            using var file = new FileStream(Path!, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            try { JsonSerializer.Serialize(file, report); }
            catch (ArgumentException)
            {
                // Keep the failed report inspectable without making it acceptable
                // evidence. The strict primary report remains missing/partial.
                using var invalid = new FileStream(Path + ".invalid.json", FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                JsonSerializer.Serialize(invalid, report, new JsonSerializerOptions
                { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals });
                throw;
            }
            Console.WriteLine("[net2] evidence completed");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            // Missing/partial evidence is INCOMPLETE in the runner, never a green run.
            Console.WriteLine($"[net2] evidence failed: {ex.Message}");
        }
    }
}
