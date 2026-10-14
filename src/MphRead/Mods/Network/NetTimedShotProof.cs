using System;
using MphRead.Entities;

namespace MphRead.Mods.Network;

internal enum TimedGrantProofMode { Off, Shadow, Enforce }
internal enum TimedShotSource { NativeInput, AcceptedFire }
internal enum TimedShotUnavailable { None, ObservationMissing, ObservationExpired, OwnershipUnavailable,
    GrantRecordMissing, FundingMismatch, Source, Unsupported }
internal enum TimedShotRetention { Stored, Duplicate, Alias, Capacity, Invalid }
internal readonly record struct TimedShotFunding(ulong Sequence, PowerUpSpawnKey Spawn, ShotKey Life,
    TimedPowerUp Type, uint ContactFrame, uint SourceContactFrame, uint ExpiresAtFrame,
    uint ObservationAuthorityFrame);
internal readonly record struct TimedShotCertificate(ShotKey Key, FireEvent Fire, uint Room,
    uint AuthorityFrame, TimedShotSource Lane, ShotSourceEvidence Source,
    TimedGrantAnswer LedgerAnswer, TimedGrantKnowledge Knowledge, TimedShotFunding? Funding,
    TimedShotUnavailable Unavailable, int Components, TimedShotDamageTemplate Damage = default,
    TimedGrantProofMode CaptureMode = TimedGrantProofMode.Off)
{
    internal bool? DoubleHintAgrees => Knowledge == TimedGrantKnowledge.Unavailable ? null
        : Fire.DoubleAtFire == (Knowledge == TimedGrantKnowledge.Granted);
    internal bool TrustedSource => Source.Source == ShotProofReason.Verified && Source.Pose == ShotProofReason.Verified;
    internal bool AuthorizedDouble => TrustedSource && Knowledge == TimedGrantKnowledge.Granted
        && Unavailable == TimedShotUnavailable.None && Funding.HasValue;
}

// Fixed value storage, independent of mutable grants, players and component pools.
// Life changes preserve flight; occupant/room/stream changes permit cell reuse.
internal sealed class TimedShotCertificateStore
{
    internal const int PerSlot = 256;
    internal const uint Retention = 1024;
    private struct Cell { internal bool Live; internal TimedShotCertificate Value; }
    private readonly Cell[,] _cells = new Cell[8, PerSlot];
    internal long CapacityRefused, Aliases;
    private static bool Retained(uint now, uint frame) => unchecked(now - frame) <= Retention;
    internal TimedShotRetention Remember(in TimedShotCertificate value, uint now)
    {
        var key = value.Key;
        if ((uint)key.ShooterSlot >= 8 || key.AuthorityEpoch == 0 || key.MatchId == 0
            || key.ShotId == 0 || key.Generation == 0 || key.LifeId == 0
            || key.ShotId != value.Fire.ShotId || value.Fire.SourceLife != key.LifeId
            || value.Components <= 0 || !Retained(now, value.AuthorityFrame)) return TimedShotRetention.Invalid;
        int free = -1;
        for (int i = 0; i < PerSlot; i++)
        {
            ref var cell = ref _cells[key.ShooterSlot, i];
            if (cell.Live && Retained(now, cell.Value.AuthorityFrame) && cell.Value.Room == value.Room
                && cell.Value.Key == key)
            {
                if (cell.Value.Fire == value.Fire) return TimedShotRetention.Duplicate;
                Aliases++; return TimedShotRetention.Alias;
            }
            if (free < 0 && (!cell.Live || !Retained(now, cell.Value.AuthorityFrame)
                || cell.Value.Room != value.Room || cell.Value.Key.AuthorityEpoch != key.AuthorityEpoch
                || cell.Value.Key.MatchId != key.MatchId || cell.Value.Key.Generation != key.Generation)) free = i;
        }
        if (free < 0) { CapacityRefused++; return TimedShotRetention.Capacity; }
        _cells[key.ShooterSlot, free] = new Cell { Live = true, Value = value };
        return TimedShotRetention.Stored;
    }
    internal bool TryGet(ShotKey key, uint sourceFrame, uint room, uint now, out TimedShotCertificate value)
    {
        value = default;
        if ((uint)key.ShooterSlot >= 8) return false;
        for (int i = 0; i < PerSlot; i++)
        {
            ref var cell = ref _cells[key.ShooterSlot, i];
            if (cell.Live && cell.Value.Key == key && cell.Value.Fire.SourceFrame == sourceFrame
                && cell.Value.Room == room && Retained(now, cell.Value.AuthorityFrame))
            { value = cell.Value; return true; }
        }
        return false;
    }
    internal bool TryGet(ShotKey key, uint room, uint now, out TimedShotCertificate value)
    {
        value = default;
        if ((uint)key.ShooterSlot >= 8) return false;
        for (int i = 0; i < PerSlot; i++)
        {
            ref var cell = ref _cells[key.ShooterSlot, i];
            if (cell.Live && cell.Value.Key == key && cell.Value.Room == room
                && Retained(now, cell.Value.AuthorityFrame)) { value = cell.Value; return true; }
        }
        return false;
    }
    internal void VisitRetained(uint room, uint now, Action<TimedShotCertificate> accept)
    {
        for (int slot = 0; slot < 8; slot++) for (int index = 0; index < PerSlot; index++)
            if (_cells[slot, index] is { Live: true } cell && cell.Value.Room == room
                && Retained(now, cell.Value.AuthorityFrame)) accept(cell.Value);
    }
}

