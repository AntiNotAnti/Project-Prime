using System;
using System.IO;
using MphRead.Entities;
using MphRead.Formats.Collision;
using MphRead.Mods.MapGen;
using OpenTK.Mathematics;
namespace MphRead.Mods.HeroModes;

/// <summary>Native moving cover: uses the existing entity-collision and linked-floor transform path.</summary>
internal sealed class PayloadEntity : EntityBase
{
    private readonly PayloadRuntime _runtime;
    private readonly PayloadMap _map;
    private int _round=-1;
    internal PayloadEntity(Scene scene,PayloadRuntime runtime,PayloadMap map):base(EntityType.Payload,MphRead.Formats.Culling.NodeRef.None,scene)
    { _runtime=runtime;_map=map; }
    public override void Initialize()
    {
        // Derived native files live only with the user's extracted data, never in the distributed map.
        string key="payload-cargo-"+_map.Route.MapId.ToString("N")+"-v1";
        string relative=Path.Combine("generated-payload",CustomRooms.RuntimeNamespace,key);
        string directory=Paths.Combine(Paths.FileSystem,relative);
        Directory.CreateDirectory(directory);
        byte[] modelBytes,collisionBytes;
        lock(MapCompiler.ContentReadLock)
        {
            var built=new BuiltMap(_map.Cargo);
            MapBuilder.AddAuthoredGeometry(built,_map.Cargo);
            // Keep detailed render wheels out of the player-contact budget. Three authored
            // convex solids supply the deck, load and instrument housing via native collision.
            var collision=new BuiltMap(_map.CargoCollision);
            MapBuilder.AddAuthoredGeometry(collision,_map.CargoCollision);
            built.Solid.Clear();built.Solid.AddRange(collision.Solid);
            (modelBytes,collisionBytes)=MapPacker.BuildEntityAssets(built);
        }
        File.WriteAllBytes(Path.Combine(directory,"model.bin"),modelBytes);
        File.WriteAllBytes(Path.Combine(directory,"collision.bin"),collisionBytes);
        string modelPath=Path.Combine(relative,"model.bin"),collisionPath=Path.Combine(relative,"collision.bin");
        var meta=new ModelMetadata(key,modelPath,null,collisionPath,new[]{new RecolorMetadata("default",modelPath,modelPath)});
        _models.Add(Read.GetGeneratedModelInstance(meta));
        Position=_runtime.Position;Rotation=new(0,MathF.Atan2(_runtime.Forward.X,_runtime.Forward.Z),0);
        SetCollision(Collision.GetCollision(meta));
        base.Initialize();
    }
    public override bool GetTargetable()=>false;
    public override bool Process()
    {
        if(!HeroRules.IsPayload(_scene.GameState.Mode))return false;
        UpdateLinkedInverse(0);
        Rotation=new(0,MathF.Atan2(_runtime.Forward.X,_runtime.Forward.Z),0);
        Position=_runtime.Position;
        UpdateCollisionTransform(0,Transform);
        int round=_runtime.Frame?.Round??0;
        if(round!=_round){_round=round;UpdateLinkedInverse(0);ModResetDrawState();}
        return true;
    }
}
