#if MPHREAD_RMLUI_DRAWLIST_TEST
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Render;

// Actual CPU bridge -> actual copied reader -> pure GLES operation planner.
// This file never creates a window/context/Scene or reads original game media.
internal static class CanvasDrawListAcceptance
{
    private static int checks;
    internal static int Run(string assetRoot)
    {
        checks=0;assetRoot=Path.GetFullPath(assetRoot);
        string name="canvas-draw-list-own-"+Guid.NewGuid().ToString("N")+".rml",path=Path.Combine(assetRoot,name);
        if(!Directory.Exists(assetRoot))throw new DirectoryNotFoundException("Pass the private runtime RmlUi output assets root.");
        File.WriteAllText(path,"""
            <rml><head><title>Invented CPU draw list fixture</title><style>
            body { margin:0px; font-family:Rajdhani; }
            div#clip { position:absolute; left:37px; top:29px; width:320px; height:240px; overflow:hidden; }
            prime-canvas { display:block; position:absolute; left:0px; top:0px; width:320px; height:240px; }
            </style></head><body><div id="clip"><prime-canvas id="canvas"></prime-canvas></div></body></rml>
            """);
        try
        {
            using var host=new RmlUiHost();
            Check(host.Initialize(640,480,1,assetRoot,RmlUiRenderBackend.DrawList),"actual native CPU backend1 initialized without window/GL");
            Check(host.ShowDocument(host.HomeDocument,false),"hide unrelated bootstrap document through its owning Host");
            var document=host.OpenDocument(name,RmlUiDocumentLayer.Page);
            var reader=new RmlUiDrawListReader();
            using var old=host.AcquireCanvas(document,"canvas")??throw new InvalidOperationException("Canvas ABI1 lease unavailable.");
            var pixels=new byte[]{200,100,50,128,255,127,63,64,31,255,7,0};
            var images=new[]{new RmlUiCanvasImage(1,1,pixels.AsMemory(0,4),0,1,0),new RmlUiCanvasImage(2,1,pixels.AsMemory(4,8),1,0,1)};
            var table=new RmlUiCanvasTextureTable(images);Array.Fill(pixels,(byte)0);images[0]=new(1,1,new byte[4],1,0,0);
            Check(old.SetTextures(table),"actual canvas admits detached straightRGBA table and sampling policy");
            var vertices=Vertices();int[] indices={2,0,1,3,5,4,6,7,8};
            var batches=new[]{new RmlUiCanvasBatch(0,0,3),new(1,3,3),new(uint.MaxValue,6,3)};
            var packet=new RmlUiCanvasFrame(200,100,vertices,indices,batches);
            vertices[0]=new(999,999,0,0,0,0,999,999);indices[0]=8;batches[0]=new(1,0,3);
            Check(old.Submit(packet),"actual native frame accepts detached vertex/index/batch snapshot");
            var first=Picture(host,reader);string firstWitness=Witness(first);
            var firstCanvas=Inspect(first);
            Check(old.TryState(out var state)&&state is{Revision:1,TextureCount:2,BatchCount:3,Flags:2},"native state matches first accepted frame/table");
            long captured=reader.CapturedFrames,reused=reader.ReusedFrames;
            Check(ReferenceEquals(first,reader.Capture())&&reader.CapturedFrames==captured&&reader.ReusedFrames==reused+1,
                "unchanged valid native revision reuses exact copied frame without recopying private data");
            Check(ReferenceEquals(first,Picture(host,reader)),"unchanged Host update/render retains reader cache identity");
            Check(!old.SetTextures(table),"managed table ownership disallows same-epoch replacement");
            var missingOrdinal=new RmlUiCanvasFrame(200,100,Vertices(),new[]{0,1,2},new[]{new RmlUiCanvasBatch(2,0,3)});
            Check(!old.Submit(missingOrdinal)&&old.TryState(out state)&&state.Revision==1,"actual table cardinality rejects otherwise legal ABI ordinal without consuming revision");
            Check(ReferenceEquals(first,Picture(host,reader)),"invalid packet preserves native draw revision and cached frame");

            Check(old.Submit(Packet(4)),"new accepted geometry publishes next native revision");
            var second=Picture(host,reader);
            Check(!ReferenceEquals(first,second)&&reader.CapturedFrames==captured+1,"accepted dirty frame invalidates copied-frame cache exactly once");
            var secondCanvas=CanvasCommands(second);Check(secondCanvas.Length==3,"second source frame retains three ordered batches");
            Position(second.Geometry[secondCanvas[0].Geometry].Vertices[0],37,101,"new accepted frame moves sourceY by4*1.6 after centered projection");
            Check(firstCanvas.Geometry.Any(key=>!second.Geometry.ContainsKey(key)),"reader advances/prunes retired geometry handles instead of retaining growing source cache");
            Stable(first,firstWitness,"old returned frame survives reader geometry pruning after accepted frame");

            using var replacement=host.AcquireCanvas(document,"canvas")??throw new InvalidOperationException("Replacement lease unavailable.");
            Check(replacement.SetTextures(table)&&replacement.Submit(Packet(0)),"replacement epoch owns new source textures and independent revision1");
            var third=Picture(host,reader);string thirdWitness=Witness(third);var thirdCanvas=Inspect(third);
            Check(firstCanvas.Textures.All(key=>!third.Textures.ContainsKey(key)),"native replacement releases old canvas texture handles and reader prunes its source cache");
            Stable(first,firstWitness,"old first frame keeps its own texture map after epoch replacement");
            Check(!old.Submit(packet)&&!old.TryState(out _)&&replacement.TryState(out state)&&state.Revision==1,
                "old native epoch cannot publish into replacement even though document token remains alive");
            Check(ReferenceEquals(third,Picture(host,reader)),"old-epoch refusal leaves current accepted draw cache unchanged");
            old.Dispose();Check(ReferenceEquals(third,Picture(host,reader)),"idempotent old-epoch release cannot retire current native resources");
            replacement.Dispose();var released=Picture(host,reader);
            Check(thirdCanvas.Geometry.All(key=>!released.Geometry.ContainsKey(key))&&thirdCanvas.Textures.All(key=>!released.Textures.ContainsKey(key)),
                "actual native release prunes both geometry and texture source caches on next capture");
            Stable(third,thirdWitness,"released frame remains complete after native resource destruction and reader pruning");

            using var closing=host.AcquireCanvas(document,"canvas")??throw new InvalidOperationException("Final close fixture lease unavailable.");
            Check(closing.SetTextures(table)&&closing.Submit(Packet(0)),"document close fixture starts with live native canvas resources");
            var beforeClose=Picture(host,reader);string closeWitness=Witness(beforeClose);var closeCanvas=Inspect(beforeClose);
            Check(host.CloseDocument(document),"existing document owner closes real canvas subtree");
            var closed=Picture(host,reader);
            Check(closeCanvas.Geometry.All(key=>!closed.Geometry.ContainsKey(key))&&closeCanvas.Textures.All(key=>!closed.Textures.ContainsKey(key)),
                "document close releases native source resources before next copied-frame cache publication");
            Check(!closing.Submit(Packet(0))&&!closing.TryState(out _),"closed document token prevents later managed/native canvas publication");
            Stable(beforeClose,closeWitness,"old returned frame remains renderable from owned copies after document close");
            reader.Forget();Stable(first,firstWitness,"explicit reader Forget cannot mutate old returned frame dictionaries");
            Stable(third,thirdWitness,"all prior generations of copied frame retain their own complete dictionaries");
            Check(first.Geometry is IDictionary<ulong,RmlUiDrawGeometry> {IsReadOnly:true}
                &&first.Textures is IDictionary<ulong,RmlUiDrawTexture> {IsReadOnly:true},"published handle maps are readonly snapshots rather than reader's mutable cache dictionaries");
            Console.WriteLine($"CANVAS DRAW LIST PASS {checks} actual CPU bridge/reader checks; captures={reader.CapturedFrames} reused={reader.ReusedFrames}; no window/GL/GLES hardware or owned media.");
            return 0;
        }
        finally{File.Delete(path);}
    }

