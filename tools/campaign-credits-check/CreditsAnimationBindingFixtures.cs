using Model=MphRead.Mods.Campaign.CampaignCreditsModelSource;
using Binding=MphRead.Mods.Campaign.CampaignCreditsAnimationBinding;
using Duration=MphRead.Mods.Campaign.CampaignCreditsAnimationDuration;
using F=CreditsRenderFixture;

internal static class CreditsAnimationBindingFixtures
{
    private static int checks;
    internal static void Run()
    {
        checks=0;var modelBytes=F.Model();var model=Model.Read(modelBytes.Bytes);var animation=F.Animation();var duration=Duration.Read(animation.Bytes);
        var admitted=Binding.Read(model,duration,animation.Bytes);
        Check(admitted.SourceSha256==duration.Sha256&&admitted.ModelSha256==model.SourceSha256&&admitted.FrameCount==3
            &&admitted.NodeAnimationCount==1&&admitted.MaterialAnimationCount==1,"animation admits exact hashes/model-node ordinals/named material counts and frame3");
        Array.Fill(modelBytes.Bytes,(byte)0);Check(Binding.Read(model,duration,animation.Bytes).ModelSha256==model.SourceSha256,"model's internal captured names and binding ownership detach from caller source bytes");
        string sourceHash=admitted.SourceSha256;Array.Fill(animation.Bytes,(byte)0);
        Check(admitted.SourceSha256==sourceHash&&admitted.FrameCount==3&&admitted.ModelSha256==model.SourceSha256,"binding snapshot detached from mutable caller animation bytes");
        var constant=F.Animation(pool:8);F.Half(constant.Bytes,constant.At["nodeRecords"]+4,1);F.Half(constant.Bytes,constant.At["nodeRecords"]+10,7);
        F.Half(constant.Bytes,constant.At["materialRecords"]+134,1);F.Half(constant.Bytes,constant.At["materialRecords"]+136,7);
        Check(Binding.Read(model,Duration.Read(constant.Bytes),constant.Bytes).FrameCount==3,"length1 channels may sample final pool entry for every animation frame");
        var manyModel=Model.Read(F.Model(nodes:64,materials:64).Bytes);var many=F.Animation(nodes:64,materials:64);
        Check(Binding.Read(manyModel,Duration.Read(many.Bytes),many.Bytes) is {NodeAnimationCount:64,MaterialAnimationCount:64},"model-bound64-node64-material boundary uses original matching ordinals/names");
        var changed=F.Animation();changed.Bytes[22]^=1;var wrongIdentity=Duration.Read(changed.Bytes);var original=F.Animation();
        Expect(()=>Binding.Read(model,wrongIdentity,original.Bytes),"provided duration hash must match exact animation bytes despite identical logical duration");
        foreach(int length in new[]{0,1,23,original.Bytes.Length-1})
            Expect(()=>Binding.Read(model,Duration.Read(original.Bytes),original.Bytes[..length]),"truncated/different animation cannot reuse earlier duration capture");
        Reject(model,F.Animation(slots:2).Bytes,"binding profile refuses extra advertised slots even when duration reader admits them");
        foreach(int absent in new[]{0,2,-1})
        {
            var bad=F.Animation();if(absent is 0 or -1)F.Word(bad.Bytes,bad.At["table0"],0);if(absent is 2 or -1)F.Word(bad.Bytes,bad.At["table2"],0);
            Reject(model,bad.Bytes,"narrow binding requires both node and material groups even though duration permits absence");
        }
        var zeroFrames=F.Animation();F.Word(zeroFrames.Bytes,zeroFrames.At["nodeGroup"],0);F.Word(zeroFrames.Bytes,zeroFrames.At["materialGroup"],0);
        Reject(model,zeroFrames.Bytes,"present groups with equal zero durations do not qualify as positive bound animation");
        foreach(string group in new[]{"nodeGroup","materialGroup"})
        {var bad=F.Animation();F.Word(bad.Bytes,bad.At[group],2);Reject(model,bad.Bytes,"both channel groups require equal positive frame counts rather than broader duration maximum");}
        var boundedScale=F.Animation();F.Word(boundedScale.Bytes,boundedScale.At["scale"],unchecked((uint)-65536));F.Word(boundedScale.Bytes,boundedScale.At["scale"]+4,65536);
        Check(Binding.Read(model,Duration.Read(boundedScale.Bytes),boundedScale.Bytes).FrameCount==3,"animated scale admits signed exact plus/minus16 boundary");
        foreach(uint value in new[]{65537u,unchecked((uint)-65537)})
        {var bad=F.Animation();F.Word(bad.Bytes,bad.At["scale"],value);Reject(model,bad.Bytes,"animated scale cannot exceed finite admitted16abs");}
        foreach(int count in new[]{0,2})Reject(model,F.Animation(nodes:count).Bytes,"node record count must equal exact model node count");
        foreach(int group in new[]{0,16,32})foreach(int axis in new[]{0,1,2})
        {
            foreach(byte blend in new byte[]{0,2,255})
            {var bad=F.Animation();bad.Bytes[bad.At["nodeRecords"]+group+axis]=blend;Reject(model,bad.Bytes,"each node channel requires observed blend1");}
            foreach(ushort length in new ushort[]{0,65535})
            {var bad=F.Animation();F.Half(bad.Bytes,bad.At["nodeRecords"]+group+4+axis*2,length);Reject(model,bad.Bytes,"node declared channel length nonempty and within exact pool");}
            var start=F.Animation();F.Half(start.Bytes,start.At["nodeRecords"]+group+10+axis*2,65535);Reject(model,start.Bytes,"node start+length uses bounded arithmetic");
        }
        var flag=F.Animation();flag.Bytes[flag.At["nodeRecords"]+3]=1;Reject(model,flag.Bytes,"unsupported node animation flag refused");
        foreach(int offset in new[]{68,69,70,84,85,86,100,101,102,132})
        {
            foreach(byte blend in new byte[]{0,2,255})
            {var bad=F.Animation();bad.Bytes[bad.At["materialRecords"]+offset]=blend;Reject(model,bad.Bytes,"each material/alpha channel requires observed blend1");}
        }
        foreach(var channel in new[]{(72,78),(74,80),(76,82),(88,94),(90,96),(92,98),(104,110),(106,112),(108,114),(134,136)})
        {
            var empty=F.Animation();F.Half(empty.Bytes,empty.At["materialRecords"]+channel.Item1,0);Reject(model,empty.Bytes,"each material/alpha declared channel is nonempty");
            var start=F.Animation();F.Half(start.Bytes,start.At["materialRecords"]+channel.Item2,65535);Reject(model,start.Bytes,"each material/alpha start and declared span bounded");
        }
        var runtimeNode=F.Animation(pool:8);F.Half(runtimeNode.Bytes,runtimeNode.At["nodeRecords"]+4,2);
        Reject(model,runtimeNode.Bytes,"frame2 cannot sample outside declared node length2 even though shared pool8 contains that address");
        var runtimeMaterial=F.Animation(pool:8);F.Half(runtimeMaterial.Bytes,runtimeMaterial.At["materialRecords"]+72,2);
        Reject(model,runtimeMaterial.Bytes,"frame2 cannot borrow adjacent material channel bytes outside declared length2");
        var runtimeAlpha=F.Animation(pool:8);F.Half(runtimeAlpha.Bytes,runtimeAlpha.At["materialRecords"]+134,2);
        Reject(model,runtimeAlpha.Bytes,"alpha obeys actual-frame access bound as well as declared span");
        var declared=F.Animation(pool:8);F.Half(declared.Bytes,declared.At["nodeRecords"]+10,7);F.Half(declared.Bytes,declared.At["nodeRecords"]+4,2);
        Reject(model,declared.Bytes,"declared node channel end beyond pool refused independently of runtime frame check");
        var color=F.Animation();color.Bytes[color.At["colors"]]=32;Reject(model,color.Bytes,"referenced color LUT entries bounded to native5-bit31");
        var name=F.Animation();name.Bytes[name.At["materialRecords"]]=(byte)'X';Reject(model,name.Bytes,"material animation name must match exact captured model material name");
        var missingTerm=F.Animation();Array.Fill(missingTerm.Bytes,(byte)'A',missingTerm.At["materialRecords"],64);Reject(model,missingTerm.Bytes,"material animation requires bounded ASCII terminated name");
        var nonAscii=F.Animation();nonAscii.Bytes[nonAscii.At["materialRecords"]]=0x80;Reject(model,nonAscii.Bytes,"nonASCII material binding name refused");
        var two=Model.Read(F.Model(materials:2).Bytes);var duplicate=F.Animation(materials:2);
        Array.Copy(duplicate.Bytes,duplicate.At["materialRecords"],duplicate.Bytes,duplicate.At["materialRecords"]+140,64);
        Reject(two,duplicate.Bytes,"duplicate animation names cannot silently target same material twice");
        var fewer=F.Animation(materials:1);Check(Binding.Read(two,Duration.Read(fewer.Bytes),fewer.Bytes).MaterialAnimationCount==1,"unanimated static model materials need not receive invented records");
        foreach(int table in new[]{3,4})
        {
            var baseData=F.Animation();var data=new F.Blob();data.Add(baseData.Bytes);int group=data.Reserve(table==3?28:32);data.Word(group,3);
            foreach(int offset in table==3?new[]{4,8,12,20}:new[]{12,16,20,24})data.Word(group+offset,(uint)group);
            data.Word(baseData.At["table"+table],(uint)group);
            Reject(model,data.Data.ToArray(),"binding explicitly refuses nonempty texture-coordinate/texture group despite bounded duration metadata");
        }
        Console.WriteLine($"CREDITS ANIMATION BINDING PASS {checks} invented checks; exact-byte admission only, no animation evaluator/model instance/GPU.");
    }
    // Refresh the independently admitted outer duration after each inner-field
    // mutation: otherwise a hash mismatch alone would mask channel defects.
    private static void Reject(Model.Snapshot model,byte[] bytes,string label)
    {var duration=Duration.Read(bytes);Expect(()=>Binding.Read(model,duration,bytes),label);}
    private static void Expect(Action action,string label)
    {checks++;try{action();}catch(InvalidDataException){return;}throw new InvalidOperationException("Binding expected refusal: "+label);}
    private static void Check(bool value,string label){checks++;if(!value)throw new InvalidOperationException("Binding fixture: "+label);}
}
