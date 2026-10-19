#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.Linq;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Studio;

internal sealed partial class NativeStudioEditorPage
{
    private NativeStudioResourcesController? _resourceControls;
    private long _resourcePresented=-1,_resourceDraftPresented=-1;
    private Guid _resourceConfirmation;
    private string _resourceFocus="",_resourceAsset="";
    private NativeStudioResourcesController? ResourceControls=>_resourceControls??= _session is INativeStudioEditorResources owner?new(owner):null;
    private void SaveResourceDrafts()
    {
        if(ResourceControls is not {} resources||resources.View!=NativeStudioResourceView.Materials)return;
        var rows=resources.Properties;
        for(int i=0;i<rows.Count;i++)resources.SetDraft(i,_host.ReadField(_page,"native_material_value"+i,Math.Min(4096,rows[i].Field.MaxLength+1)));
    }
    private bool HandleResourceIntent(int action)
    {
        if(ResourceControls is not {} r)return false;
        bool materialAction=action is >=88 and <=116 or 136 or 137;
        bool assetAction=action is >=117 and <=133 or 135;
        if(materialAction&&r.View!=NativeStudioResourceView.Materials||assetAction&&r.View!=NativeStudioResourceView.Assets)return false;
        if(action==95&&r.Delete is not {Asset:false}||action==121&&r.Delete is not {Asset:true})return false;
        SaveResourceDrafts();
        if(action is not(94 or 95 or 96 or 120 or 121 or 122))r.CancelDelete();
        switch(action)
        {
            case 85:r.Show(NativeStudioResourceView.Materials);break;
            case 86:r.Show(NativeStudioResourceView.Assets);break;
            case 87:r.Show(NativeStudioResourceView.Objects);break;
            case 88:r.MaterialPageBy(-1);break;case 89:r.MaterialPageBy(1);break;
            case 90:r.Execute(NativeStudioResourceOperation.AddMaterial);break;
            case 91:r.Execute(NativeStudioResourceOperation.DuplicateMaterial);break;
            case 92:r.Execute(NativeStudioResourceOperation.AssignSelection);break;
            case 93:r.Execute(NativeStudioResourceOperation.SelectUsages);break;
            case 94:r.RequestDelete(asset:false);break;case 95:case 121:r.ConfirmDelete();break;
            case 96:case 122:r.CancelDelete();break;
            case 97:r.PropertyPageBy(-1);break;case 98:r.PropertyPageBy(1);break;
            case 99:r.Apply();break;case 100:r.ResetDraft();break;
            case 117:r.AssetPageBy(-1);break;case 118:r.AssetPageBy(1);break;
            case 119:r.Execute(NativeStudioResourceOperation.RenameAsset,_host.ReadField(_page,"native_asset_name",257));break;
            case 120:r.RequestDelete(asset:true);break;
            case 123:r.CycleChannel();break;
            case 132:r.Execute(NativeStudioResourceOperation.BindAssetChannel);break;
            case 133:r.Execute(NativeStudioResourceOperation.ClearChannel);break;
            case 134:r.Execute(NativeStudioResourceOperation.RefreshCache);break;
            case 135:r.Execute(NativeStudioResourceOperation.FindAssetUsages);break;
            case 136:r.UseReplacement();break;
            case 137:r.Execute(NativeStudioResourceOperation.ReplaceMaterialUsages);break;
            case 138:r.Refresh();r.ResetSelection();break;
            default:
                if(action is >=101 and <109)r.CycleProperty(action-101);
                else if(action is >=109 and <117)r.SelectMaterial(action-109);
                else if(action is >=124 and <132)r.SelectAsset(action-124);
                else return false;break;
        }
        _controller.ShowStatus(r.Status);return true;
    }
    private void RefreshResources()
    {
        if(ResourceControls is not {} r)return;r.Refresh();
        Guid confirmation=r.Delete?.Confirmation??Guid.Empty;
        if(confirmation!=_resourceConfirmation)
        {
            _resourceFocus=confirmation!=Guid.Empty?(r.Delete!.Asset?"native_asset_confirm_delete":"native_material_confirm_delete")
                :r.View==NativeStudioResourceView.Assets?"native_asset_delete":"native_material_delete";
            _resourceConfirmation=confirmation;
        }
        if(_resourceFocus.Length>0&&_host.FocusDocument(_page,_resourceFocus))_resourceFocus="";
    }
    private void BindResources(Dictionary<string,RmlUiBindingValue> bindings)
    {
        var r=ResourceControls;bool materials=r?.View==NativeStudioResourceView.Materials,assets=r?.View==NativeStudioResourceView.Assets;
        bindings["visible:native_resource_tabs"]=RmlUiBindingValue.FromBoolean(r!=null);
        bindings["visible:native_object_panel"]=RmlUiBindingValue.FromBoolean(!materials&&!assets);
        bindings["visible:native_material_panel"]=RmlUiBindingValue.FromBoolean(materials);
        bindings["visible:native_asset_panel"]=RmlUiBindingValue.FromBoolean(assets);
        if(r==null)return;
        bindings["native_material_page"]=RmlUiBindingValue.FromText($"MATERIALS {r.MaterialPage+1} / {r.MaterialPages}");
        bindings["native_asset_page"]=RmlUiBindingValue.FromText($"ASSETS {r.AssetPage+1} / {r.AssetPages}");
        bindings["native_material_property_page"]=RmlUiBindingValue.FromText($"FIELDS {r.PropertyPage+1} / {r.PropertyPages}");
        bindings["native_resource_notices"]=RmlUiBindingValue.FromText(r.Snapshot.ChangedSources==0?"DECLARED PROJECT RESOURCES // NO PENDING SOURCE NOTICES":r.Snapshot.ChangedSources+" SOURCE CHANGES PENDING // RESELECT BEFORE APPLY");
        var selectedMaterial=r.Snapshot.Materials.FirstOrDefault(m=>m.Index==r.Selected.MaterialIndex&&m.Id==r.Selected.MaterialId);
        bindings["native_material_selection"]=RmlUiBindingValue.FromText(selectedMaterial==null?"SELECT A MATERIAL":$"SELECTED MATERIAL {selectedMaterial.Index} // {selectedMaterial.Name}");
        bindings["native_asset_channel"]=RmlUiBindingValue.FromText("CHANNEL: "+(r.Channel switch {
            NativeStudioMaterialChannel.Texture=>"NATIVE FALLBACK (.TEX)",NativeStudioMaterialChannel.Albedo=>"HD ALBEDO",
            NativeStudioMaterialChannel.Normal=>"HD NORMAL",NativeStudioMaterialChannel.SpecularRoughness=>"HD SPECULAR / ROUGHNESS",_=>"HD EMISSIVE"}));
        bindings["disabled:native_material_previous"]=RmlUiBindingValue.FromBoolean(r.MaterialPage==0);
        bindings["disabled:native_material_next"]=RmlUiBindingValue.FromBoolean(r.MaterialPage+1>=r.MaterialPages);
        bindings["disabled:native_asset_previous"]=RmlUiBindingValue.FromBoolean(r.AssetPage==0);
        bindings["disabled:native_asset_next"]=RmlUiBindingValue.FromBoolean(r.AssetPage+1>=r.AssetPages);
        bindings["disabled:native_material_property_previous"]=RmlUiBindingValue.FromBoolean(r.PropertyPage==0);
        bindings["disabled:native_material_property_next"]=RmlUiBindingValue.FromBoolean(r.PropertyPage+1>=r.PropertyPages);
        bindings["visible:native_material_delete_panel"]=RmlUiBindingValue.FromBoolean(r.Delete is {Asset:false});
        bindings["visible:native_asset_delete_panel"]=RmlUiBindingValue.FromBoolean(r.Delete is {Asset:true});
        bindings["native_material_delete_label"]=RmlUiBindingValue.FromText(r.Delete?.Label??"");
        bindings["native_asset_delete_label"]=RmlUiBindingValue.FromText(r.Delete?.Label??"");
        var materialRows=r.Materials;var assetRows=r.Assets;var properties=r.Properties;
        for(int i=0;i<8;i++)
        {
            var material=i<materialRows.Count?materialRows[i]:null;var asset=i<assetRows.Count?assetRows[i]:null;
            bindings["native_material_row"+i]=RmlUiBindingValue.FromText(material==null?"—":$"{material.Index} // {material.Name} // {material.Usages} OBJECTS");
            bindings["disabled:native_material_row"+i]=RmlUiBindingValue.FromBoolean(material==null);
            bindings["class:native_material_row"+i+":selected"]=RmlUiBindingValue.FromBoolean(material?.Index==r.Selected.MaterialIndex);
            bindings["native_asset_row"+i]=RmlUiBindingValue.FromText(asset==null?"—":$"{asset.Kind} // {asset.Name} // {asset.Usages} REFERENCES");
            bindings["disabled:native_asset_row"+i]=RmlUiBindingValue.FromBoolean(asset==null);
            bindings["class:native_asset_row"+i+":selected"]=RmlUiBindingValue.FromBoolean(asset?.Path==r.Selected.AssetPath);
            var field=i<properties.Count?properties[i]:null;bool cycle=field?.Field.Kind is NativeStudioPropertyKind.Boolean or NativeStudioPropertyKind.Choice;
            bindings["visible:native_material_property_row"+i]=RmlUiBindingValue.FromBoolean(field!=null);
            bindings["native_material_label"+i]=RmlUiBindingValue.FromText(field?.Field.Label??"");
            bindings["visible:native_material_value"+i]=RmlUiBindingValue.FromBoolean(field!=null&&!cycle);
            bindings["visible:native_material_cycle"+i]=RmlUiBindingValue.FromBoolean(field!=null&&cycle);
            bindings["native_material_cycle"+i]=RmlUiBindingValue.FromText(field?.Value??"");
            if(field!=null&&!cycle&&r.DraftRevision!=_resourceDraftPresented)_host.SetField(_page,"native_material_value"+i,field.Value);
        }
        var selectedAsset=r.Snapshot.Assets.FirstOrDefault(a=>a.Path==r.Selected.AssetPath);
        bindings["native_asset_detail"]=RmlUiBindingValue.FromText(selectedAsset==null?"SELECT A DECLARED ASSET":selectedAsset.Path+"\nSOURCE: "+selectedAsset.SourcePath+"\n"+selectedAsset.Usages+" REFERENCES // FILES RETAINED BY UNDO/RECOVERY");
        if(_resourceAsset!=r.Selected.AssetPath){_resourceAsset=r.Selected.AssetPath;_host.SetField(_page,"native_asset_name",selectedAsset?.Name??"");}
        _resourcePresented=r.Revision;_resourceDraftPresented=r.DraftRevision;
    }
}
#endif
