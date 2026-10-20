using MphRead.Mods.Network;

namespace MphRead.Mods.CustomWeapons;

internal readonly record struct CustomInputProjection(WeaponOwnerContext Owner,bool Selected,byte Optic,
    uint SettledSequence,uint SelectionDenied,uint OpticDenied,WorldBootstrapIdentity Bootstrap=default,WeaponDigest Manifest=default);
internal readonly record struct CustomInputSample(bool Allowed,bool SelectCustom,bool SelectNative,
    bool ShootPressed,bool ReloadPressed,bool ZoomPressed,bool ZoomDown,bool CycleOptic,bool HoldScope=false);
internal interface ICustomWeaponInputSink
{
    bool Submit(CustomWeaponActionKind kind,byte optic,out uint sequence);
}

// One fixed simulation sample. Only the sink owns request identities/sequences;
// this model predicts reversible selection/optic presentation, never resources.
internal sealed class CustomWeaponInputController
{
    private readonly record struct Selection(uint Sequence,bool Custom);
    private readonly record struct Optic(uint Sequence,byte Value);
    private WeaponOwnerContext _owner;
    private WorldBootstrapIdentity _bootstrap;
    private WeaponDigest _manifest;
    private Selection? _selection;
    private uint _nativeFence;
    private Optic? _optic;
    private bool _canonicalSelected,_available,_uncertain,_holdWasDown,_ambiguousEquip;
    private byte _canonicalOptic,_preferredOptic=1;
    internal bool Selected=>_selection?.Custom??_canonicalSelected;
    internal bool SuppressNative=>_canonicalSelected||_selection.HasValue||_nativeFence!=0||_uncertain;
    internal byte PresentationOptic=>Selected?(_optic?.Value??_canonicalOptic):(byte)0;
    internal bool Available=>_available;
    internal void Reset()
    {
        _owner=default;_bootstrap=default;_manifest=default;_selection=null;_optic=null;_nativeFence=0;_canonicalSelected=_available=_uncertain=false;
        _canonicalOptic=0;_preferredOptic=1;_holdWasDown=_ambiguousEquip=false;
    }
    internal void Synchronize(CustomInputProjection? projection)
    {
        if(projection is not {} state)
        { _uncertain|=SuppressNative;_available=false;return; }
        if(_owner!=state.Owner||_bootstrap!=state.Bootstrap||_manifest!=state.Manifest)
        {Reset();_owner=state.Owner;_bootstrap=state.Bootstrap;_manifest=state.Manifest;}
        _available=true;_uncertain=false;_canonicalSelected=state.Selected;_canonicalOptic=state.Optic;
        if(_nativeFence!=0&&(state.SettledSequence>=_nativeFence
            ||!_ambiguousEquip&&state.SelectionDenied==_nativeFence))
        {_nativeFence=0;_ambiguousEquip=false;}
        if(_selection is {} selection&&(state.SettledSequence>=selection.Sequence||state.SelectionDenied==selection.Sequence))
            _selection=null;
        if(_optic is {} optic&&(state.SettledSequence>=optic.Sequence||state.OpticDenied==optic.Sequence))_optic=null;
        if(PresentationOptic!=0)_preferredOptic=PresentationOptic;
    }
    internal void Sample(in CustomInputSample input,ICustomWeaponInputSink sink)
    {
        bool holdChanged=input.ZoomDown!=_holdWasDown;_holdWasDown=input.ZoomDown;
        if(!_available||!input.Allowed)return;
        // Explicit native intent wins a simultaneous custom selection. Selecting
        // the existing fallback beam is still an Unequip request.
        if(input.SelectNative)
        {
            if(SuppressNative&&sink.Submit(CustomWeaponActionKind.Unequip,0,out uint native))
            {_selection=new(native,false);_optic=null;}
            return;
        }
        if(input.SelectCustom&&!Selected&&sink.Submit(CustomWeaponActionKind.Equip,0,out uint equip))
        {_selection=new(equip,true);_ambiguousEquip|=_nativeFence!=0;_nativeFence=equip;}
        if(!Selected)return;
        if(input.ReloadPressed&&sink.Submit(CustomWeaponActionKind.Reload,0,out uint reload))
            _optic=new(reload,0);
        byte wanted=PresentationOptic;
        if(input.CycleOptic)
        {
            byte preferred=_preferredOptic==1?(byte)2:(byte)1;
            _preferredOptic=preferred;if(wanted!=0)wanted=preferred;
        }
        if(input.HoldScope&&(holdChanged||input.CycleOptic))wanted=input.ZoomDown?_preferredOptic:(byte)0;
        else if(input.ZoomPressed)wanted=wanted==0?_preferredOptic:(byte)0;
        if(wanted!=PresentationOptic&&sink.Submit(CustomWeaponActionKind.Optic,wanted,out uint optic))
        { _optic=new(optic,wanted);if(wanted!=0)_preferredOptic=wanted; }
        // Bolt rifle fires once per physical edge. Denials/held input never
        // resubmit a shot; rendering frequency cannot create request cadence.
        if(input.ShootPressed)sink.Submit(CustomWeaponActionKind.Fire,PresentationOptic,out _);
    }
}
