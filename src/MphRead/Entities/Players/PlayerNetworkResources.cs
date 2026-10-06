using System;
using MphRead.Mods.Network;

namespace MphRead.Entities;

public partial class PlayerEntity
{
    private bool _networkResourcesKnown;
    private uint _lastPredictedPickup;
    private readonly ResourceSpend[] _resourceSpends = new ResourceSpend[128];
    private int _resourceSpendCursor;
    private uint _lastResourceAck, _overflowResourceFrame;
    private bool _resourceOverflow, _overflowOmega;
    private readonly record struct ResourceSpend(uint Frame, int Ua, int Missiles, int Balanced, bool Omega);

    // Network ownership belongs to the admitted local slot. Camera mode is
    // presentation state: headless/free-camera owners must keep their ledger,
    // while replicas and preview scenes must not author live predictions.
    internal bool ModOwnsLocalNetworkResources => NetSession.IsClient && !NetSession.IsAuthority
        && !_scene.Services.IsReplica && !_scene.SideScene && SlotIndex == NetSession.LocalSlot;

    internal ReplayResourceState CaptureNetworkResources(ReplayActorRef actor, uint acknowledgedFrame)
    {
        ushort weapons = 0, charges = 0;
        for (int i = 0; i < 9; i++)
        {
            if (_availableWeapons[i]) weapons |= (ushort)(1 << i);
            if (_availableCharges[i]) charges |= (ushort)(1 << i);
        }
        return new(actor, _ammo[UA], _ammo[Missiles], _balancedImperialistAmmo, weapons, charges,
            (byte)_weaponSlots[0], (byte)_weaponSlots[1], (byte)_weaponSlots[2],
            _doubleDmgTimer, _cloakTimer, _deathaltTimer, acknowledgedFrame);
    }

    internal void ResetNetworkResources()
    {
        _networkResourcesKnown = false; _lastPredictedPickup = 0;
        Array.Clear(_resourceSpends); _resourceSpendCursor = 0;
        _lastResourceAck = _overflowResourceFrame = 0; _resourceOverflow = _overflowOmega = false;
    }

    internal void NotePredictedResourcePickup(ItemInstanceEntity item)
    {
        if (ModOwnsLocalNetworkResources)
        {
            _lastPredictedPickup = _scene.Services.PlayerReplication.Frame;
            NetObjectiveSync.PredictPickup(_scene, item);
        }
    }

    internal void NotePredictedResourceSpend(int ua, int missiles, int balanced, bool omega)
    {
        if (!ModOwnsLocalNetworkResources) return;
        int uaSpent = Math.Max(0, ua - _ammo[UA]), missilesSpent = Math.Max(0, missiles - _ammo[Missiles]),
            balancedSpent = Math.Max(0, balanced - _balancedImperialistAmmo);
        if (uaSpent == 0 && missilesSpent == 0 && balancedSpent == 0 && !omega) return;
        var overwritten = _resourceSpends[_resourceSpendCursor];
        if (overwritten.Frame != 0 && NetLifecycleTracker.Newer(overwritten.Frame, _lastResourceAck))
        {
            _resourceOverflow = true; _overflowResourceFrame = overwritten.Frame;
            _overflowOmega |= overwritten.Omega;
        }
        _resourceSpends[_resourceSpendCursor] = new(_scene.Services.PlayerReplication.Frame,
            uaSpent, missilesSpent, balancedSpent, omega);
        _resourceSpendCursor = (_resourceSpendCursor + 1) % _resourceSpends.Length;
    }

    internal void ApplyNetworkResources(in ReplayResourceState value)
    {
        if (ModAuthorityOwnsResources) return;
        bool predicting = ModOwnsLocalNetworkResources;
        // The local pickup stays responsive until the server has processed its
        // carrier. Then the authority either confirms it or rolls it back.
        if (predicting && _lastPredictedPickup != 0
            && !NetLifecycleTracker.Newer(value.AcknowledgedFrame, _lastPredictedPickup)) return;
        _networkResourcesKnown = true;
        int uaSpent = 0, missilesSpent = 0, balancedSpent = 0;
        bool overflowPending = predicting && _resourceOverflow
            && NetLifecycleTracker.Newer(_overflowResourceFrame, value.AcknowledgedFrame);
        if (!overflowPending) { _resourceOverflow = _overflowOmega = false; }
        _lastResourceAck = value.AcknowledgedFrame;
        bool pendingOmega = overflowPending && _overflowOmega;
        if (predicting)
            foreach (var spend in _resourceSpends)
                if (spend.Frame != 0 && NetLifecycleTracker.Newer(spend.Frame, value.AcknowledgedFrame))
                {
                    uaSpent += spend.Ua; missilesSpent += spend.Missiles; balancedSpent += spend.Balanced;
                    pendingOmega |= spend.Omega;
                }
        // An ACK older than overwritten predictions cannot reconstruct the
        // remaining balance. Hold only those pools until that watermark is
        // acknowledged; bounded history must never grant spent ammunition.
        if (!overflowPending)
        {
            _ammo[UA] = value.Ua < 0 ? value.Ua : Math.Max(0, Math.Min(value.Ua, _ammoMax[UA]) - uaSpent);
            _ammo[Missiles] = value.Missiles < 0 ? value.Missiles : Math.Max(0, Math.Min(value.Missiles, _ammoMax[Missiles]) - missilesSpent);
            _balancedImperialistAmmo = Math.Max(0, Math.Min(value.Balanced, BalancedImperialistAmmoCap) - balancedSpent);
        }
        // Only the consumed Omega Cannon needs inventory prediction. Ordinary
        // sustained fire must not defer other weapon grants or powerup expiry.
        for (int i = 0; i < 9; i++)
        {
            bool consumed = pendingOmega && i == (int)BeamType.OmegaCannon;
            _availableWeapons[i] = !consumed && (value.Weapons & (1 << i)) != 0;
            _availableCharges[i] = !consumed && (value.Charges & (1 << i)) != 0;
        }
        _weaponSlots[0] = pendingOmega && value.Weapon0 == (byte)BeamType.OmegaCannon ? BeamType.None : (BeamType)value.Weapon0;
        _weaponSlots[1] = pendingOmega && value.Weapon1 == (byte)BeamType.OmegaCannon ? BeamType.None : (BeamType)value.Weapon1;
        _weaponSlots[2] = pendingOmega && value.Weapon2 == (byte)BeamType.OmegaCannon ? BeamType.None : (BeamType)value.Weapon2;
        _cloakTimer = value.Cloak;
        if (_cloakTimer > 0) Flags2 |= PlayerFlags2.Cloaking;
        else Flags2 &= ~PlayerFlags2.Cloaking;
        bool wasDouble = _doubleDmgTimer > 0;
        _doubleDmgTimer = value.DoubleDamage; _deathaltTimer = value.Deathalt;
        if (IsMainPlayer && wasDouble != (_doubleDmgTimer > 0))
            UpdateDoubleDamageSpeed(_doubleDmgTimer > 0 ? 1 : 0);
        if (_scene.GameState.OneInTheChamber) UpdateChamberFallback();
    }
}
