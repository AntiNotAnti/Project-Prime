using System.Numerics;
using MphRead.Mods.Render.Hud;

internal static class RadarChecks
{
    public static int Run()
    {
        int checks=0;
        void Check(bool value,string message) { checks++; if(!value) throw new Exception("Radar: "+message); }
        void Near(float a,float b,string message) => Check(MathF.Abs(a-b)<.0001f,message);
        var old=HudProfileStore.Parse("""{"radar":{"style":"Basic"}}""");
        Check(old.Radar.Style==HudRadarStyle.Basic && old.Radar.Orientation==HudRadarOrientation.HeadingUp
            && old.Radar.OutOfRange==HudRadarOutOfRangeMode.Clamp && old.Radar.Elevation==HudRadarElevationMode.Off
            && !old.Radar.HunterFacing && !old.Radar.Objectives && old.Radar.TrailSamples==0 && old.Radar.RangeScale==1,"legacy defaults");
        var invalid=HudProfileDefaults.Create("Project Prime");
        invalid.Radar.Orientation=(HudRadarOrientation)99; invalid.Radar.Elevation=(HudRadarElevationMode)99;
        invalid.Radar.OutOfRange=(HudRadarOutOfRangeMode)99; invalid.Radar.RangeScale=2; invalid.Radar.TrailSamples=99;
        invalid.Radar.SweepSpeed=float.NaN; invalid.Radar.SweepOpacity=10; invalid.Radar.ElevationThreshold=-1;
        invalid.Validate();
        Check(invalid.Radar.Orientation==0 && invalid.Radar.Elevation==0 && invalid.Radar.OutOfRange==0,"invalid enums");
        Check(invalid.Radar.RangeScale==1 && invalid.Radar.TrailSamples==4 && invalid.Radar.SweepSpeed==.55f
            && invalid.Radar.SweepOpacity==1 && invalid.Radar.ElevationThreshold==.1f,"numeric clamps");
        invalid.Radar.RangeScale=-1; invalid.Validate(); Near(invalid.Radar.RangeScale,.5f,"minimum range");
        var contact=new RadarContact(RadarContactKind.Hunter,new(-6,3,8),.3f,1,0,RadarContactRelation.Enemy,Vector4.One);
        var basic=new HudRadarRuntime(old.Radar);
        var north=HudRadarProjection.BuildBasis(Vector3.UnitZ,HudRadarOrientation.NorthUp);
        for(int i=0;i<4;i++)
        {
            float angle=i*MathF.PI/2;
            var forward=new Vector3(MathF.Sin(angle),0,MathF.Cos(angle));
            var basis=HudRadarProjection.BuildBasis(forward,HudRadarOrientation.HeadingUp);
            var offsets=new[] {forward*12,-forward*12,new Vector3(-forward.Z,0,forward.X)*12,new Vector3(forward.Z,0,-forward.X)*12};
            var expected=new[] {new Vector2(0,50),new Vector2(0,-50),new Vector2(50,0),new Vector2(-50,0)};
            for(int direction=0;direction<4;direction++)
            {
                HudRadarProjection.Project(contact with {Position=offsets[direction]},default,basis,basic,100,out var projected);
                Near(projected.Position.X,expected[direction].X,"quadrant x"); Near(projected.Position.Y,expected[direction].Y,"quadrant y");
            }
            var fixedBasis=HudRadarProjection.BuildBasis(forward,HudRadarOrientation.NorthUp);
            Check(fixedBasis==north,"north invariant");
        }
        HudRadarProjection.Project(contact,default,north,basic,100,out var p);
        Near(p.Position.X,25,"-X is right"); Near(p.Position.Y,100f/3,"+Z is up");
        Check(p.Elevation==RadarElevation.Above,"above");
        Check(HudRadarProjection.Project(contact with { Position=new(0,5,0) },default,north,
            basic with { Elevation=HudRadarElevationMode.Chevron },100,out p) && p.Elevation==RadarElevation.Above,"directly above remains visible with elevation");
        Check(!HudRadarProjection.Project(contact with { Position=new(0,5,0) },default,north,basic,100,out _),"legacy coincident suppression");
        Check(HudRadarProjection.ClassifyElevation(-3,2)==RadarElevation.Below && HudRadarProjection.ClassifyElevation(2,2)==RadarElevation.Level,"below and threshold");
        var far=contact with {Position=new(-48,0,48)};
        HudRadarProjection.Project(far,default,north,basic,100,out p); Near(p.Position.Length(),100,"circle clamp");
        HudRadarProjection.Project(far,default,north,basic with {Style=HudRadarStyle.Square},100,out p);
        Near(p.Position.X,100,"square corner x"); Near(p.Position.Y,100,"square corner y");
        Check(!HudRadarProjection.Project(far,default,north,basic with {OutOfRange=HudRadarOutOfRangeMode.Hide},100,out _),"hide");
        HudRadarProjection.Project(contact,default,north,basic with {RangeScale=.5f},100,out p); Near(p.DistanceFraction,10f/12,"zoom");
        Span<HudShapePrimitive> geometry=stackalloc HudShapePrimitive[HudRadarGeometry.FrameCapacity];
        Span<Vector2> vertices=stackalloc Vector2[6];
        foreach(var style in Enum.GetValues<HudRadarStyle>())
        {
            var profile=HudProfileDefaults.Create("Project Prime"); var layout=profile.Elements["core.radar"].OffsetX;
            HudRadarStyles.Apply(profile,style); profile.Validate();
            var roundtrip=HudProfileStore.Parse(HudProfileStore.Serialize(profile));
            Check(HudProfileStore.Serialize(profile)==HudProfileStore.Serialize(roundtrip),"roundtrip "+style);
            Check(profile.Elements["core.radar"].OffsetX==layout,"preset preserves layout");
            var runtime=new HudRadarRuntime(profile.Radar);
            int count=HudRadarGeometry.Build(geometry,runtime,100,true,true,new(0,0,0,.5f),Vector4.One,Vector4.One,1,1);
            Check(count<=HudRadarGeometry.FrameCapacity,"frame bounded");
            foreach(var primitive in geometry[..count])
                Check(float.IsFinite(primitive.A.X+primitive.A.Y+primitive.B.X+primitive.B.Y+primitive.Radius)
                    && (primitive.Kind is not (HudShapeKind.Line or HudShapeKind.Ring) || primitive.Thickness>0),"finite geometry");
            var bounds=HudRadarGeometry.GetBounds(runtime);
            Check(bounds.X>227.448f*runtime.RadiusScale,"bounds include markers");
            foreach(var sample in HudRadarPreview.Contacts)
            {
                Check(HudRadarProjection.Project(sample,default,north,runtime,100,out p),"sample projects");
                count=HudRadarGeometry.BuildContact(geometry,sample,p,north,runtime,1,1);
                Check(count<=HudRadarGeometry.MarkerCapacity,"marker bounded");
                foreach(var primitive in geometry[..count]) if(primitive.Kind is HudShapeKind.Triangle or HudShapeKind.Diamond or HudShapeKind.Hexagon)
                    Check(HudRadarGeometry.Polygon(primitive,vertices)<=6,"polygon bounded");
            }
            profile.ResetElement("core.radar");
            Check(new HudRadarRuntime(profile.Radar)==basic,"reset all properties");
        }
        // Legacy geometry fixture: exact original Basic dimensions, order and palette.
        int basicCount=HudRadarGeometry.Build(geometry,basic,100,true,true,new(0,0,0,.5f),Vector4.One,Vector4.One,1);
        Check(basicCount==5 && geometry[0].Kind==HudShapeKind.Disc && geometry[1].Radius==100
            && geometry[1].Thickness==.35f && geometry[2].Radius==55 && geometry[2].Thickness==.25f,"Basic frame fixture");
        float cone=55*MathF.PI/180;
        Check(geometry[3].B==new Vector2(-100*MathF.Sin(cone),100*MathF.Cos(cone)) && geometry[4].B.X==-geometry[3].B.X,"Basic cone fixture");
        HudRadarProjection.Project(contact,default,north,basic,100,out p);
        HudRadarGeometry.BuildContact(geometry,contact,p,north,basic,1);
        Check(geometry[0].Kind==HudShapeKind.Ring && geometry[0].Radius==.59f*1.56f && geometry[0].Thickness==.2f*1.56f,"Basic hunter fixture");
        Check(HudRadarGeometry.GetBounds(basic with { Cardinals=true,RadiusScale=.1f },3).X
            > HudRadarGeometry.GetBounds(basic with { Cardinals=true,RadiusScale=.1f },1).X,"bounds include large cardinal text");
        var scanner=basic with {Style=HudRadarStyle.Scanner};
        int moving=HudRadarGeometry.Build(geometry,scanner,100,true,true,Vector4.One,Vector4.One,Vector4.One,1,1);
        int still=HudRadarGeometry.Build(geometry,scanner,100,true,true,Vector4.One,Vector4.One,Vector4.One,1,1,true);
        Check(moving==still+2,"reduce motion removes only sweep");
        HudRadarGeometry.Build(geometry,scanner,100,true,true,new(0,0,0,.1f),Vector4.One,Vector4.One,1,1,false,true);
        Check(geometry[0].Color.W>=.85f,"reduced transparency backing");
        var self=HudRadarGeometry.BuildSelfMarker(MathF.PI/2,north,1,Vector4.One);
        HudRadarGeometry.Polygon(self,vertices); Near(vertices[0].X,-1.25f,"north-up self heading");
        Check(HudProfileDefaults.Create("Competitive").Radar.Style==HudRadarStyle.Competitive
            && HudProfileDefaults.Create("Minimal").Radar.Style==HudRadarStyle.Minimal
            && HudProfileDefaults.Create("Accessibility").Radar.Style==HudRadarStyle.Tactical,"shipped enhanced presets");
        foreach(var kind in Enum.GetValues<RadarContactKind>()) Check(!HudRadarProjection.Visible(kind,basic with {Hunters=false,Weapons=false,Powerups=false,Objectives=false}),"filter "+kind);
        var trail=new RadarTrailState();
        trail.Sample(Vector3.Zero,1,1,true); trail.Sample(Vector3.One,2,1,true);
        for(int i=0;i<4;i++) trail.Sample(Vector3.One,2,1,true);
        Check(trail.Count==1 && trail.Get(0)==Vector3.Zero,"one trail sample per tick");
        trail.Sample(Vector3.One,3,2,true); Check(trail.Count==0,"slot/life change");
        trail.Sample(Vector3.One,4,2,true); trail.Sample(Vector3.One,2,2,true); Check(trail.Count==0,"rewind");
        trail.Sample(Vector3.One,30,2,true); Check(trail.Count==0,"seek gap");
        trail.Sample(new(100),31,2,true); Check(trail.Count==0,"teleport");
        trail.Sample(Vector3.One,32,2,false); Check(trail.Count==0,"death or hidden");
        foreach(int hz in new[] {60,120,144,240})
        {
            trail.Clear();
            for(ulong tick=0;tick<60;tick++) for(int draw=0;draw<(int)((tick+1)*(ulong)hz/60)-(int)(tick*(ulong)hz/60);draw++) trail.Sample(new(tick*.01f),tick,1,true);
            Check(trail.Count==4 && trail.Get(0)==new Vector3(.58f),"refresh-independent history");
        }
        for(int i=0;i<4;i++)
        {
            GC.Collect(); GC.WaitForPendingFinalizers();
            long start=GC.GetAllocatedBytesForCurrentThread();
            var point=new HudTransform(1280,720).Resolve(HudAnchor.TopRight,Vector2.Zero);
            Check(GC.GetAllocatedBytesForCurrentThread()==start && point.X==1280,"layout after GC does not allocate enum metadata");
        }
        // Warm all paths, then measure caller-owned-buffer frame + marker generation.
        for(int i=0;i<200;i++) Build(i);
        long before=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<10000;i++) Build(i);
        Check(GC.GetAllocatedBytesForCurrentThread()==before,"zero warmed geometry allocations");
        void Build(int tick)
        {
            Span<HudShapePrimitive> buffer=stackalloc HudShapePrimitive[HudRadarGeometry.FrameCapacity];
            var style=scanner with {Elevation=HudRadarElevationMode.Chevron,HunterFacing=true,Objectives=true};
            HudRadarGeometry.Build(buffer,style,100,true,true,Vector4.One,Vector4.One,Vector4.One,1,tick/60f);
            foreach(var sample in HudRadarPreview.Contacts)
                if(HudRadarProjection.Project(sample,default,north,style,100,out var result))
                    HudRadarGeometry.BuildContact(buffer,sample,result,north,style,1,tick/60f);
        }
        return checks;
    }
}
