using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace MphRead.Mods.Launcher;

public enum NativeStudioResourceView { Objects, Materials, Assets }
public enum NativeStudioMaterialChannel { Texture, Albedo, Normal, SpecularRoughness, Emissive }
public enum NativeStudioResourceOperation { AddMaterial, DuplicateMaterial, AssignSelection, SelectUsages, RenameAsset,
    BindAssetChannel, ClearChannel, RefreshCache, FindAssetUsages, ReplaceMaterialUsages }
public sealed record NativeStudioMaterialRow(int Index, Guid Id, string Name, string Fingerprint, int Usages, string Sources);
public sealed record NativeStudioAssetRow(string Path, string Kind, string Name, string SourcePath, string Fingerprint, int Usages);
public sealed record NativeStudioResourcesSnapshot(long Revision, NativeStudioEditTicket Owner, bool Alive,
    IReadOnlyList<NativeStudioMaterialRow> Materials, IReadOnlyList<NativeStudioAssetRow> Assets, int ChangedSources, string SourceNotices);
public readonly record struct NativeStudioResourceTicket(NativeStudioEditTicket Owner, int MaterialIndex, Guid MaterialId,
    string MaterialFingerprint, string AssetPath, string AssetFingerprint, string SourceNotices = "");
public sealed record NativeStudioMaterialDraft(NativeStudioResourceTicket Ticket, IReadOnlyList<NativeStudioPropertyField> Fields);
public sealed record NativeStudioResourceValues(string Text = "", NativeStudioMaterialChannel Channel = NativeStudioMaterialChannel.Texture,
    NativeStudioResourceTicket? Replacement = null);
public sealed record NativeStudioResourceDelete(NativeStudioResourceTicket Ticket, Guid Confirmation, bool Asset, string Label);
public interface INativeStudioEditorResources
{
    NativeStudioResourcesSnapshot CaptureResources();
    NativeStudioMaterialDraft ReadMaterial(NativeStudioResourceTicket ticket);
    NativeStudioResult ApplyMaterial(NativeStudioResourceTicket ticket, IReadOnlyDictionary<string,string> patch);
    NativeStudioResult ExecuteResource(NativeStudioResourceTicket ticket, NativeStudioResourceOperation operation, NativeStudioResourceValues values);
    NativeStudioResourceDelete? PrepareResourceDelete(NativeStudioResourceTicket ticket, bool asset);
    NativeStudioResult ConfirmResourceDelete(NativeStudioResourceDelete confirmation);
    void CancelResourceDelete();
}

