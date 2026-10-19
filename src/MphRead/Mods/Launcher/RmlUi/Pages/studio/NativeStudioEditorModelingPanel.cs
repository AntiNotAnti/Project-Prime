#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Studio;

internal sealed partial class NativeStudioEditorPage
{
    private NativeStudioModelingController? _modelControls;
    private INativeStudioEditorModelingViewport? ModelViewport=>_session as INativeStudioEditorModelingViewport;
    private NativeStudioModelingController ModelControls=>_modelControls??=new(_session);
    private long _modelPresented=-1,_modelDraftPresented=-1;
    private bool _modelDragging;
    private NativeStudioModelingTicket _modelFrameTicket;
    private NativeStudioViewportBasis? ModelBasis=>ModelViewport?.CaptureModelingBasis(_frameWidth,_frameHeight,_clientWidth,_clientHeight);
    private void RefreshModeling(){if(_modelPresented>=0&&_modelDraftPresented==ModelControls.DraftRevision)SaveModelingDrafts();ModelControls.Refresh();}
    private bool HandleModelingIntent(int action)
    {
        var controls=ModelControls;SaveModelingDrafts();
        if(action is >=139 and <=142)controls.Configure(mode:(NativeStudioElementMode)(action-139));
        else if(action is >=143 and <=145)controls.Configure(tool:(NativeStudioTransformTool)(action-143));
        else if(action is >=146 and <=152)controls.Configure(axes:(NativeStudioAxes)(action-146));
        else if(action is >=165 and <=172)controls.SelectComponent(action-165);
        else switch(action)
        {
            case 153:controls.Configure(local:!controls.Snapshot.Local);break;
            case 154:case 156:controls.ApplyConfiguration();break;
            case 155:controls.Configure(pivot:(NativeStudioPivot)(((int)controls.Snapshot.Pivot+1)%5));break;
            case 157:controls.Execute(NativeStudioModelingOperation.SelectAll);break;
            case 158:controls.Execute(NativeStudioModelingOperation.Clear);break;
            case 159:controls.Execute(NativeStudioModelingOperation.Linked);break;
            case 160:controls.Execute(NativeStudioModelingOperation.Grow);break;
            case 161:controls.Execute(NativeStudioModelingOperation.Shrink);break;
            case 162:controls.Execute(NativeStudioModelingOperation.Boundary);break;
            case 163:controls.Previous();break;case 164:controls.Next();break;
            case 173:controls.Execute(NativeStudioModelingOperation.ConvertMesh);break;
            case 174:controls.ApplyTransform();break;
            case 175:controls.ApplyUv();break;
            case 176:controls.Execute(NativeStudioModelingOperation.FitUv);break;
            case 177:controls.MatchDensity();break;
            case 178:controls.Execute(NativeStudioModelingOperation.CopyUv);break;
            case 179:controls.Execute(NativeStudioModelingOperation.PasteUv);break;
            case 180:controls.Execute(NativeStudioModelingOperation.RotateUvPositive);break;
            case 181:controls.Execute(NativeStudioModelingOperation.RotateUvNegative);break;
            case 182:controls.Execute(NativeStudioModelingOperation.FlipU);break;
            case 183:controls.Execute(NativeStudioModelingOperation.FlipV);break;
            case 184:controls.Execute(NativeStudioModelingOperation.ExplicitUv);break;
            case 185:controls.Execute(NativeStudioModelingOperation.ProjectedUv);break;
            case 186:controls.Execute(NativeStudioModelingOperation.ToggleChecker);break;
            case 187:controls.Configure(selection:(NativeStudioSelectionMode)(((int)controls.Snapshot.SelectionMode+1)%3));break;
            case 188:_controller.ShowStatus("MODELING GESTURE CANCELED // AUTHORED DOCUMENT RETAINED");return true;
            case 189:controls.ResetUvDraft();break;
            default:return false;
        }
        _controller.ShowStatus(controls.Status);return true;
    }
    private void SaveModelingDrafts()
    {
        var c=ModelControls;
        string grid=ModelField("grid"),snapAngle=ModelField("angle_snap"),snapScale=ModelField("scale_snap"),cx=ModelField("cursor_x"),cy=ModelField("cursor_y"),cz=ModelField("cursor_z");
        if(grid!=c.GridSnapDraft||snapAngle!=c.AngleSnapDraft||snapScale!=c.ScaleSnapDraft||cx!=c.CursorXDraft||cy!=c.CursorYDraft||cz!=c.CursorZDraft)
            c.SetConfigurationDraft(grid,snapAngle,snapScale,cx,cy,cz);
        string x=ModelField("move_x"),y=ModelField("move_y"),z=ModelField("move_z"),angle=ModelField("angle"),scale=ModelField("scale");
        if(x!=c.TransformXDraft||y!=c.TransformYDraft||z!=c.TransformZDraft||angle!=c.TransformAngleDraft||scale!=c.TransformScaleDraft)c.SetTransformDraft(x,y,z,angle,scale);
        string u=ModelField("uv_u"),v=ModelField("uv_v"),ou=ModelField("uv_offset_u"),ov=ModelField("uv_offset_v"),density=ModelField("uv_density"),uvAngle=ModelField("uv_angle");
        if(u!=c.UvScaleUDraft||v!=c.UvScaleVDraft||ou!=c.UvOffsetUDraft||ov!=c.UvOffsetVDraft||density!=c.UvDensityDraft||uvAngle!=c.UvAngleDraft)c.SetUvDraft(u,v,ou,ov,density,uvAngle);
    }
    private string ModelField(string name)=>_host.ReadField(_page,"native_model_"+name,65);
    private void BindModeling(Dictionary<string,RmlUiBindingValue> bindings)
    {
        var c=ModelControls;var s=c.Snapshot;
        bindings["visible:native_model_panel"]=RmlUiBindingValue.FromBoolean(s.Alive);
        bindings["native_model_state"]=RmlUiBindingValue.FromText(s.Mode+" // "+s.Tool+" // "+s.Axes+" // "+s.Selection);
        bindings["native_model_local"]=RmlUiBindingValue.FromText(s.Local?"AXES: LOCAL":"AXES: WORLD");
        bindings["native_model_pivot"]=RmlUiBindingValue.FromText("PIVOT: "+s.Pivot);
        bindings["native_model_selection_mode"]=RmlUiBindingValue.FromText("SELECTION: "+s.SelectionMode);
        bindings["native_model_checker"]=RmlUiBindingValue.FromText(s.Checker?"UV CHECKER: ON":"UV CHECKER: OFF");
        bindings["native_model_page"]=RmlUiBindingValue.FromText($"COMPONENTS {c.Page+1} / {c.Pages}");
        bindings["disabled:native_model_previous"]=RmlUiBindingValue.FromBoolean(!c.HasPrevious);
        bindings["disabled:native_model_next"]=RmlUiBindingValue.FromBoolean(!c.HasNext);
        for(int i=0;i<8;i++)
        {
            var row=i<c.Rows.Count?c.Rows[i]:null;
            bindings["native_model_row"+i]=RmlUiBindingValue.FromText(row?.Label??"—");
            bindings["disabled:native_model_row"+i]=RmlUiBindingValue.FromBoolean(row==null);
            bindings["class:native_model_row"+i+":selected"]=RmlUiBindingValue.FromBoolean(row?.Selected==true);
        }
        if(_modelDraftPresented!=c.DraftRevision)
        {
            var values=new Dictionary<string,string>{{"grid",c.GridSnapDraft},{"angle_snap",c.AngleSnapDraft},{"scale_snap",c.ScaleSnapDraft},
                {"cursor_x",c.CursorXDraft},{"cursor_y",c.CursorYDraft},{"cursor_z",c.CursorZDraft},{"move_x",c.TransformXDraft},{"move_y",c.TransformYDraft},{"move_z",c.TransformZDraft},
                {"angle",c.TransformAngleDraft},{"scale",c.TransformScaleDraft},{"uv_u",c.UvScaleUDraft},{"uv_v",c.UvScaleVDraft},{"uv_offset_u",c.UvOffsetUDraft},
                {"uv_offset_v",c.UvOffsetVDraft},{"uv_angle",c.UvAngleDraft},{"uv_density",c.UvDensityDraft}};
            foreach(var pair in values)_host.SetField(_page,"native_model_"+pair.Key,pair.Value);
            _modelDraftPresented=c.DraftRevision;
        }
        _modelPresented=c.Revision;
    }
    private void ModelDrawComplete()
    {
        _modelFrameTicket=ModelControls.Snapshot.Ticket;
        ModelViewport?.ValidateModelingInput(ModelBasis);
    }
    private bool ModelPointerDown(double x,double y)
    {
        if(ModelViewport is not {} viewport)return false;
        if(ModelBasis is not {} basis)return true;
        if(viewport.BeginModelingPointer(_modelFrameTicket,basis,x,y)){_modelDragging=true;return true;}
        if(ModelControls.Snapshot.Mode==NativeStudioElementMode.Object)return false;
        if(viewport.PickModelingPointer(_modelFrameTicket,basis,x,y)){_controller.AcceptSelection();_details.Reset();}
        return true;
    }
    private void ModelPointerUp(double x,double y)
    {
        if(!_modelDragging)return;
        if(ModelViewport is {} viewport&&ModelBasis is {} basis)
        {var result=viewport.EndModelingPointer(basis,x,y);_controller.ShowStatus(result.Message);if(result.Success){_controller.AcceptSelection();_details.Reset();}}
        _modelDragging=false;
    }
}
#endif
