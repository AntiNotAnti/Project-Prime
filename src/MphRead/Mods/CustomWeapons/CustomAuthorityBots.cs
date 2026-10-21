using System;
using MphRead.Entities;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Mods.CustomWeapons;

// No peer, connection, Ready or application ACK is manufactured for an AI.
// Its source contract comes from the server's real published world baseline.
internal sealed partial class CustomAuthorityCombatAdapter
{
    private sealed class AuthorityBotSource(WorldBootstrapIdentity bootstrap)
    {
        internal readonly WorldBootstrapIdentity Bootstrap = bootstrap;
        internal CustomWeaponAction? Pending;
        internal uint NextSequence = 1;
        internal uint FundedSource;
        internal bool Funded;
        internal readonly CustomWeaponAction?[] Contacts = new CustomWeaponAction?[64];
        internal readonly uint[] ContactDeadlines = new uint[64];
    }
    private Func<WeaponOwnerContext, WorldBootstrapIdentity?>? _botSourceFactory;
    internal static bool AuthorityBotsEnabled => Environment.GetEnvironmentVariable("PROJECT_PRIME_CUSTOM_WEAPON_BOTS") == "1";
    internal void ConfigureAuthorityBots(Func<WeaponOwnerContext, WorldBootstrapIdentity?> factory)
    {
        if (!AuthorityBotsEnabled || !_pickupOnly || Pickups == null || _botSourceFactory != null)
            throw new InvalidOperationException("Authority bots require an explicitly enabled pickup world.");
        _botSourceFactory = factory;
    }
    private bool IsAuthorityBot(PlayerEntity player) => AuthorityBotsEnabled && _botSourceFactory != null
        && CurrentOwner(player, out var owner) && player.IsBot && NetSession.SlotOccupied[owner.Slot]
        && NetSession.SlotIsBot[owner.Slot] && !Spectating(player);

    internal void RecordAuthorityBotInput(PlayerEntity player, in IntentPacket intent)
    {
        if (!_inputPhase || Now != _inputWorld || !IsAuthorityBot(player) || !CurrentOwner(player, out var owner)
            || intent.Frame != Now || intent.MatchId != owner.MatchId || intent.AuthorityEpoch != owner.AuthorityEpoch
            || intent.SlotGeneration != owner.Generation || intent.LifeId != owner.Life) return;
        if (_current[owner.Slot]?.Owner != owner)
        {
            if (_botSourceFactory!(owner) is not { } bootstrap || !CustomWeaponProtocol.ValidBootstrap(bootstrap)
                || bootstrap.Start != Pickups!.World.Start || bootstrap.SlotGeneration != owner.Generation
                || !player.ModTryCaptureCustomPrecombatFacts(intent.Aim, out var facts)
                || !BindLife(player, Now, facts.CanUseWeapon)) return;
            _current[owner.Slot]!.Bot = new(bootstrap);
        }
        var input = new CustomSourceInput(owner, Now, Now, 0, player.Position, intent.Aim, (uint)intent.Buttons,
            IngressWorld: Now);
        if (input.IsValid) _acceptedInputs[owner.Slot] = new(player, input, player.Position,
            !player.IsAltForm && !player.IsMorphing && !player.IsUnmorphing, true, true);
    }
    internal bool TryAuthorityBotBootstrap(WeaponOwnerContext owner, out WorldBootstrapIdentity bootstrap)
    {
        bootstrap = default;
        if (!IsEligible || Find(owner)?.Bot is not { } bot) return false;
        bootstrap = bot.Bootstrap; return true;
    }
    internal bool HasAuthorityBotReservation(in CustomWeaponAction action)
        => action.IsValid && Find(action.Owner)?.Bot is { } bot && bot.Bootstrap == action.Bootstrap && bot.Pending == action;
    internal bool CompleteAuthorityBotReceipt(in CustomActionReceipt receipt)
    {
        if (!HasAuthorityBotReservation(receipt.Action) || Find(receipt.Action.Owner)?.Bot is not { } bot) return false;
        bot.Pending = null;
        if(receipt.Emission!=CustomEmissionDisposition.Spawned) ReleaseAuthorityBotContact(CustomActionIdentity.For(receipt.Action));
        return true;
    }
    internal bool HasAuthorityBotContactReservation(in CustomWeaponAction action)
    {
        if(!action.IsValid||Find(action.Owner)?.Bot is not {} bot)return false;
        for(int i=0;i<bot.Contacts.Length;i++)
            if(bot.Contacts[i]==action&&unchecked((int)(bot.ContactDeadlines[i]-Now))>=0)return true;
        return false;
    }
    internal void ReleaseAuthorityBotContact(CustomActionIdentity identity)
    {
        if(Find(identity.Owner)?.Bot is not {} bot)return;
        for(int i=0;i<bot.Contacts.Length;i++)
            if(bot.Contacts[i] is {} action&&CustomActionIdentity.For(action)==identity)bot.Contacts[i]=null;
    }
    internal bool AuthorityBotSelected(PlayerEntity player)
        => IsAuthorityBot(player) && CurrentOwner(player, out var owner)
            && _current[owner.Slot] is { Journal: { IsRetired: false } journal, Bot: not null } life
            && life.Owner == owner && (journal.Selected.Value == 10 || life.Bot.Pending?.Kind == CustomWeaponActionKind.Equip);

