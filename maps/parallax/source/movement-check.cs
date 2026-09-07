using MphRead;
using MphRead.Entities;
using MphRead.Mods.MapGen;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
// Run through check-movement.py against an already generated room.
Directory.SetCurrentDirectory(args[0]);
CustomRooms.MapDirectory=args[2];
ServerContent.Open(args[1],"AMHE1");
var scene=Scene.CreateHeadless();
scene.LoadServerRoom("PARALLAX",GameMode.Battle,7);
GameState.MatchState=MatchState.InProgress;
foreach(var p in scene.GetPlayerEntities()) p.LoadFlags &= ~LoadFlags.Active;
int failures=0;
void Reset(PlayerEntity p,Vector3 position,Vector3 facing,bool alt) {
 p.LoadFlags |= LoadFlags.Active;
 foreach(var k in p.Controls.All) {k.IsDown=false;k.IsPressed=false;k.IsReleased=false;}
 if(p.IsAltForm) p.ExitAltForm();
 p.Spawn(position,facing,Vector3.UnitY,scene.GetNodeRefByPosition(position),false);
 for(int f=0;f<45;f++)scene.StepHeadlessFrame(false);
 if(alt) {
  for(int m=0;m<120 && !p.IsAltForm;m++){p.Controls.Morph.IsPressed=m%30<2;p.Controls.Morph.IsDown=p.Controls.Morph.IsPressed;scene.StepHeadlessFrame(false);}
  p.Controls.Morph.IsPressed=false;p.Controls.Morph.IsDown=false;
  for(int f=0;f<45;f++)scene.StepHeadlessFrame(false);
  if(!p.IsAltForm)throw new Exception("Failed to morph "+p.Hunter);
 }
}
foreach(var p in PlayerEntity.Players.Take(7)) {
 foreach(bool alt in new[]{false,true}) {
  Reset(p,new(-11,.6f,0),Vector3.UnitZ,alt);
  int f=0;for(;f<600 && p.Position.Z<16.5f;f++){p.Controls.MoveUp.IsDown=true;p.Controls.RollUp.IsDown=true;p.CameraInfo.Field48=0;p.CameraInfo.Field4C=1;scene.StepHeadlessFrame(false);}
  bool ok=p.Position.Y>5.75f && p.Position.Z>=16.5f; if(!ok)failures++;
  Console.WriteLine($"UPPER {p.Hunter} alt={alt} {(ok?"PASS":"FAIL")} {f/60f:F2}s end {p.Position}");
  Reset(p,new(12,-3.4f,7),Vector3.UnitX,alt);
  f=0;for(;f<600 && p.Position.X<23.5f;f++){p.Controls.MoveUp.IsDown=true;p.Controls.RollUp.IsDown=true;p.CameraInfo.Field48=1;p.CameraInfo.Field4C=0;scene.StepHeadlessFrame(false);}
  ok=p.Position.Y>-.25f && p.Position.X>=23.5f;if(!ok)failures++;
  Console.WriteLine($"LOWER {p.Hunter} alt={alt} {(ok?"PASS":"FAIL")} {f/60f:F2}s end {p.Position}");
  Reset(p,new(6,-3.4f,10),Vector3.UnitZ,alt);
  float maxY=p.Position.Y;bool launched=false,landed=false;
  f=0;for(;f<600;f++){
   p.Controls.MoveUp.IsDown=!launched;p.Controls.RollUp.IsDown=!launched;p.CameraInfo.Field48=0;p.CameraInfo.Field4C=1;scene.StepHeadlessFrame(false);
   if(p.Speed.Y>.15f)launched=true;
   maxY=Math.Max(maxY,p.Position.Y);
   if(launched && f>60 && Math.Abs(p.Speed.Y)<.001f && p.Position.Y>5.75f && p.Position.Z>14 && p.Position.Z<21) {landed=true;break;}
  }
  if(!landed)failures++;
  Console.WriteLine($"PAD {p.Hunter} alt={alt} {(landed?"PASS":"FAIL")} {f/60f:F2}s peak {maxY:F2} end {p.Position}");
 }
 if(p.Hunter==Hunter.Spire){
  Reset(p,new(-7.5f,.6f,14),Vector3.UnitZ,true);
  int f=0;for(;f<600 && p.Position.Y<6.05f;f++){p.Controls.MoveUp.IsDown=true;p.Controls.RollUp.IsDown=true;p.CameraInfo.Field48=0;p.CameraInfo.Field4C=1;scene.StepHeadlessFrame(false);}
  bool ok=p.Position.Y>=6.05f;if(!ok)failures++;
  Console.WriteLine($"CLIMB-TOP-HEIGHT Spire {(ok?"PASS":"FAIL")} {f/60f:F2}s end {p.Position}");
 }
 p.LoadFlags &= ~LoadFlags.Active;
}
scene.CloseHeadless();
Console.WriteLine($"TOTAL failures={failures}");
Environment.ExitCode=failures==0?0:1;
