using System.Collections;
using MphRead.Mods.Campaign;
using OpenTK.Mathematics;
using Source=MphRead.Mods.Campaign.CampaignCreditsSource;
using Model=MphRead.Mods.Campaign.CampaignCreditsModelSource;
using Binding=MphRead.Mods.Campaign.CampaignCreditsAnimationBinding;
using Font=MphRead.Mods.Campaign.CampaignCreditsFontSource;
using Background=MphRead.Mods.Campaign.CampaignCreditsBackgroundSource;
using Catalog=MphRead.Mods.Campaign.CampaignCreditsCatalog;
using Owner=MphRead.Mods.Campaign.CampaignCreditsFrameOwner;
using F=CreditsRenderFixture;

// Invented documents, models, metrics and backgrounds. No source art, source
// strings, filesystem asset lookup, Scene, save, audio or GPU owner is used.
internal static class CreditsFrameFixtures
{
    private const string ModelPath="gameoptions/invented_Model.bin", AnimationPath="gameoptions/invented_Anim.bin";
    private static int checks;
    private sealed record Fixture(Source.Document Document,Dictionary<string,Model.Snapshot> Models,
        Dictionary<string,Binding.Snapshot> Animations,Font.Snapshot Font,Background.Snapshot[] Pictures);

    internal static void Run()
    {
        checks=0;CatalogIdentity();AggregateBudgets();ResultsFacts();FrameCollection();BackgroundRoles();OwnerIdentityAndLifetime();
        Console.WriteLine($"CREDITS FRAME PASS {checks} invented catalog/frame checks; no source media, IO, Scene, GL or save authority.");
    }
    private static void ResultsFacts()
    {
        var f=Make();var receipt=Summary();var results=new CampaignCreditsResultsBinding(f.Document,receipt);
        string Value(int page,int item)=>results.Resolve(f.Document,page,item,f.Document.Pages.Single(p=>p.Index==page).Items[item].States[0].Text!);
        Check(Value(49,0)=="3h 07m"&&Value(49,1)=="2m 05s"&&Value(49,2)=="73%"&&Value(49,3)=="12"&&Value(49,4)=="34",
            "result roles use captured ticks, percentage, deaths and hunterkills, never octoliths or enemykills");
        Check(Value(50,0)=="3h 07m"&&Value(50,1)=="F"&&Value(50,2)=="73%"&&Value(50,4)=="34",
            "bad result keeps distinct invented static Gorea status despite known battle ticks");
        Check(results.Resolve(f.Document,6,0,Style(0))=="A"&&f.Document.Strings[0].ContentLatin1=="A",
            "replacement is page/item-scoped and never mutates original document strings");
        receipt=receipt with{PlayTicks=0,Deaths=99,HunterKills=100};Check(Value(49,0)=="3h 07m"&&Value(49,3)=="12",
            "bound facts remain captured after caller replaces its receipt value");
        var unknown=new CampaignCreditsResultsBinding(f.Document,Summary() with{PlayTicks=null,GoreaTicks=-1,CompletionPercent=null});
        Check(unknown.Resolve(f.Document,49,0,Style(0))=="NOT AVAILABLE"&&unknown.Resolve(f.Document,49,1,Style(1))=="NOT AVAILABLE"
            &&unknown.Resolve(f.Document,49,2,Style(2))=="NOT AVAILABLE","missing/invalid time and percentage do not invent zero achievement");
        var zero=new CampaignCreditsResultsBinding(f.Document,Summary() with{PlayTicks=0,GoreaTicks=0,CompletionPercent=0});
        Check(zero.Resolve(f.Document,49,0,Style(0))=="0h 00m"&&zero.Resolve(f.Document,49,1,Style(1))=="0m 00s"&&zero.Resolve(f.Document,49,2,Style(2))=="0%",
            "explicit known zero remains distinct from unavailable");
        var huge=new CampaignCreditsResultsBinding(f.Document,Summary() with{PlayTicks=long.MaxValue,GoreaTicks=long.MaxValue,CompletionPercent=101});
        Check(huge.Resolve(f.Document,49,0,Style(0))=="42700796466920h 15m"&&huge.Resolve(f.Document,49,1,Style(1))=="15m 30s"
            &&huge.Resolve(f.Document,49,2,Style(2))=="NOT AVAILABLE","long tick formatting is bounded/invariant and invalid percentage remains unavailable");
        Expect<InvalidDataException>(()=>results.Resolve(f.Document,49,0,Style(1)),"a bound item cannot change its source StringId at resolve time");
        Expect<InvalidDataException>(()=>results.Resolve(Make().Document,49,0,Style(0)),"equivalent hashes cannot replace exact document captured by result binding");
        foreach(int kind in new[]{0,1,2,3,4})
        {
            var pages=f.Document.Pages.ToArray();int pageIndex=Array.FindIndex(pages,p=>p.Index==(kind==3||kind==4?50:49));var page=pages[pageIndex];var items=page.Items.ToArray();
            if(kind==0)items[0]=items[0] with{XFx=0}; // Missing anchor.
            if(kind==1)items=items.Append(items[0] with{Index=5}).ToArray(); // Ambiguous anchor.
            if(kind==2){var states=items[0].States.ToArray();states[1]=states[1] with{Text=Style(1)};items[0]=items[0] with{States=states};}
            if(kind==3)items[1]=items[1] with{States=items[1].States.Select(s=>s with{Text=Style(1)}).ToArray()};
            if(kind==4)items[0]=items[0] with{States=items[0].States.Select(s=>s with{Text=Style(6)}).ToArray()};
            pages[pageIndex]=page with{Items=items};var altered=Replace(f,pages:pages);
            Expect<InvalidDataException>(()=>new CampaignCreditsResultsBinding(altered.Document,Summary()),"missing/ambiguous/inconsistent/shared-bad-status result identities refuse atomically");
        }
        foreach(bool xAxis in new[]{true,false})foreach(int sign in new[]{-1,1})foreach(int distance in new[]{6143,6144})
        {
            var pages=f.Document.Pages.ToArray();int at=Array.FindIndex(pages,p=>p.Index==49);var items=pages[at].Items.ToArray();
            items[0]=xAxis?items[0] with{XFx=items[0].XFx+sign*distance}:items[0] with{YFx=items[0].YFx+sign*distance};
            pages[at]=pages[at] with{Items=items};var shifted=Replace(f,pages:pages);
            if(distance==6143)
            {
                var admitted=new CampaignCreditsResultsBinding(shifted.Document,Summary());
                Check(admitted.Resolve(shifted.Document,49,0,Style(0))=="3h 07m","result role admits both signs/axes strictly inside1.5pixel fixed-point tolerance");
            }
            else Expect<InvalidDataException>(()=>new CampaignCreditsResultsBinding(shifted.Document,Summary()),
                "result role refuses exact1.5pixel boundary on both signs/axes");
        }
    }
    private static void FrameCollection()
    {
        var f=Make();var catalog=Create(f);using var timeline=new CampaignCreditsTimeline(f.Document,catalog.Frames,true);using var owner=new Owner(catalog,timeline,Summary());
        var a=owner.Capture();var again=owner.Capture();
        Check(a.Page==6&&a.PageFrame==0&&!a.Closed&&a.Failure==null&&a.Backgrounds.Count==0&&a.Triangles.Count==6,
            "credits frame collects two widgets plus one glyph on black, without backgrounds");
        Check(a.Triangles.SequenceEqual(again.Triangles)&&timeline.Snapshot().PageFrame==0&&!timeline.TryTakeReturn(),"repeated Capture advances no source clock or return intent");
        Position(a.Triangles[0].A.Position,new(10,20,0),"widget itemXY applies once while itemDepth remains a separate sortkey, no text -192 shift");
        Position(a.Triangles[1].C.Position,new(10,22,0),"first widget retains second triangle native corner and sourceZ");
        Position(a.Triangles[2].A.Position,new(30,20,0),"equal-depth widgets preserve source item then triangle order");
        Position(a.Triangles[4].A.Position,new(20,-154,27),"text anchorY192 subtraction and negative-depth mapping each applied once");
        Position(a.Triangles[4].C.Position,new(28,-162,27),"glyph quad stays eight units with Y-up top-left coordinates");
        Check((a.Triangles[4].A.Texcoord-new Vector2((56+.02f)/128,(16+.02f)/256)).Length<.000001f
            &&(a.Triangles[4].C.Texcoord-new Vector2((64-.02f)/128,(24-.02f)/256)).Length<.000001f,
            "invented letterG independently numbers native glyph39 atlas cell with source.02pixel inset");
        var text=a.Triangles[4];Check(text.Texture.ModelSha256==f.Font.ModelSha256&&text.Texture.TextureId==0&&text.Texture.PaletteId==0
            &&text.WrapS==MphRead.RepeatMode.Clamp&&text.WrapT==MphRead.RepeatMode.Clamp,"glyph uses exact admitted font pair with clamped axes");
        Check(a.Triangles.Select(t=>t.Depth).SequenceEqual(new float[]{4,4,4,4,27,27}),"stable ascending source depth ordering is explicit");
        timeline.Tick();var b=owner.Capture();Position(b.Triangles[0].A.Position,new(11,21,1),"source age1 reevaluates animated widget position before itemXY translation");
        Check(b.Triangles[0].Depth==5,"widget animated sourceZ1 plus itemDepth4 changes sortkey without moving geometryZ");
        timeline.Tick();timeline.Tick();var loop=owner.Capture();Position(loop.Triangles[0].A.Position,new(10,20,0),"steady widget wraps at its own admitted frame3");
        Check(a.Triangles.SequenceEqual(again.Triangles)&&a.PageFrame==0&&b.PageFrame==1,"later sampling and timeline ticks never mutate prior frame arrays");
        Expect<NotSupportedException>(()=>((IList)a.Triangles).Clear(),"published triangle collection readonly");
        Check(owner.Textures.Count==2&&owner.Textures.Values.Any(p=>p.Width==8&&p.Height==8&&p.Rgba.Length==256)
            &&owner.Textures.Values.Any(p=>p.Width==128&&p.Height==256&&p.Rgba.Length==131072),"owner decodes only admitted exact texture pair extents");
        var hidden=Make();var page=hidden.Document.Pages.Single(p=>p.Index==6);var items=page.Items.ToArray();
        items[0]=items[0] with{DelayFx=4096};items[1]=items[1] with{States=new[]{new Source.State(0,1,0,1,0,null)}};
        items[2]=items[2] with{States=new[]{new Source.State(1,2,0,null,0,Style(6) with{StartColor=0x001f1f1f,EndColor=0x001f1f1f})}};
        hidden=Replace(hidden,firstItems:items);var hiddenCatalog=Create(hidden);using var hiddenTimeline=new CampaignCreditsTimeline(hidden.Document,hiddenCatalog.Frames,true);using var hiddenOwner=new Owner(hiddenCatalog,hiddenTimeline,Summary());
        Check(hiddenOwner.Capture().Triangles.Count==0,"unstarted item, absent currentvisual and current textAlpha0 each emit no triangles");
        hiddenTimeline.Tick();Check(hiddenOwner.Capture().Triangles.Count==2,"newly due source widget starts through existing timeline, not Capture");
        var entrance=Make();var entranceItems=entrance.Document.Pages[0].Items.ToArray();
        entranceItems[0]=entranceItems[0] with{States=new[]{new Source.State(0,17,0,0,0,null),new Source.State(0,2,0,0,0,null)}};
        entrance=Replace(entrance,firstItems:entranceItems);var entranceCatalog=Create(entrance);
        using var entranceTimeline=new CampaignCreditsTimeline(entrance.Document,entranceCatalog.Frames,true);using var entranceOwner=new Owner(entranceCatalog,entranceTimeline,Summary());
        entranceTimeline.Tick();entranceTimeline.Tick();
        Check(entranceTimeline.Snapshot().Items[0].Code==17,"native transition remains active through last admitted animation frame");
        Position(entranceOwner.Capture().Triangles[0].A.Position,new(12,22,2),"transition samples final held-frame endpoint before existing source arrival");
        entranceTimeline.Tick();
        Check(entranceTimeline.Snapshot().Items[0].Code==2,"only timeline arrival switches transition to steady state");
        Position(entranceOwner.Capture().Triangles[0].A.Position,new(10,20,0),"new steady visual starts its own frame0 rather than retaining prior transition age");
    }
    private static void BackgroundRoles()
    {
        foreach(bool good in new[]{true,false})
        {
            var f=Make(navigate:true);var catalog=Create(f);using var timeline=new CampaignCreditsTimeline(f.Document,catalog.Frames,good);using var owner=new Owner(catalog,timeline,Summary(good));
            for(int i=0;i<8&&timeline.Snapshot().Page<49;i++)timeline.Tick();var frame=owner.Capture();
            Check(frame.Page==(good?49:50)&&frame.Backgrounds.Count==2,"existing source timer/callback branch reaches exact selected result background roles");
            var top=frame.Backgrounds[0];var bottom=frame.Backgrounds[1];
            Check(top.Image.SourceSha256==f.Pictures[good?0:2].SourceSha256&&bottom.Image.SourceSha256==f.Pictures[good?1:3].SourceSha256,
                "good/bad and top/bottom image identities cannot cross");
            Check(top is{X:0,Y:192,Width:256,Height:192}&&bottom is{X:0,Y:0,Width:256,Height:192},
                "two source-space stacked rectangles preserve full256x192 extents without claiming widescreen recomposition");
            Expect<NotSupportedException>(()=>((IList)frame.Backgrounds).Clear(),"background plan readonly");
            owner.Dispose();Check(!timeline.Snapshot().Closed&&timeline.Tick(),"frame disposal releases only its own caches and does not retire timeline");
            Check(frame.Backgrounds[0].Image.SourceSha256==top.Image.SourceSha256&&frame.Triangles.Count>0,"old frame remains readable after owner disposal");
        }
    }
    private static void OwnerIdentityAndLifetime()
    {
        var f=Make();var catalog=Create(f);using var timeline=new CampaignCreditsTimeline(f.Document,catalog.Frames,true);
        Expect<InvalidDataException>(()=>new Owner(catalog,timeline,Summary(false)),"captured ending outcome must match existing timeline branch");
        var another=Create(Make());using var sameHashes=new CampaignCreditsTimeline(another.Document,another.Frames,true);
        Expect<InvalidDataException>(()=>new Owner(catalog,sameHashes,Summary()),"equivalent source hashes do not transfer another catalog/timeline owner identity");
        var durations=f.Animations.ToDictionary(p=>p.Key,p=>CampaignCreditsAnimationDuration.Read(p.Value.Memory.Span));
        using var otherTable=new CampaignCreditsTimeline(f.Document,CampaignCreditsFrameTable.Bind(f.Document,durations),true);
        Expect<InvalidDataException>(()=>new Owner(catalog,otherTable,Summary()),"new frame table with same values cannot replace exact catalog timeline binding");
        var owner=new Owner(catalog,timeline,Summary());var old=owner.Capture();var foreign=new List<Exception?>();
        var thread=new Thread(()=>{foreach(System.Action action in new System.Action[]{()=>owner.Capture(),()=>{_ = owner.Textures;},()=>owner.Dispose()})
            try{action();foreign.Add(null);}catch(Exception error){foreign.Add(error);}}){IsBackground=true};
        thread.Start();Check(thread.Join(TimeSpan.FromSeconds(5))&&foreign.Count==3&&foreign.All(e=>e is InvalidOperationException),"capture/pixel ownership/disposal all reject another creating thread");
        Check(owner.Capture().Triangles.SequenceEqual(old.Triangles)&&!timeline.Snapshot().Closed,"foreign thread refusals preserve original frame and timeline");
        timeline.Retire();var closed=owner.Capture();Check(closed.Closed&&closed.Triangles.Count==0&&closed.Backgrounds.Count==0&&!timeline.TryTakeReturn(),"retired source timeline cannot publish stale geometry/backgrounds or return intent");
        owner.Dispose();owner.Dispose();Expect<ObjectDisposedException>(()=>owner.Capture(),"disposed frame owner cannot sample again");
        Check(old.Triangles.Count==6,"owner disposal cannot clear previously published detached geometry");
    }
    private static void AggregateBudgets()
    {
        var f=Make(animate:false);var widgets=new Dictionary<int,Source.Widget>();var models=new Dictionary<string,Model.Snapshot>();
        for(int id=0;id<8;id++)
        {
            string path=$"gameoptions/invented{id}_Model.bin";var bytes=F.Model();F.Word(bytes.Bytes,0,(uint)id);
            models.Add(path,Model.Read(bytes.Bytes));widgets.Add(id,new(id,0,0,new[]{new Source.WidgetFile(0,(byte)(path.Length+1),0,0,path)}));
        }
        var manyModels=Replace(f,widgets:widgets,firstItems:Enumerable.Range(0,8).Select(id=>WidgetItem(id,id,0,0,0)).ToArray()) with{Models=models};
        Expect<InvalidDataException>(()=>Create(manyModels),"eight widget model hashes plus distinct font exceeds aggregate model admission");
        var animated=Make();widgets=new();var animations=new Dictionary<string,Binding.Snapshot>();var model=animated.Models[ModelPath];
        for(int id=0;id<33;id++)
        {
            string path=$"gameoptions/invented{id}_Anim.bin";var bytes=F.Animation();F.Half(bytes.Bytes,22,(ushort)id);
            animations.Add(path,Binding.Read(model,CampaignCreditsAnimationDuration.Read(bytes.Bytes),bytes.Bytes));
            widgets.Add(id,new(id,0,0,new[]{new Source.WidgetFile(0,(byte)(ModelPath.Length+1),0,0,ModelPath),new(0,(byte)(path.Length+1),0,0,path)}));
        }
        var manySamplers=Replace(animated,widgets:widgets,firstItems:Enumerable.Range(0,33).Select(id=>WidgetItem(id,id,0,0,0)).ToArray()) with{Animations=animations};
        Expect<InvalidDataException>(()=>Create(manySamplers),"thirty-three distinct admitted model/binding pairs exceed private sampler admission despite one shared model");
        var layouts=Make();var manyText=Enumerable.Range(0,64).Select(index=>Item(index,0,0,0,0,
            new byte[]{2,3,17,10}.Select(code=>new Source.State(1,code,0,null,0,Style(6))).ToArray())).ToArray();
        Expect<InvalidDataException>(()=>Create(Replace(layouts,firstItems:manyText)),"individually bounded source items/states exceed aggregate cached text layouts");
        var strings=layouts.Document.Strings.ToDictionary(p=>p.Key,p=>new Source.Text(p.Key,2000,new string('A',2000)));
        Expect<InvalidDataException>(()=>Create(Replace(layouts,strings:strings)),"individually short source strings cannot exceed aggregate style-character budget");
        // One legal large texture can have distinct palettes per material.
        // Admission must account for reachable image pairs before any GetPixels.
        var pixels=Make(animate:false);var source=F.Model(materials:9,width:512,height:512);var b=new F.Blob();b.Add(source.Bytes);int pal=b.Reserve(9*16);
        for(int i=0;i<9;i++){b.Word(pal+16*i,(uint)source.At["paletteData"]);b.Word(pal+16*i+4,4);b.Half(source.At["material"+i]+68,(ushort)i);}
        b.Word(52,(uint)pal);b.Half(48,9);pixels.Models[ModelPath]=Model.Read(b.Data.ToArray());
        // Warm identical rejection before allocation accounting excludes exception
        // setup/JIT; all source admission and payload construction occurred above.
        Expect<InvalidDataException>(()=>Create(pixels),"nine512-square palette variants exceed8MiB decoded RGBA despite one admitted texture");
        long before=GC.GetAllocatedBytesForCurrentThread();
        Expect<InvalidDataException>(()=>Create(pixels),"repeat over-budget catalog refusal remains deterministic");
        Check(GC.GetAllocatedBytesForCurrentThread()-before<1024*1024,"aggregate pair refusal occurs before image decoding or multi-megabyte material/model allocations");
    }
    private static Fixture Replace(Fixture f,Dictionary<int,Source.Widget>? widgets=null,Source.Item[]? firstItems=null,
        Dictionary<int,Source.Text>? strings=null,Source.Page[]? pages=null)
    {
        var d=f.Document;pages??=d.Pages.Select(p=>p.Index==6&&firstItems!=null?p with{Items=Array.AsReadOnly(firstItems)}:p).ToArray();
        return f with{Document=new(d.MarmSha256,d.TextSha256,d.HeaderField4,d.HeaderField6,d.HeaderField7,d.HeaderField16,
            d.GlobalPageCount,widgets?.Count??d.GlobalWidgetCount,d.GlobalStringCount,0,pages,
            widgets??d.Widgets.ToDictionary(p=>p.Key,p=>p.Value),strings??d.Strings.ToDictionary(p=>p.Key,p=>p.Value))};
    }
    private static Catalog Create(Fixture f)=>new(f.Document,f.Models,f.Animations,f.Font,
        new(f.Pictures[0],f.Pictures[1],f.Pictures[2],f.Pictures[3]));
    private static void CatalogIdentity()
    {
        var f=Make();var catalog=Create(f);var model=f.Models[ModelPath];var binding=f.Animations[AnimationPath];
        Check(ReferenceEquals(catalog.Document,f.Document)&&ReferenceEquals(catalog.Font,f.Font),"catalog pins admitted immutable document and font");
        Check(catalog.Widgets.Count==3&&catalog.Widgets[0].Model.SourceSha256==model.SourceSha256
            &&catalog.Widgets[1].Animation?.SourceSha256==binding.SourceSha256&&catalog.Widgets[2].Animation==null,
            "each declared widget receives exact required model and optional animation, including repeated pair and still instance");
        Check(catalog.Models.Count==2&&catalog.Models.ContainsKey(model.SourceSha256)&&catalog.Models.ContainsKey(f.Font.ModelSha256),
            "immutable model inventory deduplicates exact source hashes and includes font");
        Check(catalog.Frames.Frames[0]==3&&catalog.Frames.Frames[1]==3&&catalog.Frames.Frames[2]==1,
            "catalog derives timeline durations from matching admitted bindings without sampling or filesystem IO");
        Check(catalog.TexturePairs.Count==2&&catalog.TexturePairs.Any(p=>p.Key.ModelSha256==model.SourceSha256&&p.Key.TextureId==0&&p.Key.PaletteId==0&&p.Width==8&&p.Height==8)
            &&catalog.TexturePairs.Any(p=>p.Key.ModelSha256==f.Font.ModelSha256&&p.Width==128&&p.Height==256),"texture budget inventory contains each exact model/texture/palette pair once, font included");
        Check(catalog.Budget.SamplerCount==2&&catalog.Budget.SamplerVertices==8&&catalog.Budget.SamplerTriangles==4,
            "two widget IDs sharing one animated pair cost one sampler; still pair costs its own four vertices/two triangles");
        Check(catalog.Budget.SamplerPixels==128,"private still and animated Model decodes each cost admitted64pixels despite shared source hash");
        Check(catalog.Budget.TextureRgbaBytes==4*(64+32768)&&catalog.Budget.TextLayouts==36&&catalog.Budget.TextCharacters==36,
            "independent tiny inventory oracle counts each native texture pair once and each selected textstate once");
        Check(catalog.Budget.SourceBytes==model.ByteLength+f.Font.Model.ByteLength+binding.Memory.Length+960+4*49664+36,
            "retained-source budget counts unique model/binding bytes, metrics, four encoded backgrounds and retained style characters");
        f.Models.Clear();f.Animations.Clear();
        Check(catalog.Widgets[0].Model.SourceSha256==model.SourceSha256&&catalog.Widgets[1].Animation?.SourceSha256==binding.SourceSha256,
            "caller model/animation dictionaries can be cleared without retargeting captured catalog");
        Expect<NotSupportedException>(()=>((IDictionary<int,Catalog.WidgetBinding>)catalog.Widgets).Clear(),"widget inventory cannot mutate after admission");
        Expect<NotSupportedException>(()=>((IDictionary<string,Model.Snapshot>)catalog.Models).Clear(),"model hash inventory cannot mutate after admission");
        Expect<NotSupportedException>(()=>((IList)catalog.TexturePairs).Clear(),"pair inventory cannot mutate after admission");
        var missing=Make();missing.Models.Clear();Expect<InvalidDataException>(()=>Create(missing),"missing required model rejected as complete catalog");
        missing=Make();missing.Animations.Clear();Expect<InvalidDataException>(()=>Create(missing),"missing required animation cannot silently downgrade to still");
        var extra=Make();extra.Models.Add("gameoptions/unreferenced_Model.bin",extra.Models[ModelPath]);
        Expect<InvalidDataException>(()=>Create(extra),"extra model path refused even when its source hash duplicates a required model");
        extra=Make();extra.Animations.Add("gameoptions/unreferenced_Anim.bin",extra.Animations[AnimationPath]);
        Expect<InvalidDataException>(()=>Create(extra),"extra animation path refused even when its source hash duplicates a required binding");
        var insensitive=Make();var wrongCase=new Dictionary<string,Model.Snapshot>(StringComparer.OrdinalIgnoreCase){{ModelPath.ToUpperInvariant(),insensitive.Models[ModelPath]}};
        Expect<InvalidDataException>(()=>Create(insensitive with{Models=wrongCase}),"caller insensitive comparer cannot replace exact canonical source path");
        var slashes=Make();var wrongSlashes=new Dictionary<string,Model.Snapshot>{{ModelPath.Replace('/','\\'),slashes.Models[ModelPath]}};
        Expect<InvalidDataException>(()=>Create(slashes with{Models=wrongSlashes}),"unadmitted backslash alias cannot substitute for canonical normalized source key");
        var mismatch=Make();var changed=F.Model();F.Word(changed.Bytes,0,2);mismatch.Models[ModelPath]=Model.Read(changed.Bytes);
        Expect<InvalidDataException>(()=>Create(mismatch),"animation bound to another admitted model hash cannot retarget same path");
        var variants=Make(animate:false);var source=F.Model(materials:2);var blob=new F.Blob();blob.Add(source.Bytes);int palettes=blob.Reserve(32);
        for(int i=0;i<2;i++){blob.Word(palettes+16*i,(uint)source.At["paletteData"]);blob.Word(palettes+16*i+4,4);}
        blob.Word(52,(uint)palettes);blob.Half(48,2);blob.Half(source.At["material1"]+68,1);variants.Models[ModelPath]=Model.Read(blob.Data.ToArray());
        var paletteCatalog=Create(variants);
        Check(paletteCatalog.TexturePairs.Count==3&&paletteCatalog.Budget.TextureRgbaBytes==4*(64*2+32768),
            "same encoded texture with two reachable palettes consumes two decoded-image budgets, font remains separate");
        var incomplete=Make();
        Expect<InvalidDataException>(()=>new Catalog(incomplete.Document,incomplete.Models,incomplete.Animations,incomplete.Font,
            new(incomplete.Pictures[0],incomplete.Pictures[1],null!,incomplete.Pictures[3])),"one absent ending background refuses complete catalog");
        var aliases=Make();const string secondModel="gameoptions/inventedAlias_Model.bin",secondAnimation="gameoptions/inventedAlias_Anim.bin";
        byte[] modelBytes=F.Model().Bytes,animationBytes=F.Animation().Bytes;
        var firstModel=Model.Read(modelBytes);var equalModel=Model.Read(modelBytes);
        var firstBinding=Binding.Read(firstModel,CampaignCreditsAnimationDuration.Read(animationBytes),animationBytes);
        var equalBinding=Binding.Read(equalModel,CampaignCreditsAnimationDuration.Read(animationBytes),animationBytes);
        Check(!ReferenceEquals(firstModel,equalModel)&&!ReferenceEquals(firstBinding,equalBinding),"equal-hash ownership fixture starts with separately admitted model and binding allocations");
        var aliasWidgets=aliases.Document.Widgets.ToDictionary(p=>p.Key,p=>p.Value);
        aliasWidgets[1]=aliasWidgets[1] with{Files=new[]{new Source.WidgetFile(0,(byte)(secondModel.Length+1),0,0,secondModel),
            new(0,(byte)(secondAnimation.Length+1),0,0,secondAnimation)}};
        aliases=Replace(aliases,widgets:aliasWidgets) with
        {
            Models=new(StringComparer.Ordinal){{ModelPath,firstModel},{secondModel,equalModel}},
            Animations=new(StringComparer.Ordinal){{AnimationPath,firstBinding},{secondAnimation,equalBinding}}
        };
        var canonical=Create(aliases);
        Check(ReferenceEquals(canonical.Widgets[0].Model,canonical.Models[firstModel.SourceSha256])
            &&ReferenceEquals(canonical.Widgets[0].Model,canonical.Widgets[1].Model)&&ReferenceEquals(canonical.Widgets[0].Model,canonical.Widgets[2].Model),
            "separate required paths with equal model bytes retain one canonical hash-owned snapshot in every widget");
        Check(ReferenceEquals(canonical.Widgets[0].Animation,canonical.Widgets[1].Animation),
            "separate required animation paths with equal bytes retain the same canonical binding snapshot");
        Check(canonical.Budget.SamplerCount==2&&canonical.Budget.SamplerVertices==8&&canonical.Budget.SamplerTriangles==4
            &&canonical.Budget.SourceBytes==catalog.Budget.SourceBytes,"canonical equal-hash source ownership matches deduplicated byte and sampler budgets");
        // Identical animation bytes are not the same binding when two admitted
        // models share native names but own different palette/source identities.
        var alteredPalette=F.Model();F.Half(alteredPalette.Bytes,alteredPalette.At["paletteData"]+2,2);
        var otherModel=Model.Read(alteredPalette.Bytes);
        var otherBinding=Binding.Read(otherModel,CampaignCreditsAnimationDuration.Read(animationBytes),animationBytes);
        Check(otherBinding.SourceSha256==firstBinding.SourceSha256&&otherBinding.ModelSha256!=firstBinding.ModelSha256,
            "same animation bytes independently bind to two different admitted model identities");
        var twoModels=aliases with
        {
            Models=new(StringComparer.Ordinal){{ModelPath,firstModel},{secondModel,otherModel}},
            Animations=new(StringComparer.Ordinal){{AnimationPath,firstBinding},{secondAnimation,otherBinding}}
        };
        var paired=Create(twoModels);
        Check(paired.Widgets[0].Animation!.ModelSha256==firstModel.SourceSha256&&paired.Widgets[1].Animation!.ModelSha256==otherModel.SourceSha256
            &&!ReferenceEquals(paired.Widgets[0].Animation,paired.Widgets[1].Animation),
            "canonical binding ownership uses model hash plus animation hash and keeps both model-bound copies");
        Check(paired.Budget.SamplerCount==3&&paired.Budget.SamplerVertices==12&&paired.Budget.SamplerTriangles==6
            &&paired.Budget.SourceBytes==canonical.Budget.SourceBytes+otherModel.ByteLength+otherBinding.Memory.Length,
            "two-model same-animation inventory charges both retained bindings and each private sampler");
        using var pairedTimeline=new CampaignCreditsTimeline(twoModels.Document,paired.Frames,true);
        using var pairedOwner=new Owner(paired,pairedTimeline,Summary());var pairedFrame=pairedOwner.Capture();
        Check(pairedFrame.Triangles.Count==6&&pairedFrame.Triangles[0].Texture.ModelSha256==firstModel.SourceSha256
            &&pairedFrame.Triangles[2].Texture.ModelSha256==otherModel.SourceSha256,
            "frame owner constructs and samples both correctly model-bound animations without hash-only retargeting");
    }