    internal bool TryBotPickupTarget(PlayerEntity player, out Vector3 position)
    {
        position = default;
        if (!IsAuthorityBot(player) || Pickups is not { } pickups) return false;
        if (TryPrivateState(player, out var state, out bool owned) && owned
            && state.Magazine > 0 && state.Reserve > 0) return false;
        float best = float.PositiveInfinity; bool found = false;
        for (int i = 0; i < pickups.Layout.Count; i++)
            if (pickups.TryState(i, out var item) && item.Available)
            {
                var candidate = pickups.Layout.Placements[i].Position;
                float distance = (candidate - player.Position).LengthSquared;
                if (distance < best) { best = distance; position = candidate; found = true; }
            }
        return found;
    }
    private CustomSourceDecision InspectAuthorityBot(in CustomWeaponAction action, out CustomSourceCertificate certificate)
    {
        certificate = default;
        if (!action.IsValid || !IsCurrentSourceOwner(action.Owner) || !IsAuthorityBot(_scene.Players.Items[action.Slot])
            || Find(action.Owner) is not { Journal: not null, Bot: { } bot } life || bot.Pending != action
            || bot.Bootstrap != action.Bootstrap || action.Manifest != Manifest.Digest || action.Weapon.Value != 10
            || !bot.Funded || unchecked((int)(action.SourceFrame - bot.FundedSource)) < 0)
            return CustomSourceDecision.Rejected;
        var status = ReadSourceObservation(action.Owner, action.SourceFrame, out var sample);
        if (status != CustomSourceObservationStatus.Available)
            return status == CustomSourceObservationStatus.Missing ? CustomSourceDecision.Pending : CustomSourceDecision.Rejected;
        if (action.SourceFrame != sample.ObservedWorldFrame || sample.Input.IngressWorld != sample.ObservedWorldFrame
            || action.Kind == CustomWeaponActionKind.Fire && (!sample.CanUseWeapon
                || action.AckFrame != sample.ObservedWorldFrame || action.AckSubFrame != 0
                || Vector3.Dot(action.Direction, sample.Aim) < .99f || action.Origin != sample.MuzzleOrigin
                || !sample.Modifiers.Matches(action.Owner, action.SourceFrame, sample.ObservedWorldFrame)))
            return CustomSourceDecision.Rejected;
        if (action.Kind == CustomWeaponActionKind.Fire)
        {
            if (!NetDynamicGeometryHistory.HasHistoricalFrame(_scene, sample.ObservedWorldFrame)) return CustomSourceDecision.Pending;
            if (!NetDynamicGeometryHistory.TryTraceDistanceAtFrame(_scene, sample.TraceOrigin, sample.MuzzleOrigin,
                sample.ObservedWorldFrame, out float distance) || distance < .999f) return CustomSourceDecision.Rejected;
        }
        certificate = new(action, sample.ObservedWorldFrame, sample.ObservedWorldFrame, 0,
            sample.Body, sample.Aim, sample.CanUseWeapon,
            sample.Modifiers.DoubleKnowledge == TimedGrantKnowledge.Granted, false, sample.Crouched, sample.Tucked);
        return CustomSourceDecision.Verified;
    }
    private void ProcessAuthorityBot(PlayerEntity player, Life life, in CustomSourceInput input)
    {
        if (life.Bot is not { } bot || life.Journal is not { IsRetired: false } journal || !IsAuthorityBot(player)) return;
        // One retained action/result obligation per bot. The common journal,
        // launch payments and audience own all actual effects and bounds.
        if (bot.Pending != null) { TryProcess(player, out _); return; }
        if (!bot.Funded || unchecked((int)(input.Frame - bot.FundedSource)) < 0
            || !TryPrivateState(player, out var state, out bool owned) || !owned
            || !life.Sources.TryGet(life.Owner, input.Frame, Now, out var sample) || !sample.CanUseWeapon) return;
        CustomWeaponActionKind? kind = null; byte optic = 0;
        if (journal.Selected.Value != 10) kind = CustomWeaponActionKind.Equip;
        else if (state.Magazine == 0 && state.Reserve > 0 && state.Phase != CustomWeaponPhase.Reloading) kind = CustomWeaponActionKind.Reload;
        else if (player.AiData.CustomBotWantsFire && state.Phase == CustomWeaponPhase.Ready && state.Magazine > 0)
        {
            if (state.Optic == 0) { kind = CustomWeaponActionKind.Optic; optic = 1; }
            else kind = CustomWeaponActionKind.Fire;
        }
        if (kind == null || bot.NextSequence == uint.MaxValue) return;
        bool fire = kind == CustomWeaponActionKind.Fire;
        int reservation=-1;
        if(fire)
        {
            for(int i=0;i<bot.Contacts.Length;i++)
            {
                if(bot.Contacts[i]!=null&&unchecked((int)(Now-bot.ContactDeadlines[i]))>0)bot.Contacts[i]=null;
                if(bot.Contacts[i]==null&&reservation<0)reservation=i;
            }
            if(reservation<0)return;
        }
        var action = new CustomWeaponAction(bot.Bootstrap, Manifest.Digest, life.Owner.Slot, life.Owner.Life,
            new(10), bot.NextSequence, input.Frame, fire ? input.Frame : 0, 0, kind.Value, optic,
            fire ? sample.MuzzleOrigin : Vector3.Zero, fire ? sample.Aim : Vector3.Zero);
        bot.Pending = action;
        if(fire){bot.Contacts[reservation]=action;bot.ContactDeadlines[reservation]=unchecked(Now+(uint)Definition.Mechanics.LifetimeTicks+CustomSourceActionJournal.MaxWorldAge+16);}
        if (Offer(action) == CustomQueueResult.Queued) bot.NextSequence++;
        else {bot.Pending = null;if(fire)bot.Contacts[reservation]=null;}
    }
}