/// <summary>Resource rows and eight typed draft slots stay pinned to the presented document and resource identities.</summary>
public sealed class NativeStudioResourcesController
{
    public const int PageSize = 8;
    private readonly INativeStudioEditorResources _owner;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private NativeStudioResourcesSnapshot _snapshot;
    private NativeStudioMaterialDraft? _draft;
    private NativeStudioResourceTicket _ticket;
    private NativeStudioResourceTicket? _replacement;
    private readonly Dictionary<string,string> _values = new(StringComparer.Ordinal);
    private int _materialPage, _assetPage, _propertyPage;
    public NativeStudioResourceView View { get; private set; }
    public NativeStudioMaterialChannel Channel { get; private set; }
    public string Status { get; private set; } = "SELECT A MATERIAL OR ASSET";
    public NativeStudioResourceDelete? Delete { get; private set; }
    public long Revision { get; private set; }
    public long DraftRevision { get; private set; }
    public int MaterialPage => _materialPage;
    public int AssetPage => _assetPage;
    public int PropertyPage => _propertyPage;
    public int MaterialPages => Math.Max(1,(_snapshot.Materials.Count+7)/8);
    public int AssetPages => Math.Max(1,(_snapshot.Assets.Count+7)/8);
    public int PropertyPages => Math.Max(1,((_draft?.Fields.Count??0)+7)/8);
    public NativeStudioResourceTicket Selected => _ticket;
    public NativeStudioResourcesSnapshot Snapshot => _snapshot;
    public IReadOnlyList<NativeStudioMaterialRow> Materials => Array.AsReadOnly(_snapshot.Materials.Skip(_materialPage*8).Take(8).ToArray());
    public IReadOnlyList<NativeStudioAssetRow> Assets => Array.AsReadOnly(_snapshot.Assets.Skip(_assetPage*8).Take(8).ToArray());
    public sealed record Property(NativeStudioPropertyField Field,string Value);
    public IReadOnlyList<Property> Properties => Array.AsReadOnly((_draft?.Fields??Array.Empty<NativeStudioPropertyField>()).Skip(_propertyPage*8).Take(8)
        .Select(f=>new Property(f,_values.GetValueOrDefault(f.Id,f.Value))).ToArray());
    public NativeStudioResourcesController(INativeStudioEditorResources owner)
    { _owner=owner;_snapshot=owner.CaptureResources();_ticket=Empty(_snapshot.Owner); }
    private void Check(){if(Environment.CurrentManagedThreadId!=_thread)throw new InvalidOperationException("Resource controls belong to the editor owner thread.");}
    private NativeStudioResourceTicket Empty(NativeStudioEditTicket ticket)=>new(ticket,-1,Guid.Empty,"","","",_snapshot.SourceNotices);
    public void Refresh()
    {
        Check();var next=_owner.CaptureResources();if(next.Revision==_snapshot.Revision&&next.Owner==_snapshot.Owner&&next.Alive==_snapshot.Alive&&next.SourceNotices==_snapshot.SourceNotices)return;
        _snapshot=next;_materialPage=Math.Min(_materialPage,MaterialPages-1);_assetPage=Math.Min(_assetPage,AssetPages-1);Revision++;
        // Keep the frozen property ticket/draft, so new owner work cannot retarget Apply.
    }
    public void Show(NativeStudioResourceView view){Check();if(!Enum.IsDefined(view))return;CancelDelete();View=view;Refresh();Revision++;}
    public void SelectMaterial(int slot)
    {
        Check();var rows=Materials;if(slot<0||slot>=rows.Count)return;CancelDelete();var row=rows[slot];
        if(_ticket.Owner!=_snapshot.Owner)_ticket=Empty(_snapshot.Owner);
        _ticket=new(_snapshot.Owner,row.Index,row.Id,row.Fingerprint,_ticket.AssetPath,_ticket.AssetFingerprint,_snapshot.SourceNotices);LoadDraft();
    }
    public void SelectAsset(int slot)
    {
        Check();var rows=Assets;if(slot<0||slot>=rows.Count)return;CancelDelete();var row=rows[slot];
        // A new asset row must not refresh a stale selected material identity implicitly.
        if(_ticket.Owner!=_snapshot.Owner)_ticket=Empty(_snapshot.Owner);
        _ticket=_ticket with {AssetPath=row.Path,AssetFingerprint=row.Fingerprint,SourceNotices=_snapshot.SourceNotices};Revision++;
    }
    public void MaterialPageBy(int delta){Check();CancelDelete();int next=_materialPage+delta;if(delta is -1 or 1&&next>=0&&next<MaterialPages){_materialPage=next;Revision++;}}
    public void AssetPageBy(int delta){Check();CancelDelete();int next=_assetPage+delta;if(delta is -1 or 1&&next>=0&&next<AssetPages){_assetPage=next;Revision++;}}
    public void PropertyPageBy(int delta){Check();CancelDelete();int next=_propertyPage+delta;if(delta is -1 or 1&&next>=0&&next<PropertyPages){_propertyPage=next;DraftRevision++;Revision++;}}
    private void LoadDraft(){_draft=_owner.ReadMaterial(_ticket);_values.Clear();foreach(var f in _draft.Fields)_values[f.Id]=f.Value;_propertyPage=0;
        if(_draft.Fields.Count==0)Status="RESOURCE CHANGED // SELECT IT AGAIN AFTER REFRESH";DraftRevision++;Revision++;}
    public void ResetDraft()
    {
        Check();Refresh();
        if(_ticket.MaterialIndex>=0)
        {
            var row=_snapshot.Materials.FirstOrDefault(m=>m.Index==_ticket.MaterialIndex&&m.Id==_ticket.MaterialId);
            if(row!=null)
            {
                var assets=_snapshot.Assets.Where(a=>a.Path==_ticket.AssetPath).Take(2).ToArray();
                var asset=assets.Length==1?assets[0]:null;
                _ticket=_ticket with {Owner=_snapshot.Owner,MaterialFingerprint=row.Fingerprint,SourceNotices=_snapshot.SourceNotices,
                    AssetPath=asset?.Path??"",AssetFingerprint=asset?.Fingerprint??""};
                LoadDraft();return;
            }
        }
        ResetSelection();
    }
    public void SetDraft(int slot,string value){Check();var rows=Properties;if(slot<0||slot>=rows.Count||rows[slot].Field.Kind is NativeStudioPropertyKind.Boolean or NativeStudioPropertyKind.Choice)return;var f=rows[slot].Field;if(value.Length<=4096&&_values.GetValueOrDefault(f.Id)!=value){_values[f.Id]=value;DraftRevision++;Revision++;}}
    public void CycleProperty(int slot){Check();var rows=Properties;if(slot<0||slot>=rows.Count)return;var row=rows[slot];IReadOnlyList<string>? options=row.Field.Kind==NativeStudioPropertyKind.Boolean?new[]{"false","true"}:row.Field.Choices;if(options==null||options.Count==0)return;int at=options.ToList().FindIndex(v=>v==row.Value);_values[row.Field.Id]=options[(at+1)%options.Count];CancelDelete();DraftRevision++;Revision++;}
    public bool Apply()
    {
        Check();CancelDelete();if(_draft==null)return false;var patch=_draft.Fields.Where(f=>_values[f.Id]!=f.Value).ToDictionary(f=>f.Id,f=>_values[f.Id],StringComparer.Ordinal);
        bool success=Result(_owner.ApplyMaterial(_draft.Ticket,new ReadOnlyDictionary<string,string>(patch)));if(success)ResetDraft();return success;
    }
    public bool Execute(NativeStudioResourceOperation operation,string text="")
    {
        Check();CancelDelete();var ticket=_ticket;
        // Add/cache refresh have no selected resource; still freeze the current document ticket.
        if(operation is NativeStudioResourceOperation.AddMaterial or NativeStudioResourceOperation.RefreshCache)ticket=Empty(_snapshot.Owner);
        bool success=Result(_owner.ExecuteResource(ticket,operation,new(text,Channel,_replacement)));
        if(success){Refresh();if(operation is NativeStudioResourceOperation.AddMaterial or NativeStudioResourceOperation.DuplicateMaterial){var row=_snapshot.Materials.LastOrDefault();if(row!=null){_materialPage=row.Index/8;SelectMaterial(row.Index%8);}}else ResetDraft();}return success;
    }
    public void UseReplacement(){Check();CancelDelete();_replacement=_ticket;Status="REPLACEMENT PINNED // SELECT A SOURCE MATERIAL, THEN REPLACE ITS USAGES";Revision++;}
    public void CycleChannel(){Check();CancelDelete();Channel=(NativeStudioMaterialChannel)(((int)Channel+1)%5);Revision++;}
    public void RequestDelete(bool asset){Check();CancelDelete();Delete=_owner.PrepareResourceDelete(_ticket,asset);Status=Delete==null?"SELECT AN UNUSED RESOURCE FIRST":"CONFIRM REMOVING "+Delete.Label;Revision++;}
    public bool ConfirmDelete(){Check();var pending=Delete;Delete=null;Revision++;if(pending==null)return false;bool success=Result(_owner.ConfirmResourceDelete(pending));if(success){Refresh();ResetSelection();}return success;}
    public void CancelDelete(){Check();Delete=null;_owner.CancelResourceDelete();Revision++;}
    public void ResetSelection(){Check();CancelDelete();_ticket=Empty(_snapshot.Owner);_draft=null;_values.Clear();_replacement=null;_propertyPage=0;DraftRevision++;Revision++;}
    private bool Result(NativeStudioResult result){Status=result.Message;Revision++;return result.Success;}
}
