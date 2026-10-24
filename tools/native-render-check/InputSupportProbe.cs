using System;
using System.Linq;
using System.Reflection;
using MphRead;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Utility;
using OpenTK.Mathematics;
sealed partial class CheckWindow {
 private object ProbeFinalInputSupport() {
  var p=Scene.Players.Main;bool alt=p.IsAltForm;
  float radius=Fixed.ToFloat(alt?p.Values.AltColRadius:p.Values.BipedColRadius);
  float offset=alt?Fixed.ToFloat(p.Values.AltColYPos):Fixed.ToFloat(p.Values.MinPickupHeight)+radius;
  var center=p.Position+Vector3.UnitY*offset;var contacts=new CollisionResult[128];
  int count=CollisionDetection.CheckSpherePenetration(center,radius+5f/4096,128,TestFlags.Players,Scene,contacts);
  object H(CollisionResult c)=>new{kind=c.Field0,normal=new[]{c.Plane.X,c.Plane.Y,c.Plane.Z},planeW=c.Plane.W,depth=c.Field14,flags=c.Flags.ToString(),entity=c.EntityCollision!=null};
  var scratch=(CollisionResult[])typeof(PlayerEntity).GetField("_collisionScratch",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(p)!;
  return new{position=new[]{p.Position.X,p.Position.Y,p.Position.Z},radius,offset,count,contacts=contacts.Take(count).Select(H).ToArray(),lastCanonical=scratch.Where(c=>c.Plane.Xyz.LengthSquared>0).Select(H).ToArray()};
 }
}
