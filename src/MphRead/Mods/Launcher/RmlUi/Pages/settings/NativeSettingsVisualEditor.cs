#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher.RmlUi.Host;
namespace MphRead.Mods.Launcher.RmlUi.Pages.Settings;

// A view of the existing detached setting rows, not another layout/persistence owner.
internal sealed class NativeSettingsVisualEditor : IDisposable
{
    private readonly RmlUiHost _host;
    private readonly RmlUiPageManager _pages;
    private readonly NativeFullSettingsController _controller;
    private readonly NativeSettingRow[] _rows;
    private readonly Dictionary<NativeSettingRow,string> _before;
    private RmlUiDocumentToken _document;
    private readonly int _kind; // 0 touch, 1 stylus, 2 outline colors
    private int _selected;
    private bool _drag,_preview;
    private float _grabX,_grabY;
    private DateTime _previewUntil;
    private RmlUiCanvas? _wheelCanvas, _brightnessCanvas;
    private float _hue, _saturation, _value = 1;
    private string? _colorHex;
    private string? _colorError;
    private float _drawnValue = -1, _drawnHue = -1, _drawnSaturation = -1;
    private int _colorDrag; // 1 hue/saturation disk, 2 brightness track
    private int _presentedColorSelection = -1;
    internal bool Closed=>_document==default||!_host.IsAlive(_document);
    internal bool OwnsForeground=>!Closed&&_pages.Top==_document&&_host.CurrentInputDocument==_document;
    internal NativeSettingsVisualEditor(RmlUiHost host,RmlUiPageManager pages,NativeFullSettingsController controller,int kind)
    {
        _host=host;_pages=pages;_controller=controller;_kind=kind;
        string group=kind==0?"Touch layout":kind==1?"Stylus guide":"Outline colors";
        _rows=controller.Rows.Where(r=>r.Group==group&&(kind!=1||r.Label=="Guide position / width")).ToArray();
        _before=(kind==0 ? controller.Rows.Where(r=>r.Group is "Touch layout" or "Touch buttons") : _rows).ToDictionary(r=>r,r=>r.Value);
        if(_rows.Length==0)throw new InvalidOperationException("Visual settings rows are unavailable.");
        _document=pages.OpenModal(new("native-settings-visual","pages/settings/native-visual-editor.rml",kind==2?"settings_visual_wheel":"settings_visual_canvas"));
        Present();
    }
    private static string N(float n)=>n.ToString("0.####",CultureInfo.InvariantCulture);
    private static float[] Tuple(string text)=>text.Split(',').Select(v=>float.Parse(v,CultureInfo.InvariantCulture)).ToArray();
    private float[] Layout(int i)
    {
        if(_rows[i].Value!="default")return Tuple(_rows[i].Value);
        var geometry=TouchSettings.DefaultGeometry(ActionOf(i),1000,750);
        return new[]{geometry.X/1000,geometry.Y/750,1f};
    }
    private NativeSettingRow? ExtraAction(int i) => i < TouchSettings.Order.Length ? null
        : _controller.Rows.FirstOrDefault(r=>r.Group=="Touch buttons" && r.Label=="Extra "+(i-TouchSettings.Order.Length+1)+" action");
    private TouchControl ActionOf(int i) => i < TouchSettings.Order.Length ? TouchSettings.Order[i].Control
        : Enum.TryParse<TouchControl>(ExtraAction(i)?.Value,out var action) ? action : TouchControl.Shoot;
    private bool EnabledTarget(int i) => i < TouchSettings.Order.Length || ExtraAction(i)?.Value != "none";
    private bool Box(out float x,out float y,out float w,out float h)=>_host.TryGetElementBounds(_document,"settings_visual_canvas",out x,out y,out w,out h)&&w>0&&h>0;
    private (float X,float Y,float W,float H) Rect(int i,float w,float h)
    {
        var n=Layout(i);
        if(_kind==1)return(n[0]*w,n[1]*h,n[2]*w,n[2]*w*.75f);
        var baseline=TouchSettings.DefaultGeometry(ActionOf(i),w,h);
        float diameter=2*baseline.Radius*n[2];return(n[0]*w-diameter/2,n[1]*h-diameter/2,diameter,diameter);
    }
    internal bool PointerDown(float px,float py)
    {
        if(!OwnsForeground)return false;
        if(_kind==2)
        {
            if(!float.IsFinite(px)||!float.IsFinite(py))return false;
            if(Inside("settings_visual_wheel",px,py,out float wx,out float wy,out float ww,out float wh))
            {
                float dx=(px-wx-ww/2)/(ww/2),dy=(py-wy-wh/2)/(wh/2);
                if(dx*dx+dy*dy>1||!CaptureColor())return false;
                _colorDrag=1;
            }
            else if(Inside("settings_visual_brightness",px,py,out _,out _,out _,out _)&&CaptureColor())_colorDrag=2;
            else return false;
            _host.FocusDocument(_document,_colorDrag==1?"settings_visual_wheel":"settings_visual_brightness");
            return PointerMove(px,py);
        }
        if(!Box(out float x,out float y,out float w,out float h)||px<x||py<y||px>x+w||py>y+h)return false;
        px-=x;py-=y;
        for(int i=_rows.Length-1;i>=0;i--)
        {
            var r=Rect(i,w,h);if(!EnabledTarget(i)||px<r.X||py<r.Y||px>r.X+r.W||py>r.Y+r.H)continue;
            _selected=i;var n=Layout(i);_grabX=px/w-n[0];_grabY=py/h-n[1];_drag=true;Present();return true;
        }
        return false;
    }
    internal bool PointerMove(float px,float py)
    {
        if(_kind==2)
        {
            if(_colorDrag==0||!OwnsForeground||!float.IsFinite(px)||!float.IsFinite(py))return false;
            string id=_colorDrag==1?"settings_visual_wheel":"settings_visual_brightness";
            if(!_host.TryGetElementBounds(_document,id,out float cx,out float cy,out float cw,out float ch)||cw<=0||ch<=0)return false;
            if(_colorDrag==1)(_hue,_saturation)=NativeOutlineColorWheel.Pick(px-cx,py-cy,cw,ch);
            else _value=Math.Clamp((px-cx)/cw,0,1);
            StageWheel();Present();return true;
        }
        if(!_drag||!OwnsForeground||!Box(out float x,out float y,out float w,out float h))return false;
        var n=Layout(_selected);float maxX=_kind==1?Math.Max(0,1-n[2]):1;
        float maxY=_kind==1?Math.Max(0,1-n[2]*w*.75f/h):1;
        n[0]=Math.Clamp((px-x)/w-_grabX,0,Math.Min(_kind==1?.9f:1,maxX));
        n[1]=Math.Clamp((py-y)/h-_grabY,0,Math.Min(_kind==1?.9f:1,maxY));
        Stage(n);Present();return true;
    }
    private bool Inside(string id,float px,float py,out float x,out float y,out float w,out float h)
        =>_host.TryGetElementBounds(_document,id,out x,out y,out w,out h)&&w>0&&h>0&&px>=x&&py>=y&&px<=x+w&&py<=y+h;
    internal void ReleasePointer(){_drag=false;_colorDrag=0;}
    private void Stage(float[] n)=>_controller.Stage(_rows[_selected],string.Join(",",n.Select(N)));
    private bool CaptureColor()
    {
        if(_kind!=2)return true;
        if(!_controller.Stage(_rows[_selected],_host.ReadField(_document,"settings_visual_color",32)))
        {_colorError=_controller.Status;return false;}
        _colorError=null;
        SyncColor();return true;
    }
    private void SyncColor()
    {
        if(_colorHex==_rows[_selected].Value)return;
        _colorHex=_rows[_selected].Value;
        (_hue,_saturation,_value)=NativeOutlineColorWheel.Read(_colorHex,_hue);
    }
    private void StageWheel()
    {
        _controller.Stage(_rows[_selected],NativeOutlineColorWheel.Hex(_hue,_saturation,_value));
        _colorHex=_rows[_selected].Value;
        _host.SetField(_document,"settings_visual_color",_colorHex);
    }
    internal bool Handle(in RmlUiIntent intent)
    {
        if(!OwnsForeground||intent.Document!=_document||intent.Kind!=RmlUiIntentKind.SettingsAction)return false;
        int a=intent.Argument;
        if(!(a is >=32 and <64 or >=223 and <=241)||!_pages.Accept(intent))return false;
        if(a is >=32 and <64)
        {if(a-32<_rows.Length&&CaptureColor()){_selected=a-32;_host.FocusDocument(_document,"settings_visual_pick_"+_selected);}}
        else switch(a)
        {
            case 223:if(CaptureColor())Close(false);break;
            case 224:if(!_controller.AutoApply||CaptureColor())Close(true);break;
            case 225:_controller.Stage(_rows[_selected],_kind==0?"default":_kind==1?"0.62,0.60,0.34":_controller.AutoApply?_before[_rows[_selected]]:_rows[_selected].Saved);break;
            case 226:foreach(var row in _rows)_controller.Stage(row,_kind==0?"default":_kind==1?"0.62,0.60,0.34":_controller.AutoApply?_before[row]:row.Saved);break;
            case 227:case 228:
                if(_kind!=2){var n=Layout(_selected);n[2]=Math.Clamp(n[2]+(a==227?-.05f:.05f),_kind==0?TouchSettings.MinIndividualScale:.1f,_kind==0?TouchSettings.MaxIndividualScale:1);Stage(n);}break;
            case 229:if(CaptureColor()){_preview=true;_previewUntil=DateTime.UtcNow.AddSeconds(60);}break;
            case 230:_preview=false;break;
            case 231:CaptureColor();break;
            case 232:case 233:case 234:case 235:case 240:case 241:
                if(_kind==2&&CaptureColor())
                {
                    if(a is 232 or 233){_hue+=(a==232?-1:1)/72f;_hue-=MathF.Floor(_hue);}
                    else if(a is 234 or 235)_value=Math.Clamp(_value+(a==234?-.05f:.05f),0,1);
                    else _saturation=Math.Clamp(_saturation+(a==240?-.05f:.05f),0,1);
                    StageWheel();
                }break;
            case 236:
                if(_kind==0)
                {
                    int next=Array.FindIndex(_rows,TouchSettings.Order.Length,r=>ExtraAction(Array.IndexOf(_rows,r))?.Value=="none");
                    if(next>=0){var n=Layout(_selected);var action=ActionOf(_selected);_selected=next;
                        _controller.Stage(ExtraAction(next)!,action.ToString());n[0]=Math.Clamp(n[0]+.06f,0,1);n[1]=Math.Clamp(n[1]+.06f,0,1);Stage(n);}
                }break;
            case 237:if(_kind==0&&ExtraAction(_selected) is { } remove)_controller.Stage(remove,"none");break;
            case 238:
                if(_kind==0&&ExtraAction(_selected) is { } reassign)
                    _controller.Stage(reassign,((TouchControl)(((int)ActionOf(_selected)+1)%TouchSettings.Order.Length)).ToString());break;
            case 239:
                if(_kind==0&&_selected>=TouchSettings.Order.Length)
                {var row=_controller.Rows.First(r=>r.Group=="Touch buttons"&&r.Label=="Extra "+(_selected-TouchSettings.Order.Length+1)+" enabled");
                    _controller.Stage(row,(!bool.Parse(row.Value)).ToString());}break;
        }
        if(_kind==2&&(a is 225 or 226))_host.SetField(_document,"settings_visual_color",_rows[_selected].Value);
        Present();return true;
    }
    internal void Present()
    {
        if(Closed)return;
        if(!OwnsForeground)ReleasePointer();
        if(_preview&&DateTime.UtcNow>=_previewUntil)_preview=false;
        _host.SetText(_document,"settings_visual_title",_kind==0?"TOUCH LAYOUT":_kind==1?"STYLUS ZONE":"OUTLINE COLORS");
        _host.SetText(_document,"settings_visual_help",_kind==0?"Drag a target to place it. Select a control to resize or reset. Changes remain in the Settings draft.":_kind==1?"Drag the DS touch-screen zone; resize with Smaller/Larger. Changes remain in the Settings draft.":"Select a team or player slot. Drag the wheel for hue and saturation, then adjust brightness. Hex entry gives exact RGB. Settings Apply saves your colors.");
        if(_controller.AutoApply)
        {
            _host.SetText(_document,"settings_visual_help",_kind==0?"Drag a target to place it. Select a control to resize or reset. Changes save automatically after editing.":_kind==1?"Drag the DS touch-screen zone; resize with Smaller/Larger. Changes save automatically after editing.":"Select a team or player slot. Drag the wheel, adjust brightness, or enter an exact hex color. Changes save automatically after editing.");
            _host.SetText(_document,"settings_visual_use","DONE");
            _host.SetBool(_document,"visible:settings_visual_cancel",false);
            _host.SetBool(_document,"visible:settings_visual_stage_color",false);
            _host.SetBool(_document,"visible:settings_visual_preview_color",false);
            _host.SetBool(_document,"visible:settings_visual_end_preview",false);
            if(_kind==2)_host.SetText(_document,"settings_visual_reset_all","RESTORE OPENED COLORS");
        }
        _host.SetBool(_document,"visible:settings_visual_canvas",_kind!=2);
        _host.SetBool(_document,"visible:settings_visual_color_tools",_kind==2);
        _host.SetBool(_document,"visible:settings_visual_scale_tools",_kind!=2);
        _host.SetBool(_document,"visible:settings_visual_instance_tools",_kind==0);
        _host.SetText(_document,"settings_visual_status",_kind==2?(_colorError??(_controller.AutoApply?_controller.Status:(_preview?"Palette draft preview // "+Math.Max(0,(int)Math.Ceiling((_previewUntil-DateTime.UtcNow).TotalSeconds))+" seconds":"Selected swatch shows draft // palette shows saved colors"))):_controller.Status);
        Box(out _,out _,out float w,out float h);
        for(int i=0;i<32;i++)
        {
            bool visible=i<_rows.Length;_host.SetBool(_document,"visible:settings_visual_pick_"+i,visible);_host.SetBool(_document,"visible:settings_visual_target_"+i,visible);
            if(!visible)continue;
            _host.SetText(_document,"settings_visual_pick_"+i,_rows[i].Label.Replace(" position / size",""));
            _host.SetBool(_document,"class:settings_visual_pick_"+i+":selected",i==_selected);
            _host.SetBool(_document,"class:settings_visual_target_"+i+":selected",i==_selected);
            _host.SetText(_document,"settings_visual_target_label_"+i,_kind==0&&i>=TouchSettings.Order.Length
                ? (EnabledTarget(i)?TouchSettings.ShortLabel(ActionOf(i)):"EMPTY") : _rows[i].Label.Replace(" position / size",""));
            if(_kind==2)
            {
                bool draftColor=_preview||_controller.AutoApply;
                _host.SetText(_document,"color:settings_visual_pick_"+i,draftColor?_rows[i].Value:_rows[i].Saved);
                uint rgb=uint.Parse((draftColor?_rows[i].Value:_rows[i].Saved).AsSpan(1),NumberStyles.HexNumber,CultureInfo.InvariantCulture);
                float light=((rgb>>16)*.2126f+((rgb>>8)&255)*.7152f+(rgb&255)*.0722f)/255;
                _host.SetText(_document,"ink:settings_visual_pick_"+i,light>.5f?"#061520":"#FFFFFF");
                float tw=Math.Max(40,w/8),th=Math.Max(40,h/3);float tx=i%8*tw,ty=i/8*th;
                _host.SetText(_document,"rect:settings_visual_target_"+i,$"{N(tx)},{N(ty)},{N(tw-8)},{N(th-8)}");
                _host.SetText(_document,"color:settings_visual_target_"+i,draftColor?_rows[i].Value:_rows[i].Saved);
            }
            else
            {
                var r=Rect(i,w,h);_host.SetText(_document,"rect:settings_visual_target_"+i,$"{N(r.X)},{N(r.Y)},{N(r.W)},{N(r.H)}");
            }
        }
        if(_kind==2)
        {
            SyncColor();PresentWheel();
            if(_controller.AutoApply?_presentedColorSelection!=_selected:_host.FocusedElement()!="settings_visual_color")
                _host.SetField(_document,"settings_visual_color",_rows[_selected].Value);
            _presentedColorSelection=_selected;
        }
        if(_controller.AutoApply)
        {
            bool valid=true;
            if(_kind==2)
            {
                string raw=_host.ReadField(_document,"settings_visual_color",32);
                valid=_rows[_selected].Validate(raw)==null;
                if(valid&&raw!=_rows[_selected].Value)CaptureColor();
            }
            _controller.TickAutoApply(inputActive:OwnsForeground&&!_drag&&_colorDrag==0&&valid);
        }
    }
    private void PresentWheel()
    {
        _host.SetText(_document,"settings_visual_color_selected",_rows[_selected].Label+" // "+_rows[_selected].Value);
        _host.SetText(_document,"color:settings_visual_color_swatch",_rows[_selected].Value);
        _host.SetText(_document,"settings_visual_color_components",$"Hue {MathF.Round(_hue*360)}  //  Saturation {MathF.Round(_saturation*100)}%  //  Brightness {MathF.Round(_value*100)}%");
        if(OwnsForeground&&_wheelCanvas==null)
        {
            _wheelCanvas=_host.AcquireCanvas(_document,"settings_visual_wheel_canvas");
            _wheelCanvas?.SetTextures(new RmlUiCanvasTextureTable(Array.Empty<RmlUiCanvasImage>()));
            _brightnessCanvas=_host.AcquireCanvas(_document,"settings_visual_brightness_canvas");
            _brightnessCanvas?.SetTextures(new RmlUiCanvasTextureTable(Array.Empty<RmlUiCanvasImage>()));
        }
        if(_wheelCanvas!=null&&_drawnValue!=_value&&_wheelCanvas.Submit(NativeOutlineColorWheel.Frame(_value)))_drawnValue=_value;
        if(_brightnessCanvas!=null&&(_drawnHue!=_hue||_drawnSaturation!=_saturation)
            &&_brightnessCanvas.Submit(NativeOutlineColorWheel.BrightnessFrame(_hue,_saturation)))
        {_drawnHue=_hue;_drawnSaturation=_saturation;}
        if(_host.TryGetElementBounds(_document,"settings_visual_wheel",out _,out _,out float w,out float h)&&w>0&&h>0)
        {
            float angle=_hue*2*MathF.PI,radius=Math.Min(w,h)/2*_saturation;
            _host.SetText(_document,"rect:settings_visual_wheel_cursor",$"{N(w/2+radius*MathF.Cos(angle)-4)},{N(h/2+radius*MathF.Sin(angle)-4)},8,8");
        }
        if(_host.TryGetElementBounds(_document,"settings_visual_brightness",out _,out _,out float bw,out float bh)&&bw>0&&bh>0)
            _host.SetText(_document,"rect:settings_visual_brightness_cursor",$"{N(_value*bw-2)},0,4,{N(bh)}");
    }
    internal bool Back()
    {
        if(!OwnsForeground)return false;
        if(_controller.AutoApply&&_kind==2&&!CaptureColor())
        {
            _host.SetField(_document,"settings_visual_color",_rows[_selected].Value);
            _colorError=null;
        }
        Close(true);return true;
    }
    private void Close(bool discard,bool save=true)
    {
        ReleasePointer();_preview=false;
        if(save&&_controller.AutoApply&&!_controller.FlushAutoApply())return;
        _wheelCanvas?.Dispose();_wheelCanvas=null;_brightnessCanvas?.Dispose();_brightnessCanvas=null;
        if(!_pages.CloseModal())throw new InvalidOperationException("Visual settings editor could not close.");
        if(discard&&!_controller.AutoApply){foreach(var pair in _before)pair.Key.Value=pair.Value;_controller.Stage(_rows[0],_rows[0].Value);}
        _document=default;
    }
    public void Dispose(){if(Closed)return;if(!OwnsForeground)throw new InvalidOperationException("Visual settings editor is covered.");Close(true,save:false);}
}
#endif
