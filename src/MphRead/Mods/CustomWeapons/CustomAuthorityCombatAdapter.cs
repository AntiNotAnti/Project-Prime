using System;
using MphRead.Entities;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Mods.CustomWeapons;

/// <summary>Explicit developer authority-combat seam. Its supplied engine source
/// oracle is trusted, not a wire payload. This adapter does not implement that
/// oracle, readiness, ingress or transport, and cannot be enabled by a packet.
/// The source journal, private emission and canonical payment share one owner.</summary>
internal sealed partial class CustomAuthorityCombatAdapter : ICustomSourceAdmission, ICustomWeaponEmitter, ICustomNativeSelectionPolicy, IDisposable
{
    internal const int LifeCapacity = 32, DeathCapacity = 128;
    internal const uint DeathRetention = 1024;
    private sealed class Slot
    {
        internal required PlayerEntity Player;
        internal required EquipInfo Equip;
        internal required CustomSourceClock Clock;
        internal ushort Generation;
    }
    private sealed class Life
    {
        internal required WeaponOwnerContext Owner;
        internal required CustomLaunchPayments Payments;
        internal required CustomEngineSourceHistory Sources;
        internal CustomSourceActionJournal? Journal;
        internal uint? RetiredAt;
    }
    private readonly record struct Death(WeaponOwnerContext Owner,uint World,uint Observed);
    private readonly record struct ContactScope(Life Life, CustomLaunchPayments.ContactLease Lease,
        CustomShotSource Source, BeamProjectileEntity Beam, PlayerEntity Victim, CustomVictimIdentity VictimLife);
    private readonly Scene _scene;
    private readonly ushort _match;
    private readonly ulong _epoch;
    private readonly WeaponRegistry _registry;
    private readonly ICustomSourceAdmission _sourceAdmission;
    private readonly ICustomSourceBoundaryReadiness? _sourceBoundary;
    private readonly ICustomCombatDelivery? _delivery;
    private readonly bool _pickupOnly;
    private readonly Slot?[] _slots = new Slot[8];
    // Pool/flight retirement must not reset an occupant's source clock or the
    // once-per-life loadout grant fence (e.g. spectate/rejoin in the same life).
    private readonly CustomSourceClock?[] _grantClocks = new CustomSourceClock?[8];
    private readonly WeaponOwnerContext[] _grantOccupants = new WeaponOwnerContext[8];
    private readonly Life?[] _lives = new Life[LifeCapacity];
    private readonly Life?[] _current = new Life[8];
    private readonly Death?[] _deaths = new Death?[DeathCapacity];
    private ContactScope? _contact;
    private bool _damageEntered;
    private EquipInfo? _emittingEquip;
    private PlayerEntity? _emittingPlayer;
    private bool _disposed;
    internal WeaponDefinition Definition { get; }
    internal CustomWeaponManifest Manifest { get; }
    internal CustomContactOutcome? LastOutcome { get; private set; }
    internal ulong AcceptedEmissions { get; private set; }
    internal ulong TerminalContacts { get; private set; }
    internal ulong PositivePayments { get; private set; }
    internal ulong DroppedDeathRecords { get; private set; }
    internal bool IsEligible => !_disposed && NetSession.Active && NetSession.IsAuthority
        && NetSession.MatchesStream(_match,_epoch) && !_scene.Services.IsReplica
        && !DemoPlayback.IsActive && !_scene.IsReplayLab && _scene.AimTrainer == null
        && CustomWeaponMatchPolicy.TryAdmit(_scene,out _);
    private uint Now => NetSession.NetFrame;
    internal CustomAuthorityCombatAdapter(Scene scene, bool developerOptIn, ICustomSourceAdmission sourceAdmission)
        : this(scene,developerOptIn,sourceAdmission,null,null,false) { }
    internal CustomAuthorityCombatAdapter(Scene scene,bool developerOptIn,ICustomSourceReadiness readiness,ICustomCombatDelivery? delivery=null,bool pickupOnly=false)
        : this(scene,developerOptIn,null,readiness,delivery,pickupOnly) { }
    private CustomAuthorityCombatAdapter(Scene scene,bool developerOptIn,ICustomSourceAdmission? sourceAdmission,ICustomSourceReadiness? readiness,ICustomCombatDelivery? delivery,bool pickupOnly)
    {
        if (sourceAdmission == null && readiness == null) throw new ArgumentNullException(nameof(sourceAdmission));
        _scene = scene; _match = NetSession.CurrentMatchId; _epoch = NetSession.AuthorityEpoch;
        if (!developerOptIn || _match == 0 || _epoch == 0 || !IsEligible)
            throw new InvalidOperationException("Explicit authority Battle developer fixture and trusted engine source oracle required.");
        foreach (var enemy in scene.GetEnemyInstanceEntities())
            throw new InvalidOperationException("Custom campaign/enemy authority is unavailable.");
        Definition = RiftlineM7.Create(); Manifest = new(new[] { Definition });
        _registry = scene.FreezeCustomWeaponRegistry(new[] { Definition });
        _sourceBoundary = readiness as ICustomSourceBoundaryReadiness;
        _delivery=delivery;_pickupOnly=pickupOnly;
        _sourceAdmission = sourceAdmission ?? new CustomEngineSourceAdmission(this,readiness!);
    }
    private bool CurrentOwner(PlayerEntity player,out WeaponOwnerContext owner)
    {
        owner = default; int slot = player.SlotIndex;
        if (!IsEligible || player.OwningScene != _scene || (uint)slot >= 8
            || slot >= _scene.Players.Items.Count || !ReferenceEquals(_scene.Players.Items[slot],player)
            || slot >= PlayerEntity.Players.Count || !ReferenceEquals(PlayerEntity.Players[slot],player)
            || NetPlayerLifecycle.Generation(slot) == 0 || NetPlayerLifecycle.Get(slot) == 0) return false;
        owner = new(_match,_epoch,(byte)slot,NetPlayerLifecycle.Generation(slot),NetPlayerLifecycle.Get(slot)); return true;
    }
    private bool Spectating(PlayerEntity player) => player.Flags2.TestFlag(PlayerFlags2.Spectating)
        || NetSession.SlotSpectating[player.SlotIndex];
    private Life? Find(WeaponOwnerContext owner)
    { foreach (var life in _lives) if (life?.Owner == owner) return life; return null; }
    internal bool BindLife(PlayerEntity player,uint verifiedBirthSource,bool canUseWeapon)
    {
        Advance();
        if (!CurrentOwner(player,out var owner) || player.Health <= 0 || !player.ModIsInPlay || Spectating(player)
            || _current[owner.Slot] != null || Find(owner) != null) return false;
        int at = Array.FindIndex(_lives,life=>life == null);
        if (at < 0) return false;
        var slot = _slots[owner.Slot];
        var occupant = _grantOccupants[owner.Slot];
        if (_grantClocks[owner.Slot] == null || occupant.MatchId != owner.MatchId
            || occupant.AuthorityEpoch != owner.AuthorityEpoch || occupant.Generation != owner.Generation)
        {
            _grantClocks[owner.Slot] = new(owner,verifiedBirthSource,Now);
            _grantOccupants[owner.Slot] = owner;
        }
        var clock = _grantClocks[owner.Slot]!;
        if (!clock.CanBindLife(owner,verifiedBirthSource,Now)) return false;
        if (slot == null)
        {
            var pool = new BeamProjectileEntity[Definition.Mechanics.MaxActiveProjectiles];
            for (int i=0; i<pool.Length; i++) pool[i] = new(_scene);
            slot = new Slot { Player = player, Generation = owner.Generation,
                Clock = clock,
                Equip = new(CustomPrecisionAdapter.Build(Definition.Mechanics),pool) { InfiniteAmmo = true } };
            _slots[owner.Slot] = slot;
        }
        if (slot.Player != player || slot.Generation != owner.Generation) return false;
        var payments = new CustomLaunchPayments(owner,Manifest,_registry,Now);
        CustomSourceActionJournal journal;
        try { journal = new(owner,slot.Clock,Manifest,_registry,
            _pickupOnly?Array.Empty<WeaponRuntimeId>():new[] { new WeaponRuntimeId(10) },verifiedBirthSource,Now,canUseWeapon); }
        catch (ArgumentException) { payments.Dispose(); return false; }
        var life = new Life { Owner = owner,Payments = payments,Journal = journal,
            Sources = new(owner,slot.Clock,verifiedBirthSource,Now) };
        _lives[at] = _current[owner.Slot] = life; return true;
    }
    internal bool ObserveSource(WeaponOwnerContext owner,uint verifiedSourceFrame,bool canUseWeapon)
        => IsEligible && _current[owner.Slot] is { } life && life.Owner == owner
            && life.Journal?.ObserveSource(owner,verifiedSourceFrame,Now,canUseWeapon) == true;
    internal CustomQueueResult Offer(in CustomWeaponAction action,CustomResourceKnowledge knowledge=default)
    {
        if (!IsEligible || !action.IsValid || _current[action.Slot] is not { Journal: not null } life || life.Owner != action.Owner)
            return CustomQueueResult.Foreign;
        if (_delivery!=null && (!_delivery.HasActionReservation(action)
            ||!_delivery.TryKnowledge(action,out var reserved)||!reserved.Matches(knowledge))) return CustomQueueResult.Backpressure;
        return life.Journal.Offer(action,Now,knowledge);
    }
    internal bool TryProcess(PlayerEntity player,out CustomActionReceipt receipt)
    {
        receipt = default;
        if (!CurrentOwner(player,out var owner) || _current[owner.Slot] is not { Journal: not null } life || life.Owner!=owner) return false;
        if (_delivery!=null && life.Journal.TryPeek(out var next))
        {
            if (!_delivery.HasActionReservation(next)) return false;
            if (next.Kind==CustomWeaponActionKind.Fire && !_delivery.HasContactReservation(next)) return false;
            if (life.Sources.TryGet(owner,next.SourceFrame,Now,out var sample)
                && unchecked((int)(Now-sample.ObservedWorldFrame))<=0) return false;
            if(next.Kind==CustomWeaponActionKind.Fire&&_delivery.MayExecute(next)&&!_delivery.TryPrepareAudience(next))return false;
        }
        if (!life.Journal.TryProcessNext(Now,this,this,out receipt)) return false;
        if (!life.Journal.TryKnowledge(receipt.Action,out var knowledge)||_delivery?.Publish(receipt,knowledge)==false)
        { life.Journal.Retire(WeaponCancellation.RulesChanged); throw new InvalidOperationException("Reserved custom receipt publication failed."); }
        return true;
    }
    internal bool IsActionLifeActive(WeaponOwnerContext owner)
        => IsCurrentSourceOwner(owner) && _current[owner.Slot] is { Journal.IsRetired:false } life && life.Owner==owner;
    internal CustomNativeSelection ReadNativeSourceSelection(WeaponOwnerContext owner,uint source)
    {
        if (!owner.IsValid||owner.MatchId!=_match||owner.AuthorityEpoch!=_epoch)return CustomNativeSelection.Rejected;
        if (Find(owner)?.Journal is { } journal)return journal.ReadNativeSelection(owner,source);
        // A current native actor can offer its first event before the engine
        // opens custom life storage. That event waits for capture, not denial.
        return owner.Slot<_scene.Players.Items.Count
            &&CurrentOwner(_scene.Players.Items[owner.Slot],out var current)&&current==owner
            ?CustomNativeSelection.Pending:CustomNativeSelection.Rejected;
    }
    internal bool TryReleaseNativeSourceSelection(WeaponOwnerContext owner,uint source)
        =>owner.IsValid&&Find(owner)?.Journal?.TryReleaseNativeSelection(owner,source)==true;
    internal bool TryReceipt(in CustomWeaponAction action,out CustomActionReceipt receipt)
    {
        receipt=default;
        return action.IsValid && Find(action.Owner)?.Journal is { } journal && journal.TryGetReceipt(action.Sequence,out receipt) && receipt.Action==action;
    }
    internal bool HasControlWitness(in CustomWeaponAction action) => _delivery?.HasControlWitness(action)==true;
    internal bool TryControlReceipt(in CustomControlRequest request,out CustomActionReceipt receipt)
    {
        receipt=default;
        return request.IsValid && Find(request.Owner)?.Journal is { } journal
            && journal.TryGetReceipt(request.Sequence,out receipt) && request.Matches(receipt.Action)
            &&journal.TryKnowledge(receipt.Action,out var knowledge)&&knowledge.Matches(request.Knowledge);
    }
    internal bool HasRetainedControlConflict(in CustomControlRequest request)
    {
        if(!request.IsValid||Find(request.Owner)?.Journal is not {} journal)return false;
        if(journal.TryGetReceipt(request.Sequence,out var receipt))
            return !request.Matches(receipt.Action)||!journal.TryKnowledge(receipt.Action,out var knowledge)
                ||!knowledge.Matches(request.Knowledge);
        return journal.TryGetUnobserved(request.Sequence,out var denied)&&denied.Request!=request;
    }
    internal bool TryUnobservedControl(in CustomControlRequest request,out CustomUnobservedControl denied)
    {
        denied=default;
        return request.IsValid && Find(request.Owner)?.Journal is { } journal
            && journal.TryGetUnobserved(request.Sequence,out denied) && denied.Request==request;
    }
    internal bool SettleUnobserved(in CustomControlRequest request)
        => request.IsValid && Find(request.Owner)?.Journal?.SettleUnobserved(request,Now,CustomControlDenialReason.SourceUnavailable)==true;
    internal bool TryStateMetadata(PlayerEntity player,out WeaponRuntimeId selected,out uint settled,out uint committed)
    {
        selected=default; settled=committed=0;
        if (!CurrentOwner(player,out var owner) || _current[owner.Slot] is not { Journal:not null } life || life.Owner!=owner) return false;
        selected=life.Journal.Selected; settled=life.Journal.SettledSequence; committed=life.Journal.CommittedSourceFrame; return true;
    }
    internal bool TryState(PlayerEntity player,out WeaponStateSnapshot snapshot)
    {
        snapshot = default;
        return CurrentOwner(player,out var owner) && _current[owner.Slot] is { Journal: not null } life
            && life.Owner == owner && life.Journal.TryPreview(new(10),out snapshot);
    }
    // Empty life is a canonical private fact, distinct from lost readiness.
    // This read never binds a life, grants resources or advances its journal.
    internal bool TryPrivateState(PlayerEntity player,out WeaponStateSnapshot snapshot,out bool hasWeapon)
    {
        snapshot=default;hasWeapon=false;
        if(!CurrentOwner(player,out var owner)||_current[owner.Slot] is not {Journal:not null} life||life.Owner!=owner)return false;
        hasWeapon=life.Journal.TryPreview(new(10),out snapshot);
        if(!hasWeapon)snapshot=CustomStateMessage.EmptySnapshot(owner,Definition,life.Journal.CommittedSourceFrame);
        return true;
    }
    CustomSourceDecision ICustomSourceAdmission.Inspect(in CustomWeaponAction action,out CustomSourceCertificate source)
    {
        source = default;
        if (!IsEligible || _delivery?.MayExecute(action)==false) return CustomSourceDecision.Rejected;
        var decision = _sourceAdmission.Inspect(action,out source);
        if (decision == CustomSourceDecision.Verified && action.Kind == CustomWeaponActionKind.Fire
            && !TradeAllowed(action.Owner,source.TargetWorldFrame)) return CustomSourceDecision.Rejected;
        return decision;
    }
    bool ICustomWeaponEmitter.HasCapacity(WeaponDefinition definition, WeaponOwnerContext owner)
    {
        if (!IsEligible || !ReferenceEquals(definition,Definition) || Find(owner) is not { } life
            || !ReferenceEquals(_current[owner.Slot],life) || !life.Payments.CanReserveLaunch(Now)
            || _slots[owner.Slot] is not { } slot) return false;
        foreach (var beam in slot.Equip.Beams) if (beam.Lifespan <= 0) return true;
        return false;
    }
    CustomEmissionDisposition ICustomWeaponEmitter.Emit(in CustomAcceptedLaunch launch)
    {
        var owner = launch.Source.Action.Owner;
        if (!IsEligible || _emittingEquip != null || !TradeAllowed(owner,launch.Source.TargetWorldFrame)
            || _delivery!=null && !_delivery.HasContactReservation(launch.Source.Action)
            || _delivery!=null && !_delivery.HasPreparedAudience(launch.Source.Action)
            || Find(owner) is not { } life || !ReferenceEquals(_current[owner.Slot],life)
            || _slots[owner.Slot] is not { } slot || !CurrentOwner(slot.Player,out var current) || current != owner
            || _sourceAdmission.Inspect(launch.Source.Action,out var checkedSource) != CustomSourceDecision.Verified
            || checkedSource != launch.Source
            || !life.Payments.TryBeginLaunch(launch,Now,out var lease,out var source)) return CustomEmissionDisposition.NoSpawn;
        using (lease)
        {
            var equip = slot.Equip;
            equip.CustomShot = source; equip.Zoomed = launch.After.Optic != 0;
            _emittingEquip = equip; _emittingPlayer = slot.Player;
            try
            {
                if (!NetUnlagged.TryBeginCustomShot(slot.Player,launch,equip,out _))
                { life.Payments.CompleteNoSpawn(lease); return CustomEmissionDisposition.NoSpawn; }
                Vector3 aim = Spread(launch.Source.Aim,launch.After.Optic,launch.Source.Action.Sequence);
                var flags = BeamSpawnFlags.NoMuzzle;
                if (launch.Source.DoubleDamage) flags |= BeamSpawnFlags.DoubleDamage;
                else if (launch.Source.PrimeHunter) flags |= BeamSpawnFlags.PrimeHunter;
                var result = BeamProjectileEntity.SpawnWithEmission(slot.Player,equip,
                    launch.Source.MuzzleOrigin,aim,flags,slot.Player.NodeRef,_scene,out _);
                if (result == BeamResultFlags.NoSpawn)
                {
                    NetUnlagged.AbortShot();
                    if (!life.Payments.CompleteNoSpawn(lease)) throw new InvalidOperationException("No-spawn contradicted custom contact.");
                    return CustomEmissionDisposition.NoSpawn;
                }
                NetUnlagged.EndShot(slot.Player);
                if (!life.Payments.CommitEmitted(lease)) throw new InvalidOperationException("Custom emission became uncertain.");
                if(_delivery?.PublishLaunch(new(launch.Source,Now,launch.Source.MuzzleOrigin,aim))==false)
                    throw new InvalidOperationException("Prepared custom launch audience disappeared.");
                AcceptedEmissions++; return CustomEmissionDisposition.Spawned;
            }
            finally
            {
                NetUnlagged.AbortShot(); _emittingEquip = null; _emittingPlayer = null; equip.CustomShot = null;
            }
        }
    }
    private Vector3 Spread(Vector3 aim,int optic,uint sequence)
    {
        aim = aim.Normalized();
        int spread = optic == 0 ? Definition.Mechanics.HipSpreadMilliDegrees : Definition.Mechanics.ScopedSpreadMilliDegrees;
        if (spread == 0) return aim;
        uint seed = unchecked(sequence*747796405u+2891336453u);
        float angle = MathHelper.DegreesToRadians(spread/1000f)*MathF.Sqrt((seed & 65535)/65535f);
        float azimuth = ((seed >> 16)/65535f)*MathF.Tau;
        Vector3 right = MathF.Abs(aim.Y) < .99f ? Vector3.Cross(aim,Vector3.UnitY).Normalized() : Vector3.UnitX;
        Vector3 up = Vector3.Cross(right,aim).Normalized();
        return (aim*MathF.Cos(angle)+(right*MathF.Cos(azimuth)+up*MathF.Sin(azimuth))*MathF.Sin(angle)).Normalized();
    }
    internal bool OwnsEmission(EntityBase owner,EquipInfo equip)
        => IsEligible && ReferenceEquals(_emittingPlayer,owner) && ReferenceEquals(_emittingEquip,equip)
            && equip.CustomShot is { } source && ReferenceEquals(source.Definition,Definition)
            && owner is PlayerEntity player && CurrentOwner(player,out var current) && current == source.Owner;
    private bool OwnedBeamShape(BeamProjectileEntity beam,out CustomShotSource source,out Life life)
    {
        source = null!; life = null!;
        if (!IsEligible || beam.OwningScene != _scene || beam.ModCustomShot is not { } candidate
            || !ReferenceEquals(candidate.Definition,Definition) || candidate.Action == null || candidate.Certificate == null
            || beam.Owner is not PlayerEntity player || !CurrentOwner(player,out var current)
            || current.Slot != candidate.Owner.Slot || current.Generation != candidate.Owner.Generation
            || Spectating(player) || Find(candidate.Owner) is not { } retained
            || _slots[current.Slot] is not { } slot || slot.Player != player
            || beam.ModLaunchKey != default || beam.ModShotId != 0 || beam.ModLaunchMatch != 0
            || beam.ModLaunchAuthority != 0 || beam.ModLaunchGeneration != 0 || beam.ModLaunchLife != 0
            || beam.ModComponentId != 1) return false;
        foreach (var owned in slot.Equip.Beams)
            if (ReferenceEquals(beam,owned)) { source = candidate; life = retained; return true; }
        return false;
    }
    internal bool OwnsFlight(BeamProjectileEntity beam)
        => OwnedBeamShape(beam,out var source,out var life) && life.Payments.OwnsFlight(source,Now)
            && (_delivery==null || _delivery.HasContactReservation(source.Certificate!.Value.Action));
    internal bool TryEnterDamage(PlayerEntity victim,BeamProjectileEntity beam)
    {
        if (_damageEntered || !OwnsReservedContact(victim,beam)) return false;
        _damageEntered = true; return true;
    }
    internal bool OwnsDamage(PlayerEntity victim,BeamProjectileEntity beam)
        => _damageEntered && OwnsReservedContact(victim,beam);
    private bool OwnsReservedContact(PlayerEntity victim,BeamProjectileEntity beam)
    {
        if (_contact is not { } contact || contact.Beam != beam || contact.Victim != victim
            || !OwnedBeamShape(beam,out var source,out var life) || !ReferenceEquals(source,contact.Source)
            || !ReferenceEquals(life,contact.Life) || !life.Payments.OwnsContact(contact.Lease)
            || !CurrentOwner(victim,out var target)) return false;
        return target.Slot == contact.VictimLife.Slot && target.Generation == contact.VictimLife.Generation
            && target.Life == contact.VictimLife.Life;
    }
    internal void ApplyContact(PlayerEntity victim,BeamProjectileEntity beam,uint damage,DamageFlags flags,
        Vector3? direction,Vector3 point)
    {
        if (_contact != null || !OwnsFlight(beam) || !CurrentOwner(victim,out var target) || Spectating(victim)
            || !victim.ModIsInPlay || victim.Health <= 0 || NetDamage.Replaying || NetDamage.ApplyingClaim
            || NetHitPrediction.Predicting || !OwnedBeamShape(beam,out var source,out var life)) return;
        var victimLife = new CustomVictimIdentity(target.Slot,target.Generation,target.Life);
        var key = new CustomPaymentKey(source.Action!.Value,victimLife,1,
            flags.TestFlag(DamageFlags.Halfturret) ? CustomImpactChannel.Halfturret : CustomImpactChannel.Body);
        var contact = new CustomContact(key,point,flags.TestFlag(DamageFlags.Headshot));
        if (life.Payments.TryBeginContact(source,contact,Now,out var lease,out _) != CustomContactDecision.Reserved) return;
        int before = victim.Health, turretBefore = victim.Halfturret?.Health ?? 0;
        if (!TradeAllowed(source.Owner,source.Certificate!.Value.TargetWorldFrame))
        {
            // A later-arriving earlier kill can invalidate an admitted flight.
            // Its physical contact is still terminal; no target/channel retry
            // or subsequent canonical resurrection may revive this component.
            using (lease)
                if (life.Payments.CompleteContact(lease,new(before,before,turretBefore,turretBefore,0),
                    out var denied,CustomContactDisposition.TradeDenied))
                {
                    LastOutcome = denied; TerminalContacts++;
                    if (_delivery?.Publish(denied)==false) throw new InvalidOperationException("Reserved custom denied-contact publication failed.");
                }
            return;
        }
        _contact = new(life,lease,source,beam,victim,victimLife); _damageEntered = false;
        try
        {
            victim.TakeDamage(damage,flags,direction,beam);
            int after = victim.Health, turretAfter = victim.Halfturret?.Health ?? 0;
            ushort sequence = before > after || turretBefore > turretAfter ? NetDamage.AllocateCustomEvent(victim,beam) : (ushort)0;
            if (sequence != 0) NetHitClaims.NoteCustomAuthorityTiming(victim,beam,source.Certificate.Value.TargetWorldFrame);
            var health = new CustomDamageResult(before,after,turretBefore,turretAfter,sequence);
            if (!life.Payments.CompleteContact(lease,health,out var outcome))
                throw new InvalidOperationException("Canonical custom damage did not produce a consistent reserved result.");
            LastOutcome = outcome; TerminalContacts++;
            if (_delivery?.Publish(outcome)==false) throw new InvalidOperationException("Reserved custom contact publication failed.");
            if (health.BodyRemoved > 0 || health.TurretRemoved > 0) PositivePayments++;
            NoteCanonicalDeath(victim,before,source.Certificate.Value.TargetWorldFrame);
        }
        catch { life.Journal?.Retire(WeaponCancellation.RulesChanged); throw; }
        finally { _contact = null; _damageEntered = false; lease.Dispose(); }
    }
    internal bool TryGetOutcome(in CustomPaymentKey key,out CustomContactOutcome outcome)
    {
        outcome = default;
        return IsEligible && Find(key.Action.Owner) is { } life && life.Payments.TryGetOutcome(key,out outcome);
    }
    internal void NoteCanonicalDeath(PlayerEntity victim,int before,uint requestedWorld)
    {
        if (!IsEligible || before <= 0 || victim.Health != 0 || !CurrentOwner(victim,out var owner)
            || NetDamage.Replaying || NetHitPrediction.Predicting) return;
        foreach (var death in _deaths) if (death?.Owner == owner) return;
        for (int i=0; i<_deaths.Length; i++)
            if (_deaths[i] == null)
            { _deaths[i] = new(owner,requestedWorld != 0 ? requestedWorld : Now,Now); return; }
        DroppedDeathRecords++;
    }
    private bool TradeAllowed(WeaponOwnerContext owner,uint requestedWorld)
    {
        if (!IsEligible || requestedWorld == 0 || _slots[owner.Slot] is not { } slot
            || !CurrentOwner(slot.Player,out var current) || current.Generation != owner.Generation) return false;
        if (current == owner && slot.Player.Health > 0)
        {
            // Native canonical reconciliation can undo a death within this life.
            for (int i=0; i<_deaths.Length; i++) if (_deaths[i]?.Owner == owner) _deaths[i] = null;
            return true;
        }
        foreach (var death in _deaths)
            if (death?.Owner == owner) return unchecked((int)(requestedWorld-death.Value.World)) <= 0;
        return false; // A dead/retired life without retained canonical proof is unavailable.
    }
    internal void Advance()
    {
        if (_disposed) return;
        foreach(var gate in _nativeFireGates)gate?.Advance(Now);
        if (!IsEligible) { Dispose(); return; }
        for (int i=0; i<_deaths.Length; i++)
            if (_deaths[i] is { } death && unchecked(Now-death.Observed) > DeathRetention) _deaths[i] = null;
        for (int slot=0; slot<_slots.Length; slot++)
        {
            if (_slots[slot] is not { } entry) continue;
            if (!CurrentOwner(entry.Player,out var owner) || owner.Generation != entry.Generation || Spectating(entry.Player))
            { RetireSlot(slot); continue; }
            if (_current[slot] is { } life && life.Owner != owner)
            { life.Journal?.Retire(WeaponCancellation.Respawn); life.RetiredAt=Now; _current[slot] = null; }
        }
        for (int i=0; i<_lives.Length; i++)
        {
            if (_lives[i] is not { } life) continue;
            life.Payments.Advance(Now);
            if (life.Payments.IsQuarantined) life.Journal?.Retire(WeaponCancellation.RulesChanged);
            if (life.RetiredAt is { } retired&&unchecked(Now-retired)>AcceptedShotLedger.RetentionFrames
                &&!life.Payments.HasRetainedEntries)
            { life.Sources.Clear(); life.Payments.Dispose(); _lives[i] = null; }
        }
    }
    private void RetireSlot(int slot)
    {
        for (int i=0; i<_lives.Length; i++)
            if (_lives[i] is { } life && life.Owner.Slot == slot)
            { life.Journal?.Retire(WeaponCancellation.OwnerChanged); life.Sources.Clear(); life.Payments.Dispose(); _lives[i] = null; }
        _current[slot] = null;
        if (_slots[slot] is { } entry)
            foreach (var beam in entry.Equip.Beams) { beam.Destroy(); _scene.RemoveEntity(beam); }
        _slots[slot] = null;
        for (int i=0; i<_deaths.Length; i++) if (_deaths[i]?.Owner.Slot == slot) _deaths[i] = null;
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DiscardSourceInputPhase();
        for (int slot=0; slot<_slots.Length; slot++) RetireSlot(slot);
        _contact = null; _damageEntered = false; _emittingEquip = null; _emittingPlayer = null;
    }
}
