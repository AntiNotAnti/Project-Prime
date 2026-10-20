using MphRead;
using MphRead.Mods.CustomWeapons;

// Pure tagged-target resolution only. Eligibility bits stand for the caller's
// native ownership/ammo checks; these tests do not exercise physical input,
// engine equip, custom ownership, transport or resource mutation.
internal static class CustomWeaponCycleChecks
{
    private static readonly BeamType[] NativeOrder=
    {
        BeamType.PowerBeam,BeamType.Missile,BeamType.VoltDriver,BeamType.Battlehammer,
        BeamType.Imperialist,BeamType.Judicator,BeamType.Magmaul,BeamType.ShockCoil,BeamType.OmegaCannon
    };
    private static CustomCycleTarget Native(BeamType beam)=>new(false,beam);
    private static CustomCycleTarget Custom=>new(true,BeamType.None);
    private static CustomCycleTarget Resolve(ushort eligible,BeamType current=BeamType.PowerBeam,
        bool selected=false,bool ammo=true,bool all=true,bool forward=true)
        =>CustomWeaponCycle.Resolve(NativeOrder,eligible,current,selected,ammo,all,forward);

    internal static void Run(Action<bool,string> check)
    {
        const ushort power=1,full=0x1ff;
        Sequence(check,full,true,true,BeamType.PowerBeam,
            new[]{Native(BeamType.Missile),Native(BeamType.VoltDriver),Native(BeamType.Battlehammer),
                Native(BeamType.Imperialist),Native(BeamType.Judicator),Native(BeamType.Magmaul),
                Native(BeamType.ShockCoil),Native(BeamType.OmegaCannon),Custom,Native(BeamType.PowerBeam)},
            "all-weapons forward cycle preserves nine native positions and appends tagged M7");
        Sequence(check,full,true,false,BeamType.PowerBeam,
            new[]{Custom,Native(BeamType.OmegaCannon),Native(BeamType.ShockCoil),Native(BeamType.Magmaul),
                Native(BeamType.Judicator),Native(BeamType.Imperialist),Native(BeamType.Battlehammer),
                Native(BeamType.VoltDriver),Native(BeamType.Missile),Native(BeamType.PowerBeam)},
            "all-weapons reverse cycle wraps through M7 and every native beam");
        Sequence(check,full,false,true,BeamType.PowerBeam,
            new[]{Native(BeamType.VoltDriver),Native(BeamType.Battlehammer),Native(BeamType.Imperialist),
                Native(BeamType.Judicator),Native(BeamType.Magmaul),Native(BeamType.ShockCoil),Custom,
                Native(BeamType.VoltDriver)},
            "affinity cycle excludes base/omega entries after leaving Power Beam and wraps through M7");
        Sequence(check,full,false,false,BeamType.PowerBeam,
            new[]{Custom,Native(BeamType.ShockCoil),Native(BeamType.Magmaul),Native(BeamType.Judicator),
                Native(BeamType.Imperialist),Native(BeamType.Battlehammer),Native(BeamType.VoltDriver),
                Custom},
            "affinity reverse cycle reaches M7 from Power Beam then wraps through affinity entries");

        foreach(bool all in new[]{false,true})
        foreach(bool forward in new[]{false,true})
        {
            check(Resolve(power,all:all,forward:forward)==Custom,
                $"Power Beam plus M7 enters custom in either direction (all={all}, forward={forward})");
            check(Resolve(power,selected:true,all:all,forward:forward)==Native(BeamType.PowerBeam),
                $"Power Beam plus M7 returns native in either direction (all={all}, forward={forward})");
        }

        // Volt/Imperialist/Shock eligible; the missing Battlehammer/Judicator/
        // Magmaul bits model unavailable or empty native weapons, equally.
        const ushort sparse=power|(1<<2)|(1<<4)|(1<<7);
        Sequence(check,sparse,false,true,BeamType.PowerBeam,
            new[]{Native(BeamType.VoltDriver),Native(BeamType.Imperialist),Native(BeamType.ShockCoil),
                Custom,Native(BeamType.VoltDriver)},
            "cycle skips unavailable/empty affinity entries instead of stopping on a refused equip");
        check(Resolve(power|(1<<4),all:false)==Native(BeamType.Imperialist)
            &&Resolve(power|(1<<4),all:false,forward:false)==Custom,
            "a single eligible affinity beam and M7 retain their concrete forward/reverse positions");
        check(Resolve(full,BeamType.OmegaCannon,ammo:false)==Native(BeamType.PowerBeam)
            &&Resolve(full,ammo:false,forward:false)==Native(BeamType.OmegaCannon),
            "dry unselected M7 is skipped at both all-weapons wrap boundaries");
        check(Resolve(full,BeamType.ShockCoil,ammo:false,all:false)==Native(BeamType.VoltDriver)
            &&Resolve(full,BeamType.VoltDriver,ammo:false,all:false,forward:false)==Native(BeamType.ShockCoil),
            "dry M7 does not interrupt the native-only affinity loop");
        foreach(bool all in new[]{false,true})
        {
            check(Resolve(full,selected:true,ammo:false,all:all)==Native(BeamType.PowerBeam),
                $"currently selected dry M7 can escape forward to its native fallback (all={all})");
            check(Resolve(full,selected:true,ammo:false,all:all,forward:false)
                ==Native(all?BeamType.OmegaCannon:BeamType.ShockCoil),
                $"currently selected dry M7 can escape backward to an eligible native beam (all={all})");
        }
        check(Resolve(power,ammo:false,all:false)==Native(BeamType.PowerBeam),
            "no eligible affinity and dry M7 leave the usable Power Beam selected");

        check(Resolve(power|(1<<1),BeamType.Missile,selected:true,all:false)==Native(BeamType.Missile),
            "held missile is a legal native return even when scroll-all excludes other base weapons");
        check(Resolve(power|(1<<8),BeamType.OmegaCannon,selected:true,all:false)==Native(BeamType.OmegaCannon)
            &&Resolve(power|(1<<8),BeamType.OmegaCannon,selected:true,all:false,forward:false)==Native(BeamType.OmegaCannon),
            "held Omega is retained as the sole native fallback in either direction");
        foreach(bool forward in new[]{false,true})
        {
            check(Resolve(power,BeamType.Missile,selected:true,all:false,forward:forward)==Native(BeamType.PowerBeam)
                &&Resolve(power,BeamType.OmegaCannon,selected:true,all:false,forward:forward)==Native(BeamType.PowerBeam),
                "exhausted excluded-current native fallback escapes to usable Power Beam");
        }
        check(Resolve(power|(1<<4),BeamType.Missile,selected:true,all:false)==Native(BeamType.Imperialist),
            "exhausted fallback uses an eligible affinity weapon before emergency Power Beam fallback");
        check(Resolve(0,BeamType.Missile,selected:true,ammo:false,all:false)==Custom,
            "no native eligibility and dry selected M7 retain selection without fabricating a native target");
        check(Resolve(0,BeamType.Missile,ammo:false,all:false)==Native(BeamType.Missile),
            "no eligible target leaves an unselected custom owner's native selection unchanged");

        var before=(BeamType[])NativeOrder.Clone();
        for(int mask=0;mask<=full;mask++)
        {
            Resolve((ushort)mask,selected:true,all:false);
            Resolve((ushort)mask,ammo:false,forward:false);
        }
        check(NativeOrder.SequenceEqual(before),"tagged cycle resolution never mutates the caller's native order");
        foreach(int length in new[]{0,8,10})
        {
            bool refused=false;
            try{CustomWeaponCycle.Resolve(new BeamType[length],full,BeamType.PowerBeam,false,true,true,true);}
            catch(ArgumentException){refused=true;}
            check(refused,"cycle refuses a non-nine native order before indexing: "+length);
        }
    }

    private static void Sequence(Action<bool,string> check,ushort eligible,bool all,bool forward,
        BeamType fallback,CustomCycleTarget[] expected,string description)
    {
        bool selected=false,matched=true;BeamType current=fallback;
        foreach(var target in expected)
        {
            var actual=Resolve(eligible,current,selected,all:all,forward:forward);
            matched&=actual==target;
            selected=actual.Custom;
            if(!selected)current=actual.Native;
        }
        check(matched,description);
    }
}
