using System;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.Threading;
using System.Text.Json;
using MphRead.Mods.MapGen;
using OpenTK.Mathematics;

sealed partial class CheckWindow
{
    // Bounded source geometry diagnostic. Re-prove every leg and the joined
    // sweep with the current production verifier before ordinary player input.
    // It does not certify the full map, navigation graph, or visual fidelity.
    private bool _independentInputDiagnostic;
    private float? _localFixtureKillHeight;
    private void ConfigureLocalInputFixture(string[] args, JsonElement root)
    {
        if (!_movementRequireClearance || _movementPackageRoom || (_movementNonWalkable && !args.Contains("-movement-independent-diagnostic"))
            || (_movementJumpTrial&&!args.Contains("-movement-independent-diagnostic")) || _movementSourceLaunch || _movementSourceTeleport
            || args.Contains("-movement-reverse") || args.Contains("-movement-spawn")
            || args.Contains("-movement-contact-path") || args.Contains("-movement-supported-start"))
            throw new ArgumentException("Local fixture requires strict clearance and independent forward fixture input.");
        _inputSupportPrototype=args.Contains("-movement-support-prototype");
        Vector3 V(JsonElement a)
        {
            if (a.GetArrayLength()!=3) throw new InvalidDataException("Expected three finite coordinates.");
            var v=new Vector3(a[0].GetSingle(),a[1].GetSingle(),a[2].GetSingle());
            if(!float.IsFinite(v.LengthSquared))throw new InvalidDataException("Nonfinite fixture geometry.");
            return v;
        }
        _movementStart=V(root.GetProperty("start"));_movementGoal=V(root.GetProperty("end"));
        _movementWaypoints=root.GetProperty("waypoints").EnumerateArray().Select(V).ToArray();
        if(_movementWaypoints.Length is <2 or >5
            ||(_movementWaypoints[0]-_movementStart).Length>.0005f
            ||(_movementWaypoints[^1]-_movementGoal).Length>.0005f)
            throw new InvalidDataException("Invalid local waypoint endpoints.");
        var source=root.GetProperty("faces");
        if(source.GetArrayLength() is <1 or >20000)throw new InvalidDataException("Local fixture must remain bounded.");
        var faces=source.EnumerateArray().Select(f=>{
            var points=f.GetProperty("points").EnumerateArray().Select(V).ToArray();
            var normal=V(f.GetProperty("normal"));
            if(points.Length is <3 or >64 || normal.LengthSquared<.99f || normal.LengthSquared>1.01f)
                throw new InvalidDataException("Invalid fixture face.");
            return new BuiltFace(points,new Vector2[points.Length],normal,0,1);
        }).ToArray();
        _localFixtureKillHeight=faces.SelectMany(f=>f.Points).Min(v=>v.Y)-8;
        string name=_movementAlt?args[Array.IndexOf(args,"-hunter")+1]+" alt form":"Biped";
        var profile=MapAltFormCertification.Profiles.Single(p=>p.Name==name);
        const BindingFlags instance=BindingFlags.NonPublic|BindingFlags.Instance;
        var type=typeof(MapAltFormCertification).GetNestedType("NavigationPathVerifier",BindingFlags.NonPublic)!;
        var verifier=Activator.CreateInstance(type,instance,null,new object[]{faces,CancellationToken.None},null)!;
        _independentInputDiagnostic=args.Contains("-movement-independent-diagnostic");
        if(_independentInputDiagnostic&&_inputSupportPrototype)throw new ArgumentException("Independent input must use unchanged production physics.");
        if(!_independentInputDiagnostic)
        {
        var pieces=new List<MapWalkingWitness>();
        for(int i=1;i<_movementWaypoints.Length;i++)
        {
            var witness=type.GetMethod("Link",instance)!.Invoke(verifier,new object[]{_movementWaypoints[i-1],_movementWaypoints[i],profile}) as MapWalkingWitness;
            // The compiler also uses the public finite-contact proof for
            // short uphill approaches. Retain the original link first and
            // the complete joined body sweep below.
            if(witness==null&&_movementWaypoints[i].Y>_movementWaypoints[i-1].Y
                &&_movementWaypoints[i].Y-_movementWaypoints[i-1].Y<=.7f
                &&MapAltFormCertification.TryFiniteContactWalkingWitness(faces,_movementWaypoints[i-1],_movementWaypoints[i],profile,out var contact))witness=contact;
            if(witness==null)throw new InvalidDataException("Current production link proof rejected local fixture leg "+i);
            pieces.Add(witness);
        }
        if(type.GetMethod("Join",instance)!.Invoke(verifier,new object[]{_movementStart,_movementGoal,profile,pieces,"Local input fixture",_movementWaypoints}) is not MapWalkingWitness)
            throw new InvalidDataException("Current production continuous join rejected local fixture.");
        }
        var definition=new MapDefinition{Name="LOCAL_INPUT_FIXTURE"};definition.Materials.Add(new());
        var built=new BuiltMap(definition);built.Solid.AddRange(faces);built.Faces.AddRange(faces);
        _movementCollision=(byte[])typeof(MapPacker).GetMethod("BuildCollision",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,new object[]{built})!;
        _movementRoute=_independentInputDiagnostic?"Independent native-input diagnosis of an uncertified route; strict runtime clearance and overflow checks; not map certification":"Bounded source fixture; current production legs and continuous join verified; normal player input";
    }
}