// Local to one Spawn invocation. Only completed AddEntity terminals contribute.
// A successful return with zero projectiles, partial exception, child or effect
// kernel does not prove a complete ordinary originating emission.
internal struct TimedShotEmission
{
    internal int AmmoType { get; private set; }
    internal int Cost { get; private set; }
    internal bool HasSpend { get; private set; }
    internal void Spent(int ammoType, int cost) { AmmoType = ammoType; Cost = cost; HasSpend = true; }
    internal int Components { get; private set; }
    internal bool Conflict { get; private set; }
    internal void Added(BeamProjectileEntity beam, BeamProjectileEntity? parent)
    {
        if (parent != null || beam.Owner is not PlayerEntity || beam.ModComponentId == 0
            || beam.Flags.TestFlag(BeamFlags.Continuous) || !float.IsFinite(beam.Lifespan)
            || beam.Lifespan > 8.5f) { Conflict = true; return; }
        var damage = TimedShotDamageTemplate.From(beam);
        if (Components == 0) { Key = beam.ModLaunchKey; Damage = damage; }
        else if (Key != beam.ModLaunchKey || !Damage.SameChannels(damage)) Conflict = true;
        Components++;
    }
    internal ShotKey Key { get; private set; }
    internal TimedShotDamageTemplate Damage { get; private set; }
}

