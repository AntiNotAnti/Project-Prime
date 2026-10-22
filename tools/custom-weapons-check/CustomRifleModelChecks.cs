using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using MphRead.Mods;
using MphRead.Mods.CustomWeapons;
using OpenTK.Mathematics;

// Built-in CPU presentation data only. No renderer/rig/muzzle animation, GL,
// live combat, audio, Android device or full CW-06 acceptance is established.
internal static class CustomRifleModelChecks
{
    // Independently computed from committed source with Python hashlib/json.
    private const string ExpectedDigest="3dffd6317ba8fc2d861e045f808aaa4cfdb80b251ff354f7b7fb379bf199e752";
    private const int ExpectedBytes=2150;
    private static byte[] Bytes(string value)=>Encoding.UTF8.GetBytes(value);
    private static JsonObject Root(byte[] bytes)=>JsonNode.Parse(bytes)!.AsObject();
    private static byte[] Changed(byte[] source,Action<JsonObject> change)
    {var root=Root(source);change(root);return Bytes(root.ToJsonString());}
    private static JsonObject Box(JsonObject root,int lod=0,int index=0)=>root["lods"]![lod]![index]!.AsObject();
    private static bool Refuses(byte[] bytes,bool verified=false)
    {
        try{if(verified)CustomRifleModel.DecodeVerified(bytes);else CustomRifleModel.Decode(bytes);return false;}
        catch(InvalidDataException){return true;}
    }
    private static bool Near(float a,float b)=>MathF.Abs(a-b)<.000001f;
    internal static void Run(Action<bool,string> Check)
    {
        int count=0;void Verify(bool condition,string message){Check(condition,"M7 model: "+message);count++;}
        var assembly=typeof(CustomRifleModel).Assembly;
        using var stream=assembly.GetManifestResourceStream(CustomRifleModel.Resource);
        Verify(stream!=null,"compiled assembly contains exact catalogue resource key");
        using var copy=new MemoryStream();stream!.CopyTo(copy);byte[] source=copy.ToArray();
        string path=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../src/MphRead/Assets/Weapons/RiftlineM7/model-v1.json"));
        Verify(File.Exists(path)&&File.ReadAllBytes(path).AsSpan().SequenceEqual(source),"compiled resource matches editable repository source byte-for-byte");
        Verify(CustomRifleModel.Resource=="Weapons/RiftlineM7/model-v1.json"&&CustomRifleModel.Digest==ExpectedDigest
            &&source.Length==ExpectedBytes&&Convert.ToHexStringLower(SHA256.HashData(source))==ExpectedDigest,
            "catalogue name/length/digest match independent asset vector");
        var model=CustomRifleModel.DecodeVerified(source);ValidModel(model,Verify);Strict(source,Verify);Fuzz(source,Verify);HeadlessAndLoad(model,Verify);
        // Decode owns detached numeric values; later mutation of caller bytes
        // cannot modify a draw's retained CPU geometry.
        var detached=source.ToArray();var retained=CustomRifleModel.DecodeVerified(detached);Array.Fill(detached,(byte)0);
        Verify(retained.Lod(0).Length==16&&retained.Muzzle==model.Muzzle&&retained.Lod(0)[0]==model.Lod(0)[0],"decoded geometry has no mutable source-buffer alias");
        Console.WriteLine($"Original M7 model CPU checks passed: {count}; asset contract only, no full CW-06 rendering claim.");
    }
    private static void ValidModel(CustomRifleModel model,Action<bool,string> check)
    {
        check(model.Lod(0).Length==16&&model.Lod(1).Length==6,"independent LOD0/1 box counts16/6");
        check(Near(model.Muzzle.X,0)&&Near(model.Muzzle.Y,.04f)&&Near(model.Muzzle.Z,1.42f),"authored +Z-forward muzzle socket is exact");
        Vector3[] palette={new(.12f,.17f,.22f),new(.3f,.39f,.46f),new(.64f,.73f,.76f),new(.08f,.82f,.89f),new(.025f,.045f,.06f),new(.92f,.58f,.16f)};
        for(byte i=0;i<6;i++)check(model.Color(i)==new Vector4(palette[i],1),"opaque authored palette row "+i);
        for(int lod=0;lod<2;lod++)
        {
            int body=0,bolt=0,magazine=0;Vector3 min=new(float.PositiveInfinity),max=new(float.NegativeInfinity);
            foreach(var box in model.Lod(lod))
            {
                check(box.Material<6&&float.IsFinite(box.Center.LengthSquared)&&float.IsFinite(box.Half.LengthSquared)
                    &&box.Half.X>=.001f&&box.Half.Y>=.001f&&box.Half.Z>=.001f,"bounded finite primitive/material LOD"+lod);
                min=Vector3.ComponentMin(min,box.Center-box.Half);max=Vector3.ComponentMax(max,box.Center+box.Half);
                if(box.Part==CustomRiflePart.Body)body++;else if(box.Part==CustomRiflePart.Bolt)bolt++;else if(box.Part==CustomRiflePart.Magazine)magazine++;
            }
            check(lod==0?body==12&&bolt==2&&magazine==2:body==5&&bolt==0&&magazine==1,"authored rigid-group counts LOD"+lod);
            check(Near(min.X,lod==0?-.14f:-.134f)&&Near(max.X,lod==0?.19f:.12f)
                &&Near(min.Y,lod==0?-.24f:-.26f)&&Near(max.Y,.305f)
                &&Near(min.Z,lod==0?-.61f:-.5f)&&Near(max.Z,1.415f),"independently calculated geometry AABB LOD"+lod);
        }
        check(model.Lod(-1)[0]==model.Lod(0)[0]&&model.Lod(int.MinValue).Length==16
            &&model.Lod(2)[0]==model.Lod(1)[0]&&model.Lod(int.MaxValue).Length==6,"LOD query clamps safely to compiled two tiers");
        var value=model.Lod(0)[0] with{Center=Vector3.Zero};var color=model.Color(0);color.X=1;
        check(value!=model.Lod(0)[0]&&model.Color(0).X==.12f,"primitive/color queries return values without mutable array aliases");
        for(int i=0;i<1000;i++){_ = model.Lod(i&1)[0];_ = model.Color((byte)(i%6));}
        long before=GC.GetAllocatedBytesForCurrentThread();float sum=0;
        for(int i=0;i<100000;i++)sum+=model.Lod(i&1)[0].Half.X+model.Color((byte)(i%6)).X;
        long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
        check(float.IsFinite(sum)&&allocated==0,"100k warmed immutable draw-data queries allocate zero bytes");
    }
    private static void Strict(byte[] source,Action<bool,string> check)
    {
        void Bad(string label,Action<JsonObject> change)=>check(Refuses(Changed(source,change)),label);
        check(Refuses(Array.Empty<byte>())&&Refuses(new byte[CustomRifleModel.MaximumBytes+1]),"empty/over-budget decoder input refuses");
        check(Refuses(Array.Empty<byte>(),true)&&Refuses(new byte[CustomRifleModel.MaximumBytes+1],true),"verified decoder checks lengths before hashing/parsing");
        byte[] tampered=source.ToArray();tampered[20]^=1;
        check(Refuses(tampered,true)&&Refuses(source.Concat(new byte[]{32}).ToArray(),true),"exact digest rejects tampering and harmless whitespace byte drift");
        check(!Refuses(Changed(source,r=>{Box(r)["half"]![0]=.001;Box(r)["half"]![1]=1;r["palette"]![0]=new JsonArray(0,1,0);r["muzzle"]![0]=.4;})),
            "inclusive extent/color/transverse-socket limits remain valid");
        check(!Refuses(Changed(source,r=>{var rows=r["lods"]![0]!.AsArray();while(rows.Count<CustomRifleModel.MaximumBoxes)rows.Add(rows[0]!.DeepClone());})),
            "exact32-box LOD budget is accepted");
        foreach(string field in new[]{"schema","basis","palette","muzzle","lods"})Bad("missing root field "+field,r=>r.Remove(field));
        Bad("unknown root field",r=>r["extra"]=true);Bad("external URI field",r=>r["uri"]="https://invalid.example/model.glb");
        Bad("file path field",r=>r["path"]="../model.json");Bad("unsupported schema",r=>r["schema"]=2);
        Bad("fractional schema",r=>r["schema"]=1.5);Bad("string schema",r=>r["schema"]="1");
        Bad("foreign coordinate basis",r=>r["basis"]="+X forward,+Z up,centimetres");
        string compact=Root(source).ToJsonString();
        check(Refuses(Bytes(compact.Replace("\"schema\":1","\"schema\":1,\"schema\":1",StringComparison.Ordinal))),"duplicate root property refuses");
        check(Refuses(Bytes(compact.Replace("\"material\":0","\"material\":0,\"material\":0",StringComparison.Ordinal))),"duplicate nested property refuses");
        check(Refuses(Bytes("{\"extra\":"+new string('[',12)+"0"+new string(']',12)+"}")),"excessive JSON depth refuses before accepting geometry");
        check(Refuses(Bytes(compact.Replace("\"muzzle\":[0,0.04,1.42]","\"muzzle\":[0,0.04,1e999]",StringComparison.Ordinal))),"overflowing nonfinite numeric socket refuses");
        check(Refuses(Bytes(compact.Replace("\"muzzle\":[0,0.04,1.42]","\"muzzle\":[NaN,0.04,1.42]",StringComparison.Ordinal))),"non-JSON NaN refuses");
        Bad("five palette rows",r=>r["palette"]!.AsArray().RemoveAt(5));Bad("seven palette rows",r=>r["palette"]!.AsArray().Add(new JsonArray(0,0,0)));
        Bad("negative color",r=>r["palette"]![0]![0]=-.001);Bad("color above one",r=>r["palette"]![0]![0]=1.001);
        Bad("short color vector",r=>r["palette"]![0]=new JsonArray(0,0));Bad("long color vector",r=>r["palette"]![0]=new JsonArray(0,0,0,1));
        foreach(string field in new[]{"center","half","material","part"})Bad("missing primitive field "+field,r=>Box(r).Remove(field));
        Bad("unknown primitive field",r=>Box(r)["uri"]="file:///private/model");Bad("undefined rigid group",r=>Box(r)["part"]="trigger");
        Bad("null rigid group",r=>Box(r)["part"]=null);Bad("fractional material",r=>Box(r)["material"]=.5);
        Bad("negative material",r=>Box(r)["material"]=-1);Bad("material outside palette",r=>Box(r)["material"]=6);
        Bad("zero half extent",r=>Box(r)["half"]![0]=0);Bad("negative half extent",r=>Box(r)["half"]![1]=-.1);
        Bad("half extent below floor",r=>Box(r)["half"]![2]=.0009);Bad("half extent above ceiling",r=>Box(r)["half"]![0]=1.001);
        Bad("box exceeds AABB bound",r=>Box(r)["center"]![0]=2.5);Bad("short center vector",r=>Box(r)["center"]=new JsonArray(0,0));
        Bad("long half vector",r=>Box(r)["half"]=new JsonArray(.1,.1,.1,.1));Bad("string center coordinate",r=>Box(r)["center"]![0]="0");
        foreach(int axis in new[]{0,1})Bad("muzzle transverse bound "+axis,r=>r["muzzle"]![axis]=.401);
        Bad("muzzle behind supported range",r=>r["muzzle"]![2]=.499);Bad("muzzle beyond supported range",r=>r["muzzle"]![2]=2.501);
        Bad("socket too far behind geometry",r=>r["muzzle"]![2]=1);Bad("socket too far beyond geometry",r=>r["muzzle"]![2]=1.7);
        Bad("missing LOD tier",r=>r["lods"]!.AsArray().RemoveAt(1));Bad("excess LOD tier",r=>r["lods"]!.AsArray().Add(r["lods"]![0]!.DeepClone()));
        Bad("too few primitives",r=>{var rows=r["lods"]![1]!.AsArray();while(rows.Count>2)rows.RemoveAt(rows.Count-1);});
        Bad("excess primitive budget",r=>{var rows=r["lods"]![0]!.AsArray();while(rows.Count<=CustomRifleModel.MaximumBoxes)rows.Add(rows[0]!.DeepClone());});
        Bad("LOD1 more detailed than LOD0",r=>{var rows=r["lods"]![0]!.AsArray();while(rows.Count>3)rows.RemoveAt(rows.Count-1);});
        Bad("missing body group",r=>{foreach(var tier in r["lods"]!.AsArray())foreach(var row in tier!.AsArray())row!["part"]="bolt";});
        int complete=System.Array.LastIndexOf(source,(byte)'}')+1;
        for(int length=0;length<complete;length++)check(Refuses(source.AsSpan(0,length).ToArray()),"truncation rejects incomplete JSON asset "+length);
        check(!Refuses(source.AsSpan(0,complete).ToArray())&&Refuses(source.AsSpan(0,complete).ToArray(),true),
            "plain decoder permits trimmed trailing whitespace while verified catalogue requires exact bytes");
    }
    private static void Fuzz(byte[] source,Action<bool,string> check)
    {
        var random=new Random(6072026);
        for(int i=0;i<2000;i++)
        {
            byte[] data;
            if((i&1)==0){data=new byte[random.Next(0,513)];random.NextBytes(data);}
            else{data=source.ToArray();data[random.Next(data.Length)]=(byte)random.Next(256);}
            try
            {
                var model=CustomRifleModel.Decode(data);
                check(model.Lod(0).Length is >=3 and <=CustomRifleModel.MaximumBoxes&&model.Lod(1).Length>=3
                    &&model.Lod(1).Length<=model.Lod(0).Length&&float.IsFinite(model.Muzzle.LengthSquared),"bounded fuzz success preserves model contract "+i);
            }
            catch(InvalidDataException){check(true,"bounded malformed asset refuses with declared exception "+i);}
        }
    }
    private static void HeadlessAndLoad(CustomRifleModel expected,Action<bool,string> check)
    {
        bool old=Headless.Active;var active=typeof(Headless).GetProperty(nameof(Headless.Active),BindingFlags.Static|BindingFlags.Public)!;
        try
        {
            // Explicit global-role fixture seam, restored even on assertion failure.
            active.SetValue(null,false);var loaded=CustomRifleModel.LoadBuiltin();
            check(loaded!=null&&loaded.Muzzle==expected.Muzzle&&loaded.Lod(0).Length==16,"graphical-role builtin path resolves embedded verified CPU model without GL");
            Headless.Enter();check(CustomRifleModel.LoadBuiltin()==null,"headless role returns null before presentation resource decoding");
        }
        finally{active.SetValue(null,old);}
        check(Headless.Active==old,"headless global role restored exactly");
    }
}
