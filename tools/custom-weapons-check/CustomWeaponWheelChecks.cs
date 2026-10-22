using MphRead;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Render.Hud;

// Pure acquired-wheel model and shared geometry contracts. No engine input,
// rendering, selection submission, ownership or transport acceptance is claimed.
internal static class CustomWeaponWheelChecks
{
    private static readonly BeamType[] Six={BeamType.VoltDriver,BeamType.Battlehammer,BeamType.Imperialist,
        BeamType.Judicator,BeamType.Magmaul,BeamType.ShockCoil};
    private static readonly int[] Order={0,1,2,3,4,5};
    private static CustomWeaponWheel Wheel(BeamType fallback=BeamType.PowerBeam,bool selected=false)
        =>new(Six,Order,fallback,selected);
    internal static void Run(Action<bool,string> check)
    {
        Entries(check);Invalid(check);Drag(check);Directions(check);Geometry(check);Allocation(check);
    }
    private static void Entries(Action<bool,string> check)
    {
        var beams=(BeamType[])Six.Clone();int[] order={5,2,0,4,1,3};
        var wheel=new CustomWeaponWheel(beams,order,BeamType.PowerBeam,false);
        BeamType[] expected={BeamType.ShockCoil,BeamType.Imperialist,BeamType.VoltDriver,
            BeamType.Magmaul,BeamType.Battlehammer,BeamType.Judicator};
        bool same=true;for(int i=0;i<6;i++)same&=wheel[i]==new CustomCycleTarget(false,expected[i]);
        check(same&&wheel.Count==8&&wheel[6]==new CustomCycleTarget(true,BeamType.None)
            &&wheel[7]==new CustomCycleTarget(false,BeamType.PowerBeam),
            "wheel copies the six-native permutation then appends tagged M7 and Power Beam return");
        Array.Fill(beams,BeamType.None);Array.Fill(order,-1);same=true;
        for(int i=0;i<6;i++)same&=wheel[i].Native==expected[i]&&!wheel[i].Custom;
        check(same,"caller changes to beams/permutation cannot alter a frozen wheel hold");
        check(Six.Distinct().Count()==6&&Order.SequenceEqual(new[]{0,1,2,3,4,5}),
            "constructing a wheel preserves caller-owned canonical native arrays");
        foreach(var fallback in Six)
        {
            var seven=Wheel(fallback);int index=Array.IndexOf(Six,fallback);
            check(seven.Count==7&&seven[6].Custom&&seven.Drag(0,10,0x7f)==index,
                "already represented native fallback needs seven entries and anchors its own slot: "+fallback);
        }
        foreach(var fallback in new[]{BeamType.PowerBeam,BeamType.Missile,BeamType.OmegaCannon})
        {
            var eight=Wheel(fallback);
            check(eight.Count==8&&eight[7]==new CustomCycleTarget(false,fallback)
                &&eight.Selection==-1&&eight.Drag(0,10,0xff)==7,
                "base/omega native fallback is explicitly reachable without a native-ID alias: "+fallback);
        }
        var selected=Wheel(selected:true);
        check(selected.Selection==-1&&selected.Drag(0,10,0xff)==6,
            "selected M7 anchors mouse drag on its tagged entry rather than the native fallback");
        check(selected.Point(7,1<<7)==7&&selected.Point(6,1<<7)==-1
            &&selected.Point(-1,0xff)==-1&&selected.Point(8,0xffff)==-1,
            "point selection accepts only in-range enabled entries and otherwise remains neutral");
    }
    private static void Invalid(Action<bool,string> check)
    {
        void Reject(BeamType[] beams,int[] order,BeamType fallback,string name)
        {
            bool refused=false;try{_=new CustomWeaponWheel(beams,order,fallback,false);}
            catch(ArgumentException){refused=true;}
            check(refused,"wheel constructor rejects "+name);
        }
        Reject(Six[..5],Order,BeamType.PowerBeam,"five native entries");
        Reject(Six.Concat(new[]{BeamType.VoltDriver}).ToArray(),Order,BeamType.PowerBeam,"seven native entries");
        Reject(Six,Order[..5],BeamType.PowerBeam,"five permutation entries");
        Reject(Six,new[]{0,1,2,3,4,5,6},BeamType.PowerBeam,"seven permutation entries");
        Reject(Six,new[]{0,1,2,3,4,4},BeamType.PowerBeam,"duplicate permutation slot");
        Reject(Six,new[]{0,1,2,3,4,-1},BeamType.PowerBeam,"negative permutation slot");
        Reject(Six,new[]{0,1,2,3,4,6},BeamType.PowerBeam,"out-of-range permutation slot");
        var duplicate=(BeamType[])Six.Clone();duplicate[5]=duplicate[0];
        Reject(duplicate,Order,BeamType.PowerBeam,"duplicate affinity beam");
        foreach(var beam in new[]{BeamType.None,BeamType.PowerBeam,BeamType.Missile,BeamType.OmegaCannon,
            BeamType.Platform,BeamType.Enemy})
        {
            var invalid=(BeamType[])Six.Clone();invalid[2]=beam;
            Reject(invalid,Order,BeamType.PowerBeam,"non-affinity native entry "+beam);
        }
        foreach(var fallback in new[]{BeamType.None,BeamType.Platform,BeamType.Enemy,(BeamType)127})
            Reject(Six,Order,fallback,"non-native fallback "+fallback);
        var wheel=Wheel();
        foreach(int index in new[]{-1,8,int.MaxValue})
        {
            bool refused=false;try{_=wheel[index];}catch(ArgumentOutOfRangeException){refused=true;}
            check(refused,"wheel indexer refuses an invalid entry: "+index);
        }
    }
    private static void Drag(Action<bool,string> check)
    {
        const ushort sparse=(1<<0)|(1<<3)|(1<<6)|(1<<7);
        var wheel=Wheel();
        int[] backwards={6,3,0,0};bool same=true;
        foreach(int expected in backwards)same&=wheel.Drag(-10,10,sparse)==expected;
        check(same,"drag skips disabled native slots and stops at the lower end without wrapping");
        int[] forwards={3,6,7,7};same=true;
        foreach(int expected in forwards)same&=wheel.Drag(10,10,sparse)==expected;
        check(same,"drag traverses native/M7/return targets and stops at the upper end without wrapping");
        var fraction=Wheel(BeamType.VoltDriver);
        check(fraction.Drag(4,10,0x7f)==0&&fraction.Drag(5,10,0x7f)==0
            &&fraction.Drag(1,10,0x7f)==1,"sub-step mouse travel accumulates until one complete selection step");
        check(fraction.Drag(-4,10,0x7f)==1&&fraction.Drag(-6,10,0x7f)==0,
            "reverse fractional travel returns to the preceding native entry");
        var disabled=Wheel(selected:true);
        check(disabled.Drag(0,10,0)==-1&&disabled.Drag(100,10,0)==-1
            &&disabled.Point(6,0)==-1,"all-disabled input remains neutral without inventing a target");
        check(disabled.Drag(0,10,1<<6)==6,"reenabling the existing anchor restores its valid selection");
        foreach(float bad in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity})
        {
            var invalid=Wheel(selected:true);
            check(invalid.Drag(bad,10,0xff)==-1&&invalid.Drag(0,10,0xff)==6,
                "nonfinite mouse delta cancels pointing without corrupting the hold anchor: "+bad);
            var step=Wheel(selected:true);
            check(step.Drag(63,bad,0xff)==6&&step.Drag(1,bad,0xff)==7,
                "nonfinite step uses the bounded 64-pixel fallback: "+bad);
        }
        foreach(float bad in new[]{0f,-1f})
            check(Wheel(selected:true).Drag(64,bad,0xff)==7,"nonpositive step uses the 64-pixel fallback");
        var huge=Wheel(BeamType.VoltDriver);
        check(huge.Drag(float.MaxValue,.00000001f,0x7f)==6
            &&huge.Drag(-float.MaxValue,float.MaxValue,0x7f)==0,
            "extreme finite delta/step are bounded and reach the concrete drag endpoints");
    }
    private static void Directions(Action<bool,string> check)
    {
        check(CustomWeaponWheel.Direction(0,1,.2f,8)==0&&CustomWeaponWheel.Direction(1,0,.2f,8)==2
            &&CustomWeaponWheel.Direction(0,-1,.2f,8)==4&&CustomWeaponWheel.Direction(-1,0,.2f,8)==6,
            "eight-entry controller directions use concrete up/right/down/left sectors 0/2/4/6");
        check(CustomWeaponWheel.Direction(0,1,.2f,7)==0&&CustomWeaponWheel.Direction(1,0,.2f,7)==1
            &&CustomWeaponWheel.Direction(0,-1,.2f,7)==3&&CustomWeaponWheel.Direction(-1,0,.2f,7)==5,
            "seven-entry controller directions use concrete up/right/down/left sectors 0/1/3/5");
        foreach(int count in new[]{7,8})
        {
            check(CustomWeaponWheel.Direction(0,0,0,count)==-1
                &&CustomWeaponWheel.Direction(.1f,.1f,.2f,count)==-1
                &&CustomWeaponWheel.Direction(0,.2f,.2f,count)==0,
                "controller neutral/deadzone cancel while its exact live threshold is selectable: "+count);
            for(int index=0;index<count;index++)
            {
                var bounds=CustomWeaponWheel.Bounds(index,count,new(0,0,1000,1000));
                float x=bounds.Left+bounds.Width/2-500,y=500-bounds.Top-bounds.Height/2;
                check(CustomWeaponWheel.Direction(x,y,.2f,count)==index,
                    $"controller sector agrees with the drawn box center ({count}/{index})");
            }
        }
        foreach(int count in new[]{0,6,9,int.MaxValue})
            check(CustomWeaponWheel.Direction(0,1,.2f,count)==-1,"controller refuses unsupported wheel count: "+count);
        foreach(float bad in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity})
            check(CustomWeaponWheel.Direction(bad,1,.2f,8)==-1&&CustomWeaponWheel.Direction(1,bad,.2f,8)==-1
                &&CustomWeaponWheel.Direction(0,1,bad,8)==-1,"controller refuses nonfinite direction/threshold: "+bad);
        check(CustomWeaponWheel.Direction(0,1,-.1f,8)==-1,"controller refuses negative deadzone threshold");
    }
    private static void Geometry(Action<bool,string> check)
    {
        HudSpriteBounds[] zones={new(0,0,1,1),new(.25f,.5f,.5f,.375f),new(0,0,360,800),
            new(0,0,1920,1080),new(80,200,640,480)};
        foreach(int count in new[]{7,8})
        foreach(var zone in zones)
        {
            bool finite=true,inside=true,separate=true,centers=true;
            for(int index=0;index<count;index++)
            {
                var a=CustomWeaponWheel.Bounds(index,count,zone);
                finite&=float.IsFinite(a.Left)&&float.IsFinite(a.Top)&&float.IsFinite(a.Width)
                    &&float.IsFinite(a.Height)&&a.Width>0&&a.Height>0;
                inside&=a.Left>=zone.Left&&a.Top>=zone.Top&&a.Left+a.Width<=zone.Left+zone.Width
                    &&a.Top+a.Height<=zone.Top+zone.Height;
                centers&=a.Contains(a.Left+a.Width/2,a.Top+a.Height/2)
                    &&!a.Contains(a.Left+a.Width,a.Top+a.Height/2);
                for(int other=0;other<index;other++)
                {
                    var b=CustomWeaponWheel.Bounds(other,count,zone);
                    separate&=a.Left>=b.Left+b.Width||b.Left>=a.Left+a.Width
                        ||a.Top>=b.Top+b.Height||b.Top>=a.Top+a.Height;
                }
            }
            check(finite&&inside&&separate&&centers,
                $"shared {count}-entry boxes are finite, inside their zone, nonoverlapping and half-open: {zone}");
        }
        foreach(var view in new[]{(Width:360f,Height:800f),(Width:1920f,Height:1080f),(Width:940f,Height:528f)})
        foreach(int count in new[]{7,8})
        {
            bool transformed=true;
            for(int index=0;index<count;index++)
            {
                var box=CustomWeaponWheel.Bounds(index,count,new(view.Width*.1f,view.Height*.2f,view.Width*.8f,view.Height*.6f));
                float hudX=(box.Left+box.Width/2)/view.Width*256,hudY=(box.Top+box.Height/2)/view.Height*192;
                var pixels=HudSpriteBounds.Resolve(hudX/256,hudY/192,box.Width/view.Width*256,
                    box.Height/view.Height*192,true,2,1,view.Width,view.Height,.8f,12,8,1.15f);
                float expectedX=(128+(hudX*.8f+12-128)*1.15f)/256*view.Width;
                float expectedY=(96+(hudY*.8f+8-96)*1.15f)/192*view.Height;
                transformed&=pixels.Contains(expectedX,expectedY)
                    &&MathF.Abs(pixels.Left+pixels.Width/2-expectedX)<.001f
                    &&MathF.Abs(pixels.Top+pixels.Height/2-expectedY)<.001f;
            }
            check(transformed,$"shared boxes retain their hit centers under HUD/profile transform ({count}, {view.Width}x{view.Height})");
        }
        foreach(var zone in new[]{new HudSpriteBounds(0,0,0,1),new(0,0,1,-1),new(float.NaN,0,1,1),
            new(0,float.PositiveInfinity,1,1),new(0,0,float.PositiveInfinity,1),new(0,0,1,float.NaN)})
            check(CustomWeaponWheel.Bounds(0,8,zone)==default,"wheel refuses invalid zone before layout: "+zone);
        check(CustomWeaponWheel.Bounds(-1,8,new(0,0,1,1))==default
            &&CustomWeaponWheel.Bounds(8,8,new(0,0,1,1))==default
            &&CustomWeaponWheel.Bounds(0,6,new(0,0,1,1))==default,
            "wheel refuses invalid index/count before layout");
    }
    private static void Allocation(Action<bool,string> check)
    {
        var wheel=Wheel(selected:true);var zone=new HudSpriteBounds(20,30,940,528);int proof=0;
        void Interact()
        {
            proof+=wheel.Drag(-10,10,0xff);proof+=wheel.Drag(10,10,0xff);
            var bounds=CustomWeaponWheel.Bounds(6,wheel.Count,zone);
            proof+=wheel.Point(CustomWeaponWheel.Direction(1,0,.2f,wheel.Count),0xff);
            if(bounds.Contains(bounds.Left+bounds.Width/2,bounds.Top+bounds.Height/2))proof++;
            if(wheel[6].Custom)proof++;
        }
        for(int i=0;i<1000;i++)Interact();
        long before=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<100000;i++)Interact();
        long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
        check(allocated==0&&proof>0,"100000 warmed drag/point/direction/bounds/entry interactions allocate zero bytes");
        Console.WriteLine($"CUSTOM WEAPON WHEEL ALLOCATION:100000 warmed interactions={allocated}B");
    }
}
