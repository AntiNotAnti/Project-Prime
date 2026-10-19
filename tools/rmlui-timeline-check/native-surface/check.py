"""Real830b CPU RmlUi focus/scroll regression for the Timeline drag canvas."""
import argparse
import ctypes as c
import hashlib,json,shutil
from pathlib import Path

p=argparse.ArgumentParser();p.add_argument('--library',type=Path,required=True);p.add_argument('--assets',type=Path,required=True);p.add_argument('--timeline',type=Path,required=True);p.add_argument('--expected-sha',required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args()
LIB=a.library.resolve();ASSETS=a.assets.resolve();HERE=a.output.resolve();HERE.mkdir(parents=True,exist_ok=False);PIN=a.expected_sha
candidate=a.timeline.read_text();target='<div id="replay_timeline_surface" tabindex="0" data-focus-scroll="none">'
assert candidate.count(target)==1,'Current Timeline must declare local canvas no-scroll policy exactly once'
(HERE/'timeline.rml').write_text(candidate);(HERE/'timeline.before.rml').write_text(candidate.replace(target,'<div id="replay_timeline_surface" tabindex="0">'))
assert hashlib.sha256(LIB.read_bytes()).hexdigest()==PIN
native=c.CDLL(str(LIB))
def function(name,result,types):
    fn=getattr(native,name);fn.restype=result;fn.argtypes=types;return fn
init=function('pp_rmlui_initialize_backend',c.c_int,[c.c_int,c.c_int,c.c_float,c.c_char_p,c.c_int])
close=function('pp_rmlui_shutdown',None,[]);update=function('pp_rmlui_update',None,[]);render=function('pp_rmlui_render',None,[c.c_int,c.c_int])
open_doc=function('pp_rmlui_document_open',c.c_uint64,[c.c_char_p,c.c_int]);focus=function('pp_rmlui_document_focus',c.c_int,[c.c_uint64,c.c_char_p])
text=function('pp_rmlui_document_set_text',c.c_int,[c.c_uint64,c.c_char_p,c.c_char_p])
get=function('pp_rmlui_document_element_bounds',c.c_int,[c.c_uint64,c.c_char_p]+[c.POINTER(c.c_float)]*4)
checks=[];cases=[];complete=False;error=None;initialized=False
def check(value,name):
    checks.append({'name':name,'passed':bool(value)})
    if not value:raise AssertionError(name)
try:
    for density in (1,2):
        for variant in ('before','fixed'):
            output=HERE/(str(density)+'-'+variant);output.mkdir(exist_ok=False)
            assets=output/'assets';shutil.copytree(ASSETS,assets)
            shutil.copy2(HERE/('timeline.before.rml' if variant=='before' else 'timeline.rml'),assets/'pages/theatre/timeline.rml')
            w,h=640*density,360*density
            initialized=init(w,h,density,str(assets).encode(),1)==1;check(initialized,f'{density}/{variant}: actual CPU DrawList bridge initialized')
            doc=open_doc(b'pages/theatre/timeline.rml',1);check(doc!=0,f'{density}/{variant}: actual Timeline opens')
            for name,value in {'replay_timeline_window':'FRAMES 0 — 1979 // 1× ZOOM // REVIEWED PLAYHEAD 1319','replay_timeline_focus':'FOCUSED KEY 2. Toggle or range selects saved keys; dragging moves the selected group.','replay_timeline_selection':'SELECTED: 2, 659, 1319'}.items():
                check(text(doc,name.encode(),value.encode())==1,f'{density}/{variant}: actual binding '+name)
            def settle():update();render(w,h)
            def bounds(id):
                values=[c.c_float() for _ in range(4)];assert get(doc,id.encode(),*(c.byref(v) for v in values))==1
                return [v.value for v in values]
            settle();camera=bounds('replay_timeline_camera')
            check(text(doc,b'rect:replay_timeline_key_0',('0,2,10,'+str(camera[3]-4)).encode())==1,f'{density}/{variant}: actual drawn marker geometry')
            check(focus(doc,b'replay_timeline_refresh')==1,f'{density}/{variant}: initial actual control focus');settle()
            check(focus(doc,b'replay_timeline_key_0')==1,f'{density}/{variant}: actual camera marker focus');settle()
            before={'panel':bounds('replay_timeline_panel'),'camera':bounds('replay_timeline_camera'),'key':bounds('replay_timeline_key_0'),'surface':bounds('replay_timeline_surface')}
            check(focus(doc,b'replay_timeline_surface')==1,f'{density}/{variant}: actual canvas focus during pointer movement');settle()
            after={'panel':bounds('replay_timeline_panel'),'camera':bounds('replay_timeline_camera'),'key':bounds('replay_timeline_key_0'),'surface':bounds('replay_timeline_surface')}
            cases.append({'density':density,'variant':variant,'beforeCanvasFocus':before,'afterCanvasFocus':after})
            if variant=='before':check(after!=before,f'{density}/{variant}: old ordinary focus scroll moves exact captured canvas geometry')
            else:check(after==before,f'{density}/{variant}: authored canvas no-scroll policy preserves exact captured camera/key/surface geometry')
            check(focus(doc,b'replay_timeline_seek')==1,f'{density}/{variant}: ordinary Go focus remains available');settle()
            check(bounds('replay_timeline_camera')!=after['camera'],f'{density}/{variant}: ordinary Go still scrolls normally, canvas policy is local')
            close();initialized=False
    complete=True
except BaseException as caught:error=str(caught);raise
finally:
    if initialized:close()
    (HERE/'acceptance.json').write_text(json.dumps({'passed':complete and error is None,'error':error,'claim':'Actual pinned830b CPU DrawList DOM focus/scroll geometry only; no GL/window/world/sidecar acceptance','nativeSha256':PIN,'timelineSource':str(a.timeline.resolve()),'timelineSha256':hashlib.sha256(a.timeline.read_bytes()).hexdigest(),'libraryUnchanged':hashlib.sha256(LIB.read_bytes()).hexdigest()==PIN,'cases':cases,'checks':checks},indent=2)+'\n')
print('PASS',len(checks),'actual native Timeline surface focus/scroll checks (CPU only)')
