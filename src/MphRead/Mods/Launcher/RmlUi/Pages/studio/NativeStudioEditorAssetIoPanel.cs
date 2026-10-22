#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.Linq;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Studio;

internal sealed partial class NativeStudioEditorPage
{
    private INativeStudioEditorAssetIo? AssetIoOwner=>_session as INativeStudioEditorAssetIo;
    private NativeStudioAssetIoSnapshot? _assetIoView,_assetIoShown;
    private NativeStudioResourceTicket _assetIoImportTicket,_assetIoResourceTicket;
    private long _assetIoPresented=-1;
    private Guid _assetIoOperation;
    private string _assetIoFocus="";
    private bool HandleAssetIoIntent(int action)
    {
        if(AssetIoOwner is not {} owner||ResourceControls is not {View:NativeStudioResourceView.Assets} resources)return false;
        NativeStudioResult result;
        switch(action)
        {
            case 190:result=owner.ChooseAssetSource(_assetIoImportTicket);break;
            case 191:result=owner.ReviewAssetIo(_assetIoImportTicket,new(NativeStudioAssetIoKind.ImportTexture,IoField("source"),"",IoField("name",257)));break;
            case 193:result=owner.ReviewAssetIo(_assetIoResourceTicket,new(NativeStudioAssetIoKind.ReplaceTexture,IoField("source"),"",""));break;
            case 195:result=owner.ReviewAssetIo(_assetIoResourceTicket,new(NativeStudioAssetIoKind.ExportOriginal,"",IoField("destination"),""));break;
            case 192:case 194:case 196:case 197:
                if(_assetIoShown is not {} shown||shown.Ticket.Operation!=_assetIoOperation)return false;
                if(action==197?shown.Stage!=NativeStudioAssetIoStage.NeedsOverwrite:shown.Stage!=NativeStudioAssetIoStage.Ready)return false;
                if(action==192&&shown.Kind!=NativeStudioAssetIoKind.ImportTexture||action==194&&shown.Kind!=NativeStudioAssetIoKind.ReplaceTexture
                    ||action is 196 or 197&&shown.Kind!=NativeStudioAssetIoKind.ExportOriginal)return false;
                result=owner.ApplyAssetIo(_assetIoOperation,overwrite:action==197);break;
            case 198:owner.CancelAssetIo(_assetIoOperation);result=new(true,"ASSET WORK CANCELED // CURRENT PROJECT RETAINED");break;
            case 199:
                var asset=resources.Snapshot.Assets.SingleOrDefault(a=>a.Path==_assetIoResourceTicket.AssetPath);
                if(asset==null||string.IsNullOrWhiteSpace(asset.SourcePath)){_controller.ShowStatus("SELECT A TEXTURE WITH A RECORDED SOURCE TO REVIEW RELOAD");return true;}
                result=owner.ReviewAssetIo(_assetIoResourceTicket,new(NativeStudioAssetIoKind.ReplaceTexture,asset.SourcePath,"",""));break;
            default:return false;
        }
        _controller.ShowStatus(result.Message);return true;
    }
    private string IoField(string name,int maximum=32768)=>_host.ReadField(_page,"native_asset_io_"+name,maximum);
    private void RefreshAssetIo()
    {
        var next=AssetIoOwner?.CaptureAssetIo();
        if(next?.Ticket.Operation!=_assetIoView?.Ticket.Operation||next?.Revision!=_assetIoView?.Revision)
        {
            _assetIoView=next;
            if(next!=null)
            {
                _controller.ShowStatus(next.Status);
                if(!next.Busy&&next.Source.Length>0)_host.SetField(_page,"native_asset_io_source",next.Source);
                if(next.Stage==NativeStudioAssetIoStage.NeedsOverwrite)_assetIoFocus="native_asset_io_overwrite";
                else if(next.Stage==NativeStudioAssetIoStage.Ready)_assetIoFocus=next.Kind switch{
                    NativeStudioAssetIoKind.ImportTexture=>"native_asset_io_apply_import",NativeStudioAssetIoKind.ReplaceTexture=>"native_asset_io_apply_replace",_=>"native_asset_io_export"};
            }
        }
        if(_assetIoFocus.Length>0&&_host.FocusDocument(_page,_assetIoFocus))_assetIoFocus="";
    }
    private void BindAssetIo(Dictionary<string,RmlUiBindingValue> bindings)
    {
        var owner=AssetIoOwner;var state=_assetIoView;bool assets=ResourceControls?.View==NativeStudioResourceView.Assets;
        bindings["visible:native_asset_io_panel"]=RmlUiBindingValue.FromBoolean(owner!=null&&assets);
        if(owner==null)return;
        var resources=ResourceControls!;var snapshot=resources.Snapshot;
        // Same last-presented snapshot discipline as resource and component rows.
        _assetIoImportTicket=new(snapshot.Owner,-1,Guid.Empty,"","","",snapshot.SourceNotices);_assetIoResourceTicket=resources.Selected;
        _assetIoShown=state;_assetIoOperation=state?.Ticket.Operation??Guid.Empty;_assetIoPresented=state?.Revision??0;
        bool busy=state?.Busy==true,ready=state?.Stage==NativeStudioAssetIoStage.Ready,overwrite=state?.Stage==NativeStudioAssetIoStage.NeedsOverwrite;
        bindings["disabled:native_asset_io_choose"]=RmlUiBindingValue.FromBoolean(busy||!owner.AssetPickerAvailable);
        foreach(string name in new[]{"review_import","review_replace","review_export","reload_source","source","destination","name"})bindings["disabled:native_asset_io_"+name]=RmlUiBindingValue.FromBoolean(busy);
        bindings["disabled:native_asset_io_apply_import"]=RmlUiBindingValue.FromBoolean(!ready||state?.Kind!=NativeStudioAssetIoKind.ImportTexture);
        bindings["disabled:native_asset_io_apply_replace"]=RmlUiBindingValue.FromBoolean(!ready||state?.Kind!=NativeStudioAssetIoKind.ReplaceTexture);
        bindings["disabled:native_asset_io_export"]=RmlUiBindingValue.FromBoolean(!ready||state?.Kind!=NativeStudioAssetIoKind.ExportOriginal);
        bindings["visible:native_asset_io_overwrite"]=RmlUiBindingValue.FromBoolean(overwrite&&state?.Kind==NativeStudioAssetIoKind.ExportOriginal);
        bindings["disabled:native_asset_io_cancel"]=RmlUiBindingValue.FromBoolean(state==null||state.Stage is NativeStudioAssetIoStage.Complete or NativeStudioAssetIoStage.Failed);
        bindings["native_asset_io_status"]=RmlUiBindingValue.FromText(state?.Status??"REVIEW LOCAL TEXTURE BYTES BEFORE IMPORT, REPLACEMENT OR EXPORT");
        bindings["native_asset_io_source_name"]=RmlUiBindingValue.FromText(state?.SourceName??"");
        bindings["native_asset_io_source_path"]=RmlUiBindingValue.FromText(state?.Source??"");
        bindings["native_asset_io_destination_name"]=RmlUiBindingValue.FromText(state?.DestinationName??"");
        bindings["native_asset_io_destination_path"]=RmlUiBindingValue.FromText(state?.Destination??"");
        bindings["native_asset_io_summary"]=RmlUiBindingValue.FromText(state==null?"":$"PROJECT ROOT: {state.Ticket.ProjectRoot}\nMATERIAL: {state.MaterialName}\n{state.Width}×{state.Height} // PORTABLE {state.PortableBytes} BYTES // NATIVE {state.NativeBytes} BYTES\nSOURCE SHA256 {state.SourceSha256}\nREVIEWED TARGET SHA256 {state.DestinationSha256}\nRECOVERY {state.RecoveryPath??"NONE"}");
    }
}
#endif
