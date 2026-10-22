using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using MphRead.Mods.Import.Retro;

namespace MphRead.Mods.Import;

public static partial class RetroCharacterPreviewExporter
{
    private static string[] WriteFinalBallComposite(string staging, Func<uint,byte[]> resource,
        Cmdl ball, RetroNativeSkeleton ballRig, byte[] ballCmdl, byte[] ballCskr, CancellationToken cancellation)
    {
        const string stem="darksamus-overcharged-echoes.ball";
        byte[] bodyAncs=resource(0x39900a11);
        var refs=RetroCharacterSource.FindCharacter(bodyAncs,"Battle_3");
        if(refs.Cmdl!=0xfaa12c05||refs.Cskr!=0x918e83d3||refs.Cinf!=0x4abc8a50)
            throw new InvalidDataException("Exact original Final composite body resources required.");
        var body=Cmdl.Load(resource,refs.Cmdl,refs.Cskr);byte[] cinf=resource(refs.Cinf);
        var graph=RetroAnimationGraphProof.FromSource(bodyAncs,"G2ME01",0x39900a11,
            "darksamus-overcharged-echoes","Battle_3",refs);
        byte[] bankBytes=File.ReadAllBytes(Path.Combine(staging,"darksamus-overcharged-echoes.animations.bin"));
        var bank=RetroCharacterAnimationArchive.Read(bankBytes,graph,cinf,true);
        var composite=RetroFinalBallSource.Build(ball,body,RetroCharacterSource.ReadSkeleton(cinf),bank,cancellation);
        byte[] geometry=RetroCharacterSourceGlbWriter.Write(composite.Geometry,ballRig,cancellation);
        string geometryName=stem+".composite.glb",receiptName=stem+".composite.json",materialName=stem+".materials.bin";
        File.WriteAllBytes(Path.Combine(staging,geometryName),geometry);
        byte[] materials=RetroCharacterMaterialArchive.Encode(new("darksamus-overcharged-echoes-ball",0x2f2c1e6f,
            0x4403b1b9,0xc7dbc44a,0x654a3f97,BundleSpec("darksamus-overcharged-echoes").SourceFingerprint),
            ballCmdl,ballCskr,RetroAnimationGraphProof.Sha(geometry),resource,cancellation);
        _=RetroCharacterMaterialArchive.Read(materials,cancellation);
        File.WriteAllBytes(Path.Combine(staging,materialName),materials);
        string[] sourceFiles={"darksamus-overcharged-echoes.source.glb","darksamus-overcharged-echoes.source.json",
            "darksamus-overcharged-echoes.materials.bin","darksamus-overcharged-echoes.animations.bin",
            "darksamus-overcharged-echoes.cinf.bin","darksamus-overcharged-echoes.ancs-graph.json","darksamus-overcharged-echoes.ancs.bin",
            stem+".source.glb",stem+".source.json",stem+".cinf.bin",stem+".animations.bin",stem+".ancs-graph.json",stem+".ancs.bin",materialName};
        var inputs=sourceFiles.ToDictionary(name=>name,name=>RetroAnimationGraphProof.Sha(File.ReadAllBytes(Path.Combine(staging,name))),StringComparer.Ordinal);
        byte[] receipt=JsonSerializer.SerializeToUtf8Bytes(new{
            format=2,kind="generated-final-ball-composite-v1",sourceCharacter="darksamus-overcharged-echoes",
            sourcePart="ball-composite",presentationPart="alternateForm",characterId="darksamus",skinSource="MP2DarkSamusFinal",
            nativeHunter="Samus",visualOnly=true,writerVersion=RetroCharacterSourceGlbWriter.Version,
            materialPolicy=RetroCharacterSourceGlbWriter.MaterialPolicy,sourceBasis="(-x,z,y)",sourceInputs=inputs,
            sourceGlbSha256=RetroAnimationGraphProof.Sha(geometry),materialArchiveSha256=RetroAnimationGraphProof.Sha(materials),
            selection=composite.Selection,sourceTriangleCount=8170,sourceMaterialCount=6,sourceBoneCount=2,
            staticSourceBake=true,nativeRollAndCollision=true,gameplayChanged=false,online=false,replay=false});
        File.WriteAllBytes(Path.Combine(staging,receiptName),receipt);
        return new[]{geometryName,receiptName,materialName};
    }

    private static void AppendFinalBallManifest(string staging)
    {
        const string stem="darksamus-overcharged-echoes.ball";
        // Update-all shares staging, so Final retains the same appearance-local
        // temporary manifest already registered for its characters.json target.
        string path=Path.Combine(staging,"darksamus-overcharged-echoes.characters.json");
        var manifest=JsonNode.Parse(File.ReadAllBytes(path))!.AsObject();var models=manifest["models"]!.AsArray();
        if(models.Count!=2)throw new InvalidDataException("Complete Final body/cannon manifest required before composite ball capture.");
        manifest["id"]="generated-darksamus-final-parts-v3";
        models.Add(JsonSerializer.SerializeToNode(new{
            hunter="Samus",part="AlternateForm",lod=0,skinning="Weighted4",poseSource="SourceRigPreview",
            model=stem+".composite.glb",characterId="darksamus",skinSource="MP2DarkSamusFinal",
            provenance=stem+".composite.json",provenanceSha256=RetroAnimationGraphProof.Sha(File.ReadAllBytes(Path.Combine(staging,stem+".composite.json"))),
            sourceAnimationArchive=stem+".animations.bin",sourceAnimationArchiveSha256=RetroAnimationGraphProof.Sha(File.ReadAllBytes(Path.Combine(staging,stem+".animations.bin"))),
            boneMap=new Dictionary<string,string>()}));
        File.WriteAllBytes(path,JsonSerializer.SerializeToUtf8Bytes(manifest));
    }
}
