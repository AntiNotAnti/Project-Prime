using MphRead.Mods.Input;

int checks=0;
void Check(bool value,string message){checks++;if(!value)throw new InvalidOperationException(message);}
void Near(float actual,float expected,string message)=>Check(MathF.Abs(actual-expected)<.0001f,message+$" ({actual} != {expected})");
DsLayout.Enabled=true;PointerInput.StylusMode=true;
Check(DsLayout.Active==!OperatingSystem.IsAndroid(),"desktop stacked layout needs both independent switches");
PointerInput.StylusMode=false;Check(!DsLayout.Active,"layout enable does not silently change input mode");
PointerInput.StylusMode=true;
foreach(string aspect in new[]{"4:3","5:3"})
foreach(var size in new[]{(640,360),(960,540),(1280,720),(1920,1080),(3440,1440),(1080,1920),(1281,721)})
{
    DsLayout.ScreenAspect=aspect;
    DsLayout.UpdateInputBounds(size.Item1,size.Item2);
    var (top,bottom)=DsLayout.Bounds(size.Item1,size.Item2);
    Check(top.X>=0&&top.Y>=0&&bottom.X==top.X&&bottom.Z==top.Z&&bottom.W==top.W
        &&bottom.Y==top.Y+top.W&&bottom.X+bottom.Z<=size.Item1&&bottom.Y+bottom.W<=size.Item2,
        "both LCDs are adjacent and contained: "+aspect+" "+size);
    Near(top.Z/(float)top.W,aspect=="4:3"?4f/3:5f/3,"per-screen projection proportions");
    Check(Math.Abs(top.X*2+top.Z-size.Item1)<=1&&Math.Abs(top.Y*2+top.W*2-size.Item2)<=1,"stack is centered to the nearest pixel");
    Near(StylusZone.Left,bottom.X/(float)size.Item1,"input begins at actual bottom LCD x");
    Near(StylusZone.Top,bottom.Y/(float)size.Item2,"input begins at actual bottom LCD y");
    Near(StylusZone.Width,bottom.Z/(float)size.Item1,"input spans bottom LCD width");
    Near(StylusZone.Height,bottom.W/(float)size.Item2,"input spans bottom LCD height");
    foreach(float selectSize in new[]{.5f,1f,48f/44})
    {
        StylusZone.SelectSize=selectSize;
        foreach(var original in StylusZone.Buttons)
        {
            var button=StylusZone.EffectiveButton(original);
            float x=StylusZone.Left+button.X/256*StylusZone.Width,y=StylusZone.Top+button.Y/192*StylusZone.Height;
            Check(StylusZone.RegionAt(x,y)==button.Region,"native artwork center and hit target agree: "+button.Region+" "+aspect);
            float physicalWidth=button.Width/256*bottom.Z,physicalHeight=button.Height/192*bottom.W;
            Near(physicalWidth/physicalHeight,original.Width/original.Height,"physical button proportions remain uniform");
        }
    }
    Check(StylusZone.RegionAt(StylusZone.Left+StylusZone.Width/2,StylusZone.Top+StylusZone.Height/2)==StylusRegion.Aim,"bottom-screen center remains aiming surface");
    Check(StylusZone.RegionAt(StylusZone.Left-.001f,StylusZone.Top+.001f)==StylusRegion.None,"letterbox space is outside touch owner");
    Check(StylusZone.RegionAt((top.X+top.Z/2f)/size.Item1,(top.Y+top.W/2f)/size.Item2)==StylusRegion.None,"top LCD is outside bottom-screen input owner");
}
DsLayout.ScreenAspect="invalid";Check(DsLayout.ScreenAspect=="4:3","invalid proportions restore original default");
DsLayout.ScreenAspect="5:3";DsLayout.RadarSize=1;
foreach(float x in new[]{0f,.5f,1})foreach(float y in new[]{0f,.5f,1})
{
    DsLayout.RadarX=x;DsLayout.RadarY=y;var center=DsLayout.RadarCenter;
    Check(center.X*DsLayout.HorizontalStretch>=77-.001f&&center.X*DsLayout.HorizontalStretch<=768*DsLayout.HorizontalStretch-77+.001f
        &&center.Y>=77-.001f&&center.Y<=576-77+.001f,"bottom radar remains inside screen at normalized extremes");
}
Console.WriteLine($"DS LAYOUT PASS: {checks} assertions; both ratios, seven viewport shapes, real native hit regions. No render or physical-device claim.");
