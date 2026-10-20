using System;
using System.Threading;
using MphRead.Entities;
using MphRead.Mods.Input;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Network;

namespace MphRead;

public partial class Scene
{
    private sealed class CustomInputSink(Scene scene,PlayerEntity player):ICustomWeaponInputSink
    {
        public bool Submit(CustomWeaponActionKind kind,byte optic,out uint sequence)
        {
            sequence=0;
            if(!NetSession.Active)
            {
                bool submitted=scene.OfflineWeapons?.SubmitInput(player,kind,optic,out sequence)==true;
                if(submitted)scene._customFeedback?.Submitted(kind,sequence);
                return submitted;
            }
            if(!NetSession.TrySubmitCustomWeaponControl(kind,optic,out var request))return false;
            sequence=request.Sequence;scene._customFeedback?.Submitted(kind,sequence);return true;
        }
    }
    private CustomWeaponInputController? _customInput;
    private CustomInputSink? _customInputSink;
    private PlayerEntity? _customInputPlayer;
    private ShotKey _customInputLife;
    private CustomWeaponSelectionMailbox? _customMenuSelections;
    internal CustomWeaponSelectionSnapshot? CustomWeaponMenuSelection => Volatile.Read(ref _customMenuSelections)?.Snapshot;
    internal bool RequestCustomWeaponMenuSelection(CustomWeaponSelectionSnapshot? expected, bool selectCustom)
        => !_exiting && Volatile.Read(ref _customMenuSelections)?.TryRequest(expected, selectCustom, Environment.TickCount64) == true;
    private void ClearCustomMenuSelection() => _customMenuSelections?.Clear();
    private void PublishCustomMenuSelection(PlayerEntity player, CustomInputProjection? projection, bool actorReady)
    {
        if (projection is not { } input || _customInput?.Available != true)
        { ClearCustomMenuSelection(); return; }
        WeaponStateSnapshot state = default;
        bool known = NetSession.Active
            ? NetSession.CustomWeaponPresentationState is { } current && (state = current.State).Owner == input.Owner
            : OfflineWeapons?.TryPeek(player, out state, out _) == true;
        if (!known || state.Owner != input.Owner || state.Weapon.Kind != WeaponIdentityKind.Custom)
        { ClearCustomMenuSelection(); return; }
        var context = new CustomWeaponSelectionContext(LifetimeGeneration, input.Owner, input.Bootstrap,
            NetSession.Active ? input.Manifest : OfflineWeapons!.Manifest, state.Weapon);
        _customMenuSelections ??= new();
        _customMenuSelections.Publish(new(context, true, actorReady, _customInput.Selected,
            _customInput.NativeSelectionPending, _customInput.SuppressNative));
    }
    private void ReleaseCustomWeaponInput()
    {
        Interlocked.Exchange(ref _customMenuSelections, null)?.Clear();
        _customInput?.Reset();_customInput=null;_customInputSink=null;_customInputPlayer=null;_customInputLife=default;
    }
    internal bool CustomInputSuppressesNative(PlayerEntity player)
        =>ReferenceEquals(_customInputPlayer,player)&&_customInput?.SuppressNative==true
                &&(!NetSession.Active||_customInputLife==ShotKey.For(player.SlotIndex,0))
            ||!NetSession.Active&&OfflineWeapons?.InputProjection(player)?.Selected==true
            ||NetSession.IsClient&&!NetSession.IsAuthority&&player.SlotIndex==NetSession.LocalSlot
                &&NetSession.CustomWeaponInputProjection?.Selected==true;
    internal void SampleCustomWeaponInput(bool allowed)
    {
        // Public local input never takes over a server, bot or private replica.
        if(Services.IsReplica||DemoPlayback.IsActive||NetSession.IsAuthority){ClearCustomMenuSelection();return;}
        int slot=NetSession.Active?NetSession.LocalSlot:0;
        if((uint)slot>=Players.Items.Count){ClearCustomMenuSelection();return;}
        var player=Players.Items[slot];if(player.IsBot){ClearCustomMenuSelection();return;}
        var projection=NetSession.Active?NetSession.CustomWeaponInputProjection:OfflineWeapons?.InputProjection(player);
        if(projection==null&&_customInput==null){player.ModTakeNativeSelectionIntent();ClearCustomMenuSelection();return;}
        if(!ReferenceEquals(_customInputPlayer,player))
        {
            _customInput=new();_customInputPlayer=player;_customInputSink=new(this,player);_customInputLife=default;
        }
        if(NetSession.Active)
        {
            var life=ShotKey.For(slot,0);
            if(life!=_customInputLife){_customInput!.Reset();_customInputLife=life;}
        }
        _customInput!.Synchronize(projection);
        _customFeedback??=new();_customFeedback.Synchronize(projection);
        var controls=player.Controls;
        bool native=player.ModTakeNativeSelectionIntent()
            ||controls.PowerBeam.IsPressed||controls.Missile.IsPressed||controls.VoltDriver.IsPressed
            ||controls.Battlehammer.IsPressed||controls.Imperialist.IsPressed||controls.Judicator.IsPressed
            ||controls.Magmaul.IsPressed||controls.ShockCoil.IsPressed||controls.OmegaCannon.IsPressed
            ||controls.AffinitySlot.IsPressed||controls.NextWeapon.IsPressed||controls.PrevWeapon.IsPressed
            ||player.ModNativeWheelReleaseIntent;
        bool actorReady=!Mods.SpectatorMode.IsSpectating&&player.Health>0&&!player.IsAltForm&&!player.IsMorphing&&!player.IsUnmorphing
            &&player.ModTryCaptureCustomPrecombatFacts(player.ModGunVector,out var facts)&&facts.CanUseWeapon;
        bool inputAllowed=allowed&&actorReady&&GamepadContexts.Current==GamepadContext.Gameplay;
        PublishCustomMenuSelection(player,projection,actorReady);
        bool chooseCustom=controls.CustomWeapon.IsPressed,chooseNative=native;
        bool desired=false;
        if(_customMenuSelections?.TryTake(inputAllowed,
            GamepadContexts.Current==GamepadContext.Menu&&GamepadContexts.MenuVisible&&GamepadContexts.Focused,
            native,Environment.TickCount64,out desired)==true)
        {
            chooseCustom|=desired&&!_customInput.Selected;
            chooseNative|=!desired&&_customInput.SuppressNative&&!_customInput.NativeSelectionPending;
        }
        _customInput.Sample(new(inputAllowed,chooseCustom,chooseNative,controls.Shoot.IsPressed,
            controls.CustomReload.IsPressed,controls.Zoom.IsPressed,controls.Zoom.IsDown,controls.CustomOptic.IsPressed,Mods.InputSettings.CustomScopeHold),_customInputSink!);
        PublishCustomMenuSelection(player,projection,actorReady);
        player.ModSynchronizeCustomNativeSuppression();
    }
}
