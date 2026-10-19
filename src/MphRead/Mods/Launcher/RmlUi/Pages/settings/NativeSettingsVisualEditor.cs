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
    internal bool Closed=>_document==default||!_host.IsAlive(_document);
    internal bool OwnsForeground=>!Closed&&_pages.Top==_document&&_host.CurrentInputDocument==_document;
    internal NativeSettingsVisualEditor(RmlUiHost host,RmlUiPageManager pages,NativeFullSettingsController controller,int kind)
    {
        _host=host;_pages=pages;_controller=controller;_kind=kind;
        string group=kind==0?"Touch layout":kind==1?"Stylus guide":"Outline colors";
        _rows=controller.Rows.Where(r=>r.Group==group&&(kind!=1||r.Label=="Guide position / width")).ToArray();
        _before=_rows.ToDictionary(r=>r,r=>r.Value);
        if(_rows.Length==0)throw new InvalidOperationException("Visual settings rows are unavailable.");
        _document=pages.OpenModal(new("native-settings-visual","pages/settings/native-visual-editor.rml","settings_visual_canvas"));
        Present();
    }
    private static string N(float n)=>n.ToString("0.####",CultureInfo.InvariantCulture);
    private static float[] Tuple(string text)=>text.Split(',').Select(v=>float.Parse(v,CultureInfo.InvariantCulture)).ToArray();
    private float[] Layout(int i)
    {
        if(_rows[i].Value!="default")return Tuple(_rows[i].Value);
        var geometry=TouchSettings.DefaultGeometry(TouchSettings.Order[i].Control,1000,750);
        return new[]{geometry.X/1000,geometry.Y/750,1f};
    }
    private bool Box(out float x,out float y,out float w,out float h)=>_host.TryGetElementBounds(_document,"settings_visual_canvas",out x,out y,out w,out h)&&w>0&&h>0;
    private (float X,float Y,float W,float H) Rect(int i,float w,float h)
    {
        var n=Layout(i);
        if(_kind==1)return(n[0]*w,n[1]*h,n[2]*w,n[2]*w*.75f);
        var baseline=TouchSettings.DefaultGeometry(TouchSettings.Order[i].Control,w,h);
        float diameter=2*baseline.Radius*n[2];return(n[0]*w-diameter/2,n[1]*h-diameter/2,diameter,diameter);
    }
    internal bool PointerDown(float px,float py)
    {
        if(!OwnsForeground||_kind==2||!Box(out float x,out float y,out float w,out float h)||px<x||py<y||px>x+w||py>y+h)return false;
        px-=x;py-=y;
        for(int i=_rows.Length-1;i>=0;i--)
        {
            var r=Rect(i,w,h);if(px<r.X||py<r.Y||px>r.X+r.W||py>r.Y+r.H)continue;
            _selected=i;var n=Layout(i);_grabX=px/w-n[0];_grabY=py/h-n[1];_drag=true;Present();return true;
        }
        return false;
    }
    internal bool PointerMove(float px,float py)
    {
        if(!_drag||!OwnsForeground||!Box(out float x,out float y,out float w,out float h))return false;
        var n=Layout(_selected);float maxX=_kind==1?Math.Max(0,1-n[2]):1;
        float maxY=_kind==1?Math.Max(0,1-n[2]*w*.75f/h):1;
        n[0]=Math.Clamp((px-x)/w-_grabX,0,Math.Min(_kind==1?.9f:1,maxX));
        n[1]=Math.Clamp((py-y)/h-_grabY,0,Math.Min(_kind==1?.9f:1,maxY));
        Stage(n);Present();return true;
    }
    internal void ReleasePointer()=>_drag=false;
    private void Stage(float[] n)=>_controller.Stage(_rows[_selected],string.Join(",",n.Select(N)));
    private bool CaptureColor()=>_kind!=2||_controller.Stage(_rows[_selected],_host.ReadField(_document,"settings_visual_color",32));
    internal bool Handle(in RmlUiIntent intent)
    {
        if(!OwnsForeground||intent.Document!=_document||intent.Kind!=RmlUiIntentKind.SettingsAction)return false;
        int a=intent.Argument;
        if(!(a is >=32 and <64 or >=223 and <=231)||!_pages.Accept(intent))return false;
        if(a is >=32 and <64)
        {if(a-32<_rows.Length&&CaptureColor()){_selected=a-32;_host.FocusDocument(_document,"settings_visual_pick_"+_selected);}}
        else switch(a)
        {
            case 223:if(CaptureColor())Close(false);break;
            case 224:Close(true);break;
            case 225:_rows[_selected].Value=_kind==0?"default":_kind==1?"0.62,0.60,0.34":_rows[_selected].Saved;break;
            case 226:foreach(var row in _rows)row.Value=_kind==0?"default":_kind==1?"0.62,0.60,0.34":row.Saved;break;
            case 227:case 228:
                if(_kind!=2){var n=Layout(_selected);n[2]=Math.Clamp(n[2]+(a==227?-.05f:.05f),_kind==0?TouchSettings.MinIndividualScale:.1f,_kind==0?TouchSettings.MaxIndividualScale:1);Stage(n);}break;
            case 229:if(CaptureColor()){_preview=true;_previewUntil=DateTime.UtcNow.AddSeconds(60);}break;
            case 230:_preview=false;break;
            case 231:CaptureColor();break;
        }
        if(_kind==2&&(a is 225 or 226))_host.SetField(_document,"settings_visual_color",_rows[_selected].Value);
        Present();return true;
    }
    internal void Present()
    {
        if(Closed)return;
        if(!OwnsForeground)_drag=false;
        if(_preview&&DateTime.UtcNow>=_previewUntil)_preview=false;
        _host.SetText(_document,"settings_visual_title",_kind==0?"TOUCH LAYOUT":_kind==1?"STYLUS ZONE":"OUTLINE COLORS");
        _host.SetText(_document,"settings_visual_help",_kind==0?"Drag a target to place it. Select a control to resize or reset. Changes remain in the Settings draft.":_kind==1?"Drag the DS touch-screen zone; resize with Smaller/Larger. Changes remain in the Settings draft.":"Edit #RRGGBB colors. Preview is native and temporary; game colors remain unchanged until Settings Apply.");
        _host.SetBool(_document,"visible:settings_visual_color_tools",_kind==2);
        _host.SetBool(_document,"visible:settings_visual_scale_tools",_kind!=2);
        _host.SetText(_document,"settings_visual_status",_kind==2?(_preview?"Native draft preview // "+Math.Max(0,(int)Math.Ceiling((_previewUntil-DateTime.UtcNow).TotalSeconds))+" seconds":"Saved color preview // changes remain staged"):_controller.Status);
        Box(out _,out _,out float w,out float h);
        for(int i=0;i<32;i++)
        {
            bool visible=i<_rows.Length;_host.SetBool(_document,"visible:settings_visual_pick_"+i,visible);_host.SetBool(_document,"visible:settings_visual_target_"+i,visible);
            if(!visible)continue;
            _host.SetText(_document,"settings_visual_pick_"+i,_rows[i].Label.Replace(" position / size",""));
            _host.SetBool(_document,"class:settings_visual_pick_"+i+":selected",i==_selected);
            _host.SetBool(_document,"class:settings_visual_target_"+i+":selected",i==_selected);
            _host.SetText(_document,"settings_visual_target_label_"+i,_rows[i].Label.Replace(" position / size",""));
            if(_kind==2)
            {
                float tw=Math.Max(40,w/8),th=Math.Max(40,h/3);float tx=i%8*tw,ty=i/8*th;
                _host.SetText(_document,"rect:settings_visual_target_"+i,$"{N(tx)},{N(ty)},{N(tw-8)},{N(th-8)}");
                _host.SetText(_document,"color:settings_visual_target_"+i,_preview?_rows[i].Value:_rows[i].Saved);
            }
            else
            {
                var r=Rect(i,w,h);_host.SetText(_document,"rect:settings_visual_target_"+i,$"{N(r.X)},{N(r.Y)},{N(r.W)},{N(r.H)}");
            }
        }
        if(_kind==2&&_host.FocusedElement()!="settings_visual_color")_host.SetField(_document,"settings_visual_color",_rows[_selected].Value);
    }
    internal bool Back(){if(!OwnsForeground)return false;Close(true);return true;}
    private void Close(bool discard)
    {
        _drag=_preview=false;
        if(!_pages.CloseModal())throw new InvalidOperationException("Visual settings editor could not close.");
        if(discard){foreach(var pair in _before)pair.Key.Value=pair.Value;_controller.Stage(_rows[0],_rows[0].Value);}
        _document=default;
    }
    public void Dispose(){if(Closed)return;if(!OwnsForeground)throw new InvalidOperationException("Visual settings editor is covered.");Close(true);}
}
#endif