internal static class NetTimedShotProof
{
    internal static TimedGrantProofMode Mode { get; set; } =
        Environment.GetEnvironmentVariable("PRIME_TIMED_GRANT_PROOF") switch
        // Enforcement is exercised by controlled fixtures only until the
        // per-family fallback and live/replay acceptance gates are closed.
        { "shadow" => TimedGrantProofMode.Shadow, _ => TimedGrantProofMode.Off };
    internal static bool Active => Mode != TimedGrantProofMode.Off && NetSession.Active && NetSession.IsAuthority;
    internal static bool Eligible(PlayerEntity player) => Active && !player.SceneServices.IsReplica
        && player.OwningScene.GameState.Multiplayer && (uint)player.SlotIndex < 8;
    private static TimedShotCertificateStore? _store;
    private static readonly ShotKey[,] _unavailableSourceLives = new ShotKey[8, 8];
    private static ShotKey _nativeLife;
    private static uint _nativeFrame;
    private static bool _nativeInput;
    internal static long Stored, Granted, NotGranted, Unavailable, HintMismatch, NoEmission, Unsupported;
    internal readonly struct InputScope : IDisposable
    {
        private readonly bool _entered;
        internal InputScope(bool entered) { _entered = entered; }
        public void Dispose() { if (_entered) { _nativeInput = false; _nativeLife = default; } }
    }
    internal static InputScope NativeInput(PlayerEntity player)
    {
        if (!Eligible(player) || !(player.IsBot || player.SlotIndex == NetSession.LocalSlot)) return default;
        if (_nativeInput) { _nativeInput = false; Unsupported++; return default; }
        _nativeInput = true; _nativeLife = ShotKey.For(player.SlotIndex, 0); _nativeFrame = NetSession.NetFrame;
        return new(true);
    }
    internal static TimedShotCertificate? NativeAttempt(PlayerEntity player)
    {
        if (!Eligible(player) || !_nativeInput || _nativeFrame != NetSession.NetFrame
            || _nativeLife != ShotKey.For(player.SlotIndex, 0)
            || !NetFireEvents.TryActiveAttempt(player, out var key, out var fire)
            || (key with { ShotId = 0 }) != _nativeLife) return null;
        return Snapshot(player, key, fire, TimedShotSource.NativeInput);
    }
    // Caller is the private admitted-discrete DrainAccepted lane. Direct helper
    // playback cannot supply that admission, even with an exact-looking key.
    internal static TimedShotCertificate? AcceptedAttempt(PlayerEntity player, ShotKey key, in FireEvent fire)
    {
        if (!Eligible(player) || !CurrentStream(key)
            || key.Generation != NetPlayerLifecycle.Generation(player.SlotIndex)
            || !NetFireEvents.PendingAccepted(key, fire)) return null;
        return Snapshot(player, key, fire, TimedShotSource.AcceptedFire);
    }
    private static bool CurrentStream(ShotKey key) => key.MatchId != 0 && key.AuthorityEpoch != 0
        && key.MatchId == NetSession.CurrentMatchId && key.AuthorityEpoch == NetSession.AuthorityEpoch;
    private static bool Before(uint a, uint b) => unchecked((int)(a - b)) < 0;
    private static TimedShotCertificate? Snapshot(PlayerEntity player, ShotKey key, in FireEvent fire, TimedShotSource lane)
    {
        if (fire.Kind is FireEventKind.ContinuousTick or FireEventKind.TurretFire
            || fire.Weapon is (byte)BeamType.Judicator or (byte)BeamType.OmegaCannon
            || fire.Weapon > (byte)BeamType.OmegaCannon)
        {
            Unsupported++;
            return new(key, fire, NetPowerUpLedger.Ledger?.Room ?? 0, NetSession.NetFrame,
                lane, default, new(TimedGrantKnowledge.Unavailable), TimedGrantKnowledge.Unavailable,
                null, TimedShotUnavailable.Unsupported, 0);
        }
        uint now = NetSession.NetFrame;
        var source = NetShotProof.EvaluateSource(player, key, fire, ignoreDoubleHint: true,
            nativeInput: lane == TimedShotSource.NativeInput);
        if (_unavailableSourceLives[key.ShooterSlot, key.LifeId % 8] == (key with { ShotId = 0 }))
            source = source with { Source = ShotProofReason.SourceChanged };
        var ledger = NetPowerUpLedger.Ledger;
        var answer = new TimedGrantAnswer(TimedGrantKnowledge.Unavailable);
        var knowledge = TimedGrantKnowledge.Unavailable;
        TimedShotFunding? funding = null;
        var reason = TimedShotUnavailable.ObservationMissing;
        uint room = ledger?.Room ?? 0;
        if (ledger != null && ledger.TryReadObservation(key, fire.SourceFrame, TimedPowerUp.DoubleDamage,
            out answer, out uint observed))
        {
            if (unchecked(now - observed) > NetTimedGrants.Retention) reason = TimedShotUnavailable.ObservationExpired;
            else if (answer.Knowledge == TimedGrantKnowledge.NotGranted)
            { knowledge = TimedGrantKnowledge.NotGranted; reason = TimedShotUnavailable.None; }
            else if (answer.Knowledge == TimedGrantKnowledge.Unavailable) reason = TimedShotUnavailable.OwnershipUnavailable;
            else if (!ledger.TryRecord(answer.Sequence, out var grant)) reason = TimedShotUnavailable.GrantRecordMissing;
            else if (grant.Sequence != answer.Sequence || grant.Type != TimedPowerUp.DoubleDamage
                || grant.Life != (key with { ShotId = 0 }) || grant.Spawn.Epoch != key.AuthorityEpoch
                || grant.Spawn.Match != key.MatchId || grant.Spawn.Room != room || grant.Spawn.Generation == 0
                || grant.SourceContactFrame is not uint contactSource
                || grant.ExpiresAtFrame != unchecked(grant.ContactFrame + NetTimedGrants.Duration)
                || Before(observed, grant.EffectiveFrame) || !Before(observed, grant.ExpiresAtFrame)
                || Before(fire.SourceFrame, unchecked(contactSource + 1))) reason = TimedShotUnavailable.FundingMismatch;
            else
            {
                funding = new(grant.Sequence, grant.Spawn, grant.Life, grant.Type, grant.ContactFrame,
                    contactSource, grant.ExpiresAtFrame, observed);
                knowledge = TimedGrantKnowledge.Granted; reason = TimedShotUnavailable.None;
            }
        }
        // Ownership and admission are separate facts. The Double hint never
        // replaces either; other legacy modifier contracts remain independent.
        if (reason == TimedShotUnavailable.None && (source.Source != ShotProofReason.Verified
            || source.Pose != ShotProofReason.Verified)) reason = TimedShotUnavailable.Source;
        return new(key, fire, room, now, lane, source, answer, knowledge, funding, reason, 0);
    }
    private static bool Admitted(in TimedShotCertificate value)
        => CurrentStream(value.Key) && (value.Lane == TimedShotSource.NativeInput
            ? _nativeInput && _nativeFrame == value.Fire.SourceFrame
                && _nativeLife == (value.Key with { ShotId = 0 })
            : value.Lane == TimedShotSource.AcceptedFire && NetFireEvents.PendingAccepted(value.Key, value.Fire));
    private static void InvalidateSource(ShotKey key)
    {
        if ((uint)key.ShooterSlot < 8 && NetShotSourceJournal.Birth(key).HasValue)
            _unavailableSourceLives[key.ShooterSlot, key.LifeId % 8] = key with { ShotId = 0 };
    }
    // Unknown expenditure cannot fund later timed certificates. Keep this bounded
    // uncertainty private to timed Shadow so legacy enforcement stays unchanged.
    // Previously retained certificates remain immutable.
    internal static void Abort(TimedShotCertificate? attempt)
    {
        if (Active && attempt is { } value && Admitted(value)) InvalidateSource(value.Key);
    }
    // Called only after the existing admitted continuous lane emits or aborts.
    // NET-03 will provide its accounting; NET-01 cannot invent that evidence.
    internal static void UnsupportedSource(PlayerEntity player, ShotKey key)
    {
        if (Eligible(player) && CurrentStream(key) && key.ShooterSlot == player.SlotIndex
            && key.Generation == NetPlayerLifecycle.Generation(player.SlotIndex))
            InvalidateSource(key);
    }
    internal static void Complete(TimedShotCertificate? attempt, in TimedShotEmission emission)
    {
        if (!Active || attempt is not { } value) return;
        if (!Admitted(value)) { NoEmission++; return; }
        if (value.Unavailable == TimedShotUnavailable.Unsupported || emission.Conflict
            || !emission.HasSpend || emission.Components == 0 || emission.Key != value.Key)
        { NoEmission++; Abort(attempt); return; }
        var final = value with { Components = emission.Components, Damage = emission.Damage, CaptureMode = Mode };
        if (value.Source.Cost != emission.Cost || value.Source.AmmoType != emission.AmmoType)
            final = final with { Source = final.Source with { Source = ShotProofReason.SourceChanged },
                Unavailable = TimedShotUnavailable.Source };
        // One journal, exact-shot deduplication; old proof Fired may already have
        // recorded this spend. No admission or failed/partial spawn spends here.
        NetShotSourceJournal.Fired(value.Key, value.Fire, emission.AmmoType, emission.Cost, value.Source.Cooldown);
        var result = (_store ??= new()).Remember(final, NetSession.NetFrame);
        if (result != TimedShotRetention.Stored) return;
        Stored++;
        ReplayCapture.AcceptedTimedShotCertificate(final);
        if (final.DoubleHintAgrees == false) HintMismatch++;
        if (final.Unavailable != TimedShotUnavailable.None) Unavailable++;
        else if (value.Knowledge == TimedGrantKnowledge.Granted) Granted++;
        else if (value.Knowledge == TimedGrantKnowledge.NotGranted) NotGranted++;
        else Unavailable++;
    }
    internal static void VisitRetained(uint room, uint now, Action<TimedShotCertificate> accept)
        => _store?.VisitRetained(room, now, accept);
    internal static bool TryGet(ShotKey key, uint sourceFrame, out TimedShotCertificate certificate)
    {
        certificate = default;
        return Active && CurrentStream(key)
            && (uint)key.ShooterSlot < 8 && key.Generation == NetPlayerLifecycle.Generation(key.ShooterSlot)
            && NetPowerUpLedger.Ledger is { } ledger && _store != null
            && _store.TryGet(key, sourceFrame, ledger.Room, NetSession.NetFrame, out certificate);
    }
    internal static bool TryGet(ShotKey key, out TimedShotCertificate certificate)
    {
        certificate = default;
        return Active && CurrentStream(key) && (uint)key.ShooterSlot < 8
            && key.Generation == NetPlayerLifecycle.Generation(key.ShooterSlot)
            && NetPowerUpLedger.Ledger is { } ledger && _store != null
            && _store.TryGet(key, ledger.Room, NetSession.NetFrame, out certificate);
    }
    internal static void Reset()
    { _store = null; Array.Clear(_unavailableSourceLives); _nativeInput = false; _nativeLife = default;
      NetTimedShotModifiers.Reset();
      NetTimedDamageProfile.Reset();
      Stored = Granted = NotGranted = Unavailable = HintMismatch = NoEmission = Unsupported = 0; }
}
