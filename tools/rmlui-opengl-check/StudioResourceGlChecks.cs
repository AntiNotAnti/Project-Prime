using System.Reflection;
using MphRead;
using MphRead.Mods;
using MphRead.Mods.Render;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Studio;
using MphRead.Mods.Launcher.RmlUi.Render;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;
using OpenTK.Graphics.OpenGL;

internal static class StudioResourceGlChecks
{
    internal static void Run(RmlUiHost host,RmlUiPageManager pages,NativeStudioEditorPage page,MapStudioScreen owner,int width,int height,string root,Action<bool,string> check)
    {
        var document=Field<MapDocument>(owner,"_document");var viewport=Field<MapViewport>(owner,"_viewport");var renderer=Field<Scene>(viewport,"_renderer");
        var history=document.History;var session=Field<INativeStudioEditorSession>(page,"_session");
        Action("native_resource_materials",85);Action("native_material_add",90);
        var controls=Field<NativeStudioResourcesController>(page,"_resourceControls");int materialIndex=controls.Selected.MaterialIndex;
        Guid materialId=document.Project.Definition.Materials[materialIndex].Id;
        check(materialId!=Guid.Empty&&controls.View==NativeStudioResourceView.Materials,"actual native Materials tab adds/selects a retained material identity");
        Draft("name","NATIVE_GL_MATERIAL");Draft("texScale","32");Draft("alpha","16");
        var sided=Locate("twoSided");PageTo(sided.Page);Action("native_material_cycle"+sided.Slot,101+sided.Slot);
        string before=document.Project.Definition.Serialize();Action("native_material_apply",99);string applied=document.Project.Definition.Serialize();
        check(document.Project.Definition.Materials[materialIndex] is {Name:"NATIVE_GL_MATERIAL",TexScale:32,Alpha:16,TwoSided:true},
            "native paged material text/number/boolean Apply reaches real material writer");
        Action("native_editor_undo",33);check(document.Project.Definition.Serialize()==before,"native material Apply undo restores exact authored bytes");
        Action("native_editor_redo",34);check(document.Project.Definition.Serialize()==applied,"native material Apply redo restores exact authored bytes");
        Action("native_material_reset",100);Draft("name","MUST_NOT_APPLY");Draft("texScale","NaN");Action("native_material_apply",99);
        check(document.Project.Definition.Serialize()==applied,"native invalid material field rejects all simultaneous drafts atomically");Action("native_material_reset",100);
        // More than one native page, using actual material commands rather than memory rows.
        for(int i=0;i<9;i++)Action("native_material_add",90);
        check(controls.MaterialPages>1&&controls.Materials.Count<=8,"actual native material list paginates bounded rows");
        SelectMaterial(materialIndex);Action("native_material_assign",92);
        check(MapObjects.Find(document.Project.Definition,document.ActiveObjectId)?.Value is MapGeometry geometry&&geometry.Material==materialIndex,
            "actual Assign Material changes real selected geometry through retained object history");
        string assets=Path.Combine(root,"resource-fixture");Directory.CreateDirectory(Path.Combine(assets,"textures"));
        using(var bitmap=new SkiaSharp.SKBitmap(4,4))
        {bitmap.Erase(SkiaSharp.SKColors.CornflowerBlue);using var encoded=bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Png,100);byte[] png=encoded.ToArray();File.WriteAllBytes(Path.Combine(assets,"textures/blue.png"),png);File.WriteAllBytes(Path.Combine(assets,"textures/blue.tex"),MapTextureBake.BakeImage(png));
            // Pagination declarations remain real private assets so the later
            // production Save/export can read every declared file unchanged.
            Directory.CreateDirectory(Path.Combine(assets,"unused"));
            for(int i=0;i<9;i++)File.WriteAllBytes(Path.Combine(assets,"unused",i+".png"),png);
        }
        document.Edit("Synthetic private resource declarations",d=>{
            d.BaseDirectory=assets;d.Assets.Add(new(){Path="textures/blue.tex",Kind="texture",Name="Native blue"});d.Assets.Add(new(){Path="textures/blue.png",Kind="texture",Name="HD blue"});
            for(int i=0;i<9;i++)d.Assets.Add(new(){Path="unused/"+i+".png",Kind="texture",Name="Unused "+i});
        });
        Action("native_material_reset",100);Action("native_resource_assets",86);
        check(controls.AssetPages>1&&controls.Assets.Count==8,"actual native declared asset list has bounded pagination");
        Action("native_asset_row0",124);int uploads=renderer.EditorTextureUploads;Action("native_asset_bind",132);
        check(document.Project.Definition.Materials[materialIndex].Texture=="textures/blue.tex"&&renderer.EditorTextureUploads>uploads,
            "native declared fallback binding updates existing real viewport texture cache");
        Action("native_asset_row1",125);Action("native_asset_channel",123);Action("native_asset_bind",132);
        check(document.Project.Definition.Materials[materialIndex].Albedo=="textures/blue.png"&&controls.Snapshot.Assets.Single(a=>a.Path=="textures/blue.png").Usages==1,
            "native HD channel binds exact selected asset/material references with real usage counts");
        host.SetField(page.Document,"native_asset_name","Native HD blue");Action("native_asset_rename",119);
        check(document.Project.Definition.Assets.Single(a=>a.Path=="textures/blue.png").Name=="Native HD blue","native asset logical-name edit uses existing document history");
        Action("native_asset_row2",126);string beforeRemove=document.Project.Definition.Serialize();
        Action("native_asset_delete",120);check(host.FocusedElement()=="native_asset_confirm_delete","native asset confirmation takes focus after visibility applies");
        Action("native_asset_cancel_delete",122);check(document.Project.Definition.Serialize()==beforeRemove,"native Keep Asset Reference leaves exact document unchanged");
        Action("native_asset_delete",120);Action("native_asset_confirm_delete",121);
        check(!document.Project.Definition.Assets.Any(a=>a.Path=="unused/0.png"),"native reviewed removal affects only unused declared reference");
        Action("native_editor_undo",33);check(document.Project.Definition.Serialize()==beforeRemove,"native asset reference undo restores exact authoring state");
        Action("native_editor_redo",34);Action("native_resource_materials",85);SelectMaterial(materialIndex);
        Draft("name","STALE_NATIVE_MATERIAL");document.Edit("Concurrent retained owner resource edit",d=>d.Description="Owner changed after draft");Settle();string stale=document.Project.Definition.Serialize();
        Action("native_material_apply",99);check(document.Project.Definition.Serialize()==stale&&controls.Status.Contains("CHANGED",StringComparison.Ordinal),"actual native resource draft cannot retarget after real owner history change");
        Action("native_material_reset",100);Capture("resources-materials");Action("native_resource_assets",86);Capture("resources-assets");
        Action("native_resource_objects",87);
        check(ReferenceEquals(document,Field<MapDocument>(owner,"_document"))&&ReferenceEquals(history,document.History)
            &&ReferenceEquals(viewport,Field<MapViewport>(owner,"_viewport"))&&ReferenceEquals(renderer,Field<Scene>(viewport,"_renderer"))&&session.Capture().Alive,
            "all native material/asset views retain sole document/history/viewport renderer and editor lease");
        check(GL.GetError()==ErrorCode.NoError,"native resource controls leave no GL error");

