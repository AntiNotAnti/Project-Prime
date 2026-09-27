using System;
using System.Collections.Generic;
using MphRead.Mods.Multiplayer;
using MphRead.Hud;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using MphRead.Mods.Render.Hud;
using OpenTK.Mathematics;
using NVector3 = System.Numerics.Vector3;
using NVector4 = System.Numerics.Vector4;

namespace MphRead.Entities;

public partial class PlayerEntity
{
    // Hunters and authorized objectives are collected first. The remaining slots
    // cover even dense custom-map pickup fields without draw-time buffer growth.
    private readonly RadarContact[] _radarContacts = new RadarContact[4096];
    private readonly RadarTrailState[] _radarTrails = new RadarTrailState[SlotCapacity];
    private readonly List<LocatorInfo> _radarLocatorInfo = new(128);
    private bool _collectingRadarLocators;
    private RadarTrailState _radarHistory;
    private ulong _radarEpoch;
    private ushort _radarMatch;
    internal void ModResetRadarHistory() => _radarHistory.Clear();
    internal void ModCaptureRadarHistory()
    {
        if(Radar.Enabled) _scene.ReplayPoses?.Prepare();
        ulong identity, epoch; ushort match;
        if (_scene.Services is ReplaySceneServices replay)
        {
            // Missing replica state must never fall through to the live session.
            if(!replay.State.TryGetPlayer(SlotIndex,out var state)) { _radarHistory.Clear(); return; }
            identity=((ulong)state.SlotGeneration << 16) | state.LifeId;
            epoch=replay.State.Match?.AuthorityEpoch ?? 0; match=replay.State.Match?.MatchId ?? 0;
        }
        else
        {
            identity=((ulong)NetPlayerLifecycle.Generation(SlotIndex) << 16) | NetPlayerLifecycle.Get(SlotIndex);
            epoch=NetSession.AuthorityEpoch; match=NetSession.CurrentMatchId;
        }
        if(epoch!=_radarEpoch || match!=_radarMatch) _radarHistory.Clear();
        _radarEpoch=epoch; _radarMatch=match;
        _radarHistory.Sample(RadarVector(Position), _scene.FrameCount, identity,
            Radar.Enabled && Health > 0 && LoadFlags.TestFlag(LoadFlags.Spawned) && HudProfiles.Runtime.Radar.Hunters
            && HudProfiles.Runtime.Radar.TrailSamples > 0 && !HudProfiles.Runtime.ReduceMotion);
    }
    private Vector3 RadarPresentedPosition()
    {
        // Translation-only sampling avoids decomposing an entire render matrix.
        if(_scene.ReplayPoses?.Sample(SlotIndex,_scene.ReplayRenderAlpha,out var position,out _)==true) return position;
        return SimulationDrawPosition;
    }
    private static NVector3 RadarVector(Vector3 v) => new(v.X,v.Y,v.Z);
    private static NVector4 RadarColor(Vector4 v) => new(v.X,v.Y,v.Z,v.W);
    internal ReadOnlySpan<RadarContact> CollectRadarContacts(HudRadarRuntime style)
    {
        int count=0;
        var palette=Radar.PaletteOf;
        for(int playerIndex=0;playerIndex<_scene.Players.Items.Count;playerIndex++)
        {
            var other=_scene.Players.Items[playerIndex];
            if(other==this || other.Health<=0 || !other.LoadFlags.TestFlag(LoadFlags.Spawned) || !style.Hunters) continue;
            var color=_scene.GameState.Teams ? other.TeamIndex >= 0
                ? TeamVisuals.Get(other.TeamIndex).RadarColor.AsVector4()*new Vector4(255f/31,255f/31,255f/31,1) : palette.Hunter : palette.Hunter;
            _radarContacts[count++]=new(RadarContactKind.Hunter,RadarVector(other.RadarPresentedPosition()),
                MathF.Atan2(other.FacingVector.X,other.FacingVector.Z),other.SlotIndex,other.TeamIndex,
                _scene.GameState.Teams && other.TeamIndex==TeamIndex ? RadarContactRelation.Friendly : RadarContactRelation.Enemy,RadarColor(color));
            _radarTrails[other.SlotIndex]=other._radarHistory;
        }
        if(style.Objectives && _scene.Services.IsReplica) RebuildRadarObjectiveLocators();
        if(style.Objectives) foreach(var locator in _scene.Services.IsReplica ? _radarLocatorInfo : _locatorInfo)
        {
            if(locator.RadarKind==RadarContactKind.Hunter || count==_radarContacts.Length) continue;
            var color=locator.Color.AsVector4()*new Vector4(255f/31,255f/31,255f/31,1);
            _radarContacts[count++]=new(locator.RadarKind,RadarVector(locator.Position),0,locator.StableId,-1,
                RadarContactRelation.Neutral,RadarColor(color),locator.Alpha,RadarContactFlags.Revealed);
        }
        // Weapon priority matters only if an unusually dense map fills the buffer.
        for(int pass=0;pass<(style.Style==HudRadarStyle.Basic ? 1 : 2);pass++) foreach(var item in _scene.GetItemInstanceEntities())
        {
            if(item.Hidden || item.DespawnTimer==0 || count==_radarContacts.Length) continue;
            bool weapon=Radar.IsWeaponItem(item.ItemType);
            if(style.Style!=HudRadarStyle.Basic && weapon!=(pass==0) || !(weapon ? style.Weapons : style.Powerups)) continue;
            _radarContacts[count++]=new(weapon ? RadarContactKind.Weapon : RadarContactKind.Powerup,
                RadarVector(item.Position),0,item.Id,-1,RadarContactRelation.Neutral,RadarColor(weapon ? palette.Weapon : palette.Powerup));
        }
        return _radarContacts.AsSpan(0,count);
    }
    private void DrawEnhancedRadar()
    {
        if(!Radar.Enabled || _scene.GameState.Teams && ShowScoreboard || _scene.CameraSequences.Current?.IsIntro==true
            || _scene.GameState.Multiplayer && _scene.GameState.MatchState!=MatchState.InProgress) return;
        long radarAllocated=HudDrawMetrics.Enabled ? GC.GetAllocatedBytesForCurrentThread() : 0;
        var runtime=HudProfiles.Runtime;
        var style=runtime.Radar;
        float unit=_scene.Size.Y/192f;
        float radius=19.44f*1.04f*unit*style.RadiusScale;
        float posX=(_scene.Size.X-5*unit-radius)/_scene.Size.X, posY=(10*unit+radius)/_scene.Size.Y;
        using var layout=UseHudLayout(4,posX*256,posY*192);
        var basis=HudRadarProjection.BuildBasis(RadarVector(CameraInfo.Facing),style.Orientation);
        var origin=RadarVector(RadarPresentedPosition());
        var palette=Radar.PaletteOf;
        // Replica alpha freezes with playback. No wall clock or live-session time.
        float alpha=_scene.Services.IsReplica ? _scene.ReplayRenderAlpha : (float)FrameTiming.PresentationAlpha;
        float time=(float)((_scene.FrameCount+alpha)/60.0);
        if(HudDrawMetrics.Enabled) HudDrawMetrics.RadarCheckpoint(0,ref radarAllocated);
        Span<HudShapePrimitive> shapes=stackalloc HudShapePrimitive[HudRadarGeometry.FrameCapacity];
        int length=HudRadarGeometry.Build(shapes,style,radius,Radar.ShowBackground,Radar.ShowOutlines,
            RadarColor(palette.Background),RadarColor(palette.Ring),RadarColor(palette.Cone),1.04f*unit,time,runtime.ReduceMotion,runtime.ReduceTransparency);
        DrawRadarShapes(shapes[..length],posX,posY);
        if(HudDrawMetrics.Enabled) HudDrawMetrics.RadarCheckpoint(1,ref radarAllocated);
        int count=CollectRadarContacts(style).Length;
        if(HudDrawMetrics.Enabled) HudDrawMetrics.RadarCheckpoint(2,ref radarAllocated);
        // Trails precede all current contacts, with history captured by simulation presentation hooks.
        if(!runtime.ReduceMotion && style.TrailSamples>0) for(int i=0;i<count;i++)
        {
            var contact=_radarContacts[i];
            if(contact.Kind!=RadarContactKind.Hunter || !HudRadarProjection.Project(contact,origin,basis,style,radius,out _)) continue;
            var history=_radarTrails[contact.StableId];
            for(int sample=Math.Min(style.TrailSamples,history.Count)-1;sample>=0;sample--)
            {
                var trail=contact with { Position=history.Get(sample), Alpha=.35f*(1-(float)sample/4) };
                if(!HudRadarProjection.Project(trail,origin,basis,style,radius,out var projected)) continue;
                length=HudRadarGeometry.BuildContact(shapes,trail,projected,basis,style,unit,time,runtime.ReduceMotion);
                DrawRadarShapes(shapes[..length],posX,posY);
            }
        }
        if(HudDrawMetrics.Enabled) HudDrawMetrics.RadarCheckpoint(3,ref radarAllocated);
        // Preserve the legacy Basic overlap order; enhanced styles put hunters on top.
        for(int pass=0;pass<3;pass++) for(int i=0;i<count;i++)
        {
            var contact=_radarContacts[i];
            int layer=HudRadarGeometry.ContactLayer(contact.Kind,style.Style);
            if(layer!=pass || !HudRadarProjection.Project(contact,origin,basis,style,radius,out var projected)) continue;
            length=HudRadarGeometry.BuildContact(shapes,contact,projected,basis,style,unit,time,runtime.ReduceMotion);
            DrawRadarShapes(shapes[..length],posX,posY);
        }
        if(HudDrawMetrics.Enabled) HudDrawMetrics.RadarCheckpoint(4,ref radarAllocated);
        length=HudRadarGeometry.BuildSelfMarker(shapes,style,MathF.Atan2(CameraInfo.Facing.X,CameraInfo.Facing.Z),basis,unit,RadarColor(palette.Player));
        DrawRadarShapes(shapes[..length],posX,posY);
        if(style.Cardinals)
        {
            ReadOnlySpan<char> labels="NESW";
            for(int i=0;i<4;i++)
            {
                var direction=basis.Project(new(i==1 ? 1 : i==3 ? -1 : 0,0,i==0 ? 1 : i==2 ? -1 : 0))*radius*.83f;
                DrawText2D(posX*256+direction.X*256/_scene.Size.X,posY*192-direction.Y*192/_scene.Size.Y-2,Align.Center,0,labels.Slice(i,1),scale:.4f);
            }
        }
        if(HudDrawMetrics.Enabled) HudDrawMetrics.RadarCheckpoint(5,ref radarAllocated);
    }
    private void DrawRadarShapes(ReadOnlySpan<HudShapePrimitive> shapes,float x,float y)
    {
        Span<System.Numerics.Vector2> polygon=stackalloc System.Numerics.Vector2[6];
        Span<Vector2> vertices=stackalloc Vector2[6];
        foreach(var primitive in shapes)
        {
            var c=primitive.Color; var color=new Vector4(c.X,c.Y,c.Z,c.W);
            var a=new Vector2(primitive.A.X,primitive.A.Y); var b=new Vector2(primitive.B.X,primitive.B.Y);
            switch(primitive.Kind)
            {
                case HudShapeKind.Disc: _scene.DrawFlatDisc(x,y,a,primitive.Radius,color); break;
                case HudShapeKind.Square: _scene.DrawFlatSquare(x,y,a,primitive.Radius,color); break;
                case HudShapeKind.Ring: _scene.DrawFlatRing(x,y,a,primitive.Radius,primitive.Thickness,color); break;
                case HudShapeKind.Line: _scene.DrawFlatLine(x,y,a,b,primitive.Thickness,color); break;
                default:
                    int count=HudRadarGeometry.Polygon(primitive,polygon);
                    for(int i=0;i<count;i++) vertices[i]=new(polygon[i].X,polygon[i].Y);
                    _scene.DrawFlatPolygon(x,y,a,vertices[..count],color); break;
            }
        }
    }
}
