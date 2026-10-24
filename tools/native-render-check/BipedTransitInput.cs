using System;
using MphRead;
using MphRead.Entities;
using OpenTK.Mathematics;

sealed partial class CheckWindow
{
    // Independent input diagnosis only. This controller requests ordinary keys;
    // it never moves a player, edits velocity, forces a form, or changes physics.
    private bool _bipedTransit;
    private enum TransitPhase { InitialAlt, Unmorph, SettleBiped, Flight, Remorph, Complete }
    private TransitPhase _transitPhase;
    private ulong _transitPhaseBegin;
    private int _transitStable, _transitStartAltFrames, _transitBipedFlightFrames, _transitEndAltFrames;
    private bool _transitUnmorphObserved;
    private bool TransitDrivingAlt => !_bipedTransit || _transitPhase is TransitPhase.InitialAlt or TransitPhase.Remorph or TransitPhase.Complete;
    private bool ReplayDrivingAlt => _movementAlt&&(!_bipedTransit||TransitDrivingAlt);
    private bool TransitMovementAllowed => !_bipedTransit || _transitPhase != TransitPhase.Unmorph;
    private void ConfigureBipedTransit(string[] args)
    {
        _bipedTransit=Array.IndexOf(args,"-movement-biped-transit")>=0;
        if(!_bipedTransit)return;
        foreach(string flag in new[]{"-movement-local-fixture","-movement-independent-diagnostic","-movement-alt","-movement-jump-trial","-movement-require-clearance"})
            if(Array.IndexOf(args,flag)<0)throw new ArgumentException("Biped transit requires explicit independent alternate-form local input and strict clearance.");
        if(Array.IndexOf(args,"-movement-jump-frame")>=0)
            throw new ArgumentException("Biped transit waits for a real settled takeoff and selects its own ordinary jump input frame.");
    }
    private void ChangeTransitPhase(TransitPhase phase,ulong elapsed)
    {
        _transitPhase=phase;_transitPhaseBegin=elapsed;_transitStable=0;
        _movementAltCoasting=false;_movementBraking=false;
    }
    private Vector3 TransitTarget(PlayerEntity player,Vector3 original)
    {
        if(!_bipedTransit)return original;
        ulong elapsed=Scene.FrameCount-_movementBegin;
        bool stableAlt=player.IsAltForm&&!player.IsMorphing&&!player.IsUnmorphing;
        bool stableBiped=!player.IsAltForm&&!player.IsMorphing&&!player.IsUnmorphing;
        bool standing=(player.Flags1&PlayerFlags1.Standing)!=0||OwnTurretTransitSupport(player,out _);
        switch(_transitPhase)
        {
            case TransitPhase.InitialAlt:
                // Observe the actual alternate form for60frames. Sylux can hover
                // and Weavel can rest on his lower turret. Neither needs a false
                // floor-standing assertion here. Settled biped takeoff and the
                // original strict final supported arrival are still mandatory.
                _transitStable=stableAlt&&(player.Position-_movementStart).Xz.Length<.25f?_transitStable+1:0;
                _transitStartAltFrames=Math.Max(_transitStartAltFrames,_transitStable);
                if(_transitStable>=60)ChangeTransitPhase(TransitPhase.Unmorph,elapsed);
                break;
            case TransitPhase.Unmorph:
                _transitUnmorphObserved|=player.IsUnmorphing;
                if(stableBiped&&_transitUnmorphObserved)ChangeTransitPhase(TransitPhase.SettleBiped,elapsed);
                break;
            case TransitPhase.SettleBiped:
                _transitStable=stableBiped&&standing&&player.Speed.Xz.Length<.025f&&Math.Abs(player.Speed.Y)<.02f
                    &&(player.Position-_movementStart).Xz.Length<.2f?_transitStable+1:0;
                if(_transitStable>=20)
                {
                    ChangeTransitPhase(TransitPhase.Flight,elapsed);
                    _movementJumpFrame=checked((int)elapsed+1);
                }
                break;
            case TransitPhase.Flight:
                if(stableBiped)_transitBipedFlightFrames++;
                _transitStable=stableBiped&&_movementJumpObserved&&standing
                    &&(player.Position-_movementGoal).Xz.Length<.25f&&Math.Abs(player.Position.Y-_movementGoal.Y)<.35f?_transitStable+1:0;
                if(_transitStable>=20)ChangeTransitPhase(TransitPhase.Remorph,elapsed);
                break;
            case TransitPhase.Remorph:
                _transitStable=stableAlt&&standing?_transitStable+1:0;
                _transitEndAltFrames=Math.Max(_transitEndAltFrames,_transitStable);
                if(_transitStable>=30)ChangeTransitPhase(TransitPhase.Complete,elapsed);
                break;
        }
        return _transitPhase<=TransitPhase.SettleBiped?_movementStart:original;
    }
    private bool TransitMorphPulse(PlayerEntity player,ulong elapsed,bool original)
    {
        if(!_bipedTransit)return original;
        bool pulse;
        if(_transitPhase==TransitPhase.InitialAlt)
            pulse=elapsed>=40&&(elapsed-40)%30<2&&!player.IsAltForm;
        else if(_transitPhase==TransitPhase.Unmorph)
            pulse=(elapsed-_transitPhaseBegin)%30<2&&player.IsAltForm;
        else if(_transitPhase==TransitPhase.Remorph)
            pulse=(elapsed-_transitPhaseBegin)%30<2&&!player.IsAltForm;
        else return false;
        return pulse&&!player.IsMorphing&&!player.IsUnmorphing;
    }
    private bool TransitAccepted => !_bipedTransit || _transitStartAltFrames>=60&&_transitUnmorphObserved
        &&_transitBipedFlightFrames>=20&&(_movementWall||_transitPhase==TransitPhase.Complete&&_transitEndAltFrames>=30);
}
