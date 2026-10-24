using System;
using MphRead;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Utility;
using OpenTK.Mathematics;
sealed partial class CheckWindow {
 private bool _inputSupportPrototype;
 private readonly CollisionResult[] _inputSupportContacts=new CollisionResult[128];
 private void ApplyInputSupportPrototype() {
  if(!_inputSupportPrototype||!_movementStarted||_movementFinished)return;
  var p=Scene.Players.Main;
  bool hover=p.IsAltForm&&p.Hunter==Hunter.Sylux&&(p.Flags1&PlayerFlags1.NoUnmorphPrevious)==0;
  if((p.Flags1&(PlayerFlags1.Standing|PlayerFlags1.UsedJumpPad))!=0||p.Speed.Y>(hover?.25f:.001f)||p.Speed.Y<-.12f)return;
  bool alt=p.IsAltForm;float radius=Fixed.ToFloat(alt?p.Values.AltColRadius:p.Values.BipedColRadius);
  float offset=alt?Fixed.ToFloat(p.Values.AltColYPos):Fixed.ToFloat(p.Values.MinPickupHeight)+radius;
  var center=p.Position+Vector3.UnitY*offset;
  float reach=hover?.3f:5f/4096;
  CollisionResult c=default;
  if(!CollisionDetection.CheckBetweenPoints(center,center-Vector3.UnitY*((radius+reach)/.7f),TestFlags.Players,Scene,ref c))return;
  float side=Vector3.Dot(center,c.Plane.Xyz)-c.Plane.W;
  if(c.EntityCollision!=null||c.Field0!=0||c.Plane.Y<(hover?3138f/4096:.7f)||side<=0||radius-side>0.025f||side-radius>reach)return;
  p.HandleCollision(c);
 }
}
