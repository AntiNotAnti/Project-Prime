using System.Collections.Immutable;
using MphRead.Mods.Launcher.Core;

int checks=0;
void Check(bool pass,string label){if(!pass)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
var samples=ImmutableArray.Create(
    new TheatreSpatialSample(0,0,-10,0,20,TheatreSpatialKind.Movement,1),
    new TheatreSpatialSample(60,1,10,0,40,TheatreSpatialKind.Movement,2),
    new TheatreSpatialSample(61,0,0,0,30,TheatreSpatialKind.Damage,100),
    new TheatreSpatialSample(62,0,float.NaN,0,0,TheatreSpatialKind.Movement,1));
var analysis=new TheatreSpatialAnalysis(120,samples);
var all=TheatreSpatialProjection.Project(analysis,TheatreSpatialKind.Movement,-1,0,120);
Check(all.Levels.Length==256&&all.Samples==2&&all.Weight==3,"finite bounded cells exclude invalid positions");
Check(all.Levels[0]==2&&all.Levels[255]==4,"top-down world coordinates and relative intensity");
var player=TheatreSpatialProjection.Project(analysis,TheatreSpatialKind.Movement,1,0,120);
Check(player.Samples==1&&player.Levels[255]==4&&player.Levels[0]==0,"player filter preserves world bounds");
var range=TheatreSpatialProjection.Project(analysis,TheatreSpatialKind.Movement,-1,0,59);
Check(range.Samples==1&&range.Levels[0]==4&&range.MinX==all.MinX&&range.MaxZ==all.MaxZ,"time filter preserves world bounds");
var damage=TheatreSpatialProjection.Project(analysis,TheatreSpatialKind.Damage,-1,0,120);
Check(damage.Samples==1&&damage.Weight==100&&damage.Levels.Count(v=>v>0)==1,"damage weight comes from recorded events");
var empty=TheatreSpatialProjection.Project(analysis,TheatreSpatialKind.Kills,-1,0,120);
Check(empty.Samples==0&&empty.Levels.All(v=>v==0),"empty filters publish empty cells");
var point=TheatreSpatialProjection.Project(new(0,ImmutableArray.Create(new TheatreSpatialSample(0,0,1,1,1,TheatreSpatialKind.Movement,1))),TheatreSpatialKind.Movement,-1,0,0);
Check(point.Samples==1&&point.Levels[0]==4,"zero-area recording remains projectable");
try{TheatreSpatialProjection.Project(analysis,TheatreSpatialKind.Movement,-1,90,80);throw new Exception("bad range accepted");}catch(ArgumentException){checks++;}
try{TheatreSpatialProjection.Project(analysis,TheatreSpatialKind.Movement,-1,0,121);throw new Exception("outside duration accepted");}catch(ArgumentException){checks++;}


var advanced=new TheatreSpatialAnalysis(600,ImmutableArray.Create(
    new TheatreSpatialSample(180,0,1,2,3,TheatreSpatialKind.SpawnPressure,25),
    new TheatreSpatialSample(600,1,4,5,6,TheatreSpatialKind.PickupRoutes,.1f),
    new TheatreSpatialSample(6,0,7,8,9,TheatreSpatialKind.ImperialistSightlines,.1f,new(1,0,0)),
    new TheatreSpatialSample(12,0,7,8,9,TheatreSpatialKind.ObjectivePresence,.1f),
    new TheatreSpatialSample(20,0,70,8,90,TheatreSpatialKind.Engagements,40)));
Check(Enum.GetValues<TheatreSpatialKind>().Length==13,"all thirteen legacy heatmap categories exist");
Check(TheatreSpatialProjection.Filter(advanced,TheatreSpatialKind.PickupRoutes,1,600,600).Length==1,"route range endpoint remains inclusive");
Check(TheatreSpatialProjection.Filter(advanced,TheatreSpatialKind.SpawnPressure,1,0,600).Length==0,"advanced spatial categories retain player filter");
Check(TheatreSpatialProjection.Filter(advanced,TheatreSpatialKind.ImperialistSightlines,0,0,600)[0].Direction?.X==1,"recorded facing vectors survive projection filtering");
var markers=TheatreSpatialProjection.WorldMarkers(Enumerable.Range(0,100).Select(i=>new TheatreSpatialSample(0,0,i,0,0,TheatreSpatialKind.Damage,i+1)).ToImmutableArray());
Check(markers.Length==64&&markers[0].Weight==100&&markers[^1].Weight==37,"world marker budget retains highest recorded weights");

Check(TheatreSpatialProjection.WithinWindow(280,100,180)&&!TheatreSpatialProjection.WithinWindow(281,100,180),"recorded spawn pressure includes exactly three seconds");
Check(TheatreSpatialProjection.WithinWindow(700,100,600)&&!TheatreSpatialProjection.WithinWindow(701,100,600)&&!TheatreSpatialProjection.WithinWindow(99,100,600),"recorded pickup route includes ten seconds without unsigned underflow");
Console.WriteLine($"{checks} spatial projection checks passed.");
