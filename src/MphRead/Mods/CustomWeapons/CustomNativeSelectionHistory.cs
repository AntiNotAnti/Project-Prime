using System;

namespace MphRead.Mods.CustomWeapons;

internal enum CustomNativeSelection : byte { Native,Custom,Pending,Rejected }

// Source-time ownership only, independent of pose, native proof and resources.
// Reading Native is provisional until TryReleaseNative freezes that permission.
// A later raw custom Equip cannot cross a native permission already released.
internal sealed class CustomNativeSelectionHistory
{
    internal const int Capacity=128;
    private readonly record struct Boundary(uint Source,WeaponRuntimeId Selected);
    private readonly Boundary[] _boundaries=new Boundary[Capacity];
    private readonly WeaponOwnerContext _owner;
    private readonly uint _birth;
    private uint _observed,_sealed,_committed,_nativeReleased,_customReleased;
    private bool _hasSeal,_hasRelease,_hasCustomRelease,_retired;
    private int _head,_count;
    private WeaponRuntimeId _baseline,_selected;
    internal CustomNativeSelectionHistory(WeaponOwnerContext owner,uint birth)
    {
        if (!owner.IsValid) throw new ArgumentException("Complete owner required.",nameof(owner));
        _owner=owner;_birth=_observed=_committed=birth;
    }
    private static bool Before(uint a,uint b)=>unchecked((int)(a-b))<0;
    private bool Owner(WeaponOwnerContext owner)=>owner.IsValid&&owner==_owner;
    internal bool Observe(WeaponOwnerContext owner,uint source)
    {
        if (!Owner(owner)||_retired||Before(source,_observed))return false;
        _observed=source;
        // Keep the ownership immediately before the retained window as its
        // baseline. Expired queries reject rather than borrowing this value.
        while (_count>0&&unchecked((int)(_observed-_boundaries[_head].Source))>=Capacity)
        {
            _baseline=_boundaries[_head].Selected;_boundaries[_head]=default;
            _head=(_head+1)%Capacity;_count--;
        }
        return true;
    }
    internal bool Seal(WeaponOwnerContext owner,uint source)
    {
        if (!Owner(owner)||_retired||source!=_observed||_hasSeal&&Before(source,_sealed))return false;
        _sealed=source;_hasSeal=true;return true;
    }
    internal CustomNativeSelection Read(WeaponOwnerContext owner,uint source)
    {
        if (!Owner(owner))return CustomNativeSelection.Rejected;
        int age=unchecked((int)(_observed-source));
        if (age>=Capacity)return CustomNativeSelection.Rejected;
        if (age<0||!_hasSeal||Before(_sealed,source))
            return _retired?CustomNativeSelection.Rejected:CustomNativeSelection.Pending;
        var selected=_baseline;
        for (int i=0;i<_count;i++)
        {
            var boundary=_boundaries[(_head+i)%Capacity];
            if (Before(source,boundary.Source))break;
            selected=boundary.Selected;
        }
        return selected.IsValid?CustomNativeSelection.Custom:CustomNativeSelection.Native;
    }
    private bool CanCommit(WeaponOwnerContext owner,uint source)
        =>Owner(owner)&&!_retired&&!Before(source,_birth)&&!Before(source,_committed)
            &&!Before(_observed,source)&&unchecked((int)(_observed-source))<Capacity;
    internal bool CanEquip(WeaponOwnerContext owner,uint source)
        =>CanCommit(owner,source)&&(!_hasRelease||Before(_nativeReleased,source));
    internal bool CanUnequip(WeaponOwnerContext owner,uint source)
        =>CanCommit(owner,source)&&(!_hasCustomRelease||Before(_customReleased,source));
    internal bool Commit(WeaponOwnerContext owner,uint source,WeaponRuntimeId selected)
    {
        if (selected.IsValid&&(selected.Value is <10 or >73) || !CanCommit(owner,source)
            || (selected.IsValid?!CanEquip(owner,source):!CanUnequip(owner,source)))return false;
        if (selected==_selected){_committed=source;return true;}
        if (_count>0&&_boundaries[(_head+_count-1)%Capacity].Source==source)
            _boundaries[(_head+_count-1)%Capacity]=new(source,selected);
        else
        {
            if (_count==Capacity)return false;
            _boundaries[(_head+_count)%Capacity]=new(source,selected);_count++;
        }
        _committed=source;_selected=selected;return true;
    }
    internal bool TryReleaseNative(WeaponOwnerContext owner,uint source)
    {
        if (Read(owner,source)!=CustomNativeSelection.Native)return false;
        if (!_hasRelease||Before(_nativeReleased,source)){_nativeReleased=source;_hasRelease=true;}
        return true;
    }
    internal bool TryReleaseCustom(WeaponOwnerContext owner,uint source)
    {
        if (!CanCommit(owner,source)||!_selected.IsValid)return false;
        if (!_hasCustomRelease||Before(_customReleased,source)){_customReleased=source;_hasCustomRelease=true;}
        return true;
    }
    internal void Retire()=>_retired=true;
}
