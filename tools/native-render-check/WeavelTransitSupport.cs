using System;
using MphRead;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Formats.Collision;
using OpenTK.Mathematics;

sealed partial class CheckWindow
{
    private int _transitOwnTurretSupportFrames;
    // Native CheckPlayerCollision supports Weavel on his own lower turret
    // before the room pass clears Standing. Prove that specific support;
    // StandingPrevious alone is never a ground/arrival exemption.
    private bool OwnTurretTransitSupport(PlayerEntity player,out float expectedY)
    {
        expectedY=0;
        if(!_bipedTransit||player.Hunter!=Hunter.Weavel||!player.IsAltForm||player.IsMorphing||player.IsUnmorphing
            ||(player.Flags2&PlayerFlags2.Halfturret)==0||Math.Abs(player.Speed.Y)>.01f)return false;
        var turret=player.Halfturret;
        if(turret==null||turret.Owner!=player||turret.Health<=0
            ||!(bool)typeof(HalfturretEntity).GetField("_grounded",ReplayPrivate)!.GetValue(turret)!)return false;
        var anchor=_transitPhase<=TransitPhase.SettleBiped?_movementStart:_movementGoal;
        if((turret.Position-anchor).Xz.Length>.4f)return false;
        CollisionResult floor=default;
        if(!CollisionDetection.CheckBetweenPoints(turret.Position+Vector3.UnitY*.1f,turret.Position-Vector3.UnitY*.75f,
            TestFlags.Players,Scene,ref floor)||floor.EntityCollision!=null||floor.Plane.Y<.7f
            ||Math.Abs(floor.Position.Y-(anchor.Y-.5f))>.35f)return false;
        var normal=floor.Plane.Xyz;
        if(Math.Abs(Vector3.Dot(turret.Position,normal)-floor.Plane.W-.45f)>.08f)return false;
        float radius=Fixed.ToFloat(player.Values.AltColRadius),offset=Fixed.ToFloat(player.Values.AltColYPos);
        var separation=player.Position+Vector3.UnitY*offset-turret.Position;
        float reach=radius+.45f;
        if(separation.Y<=0||Math.Abs(separation.Length-reach)>.025f||separation.Normalized().Y<.7f)return false;
        float heightSquared=reach*reach-separation.Xz.LengthSquared;
        if(heightSquared<=0)return false;
        expectedY=turret.Position.Y+MathF.Sqrt(heightSquared)-offset;
        return Math.Abs(player.Position.Y-expectedY)<=.025f;
    }
}
