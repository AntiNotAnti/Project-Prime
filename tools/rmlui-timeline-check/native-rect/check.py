"""CPU-only actual native bridge/Timeline rectangle contract. No GL or window."""
import argparse
import ctypes as c
import hashlib
import json
import math
from pathlib import Path
import re
import shutil
import subprocess
import tempfile


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--presenter', type=Path, required=True)
    parser.add_argument('--library', type=Path, required=True)
    parser.add_argument('--assets', type=Path, required=True)
    parser.add_argument('--expected-sha', required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--dotnet', default='/Users/jarrett/.dotnet/dotnet')
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=False)
    observed_sha = sha(args.library)
    if observed_sha != args.expected_sha:
        raise AssertionError('Pinned actual bridge SHA mismatch')
    source = args.presenter.read_text()
    method = re.search(r'^    private static string Rect\(double x,double y,double w,double h\)=>[^\n]+;', source, re.M)
    if method is None:
        raise AssertionError('Actual Timeline formatter method not found')
    project = args.output/'formatter'
    project.mkdir()
    (project/'formatter.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings></PropertyGroup></Project>\n')
    # Compile the exact candidate method rather than mirroring the formatter in Python.
    (project/'Program.cs').write_text('using System.Globalization;\nusing System.Text.Json;\nclass Program {\n'+method.group(0)+'\nstatic void Main() { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR"); Console.WriteLine(JsonSerializer.Serialize(new string[] {Rect(12.125,2.5,10.75,20.875),Rect(0,0,1,1),Rect(double.NaN,2,3,4),Rect(double.PositiveInfinity,2,3,4)})); }\n}\n')
    subprocess.run([args.dotnet,'build',str(project/'formatter.csproj'),'-c','Release','-p:BuildProjectReferences=false','-v:q'], check=True)
    payloads = json.loads(subprocess.check_output([args.dotnet,str(project/'bin/Release/net10.0/formatter.dll')], text=True))
    checks = []
    def check(value, name):
        checks.append({'name':name,'passed':bool(value)})
        if not value:
            raise AssertionError(name)
    native = c.CDLL(str(args.library.resolve()))
    native.pp_rmlui_initialize_backend.argtypes = [c.c_int,c.c_int,c.c_float,c.c_char_p,c.c_int]
    native.pp_rmlui_initialize_backend.restype = c.c_int
    native.pp_rmlui_document_open.argtypes = [c.c_char_p,c.c_int]
    native.pp_rmlui_document_open.restype = c.c_uint64
    native.pp_rmlui_document_set_text.argtypes = [c.c_uint64,c.c_char_p,c.c_char_p]
    native.pp_rmlui_document_set_text.restype = c.c_int
    native.pp_rmlui_document_element_bounds.argtypes = [c.c_uint64,c.c_char_p]+[c.POINTER(c.c_float)]*4
    native.pp_rmlui_document_element_bounds.restype = c.c_int
    native.pp_rmlui_update.argtypes = []
    native.pp_rmlui_update.restype = None
    native.pp_rmlui_shutdown.argtypes = []
    native.pp_rmlui_shutdown.restype = None
    initialized = False
    completed = False
    error = None
    assets = args.output/'assets'
    shutil.copytree(args.assets, assets)
    try:
        initialized = native.pp_rmlui_initialize_backend(1280,720,1,str(assets).encode(),1) == 1
        check(initialized,'Actual pinned830b bridge initializes DrawList CPU backend without GL/window')
        document = native.pp_rmlui_document_open(b'pages/theatre/timeline.rml',1)
        check(document != 0,'Actual packaged Timeline RML opens in the real DOM')
        native.pp_rmlui_update()
        def bounds(element):
            values = [c.c_float() for _ in range(4)]
            valid = native.pp_rmlui_document_element_bounds(document,element.encode(),*(c.byref(v) for v in values))
            if valid != 1:
                raise AssertionError('No actual DOM bounds: '+element)
            return [v.value for v in values]
        def write(value):
            return native.pp_rmlui_document_set_text(document,b'rect:replay_timeline_key_0',value.encode())
        check(payloads[0] == '12.125,2.5,10.75,20.875','Actual candidate formatter uses invariant commas under fr-FR culture')
        check(write('12.125 2.5 10.75 20.875') == 0,'Original space-separated Timeline rectangle is rejected by actual native parser')
        check(write(payloads[0]) == 1,'Actual candidate comma-separated Timeline rectangle is accepted')
        native.pp_rmlui_update()
        lane, key = bounds('replay_timeline_camera'), bounds('replay_timeline_key_0')
        expected = [lane[0]+12.125,lane[1]+2.5,10.75,20.875]
        check(all(math.isclose(a,b,abs_tol=.01) for a,b in zip(key,expected)), 'Actual DOM stores exact candidate x/y/width/height')
        for bad in [payloads[2],payloads[3],'1,2,-3,4','1,2,3,-4','100001,2,3,4','1,2,100001,4','1,2,3,4,5','1,2,3','1,2,3,4garbage','1,2,1e100,4']:
            check(write(bad) == 0,'Native rectangle rejects invalid/nonfinite/out-of-range payload: '+bad)
            native.pp_rmlui_update()
            check(bounds('replay_timeline_key_0') == key,'Rejected rectangle leaves prior actual DOM bounds unchanged: '+bad)
        check(write(payloads[1]) == 1,'Small finite candidate rectangle remains accepted')
        native.pp_rmlui_update()
        tiny=bounds('replay_timeline_key_0')
        check(math.isclose(tiny[2],1,abs_tol=.01) and math.isclose(tiny[3],1,abs_tol=.01),'Exact one-pixel bounds survive real Timeline authored styles')
        completed = True
    except BaseException as caught:
        error = str(caught)
        raise
    finally:
        if initialized:
            native.pp_rmlui_shutdown()
        (args.output/'acceptance.json').write_text(json.dumps({'passed':completed and error is None and all(x['passed'] for x in checks),'error':error,'claim':'Actual pinned native bridge CPU DrawList DOM contract only; no world/GL/window acceptance','library':str(args.library.resolve()),'librarySha256':observed_sha,'presenter':str(args.presenter.resolve()),'presenterSha256':sha(args.presenter),'exactCompiledMethod':method.group(0),'checks':checks,'libraryUnchanged':sha(args.library)==observed_sha},indent=2)+'\n')
    print('PASS',len(checks),'actual native Timeline rectangle checks (CPU only)')


if __name__ == '__main__':
    main()