    private static (ulong[] Geometry,ulong[] Textures) Inspect(RmlUiDrawListFrame frame)
    {
        Consistent(frame);
        var explicitTextures=frame.Textures.Where(p=>p.Value.Sampling is{Flags:1}).ToArray();
        Check(explicitTextures.Length==2,"real reader discovers exactly two explicit canvas samplers");
        var nearest=explicitTextures.Single(p=>p.Value.Width==1&&p.Value.Height==1);
        var linear=explicitTextures.Single(p=>p.Value.Width==2&&p.Value.Height==1);
        Check(nearest.Value.Sampling is{Filter:0,WrapS:1,WrapT:0}&&linear.Value.Sampling is{Filter:1,WrapS:0,WrapT:1},
            "real native sampling export reaches copied reader with independent filter/S/T policy");
        Check(nearest.Value.Pixels.SequenceEqual(new byte[]{100,50,25,128})
            &&linear.Value.Pixels.SequenceEqual(new byte[]{64,31,15,64,0,0,0,0}),"detached straight source texels premultiply floor once at native boundary");
        var commands=CanvasCommands(frame);Check(commands.Length==3,"real native source batch order remains three geometry commands");
        Check(commands.Select(c=>c.Texture).SequenceEqual(new[]{nearest.Key,linear.Key,0UL}),"native ordinals become correct opaque texture handles; UINTMAX becomes explicit no texture");
        var geometry=frame.Geometry[commands[0].Geometry];
        Check(geometry.Indices.SequenceEqual(new[]{0,1,2})&&geometry.Vertices.Length==3,"native copies only referenced vertices and emits local valid indices");
        // Input index order2,0,1 is deliberately different from vertex order.
        //320x240 content over200x100 virtual gives scale1.6, vertical margin40;
        // absolute content(37,29) therefore gives origin(37,69).
        Position(geometry.Vertices[0],37,94.6f,"source index2 projects through centered virtual fit once");
        Position(geometry.Vertices[1],37,69,"source index0 preserves absolute content offset");
        Position(geometry.Vertices[2],62.6f,69,"source index1 scales sourceX16 by1.6");
        Check(geometry.Vertices[0].U==0&&geometry.Vertices[0].V==1&&geometry.Vertices[1].U==-.5f&&geometry.Vertices[2].U==2,
            "signed/outside-unit repeating source UV retained without geometry clamping");
        foreach(var command in commands)
        {
            Check(command.TranslationX==0&&command.TranslationY==0,"native embeds fractional content origin without double renderer translation");
            Check(frame.Geometry[command.Geometry].Vertices.All(v=>v.Color==0x80193264),"reader sees once-premultiplied little-endian RGBA vertex color100/50/25/128");
        }
        var plan=RmlUiGlesPlan.Build(frame,640,480);
        var draws=plan.Where(p=>p.Kind==RmlUiGlesOperationKind.Geometry&&commands.Any(c=>c.Geometry==p.Geometry)).ToArray();
        Check(draws.Length==3&&draws.Select(p=>p.Texture).SequenceEqual(commands.Select(c=>c.Texture)),"pure GLES planner retains source geometry order and exact texture handles");
        Check(draws.All(p=>p.Scissor==new RmlUiGlesRect(37,211,320,240)),"pure GLES plan maps native top-left clip(37,29,320,240) to bottom-left targetY211");
        Check(draws.All(p=>p.TranslationX==0&&p.TranslationY==0&&p.Transform.SequenceEqual(Identity())),"pure planner neither reapplies source projection/translation nor changes transform matrix");
        Check(frame.Textures[draws[0].Texture].Sampling is{Filter:0,WrapS:1,WrapT:0}
            &&frame.Textures[draws[1].Texture].Sampling is{Filter:1,WrapS:0,WrapT:1},"GLES operations still resolve their original explicit sampler policy through copied texture identities");
        return(commands.Select(c=>c.Geometry).ToArray(),explicitTextures.Select(p=>p.Key).ToArray());
    }
    private static RmlUiDrawCommand[] CanvasCommands(RmlUiDrawListFrame frame)=>frame.Commands.Where(c=>c.Kind==RmlUiDrawCommandKind.Geometry
        &&frame.Geometry[c.Geometry].Vertices.Any(v=>v.Color==0x80193264)).ToArray();
    private static void Consistent(RmlUiDrawListFrame frame)
    {
        Check(frame.Generation!=0,"copied frame has actual native generation identity");
        foreach(var command in frame.Commands)
        {
            if(command.Kind is not(RmlUiDrawCommandKind.Geometry or RmlUiDrawCommandKind.ClipMask))continue;
            Check(frame.Geometry.ContainsKey(command.Geometry),"every old/copied command resolves its own retained geometry handle");
            if(command.Kind==RmlUiDrawCommandKind.Geometry&&command.Texture!=0)Check(frame.Textures.ContainsKey(command.Texture),"every old/copied textured command resolves its own retained pixel handle");
        }
        foreach(var geometry in frame.Geometry.Values)Check(geometry.Indices.All(i=>(uint)i<(uint)geometry.Vertices.Length),"copied source indices remain within owned vertex array");
        _=RmlUiGlesPlan.Build(frame,640,480);
    }
    private static void Stable(RmlUiDrawListFrame frame,string witness,string label){Consistent(frame);Check(Witness(frame)==witness,label);}
    private static string Witness(RmlUiDrawListFrame frame)
    {
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(BitConverter.GetBytes(frame.Generation));hash.AppendData(MemoryMarshal.AsBytes(frame.Commands.AsSpan()));
        foreach(var pair in frame.Geometry.OrderBy(p=>p.Key))
        {hash.AppendData(BitConverter.GetBytes(pair.Key));hash.AppendData(MemoryMarshal.AsBytes(pair.Value.Vertices.AsSpan()));hash.AppendData(MemoryMarshal.AsBytes(pair.Value.Indices.AsSpan()));}
        foreach(var pair in frame.Textures.OrderBy(p=>p.Key))
        {
            hash.AppendData(BitConverter.GetBytes(pair.Key));hash.AppendData(BitConverter.GetBytes(pair.Value.Width));hash.AppendData(BitConverter.GetBytes(pair.Value.Height));hash.AppendData(pair.Value.Pixels);
            hash.AppendData(Encoding.ASCII.GetBytes(pair.Value.Sampling is{} s?$"{s.Size}/{s.Version}/{s.Filter}/{s.WrapS}/{s.WrapT}/{s.Flags}":"legacy"));
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
    private static RmlUiDrawListFrame Picture(RmlUiHost host,RmlUiDrawListReader reader){host.Update();host.Render(640,480);return reader.Capture();}
    private static RmlUiCanvasFrame Packet(float move)=>new(200,100,Vertices(move),new[]{2,0,1,3,5,4,6,7,8},
        new[]{new RmlUiCanvasBatch(0,0,3),new(1,3,3),new(uint.MaxValue,6,3)});
    private static RmlUiCanvasVertex[] Vertices(float move=0)=>new float[]{0,20,40}.SelectMany(offset=>new[]{
        new RmlUiCanvasVertex(offset,move,200,100,50,128,-.5f,0),new(offset+16,move,200,100,50,128,2,0),new(offset,move+16,200,100,50,128,0,1)}).ToArray();
    private static float[] Identity()=>new float[]{1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1};
    private static void Position(RmlUiVertex vertex,float x,float y,string label)=>Check(MathF.Abs(vertex.X-x)<.0001f&&MathF.Abs(vertex.Y-y)<.0001f,label);
    private static void Check(bool value,string label){checks++;if(!value)throw new InvalidOperationException("Canvas draw list: "+label);}
}
#endif
