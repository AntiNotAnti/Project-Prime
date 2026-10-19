using System;
using System.Collections.Generic;
using System.Linq;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Launcher.Gui;

internal sealed partial class MapStudioScreen
{
    internal sealed partial class NativeStudioEditorSession : INativeStudioEditorResources
    {
        private NativeStudioResourcesSnapshot? _resources;
        private NativeStudioResourceDelete? _resourceDelete;
        private string ResourceNotices=>MapStudioResourceCommands.Fingerprint(new {Revision=_owner._sourceChangeRevision,Paths=_owner._changedSources.Order(StringComparer.Ordinal).ToArray()});
        public NativeStudioResourcesSnapshot CaptureResources()
        {
            CheckThread();string notices=ResourceNotices;
            if(_resources is {} cached&&cached.Revision==_revision&&cached.Owner==Ticket&&cached.Alive==Alive&&cached.SourceNotices==notices)return cached;
            var definition=_document.Project.Definition;
            return _resources=new(_revision,Ticket,Alive,
                Array.AsReadOnly(definition.Materials.Select((m,i)=>new NativeStudioMaterialRow(i,m.Id,m.Name,
                    MapStudioResourceCommands.Fingerprint(m),MapMaterialEditing.Usages(definition,i).Length,
                    string.Join(" // ",MapStudioResourceCommands.MaterialPaths(m).Where(p=>!string.IsNullOrWhiteSpace(p))))).ToArray()),
                Array.AsReadOnly(definition.Assets.Select(a=>new NativeStudioAssetRow(a.Path,a.Kind,a.Name??System.IO.Path.GetFileName(a.Path),a.SourcePath??"",
                    MapStudioResourceCommands.Fingerprint(a),MapStudioResourceCommands.AssetUses(definition,a.Path))).ToArray()),_owner._changedSources.Count,notices);
        }
        private bool ResourceCurrent(NativeStudioResourceTicket ticket,bool material=false,bool asset=false)
        {
            if(!Current(ticket.Owner)||ticket.SourceNotices!=ResourceNotices)return false;
            if(material)
            {
                if(ticket.MaterialIndex<0||ticket.MaterialIndex>=_document.Project.Definition.Materials.Count)return false;
                var row=_document.Project.Definition.Materials[ticket.MaterialIndex];
                if(row.Id!=ticket.MaterialId||MapStudioResourceCommands.Fingerprint(row)!=ticket.MaterialFingerprint)return false;
            }
            if(asset)
            {
                var rows=_document.Project.Definition.Assets.Where(a=>a.Path==ticket.AssetPath).ToArray();
                if(rows.Length!=1||MapStudioResourceCommands.Fingerprint(rows[0])!=ticket.AssetFingerprint)return false;
            }
            return true;
        }
        public NativeStudioMaterialDraft ReadMaterial(NativeStudioResourceTicket ticket)
        {
            CheckThread();if(!ResourceCurrent(ticket,material:true))return new(ticket,Array.Empty<NativeStudioPropertyField>());
            return new(ticket,Array.AsReadOnly(MapMaterialPropertySchema.Read(_document.Project.Definition,ticket.MaterialIndex)
                .Select(f=>new NativeStudioPropertyField(f.Id,f.Label,(NativeStudioPropertyKind)(int)f.Kind,f.Value,f.MaxLength,f.Min,f.Max,f.Choices)).ToArray()));
        }
        public NativeStudioResult ApplyMaterial(NativeStudioResourceTicket ticket,IReadOnlyDictionary<string,string> patch)
        {
            CheckThread();_resourceDelete=null;if(!ResourceCurrent(ticket,material:true))return Stale();
            try{MapMaterialPropertySchema.Apply(_document,ticket.MaterialIndex,patch);ResourceChanged();return new(true,"MATERIAL APPLIED // EXISTING HISTORY AND VIEWPORT RETAINED");}
            catch(Exception error) when(ResourceRecoverable(error)){return ResourceError(error);}
        }
        public NativeStudioResult ExecuteResource(NativeStudioResourceTicket ticket,NativeStudioResourceOperation operation,NativeStudioResourceValues values)
        {
            CheckThread();_resourceDelete=null;
            bool needsMaterial=operation is NativeStudioResourceOperation.DuplicateMaterial or NativeStudioResourceOperation.AssignSelection
                or NativeStudioResourceOperation.SelectUsages or NativeStudioResourceOperation.BindAssetChannel or NativeStudioResourceOperation.ClearChannel
                or NativeStudioResourceOperation.ReplaceMaterialUsages;
            bool needsAsset=operation is NativeStudioResourceOperation.RenameAsset or NativeStudioResourceOperation.BindAssetChannel or NativeStudioResourceOperation.FindAssetUsages;
            if(!Enum.IsDefined(operation)||!ResourceCurrent(ticket,needsMaterial,needsAsset))return Stale();
            try
            {
                switch(operation)
                {
                    case NativeStudioResourceOperation.AddMaterial:MapStudioResourceCommands.AddMaterial(_document);break;
                    case NativeStudioResourceOperation.DuplicateMaterial:MapStudioResourceCommands.AddMaterial(_document,ticket.MaterialIndex);break;
                    case NativeStudioResourceOperation.AssignSelection:
                        if(_viewport.ElementMode=="Face")
                        {
                            if(ModelMesh is not {Locked:false} faceMesh||_viewport.SubSelection.ObjectId!=faceMesh.Id||_viewport.SubSelection.Faces.Count==0)
                                return new(false,"SELECT FACES ON THE CURRENT UNLOCKED MESH FIRST");
                            MapStudioModelingCommands.AssignFaces(_document,faceMesh.Id,_viewport.SubSelection.Faces,ticket.MaterialIndex);
                        }
                        else if(!MapStudioResourceCommands.AssignSelection(_document,ticket.MaterialIndex))return new(false,"SELECT UNLOCKED AUTHORED GEOMETRY TO ASSIGN THIS MATERIAL");break;
                    case NativeStudioResourceOperation.SelectUsages:MapStudioResourceCommands.SelectMaterialUsages(_document,ticket.MaterialIndex);_viewport.FrameSelection();break;
                    case NativeStudioResourceOperation.RenameAsset:MapStudioResourceCommands.RenameAsset(_document,ticket.AssetPath,values.Text);break;
                    case NativeStudioResourceOperation.BindAssetChannel:MapStudioResourceCommands.BindChannel(_document,ticket.MaterialIndex,(MapStudioMaterialChannel)(int)values.Channel,ticket.AssetPath);break;
                    case NativeStudioResourceOperation.ClearChannel:MapStudioResourceCommands.BindChannel(_document,ticket.MaterialIndex,(MapStudioMaterialChannel)(int)values.Channel,null);break;
                    case NativeStudioResourceOperation.RefreshCache:break;
                    case NativeStudioResourceOperation.FindAssetUsages:MapStudioResourceCommands.SelectAssetUsages(_document,ticket.AssetPath);_viewport.FrameSelection();break;
                    case NativeStudioResourceOperation.ReplaceMaterialUsages:
                        if(values.Replacement is not {} replacement||!ResourceCurrent(replacement,material:true))return Stale();
                        MapStudioResourceCommands.ReplaceMaterialUsages(_document,ticket.MaterialIndex,replacement.MaterialIndex);break;
                }
                ResourceChanged();return new(true,"RESOURCE COMMAND COMPLETE // SAME DOCUMENT, HISTORY AND RENDER OWNER");
            }
            catch(Exception error) when(ResourceRecoverable(error)){return ResourceError(error);}
        }
        public NativeStudioResourceDelete? PrepareResourceDelete(NativeStudioResourceTicket ticket,bool asset)
        {
            CheckThread();_resourceDelete=null;if(!ResourceCurrent(ticket,material:!asset,asset:asset))return null;
            if(asset&&MapStudioResourceCommands.AssetUses(_document.Project.Definition,ticket.AssetPath)!=0
                ||!asset&&!MapStudioResourceCommands.CanDeleteMaterial(_document.Project.Definition,ticket.MaterialIndex))return null;
            return _resourceDelete=new(ticket,Guid.NewGuid(),asset,asset?ticket.AssetPath:_document.Project.Definition.Materials[ticket.MaterialIndex].Name);
        }
        public NativeStudioResult ConfirmResourceDelete(NativeStudioResourceDelete confirmation)
        {
            CheckThread();var pending=_resourceDelete;_resourceDelete=null;
            if(pending==null||pending!=confirmation||!ResourceCurrent(confirmation.Ticket,material:!confirmation.Asset,asset:confirmation.Asset))return Stale();
            try{if(confirmation.Asset)MapStudioResourceCommands.RemoveUnusedAsset(_document,confirmation.Ticket.AssetPath);else MapStudioResourceCommands.DeleteMaterial(_document,confirmation.Ticket.MaterialIndex);
                ResourceChanged();return new(true,"UNUSED REFERENCE REMOVED // UNDO RETAINS ASSETS AND HISTORY");}
            catch(Exception error) when(ResourceRecoverable(error)){return ResourceError(error);}
        }
        public void CancelResourceDelete(){CheckThread();if(Alive)_resourceDelete=null;}
        private void ResourceChanged()
        {
            _resources=null;_snapshot=null;
            try{_owner.ClearEnhancedPreviews();_viewport.RefreshMaterialPreview();}
            catch(Exception error) when(ResourceRecoverable(error))
            {
                // Presentation failure cannot turn a committed history command
                // into a reported failed/partly-applied authoring transaction.
                _renderUnavailable=true;_snapshot=null;
            }
            // Existing Changed/RefreshSourceWatch subscribers own authoring/source
            // invalidation. No second watcher, asset writer or renderer is made.
        }
        private static bool ResourceRecoverable(Exception error)=>error is not OutOfMemoryException and not StackOverflowException;
        private static NativeStudioResult ResourceError(Exception error)=>new(false,error is FormatException or InvalidOperationException?error.Message:"RESOURCE COMMAND FAILED // CURRENT DOCUMENT RETAINED");
    }
}
