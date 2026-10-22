using System;

namespace MphRead.Mods.CustomWeapons;

// A local selection target, never a runtime ID written into native storage.
internal readonly record struct CustomCycleTarget(bool Custom,BeamType Native);
internal static class CustomWeaponCycle
{
    internal static CustomCycleTarget Resolve(ReadOnlySpan<BeamType> nativeOrder,ushort nativeEligible,
        BeamType current,bool customSelected,bool customHasAmmo,bool scrollAll,bool forward)
    {
        if(nativeOrder.Length!=9)throw new ArgumentException("Expected native cycle order.",nameof(nativeOrder));
        int anchor=customSelected?9:-1;
        ushort eligible=0;
        for(int i=0;i<9;i++)
        {
            if(!customSelected&&nativeOrder[i]==current)anchor=i;
            if((nativeEligible&(1<<i))!=0&&(scrollAll||i is >=2 and <=7||nativeOrder[i]==current))
                eligible|=(ushort)(1<<i);
        }
        // An exhausted native fallback must not trap an acquired custom owner.
        if(eligible==0&&(nativeEligible&1)!=0)eligible=1;
        int index=anchor;
        for(int steps=0;steps<10;steps++)
        {
            index=forward?(index+1)%10:(index+9)%10;
            if(index==9)
            {if(customHasAmmo)return new(true,BeamType.None);}
            else if((eligible&(1<<index))!=0)return new(false,nativeOrder[index]);
        }
        return new(customSelected,customSelected?BeamType.None:current);
    }
}