        void Frame(){page.Refresh();host.Update();GL.BindFramebuffer(FramebufferTarget.Framebuffer,0);GL.UseProgram(0);GL.ActiveTexture(TextureUnit.Texture0);GL.Viewport(0,0,width,height);GL.Disable(EnableCap.ScissorTest);GL.ClearColor(.02f,.04f,.06f,1);GL.Clear(ClearBufferMask.ColorBufferBit|ClearBufferMask.DepthBufferBit);check(page.DrawViewport(width,height,width,height),"resource frame uses existing contained viewport");RmlUiOpenGlFrame.Draw(host,width,height);}
        void Settle(){for(int i=0;i<3;i++)Frame();}
        void Action(string id,int argument){host.DiscardIntents();check(host.FocusDocument(page.Document,id),"native resource control focuses: "+id);Frame();host.Input.Key(2,true);host.Input.Key(2,false);host.Update();check(host.TryTakeIntent(out var intent)&&intent.Document==page.Document&&intent.Kind==RmlUiIntentKind.StudioAction&&intent.Argument==argument&&page.Handle(intent)&&!host.TryTakeIntent(out _),"one typed native resource intent: "+id);Settle();}
        (int Page,int Slot) Locate(string id){var fields=MapMaterialPropertySchema.Read(document.Project.Definition,controls.Selected.MaterialIndex);int at=fields.ToList().FindIndex(f=>f.Id==id);check(at>=0,"known native material property: "+id);return(at/8,at%8);}
        void PageTo(int target){while(controls.PropertyPage<target)Action("native_material_property_next",98);while(controls.PropertyPage>target)Action("native_material_property_previous",97);}
        void Draft(string id,string value){var at=Locate(id);PageTo(at.Page);check(host.FocusDocument(page.Document,"native_material_value"+at.Slot),"native material input focuses: "+id);host.SetField(page.Document,"native_material_value"+at.Slot,value);}
        void SelectMaterial(int index){while(controls.MaterialPage<index/8)Action("native_material_next",89);while(controls.MaterialPage>index/8)Action("native_material_previous",88);Action("native_material_row"+index%8,109+index%8);check(controls.Selected.MaterialId==materialId,"native material page/slot resolves exact original material identity");}
        void Capture(string name){Settle();string path=Path.Combine(root,name+".png");check(ScreenCapture.SaveWindow(width,height,path),"native resource capture: "+name);FinalCompositeCapture.WriteEvidence(path,width,height,"Actual RmlUi material/declared-asset controls and same retained synthetic-map renderer; private generated fixture textures, no asset import/app/account/network/performance claim.");}
    }
    private static T Field<T>(object instance,string name)=>(T)instance.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(instance)!;
}