    // Helpers deliberately author public value records independently of MARM
    // pointer serialization. Other fixture suites own raw source admission.
    private static Fixture Make(bool navigate=false,bool animate=true,string marmIdentity="a",string textIdentity="b")
    {
        var model=Model.Read(F.Model().Bytes);var animation=F.Animation();
        F.Word(animation.Bytes,animation.At["translation"]+4,4096);
        F.Word(animation.Bytes,animation.At["translation"]+8,8192);
        var binding=Binding.Read(model,CampaignCreditsAnimationDuration.Read(animation.Bytes),animation.Bytes);
        var atlas=Model.Read(F.Model(format:6,width:128,height:256).Bytes);
        var font=Font.Read(atlas,Enumerable.Repeat((byte)4,480).ToArray(),new byte[480]);
        var widgets=new Dictionary<int,Source.Widget>();
        for(int id=0;id<3;id++)
        {
            var files=new List<Source.WidgetFile>{new(0,(byte)(ModelPath.Length+1),0,0,ModelPath)};
            if(animate&&id!=2)files.Add(new(0,(byte)(AnimationPath.Length+1),0,0,AnimationPath));
            widgets.Add(id,new(id,0,0,Array.AsReadOnly(files.ToArray())));
        }
        var strings=new Dictionary<int,Source.Text>();
        for(int id=0;id<7;id++)strings.Add(id,new(id,1,((char)('A'+id)).ToString()));
        Source.Page[] pages=new[]{6,7,8,9,10,11,12,49,50}.Select(index=>
        {
            Source.Item[] items=index switch
            {
                6=>new[]{WidgetItem(0,0,10,20,4),WidgetItem(1,1,30,20,4),TextItem(2,6,20,30,-5)},
                7=>new[]{WidgetItem(0,2,0,0,0)},
                49 or 50=>new[]{TextItem(0,0,38,136,0,true),TextItem(1,index==49?1:5,38,82,0,true),
                    TextItem(2,2,128,25,0,true),TextItem(3,3,218,136,0,true),TextItem(4,4,218,82,0,true)},
                _=>new[]{TextItem(0,6,0,0,0)}
            };
            Source.Timer[] timers=index==6&&navigate?new[]{new Source.Timer(0,Action(targetPage:12),0,0,0)}:
                index==12&&navigate?new[]{new Source.Timer(0,Action(callback:12),0,0,0)}:Array.Empty<Source.Timer>();
            return new Source.Page(index,0,0,200,201,0,0,0,Array.AsReadOnly(items),Array.Empty<Source.Action>(),Array.AsReadOnly(timers));
        }).ToArray();
        var document=new Source.Document(new string(marmIdentity[0],64),new string(textIdentity[0],64),0,0,0,0,51,3,7,0,pages,widgets,strings);
        Background.Snapshot Picture(int index){var bytes=new byte[Background.EncodedBytes];F.Half(bytes,0,(ushort)(index+1));return Background.Read(bytes);}
        return new(document,new(StringComparer.Ordinal){{ModelPath,model}},animate?new(StringComparer.Ordinal){{AnimationPath,binding}}:new(StringComparer.Ordinal),
            font,Enumerable.Range(0,4).Select(Picture).ToArray());
    }
    private static Source.Item WidgetItem(int index,int widget,int x,int y,int depth,int delay=0)=>Item(index,x,y,depth,delay,
        new[]{new Source.State(0,2,0,widget,0,null)});
    private static Source.Item TextItem(int index,int text,int x,int y,int depth,bool result=false)=>Item(index,x,y,depth,0,
        (result?new byte[]{17,2,10}:new byte[]{2}).Select(code=>new Source.State(1,code,0,null,0,Style(text))).ToArray());
    private static Source.Item Item(int index,int x,int y,int depth,int delay,Source.State[] states)=>new(index,0,0,delay,0,0,
        x*4096,y*4096,depth*4096,0,2,0,0,0,0,Array.AsReadOnly(states),Array.Empty<Source.Action>());
    private static Source.TextStyle Style(int id)=>new(id,0,0x1f1f1f1f,0x1f1f1f1f,0,0,0,8,0,0,0,0,0,0);
    private static Source.Action Action(int targetPage=255,int callback=0)=>new(0,0,0,0,0,0,0,0,
        callback==0?Array.Empty<Source.Call>():new[]{new Source.Call(0,callback)},65535,(byte)targetPage,0);
    private static CampaignEndingSummary Summary(bool good=true)=>new(1,good?CampaignEndingKind.Complete:CampaignEndingKind.Incomplete,
        false,73,8,12,34,999){PlayTicks=60L*(3*3600+7*60),GoreaTicks=60L*(2*60+5)};
    private static void Position(Vector3 actual,Vector3 expected,string label)=>Check((actual-expected).Length<.00001f,label);
    private static void Check(bool value,string label){checks++;if(!value)throw new InvalidOperationException("Frame fixture: "+label);}
    private static void Expect<T>(System.Action action,string label)where T:Exception
    {checks++;try{action();}catch(T){return;}throw new InvalidOperationException("Frame expected "+typeof(T).Name+": "+label);}
}
