"""Actual pinned CPU RmlUi compact scroll geometry; never creates a GL/window."""
import argparse
import ctypes as c
import hashlib
import json
from pathlib import Path
import shutil


def main():
    p=argparse.ArgumentParser();p.add_argument('--library',type=Path,required=True);p.add_argument('--assets',type=Path,required=True)
    p.add_argument('--expected-sha',required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args()
    a.output.mkdir(parents=True,exist_ok=False)
    sha=lambda path:hashlib.sha256(path.read_bytes()).hexdigest()
    assert sha(a.library)==a.expected_sha,'Pinned actual bridge mismatch'
    assets=a.output/'assets';shutil.copytree(a.assets,assets)
    native=c.CDLL(str(a.library.resolve()))
    def fn(name,result,types):
        f=getattr(native,name);f.restype=result;f.argtypes=types;return f
    init=fn('pp_rmlui_initialize_backend',c.c_int,[c.c_int,c.c_int,c.c_float,c.c_char_p,c.c_int])
    shutdown=fn('pp_rmlui_shutdown',None,[]);update_raw=fn('pp_rmlui_update',None,[])
    render=fn('pp_rmlui_render',None,[c.c_int,c.c_int])
    def update():update_raw();render(width,height)
    open_document=fn('pp_rmlui_document_open',c.c_uint64,[c.c_char_p,c.c_int])
    focus=fn('pp_rmlui_document_focus',c.c_int,[c.c_uint64,c.c_char_p])
    text=fn('pp_rmlui_document_set_text',c.c_int,[c.c_uint64,c.c_char_p,c.c_char_p])
    boolean=fn('pp_rmlui_document_set_bool',c.c_int,[c.c_uint64,c.c_char_p,c.c_int])
    get=fn('pp_rmlui_document_element_bounds',c.c_int,[c.c_uint64,c.c_char_p]+[c.POINTER(c.c_float)]*4)
    semantics=fn('pp_rmlui_document_accessibility_snapshot',c.c_int,[c.c_uint64,c.POINTER(c.c_ubyte),c.c_int])
    checks=[];cases=[];completed=False;error=None;initialized=False
    def check(value,name):
        checks.append({'name':name,'passed':bool(value)})
        if not value:raise AssertionError(name)
    def inside(rect,clip):
        return rect[2]>0 and rect[3]>0 and rect[0]>=clip[0] and rect[1]>=clip[1] and rect[0]+rect[2]<=clip[0]+clip[2] and rect[1]+rect[3]<=clip[1]+clip[3]
    try:
        for density in (1,2):
            width,height=640*density,360*density
            initialized=init(width,height,density,str(assets).encode(),1)==1
            check(initialized,f'density{density}: actual native CPU DrawList initialization')
            doc=open_document(b'pages/theatre/timeline.rml',1);check(doc!=0,f'density{density}: actual packaged Timeline modal opens')
            def set_text(name,value):
                check(text(doc,name.encode(),value.encode())==1,f'density{density}: real native binding '+name)
            set_text('replay_timeline_window','FRAMES 0 — 1979 // 1× ZOOM // REVIEWED PLAYHEAD 2')
            set_text('replay_timeline_focus','FOCUSED KEY 2. Toggle or range selects saved keys; dragging moves the selected group.')
            set_text('replay_timeline_selection','SELECTED: 2')
            for lane,count in [('events',0),('bookmarks',1),('ranges',10),('reel',0)]:
                set_text('replay_timeline_'+lane+'_count',lane.upper()+' // '+str(count)+' REVIEWED ENTRIES')
            update()
            def bounds(name):
                values=[c.c_float() for _ in range(4)]
                valid=get(doc,name.encode(),*(c.byref(v) for v in values))
                return [v.value for v in values] if valid==1 else None
            def picture():
                panel=bounds('replay_timeline_panel');camera=bounds('replay_timeline_camera');seek=bounds('replay_timeline_seek')
                assert panel is not None and camera is not None
                left,top=max(0,panel[0]+density),max(0,panel[1]+density)
                right,bottom=min(width,panel[0]+panel[2]-density),min(height,panel[1]+panel[3]-density)
                clip=[left,top,right-left,bottom-top]
                return {'panel':panel,'clip':clip,'camera':camera,'seek':seek,'cameraInsideClip':inside(camera,clip),'seekInsideClip':seek is not None and inside(seek,clip)}
            check(focus(doc,b'replay_timeline_refresh')==1,f'density{density}: actual native Refresh focus')
            update();before=picture();(a.output/('density'+str(density)+'-initial.json')).write_text(json.dumps(before,indent=2));check(before['cameraInsideClip'],f'density{density}: initial camera lane is fully visible')
            set_text('rect:replay_timeline_key_0','0,2,10,'+str(before['camera'][3]-4))
            set_text('replay_timeline_key_0','Actual native camera key')
            update()
            check(focus(doc,b'replay_timeline_seek')==1,f'density{density}: actual native Go focus with normal nearest scrolling')
            update();after=picture()
            check(after['seekInsideClip'],f'density{density}: Go is fully visible inside actual authored scroll region')
            check(not after['cameraInsideClip'] and after['camera'][1]<after['clip'][1],f'density{density}: the same focus scroll moves camera above viewport, reproducing actual68 rejection')
            buffer=(c.c_ubyte*(1024*1024+1))();length=semantics(doc,buffer,len(buffer));check(0<length<len(buffer),f'density{density}: actual native accessibility visibility projection is available')
            nodes=json.loads(bytes(buffer[:length]))['nodes'];go=next(row for row in nodes if row['id']=='replay_timeline_seek');key=next(row for row in nodes if row['id']=='replay_timeline_key_0')
            (a.output/('density'+str(density)+'-semantic.json')).write_text(json.dumps({'go':go,'key':key,'picture':after},indent=2))
            check(all(abs(go[name]-after['seek'][i])<.001 for i,name in enumerate(('x','y','width','height'))),f'density{density}: semantic projection reports same actual transformed Go bounds')
            check(key['offscreen'],f'density{density}: native semantic projection also rejects the offscreen camera key')
            # Current830b has an independently preserved mixed-transform clipping
            # classification failure: Go may report offscreen despite these measured
            # transformed bounds being visibly inside its transformed scroll panel.
            # Do not turn that flag into lease authority or claim it was qualified.
            check(boolean(doc,b'replay_timeline_seek',0)==1,f'density{density}: real native Go hide binding accepted')
            update();hidden=picture();check(hidden['seek'] is None,f'density{density}: hidden Go has no actual visible native bounds')
            cases.append({'density':density,'framebuffer':[width,height],'client':[640,360],'beforeGoFocus':before,'afterGoFocus':after,'hiddenGo':hidden,'semanticProjection':{'go':go,'key':key}})
            shutdown();initialized=False
        completed=True
    except BaseException as caught:
        error=str(caught);raise
    finally:
        if initialized:shutdown()
        (a.output/'acceptance.json').write_text(json.dumps({'passed':completed and error is None,'error':error,'claim':'Actual pinned native CPU DOM scroll/visibility geometry only; no GL/swap/world claim','library':str(a.library.resolve()),'librarySha256':a.expected_sha,'libraryUnchanged':sha(a.library)==a.expected_sha,'cases':cases,'checks':checks,'knownLimit':'Native830b accessibility offscreen flag can misclassify transformed visible Go; raw flag recorded only, not qualified or used as lease authority.'},indent=2)+'\n')
    print('PASS',len(checks),'actual native compact Timeline scroll geometry checks (CPU only)')


if __name__=='__main__':main()
