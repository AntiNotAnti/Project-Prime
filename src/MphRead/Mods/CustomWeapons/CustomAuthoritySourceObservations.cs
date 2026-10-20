using System;
using MphRead.Entities;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Mods.CustomWeapons;

internal sealed partial class CustomAuthorityCombatAdapter
{
    private readonly record struct AcceptedInput(PlayerEntity Player,CustomSourceInput Input,Vector3 PlacedBody,bool StableBiped,bool AcceptedPose);
    private readonly AcceptedInput?[] _acceptedInputs = new AcceptedInput?[8];
    private bool _inputPhase;
    private uint _inputWorld;
    private byte _deferredNativeDrains;
    internal bool BeginSourceInputPhase()
    {
        if (!IsEligible || _inputPhase) return false;
        Array.Clear(_acceptedInputs); _deferredNativeDrains = 0;
        _inputWorld = Now; _inputPhase = true; return true;
    }
    internal bool DeferNativeDrain(PlayerEntity player)
    {
        if (!_inputPhase || player.OwningScene != _scene || (uint)player.SlotIndex >= 8) return false;
        _deferredNativeDrains |= (byte)(1 << player.SlotIndex); return true;
    }
    internal void RecordSourceInput(PlayerEntity player,in IntentPacket intent,Vector3 placedBody,bool stableBiped,bool acceptedPose)
    {
        if (!_inputPhase || Now != _inputWorld || !CurrentOwner(player,out var owner)
            || intent.MatchId != owner.MatchId || intent.AuthorityEpoch != owner.AuthorityEpoch
            || intent.SlotGeneration != owner.Generation || intent.LifeId != owner.Life
            || NetSession.RemoteIntentAge(owner.Slot) > 30 || Spectating(player)) return;
        var input = new CustomSourceInput(owner,intent.Frame,intent.AckFrame,intent.AckSubFrame,
            intent.Position,intent.Aim,(uint)intent.Buttons);
        if (_sourceBoundary == null && (_current[owner.Slot]?.Owner != owner
            || _sourceAdmission is CustomEngineSourceAdmission engine && !engine.CanCapture(owner))) return;
        if (input.IsValid) _acceptedInputs[owner.Slot] = new(player,input,placedBody,stableBiped,acceptedPose);
    }
    internal void CompleteSourceInputPhase(bool capture = true)
    {
        if (!_inputPhase) return;
        byte drains = _deferredNativeDrains;
        try
        {
            // Freeze every qualified actor before the first accepted projectile
            // drain. ApplyIntent's owner-self-death reconciliation has already run:
            // these are post-control/pre-projectile observations, not pre-damage.
            if (capture && IsEligible && _inputWorld == Now)
                foreach (var accepted in _acceptedInputs)
                    if (accepted is { } token && CurrentOwner(token.Player,out var owner)
                        && token.Input.Owner == owner && !Spectating(token.Player))
                    {
                        CustomPrecombatFacts facts = default;
                        bool hasFacts = token.AcceptedPose
                            && token.Player.ModTryCaptureCustomPrecombatFacts(token.Input.ReportedAim,out facts);
                        // A packet or action cannot grant a loadout. The exact
                        // accepted post-ready carrier must reach actual engine
                        // placement/controls before selecting a source boundary.
                        if (_sourceBoundary != null)
                        {
                            if (_current[owner.Slot]?.Owner != owner
                                && (!hasFacts || !token.StableBiped || token.Player.Position != token.PlacedBody)) continue;
                            if (!_sourceBoundary.TryOpen(token.Input,out _)
                                || _sourceAdmission is not CustomEngineSourceAdmission admission || !admission.CanCapture(owner)) continue;
                            if (_current[owner.Slot]?.Owner != owner
                                && !BindLife(token.Player,token.Input.Frame,facts.CanUseWeapon)) continue;
                        }
                        if (_current[owner.Slot] is not { Journal: not null } life || life.Owner != owner) continue;
                        CustomObservationResult result;
                        bool canUse = false;
                        if (hasFacts)
                        {
                            facts = facts with { Body = token.PlacedBody,CanUseWeapon = facts.CanUseWeapon
                                && token.StableBiped && token.Player.Position == token.PlacedBody,
                                Modifiers = CustomSourceModifierEvidence.Capture(owner,token.Input.Frame,Now,
                                    _scene.GameState.PrimeHunter < 0,NetPowerUpLedger.Ledger) };
                            canUse = facts.CanUseWeapon;
                            result = life.Sources.Observe(token.Input,facts,Now);
                        }
                        else result = life.Sources.ObserveDenied(token.Input,Now);
                        // A trusted negative input-phase fact cancels resources
                        // without manufacturing an accepted pose. Reconsuming an
                        // older frame can neither revise permission nor promote
                        // a rejected placement after the actor thaws.
                        if (result == CustomObservationResult.Captured)
                            life.Journal.ObserveSource(owner,token.Input.Frame,Now,canUse);
                        else if (result == CustomObservationResult.Conflict)
                            life.Journal.Retire(WeaponCancellation.RulesChanged);
                    }
        }
        finally
        {
            _inputPhase = false; _deferredNativeDrains = 0; Array.Clear(_acceptedInputs);
            // Even if readiness/rules/lifecycle changed during controls, preserve
            // native draining. Ordinary rooms never enter this deferred path.
            for (int slot=0; slot<8; slot++)
                if ((drains & (1 << slot)) != 0 && slot < _scene.Players.Items.Count)
                    NetFireEvents.DrainAccepted(_scene.Players.Items[slot]);
        }
    }
    private void DiscardSourceInputPhase()
    {
        _inputPhase = false; _deferredNativeDrains = 0; Array.Clear(_acceptedInputs);
    }
    internal Scene SourceScene => _scene;
    internal bool IsCurrentSourceOwner(WeaponOwnerContext owner)
        => owner.IsValid && _slots[owner.Slot] is { } slot && CurrentOwner(slot.Player,out var current) && current == owner;
    internal CustomSourceObservationStatus ReadSourceObservation(WeaponOwnerContext owner,uint source,out CustomEngineObservation observation)
    {
        observation = default;
        return IsEligible && Find(owner) is { } life ? life.Sources.Read(owner,source,Now,out observation)
            : CustomSourceObservationStatus.Rejected;
    }
    internal bool TryGetSourceObservation(WeaponOwnerContext owner,uint source,out CustomEngineObservation observation)
    {
        observation = default;
        return IsEligible && Find(owner) is { } life && life.Sources.TryGet(owner,source,Now,out observation);
    }
}
