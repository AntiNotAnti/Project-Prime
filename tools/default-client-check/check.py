#!/usr/bin/env python3
"""Check evaluated default/compatibility/Studio project boundaries without building outputs."""
import argparse,json,subprocess
from pathlib import Path

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--dotnet',default='dotnet')
parser.add_argument('--repository',type=Path,default=Path(__file__).resolve().parents[2])
parser.add_argument('--output',type=Path)
a=parser.parse_args();root=a.repository.resolve();report=[]
props='MphReadAvalonia,MphReadRmlUi,MphReadRmlUiEnabled,MphReadNativeClient,MphReadServer,MphReadStudioEngine,MphReadRmlUiAndroid,MphReadAndroidNativeClient,OutputType,PublishSingleFile,DefineConstants'
def evaluate(project,values):
    command=[a.dotnet,'msbuild',str(root/project),'-nologo','-getProperty:'+props,'-getItem:Compile,PackageReference,ProjectReference,AvaloniaResource']
    command += ['-p:'+key+'='+value for key,value in values.items()]
    result=subprocess.run(command,cwd=root,capture_output=True,text=True,check=True)
    return json.loads(result.stdout)
def check(name,project,values,*,toolkit,native=False,android=False,studio=False,server=False):
    result=evaluate(project,values);p=result['Properties'];items=result['Items']
    packages=[x['Identity'] for x in items.get('PackageReference',[])];avalonia=[x for x in packages if x.lower().startswith('avalonia')]
    if bool(avalonia)!=toolkit:raise RuntimeError(name+': incorrect toolkit dependency boundary')
    if native and (p['MphReadAvalonia']!='false' or p['MphReadRmlUi']!='true' or p['MphReadNativeClient']!='true'):raise RuntimeError(name+': ordinary desktop did not select the native-only graph')
    if android and (p['MphReadAvalonia']!='false' or p['MphReadRmlUiAndroid']!='true' or p['MphReadAndroidNativeClient']!='true'):raise RuntimeError(name+': ordinary Android did not select the native-only graph')
    source=[x['Identity'].replace('\\','/') for x in items.get('Compile',[])]
    if android:
        if not any(x.endswith('/Native/NativeMainActivity.cs') or x=='Native/NativeMainActivity.cs' for x in source):raise RuntimeError(name+': real native Activity excluded')
        if any(x=='MainActivity.cs' or x.endswith('/AndroidApp.cs') or '/Mods/Launcher/Gui/HomeView.cs' in x for x in source):raise RuntimeError(name+': legacy entry/controls included')
    if name=='studio-isolated-engine' and (p['MphReadRmlUi']!='false' or p['MphReadNativeClient']!='false' or p['OutputType']!='Library' or p['PublishSingleFile']!='false'):
        raise RuntimeError(name+': Studio engine includes a client UI payload or executable projection')
    if not toolkit and items.get('AvaloniaResource'):raise RuntimeError(name+': toolkit resources included')
    if server and ('MPHREAD_SHELL' in p['DefineConstants'] or 'MPHREAD_RMLUI' in p['DefineConstants']):raise RuntimeError(name+': server UI feature included')
    if studio:
        ref=next(x for x in items['ProjectReference'] if x['Identity'].replace('\\','/').endswith('/MphRead/MphRead.StudioEngine.csproj'))
        expected={'MphReadAvalonia=true','MphReadRmlUi=false','MphReadRmlUiPoc=false','MphReadStudioEngine=true','MphReadServer=false'}
        if not expected.issubset(set(ref.get('AdditionalProperties','').split(';'))):raise RuntimeError(name+': Studio engine properties not isolated')
        if not {'MphReadRmlUiEnabled','MphReadNativeClient','MphReadNativeDefault'}.issubset(set(ref.get('GlobalPropertiesToRemove','').split(';'))):raise RuntimeError(name+': game command-line properties can leak into Studio')
    report.append({'case':name,'project':project,'properties':p,'avaloniaPackages':avalonia,'compileCount':len(source)})
for rid in ('win-x64','linux-x64','osx-arm64','osx-x64'):
    check('default-'+rid,'src/MphRead/MphRead.csproj',{'RuntimeIdentifier':rid},toolkit=False,native=True)
check('compatibility','src/MphRead/MphRead.csproj',{'RuntimeIdentifier':'linux-x64','MphReadAvalonia':'true'},toolkit=True)
check('studio-isolated-engine','src/MphRead/MphRead.StudioEngine.csproj',{'RuntimeIdentifier':'linux-x64','MphReadAvalonia':'false','MphReadRmlUi':'true'},toolkit=True)
check('studio-reference-with-inherited-game-flags','src/ProjectPrime.Studio/ProjectPrime.Studio.csproj',{'RuntimeIdentifier':'linux-x64','MphReadAvalonia':'false','MphReadRmlUi':'true','MphReadNativeClient':'true'},toolkit=True,studio=True)
for rid in ('linux-x64','linux-arm64','win-x64'):
    check('server-'+rid,'src/MphRead/MphRead.csproj',{'RuntimeIdentifier':rid,'MphReadServer':'true'},toolkit=False,server=True)
for rid in ('android-arm64','android-x64'):
    check('default-'+rid,'src/MphRead.Android/MphRead.Android.csproj',{'RuntimeIdentifier':rid},toolkit=False,android=True)
check('android-compatibility','src/MphRead.Android/MphRead.Android.csproj',{'RuntimeIdentifier':'android-arm64','MphReadAvalonia':'true'},toolkit=True)
if a.output:
    a.output.parent.mkdir(parents=True,exist_ok=True);a.output.write_text(json.dumps(report,indent=2)+'\n')
print(f'DEFAULT CLIENT BOUNDARY PASS {len(report)} evaluated cases; actual compilation and package checks remain required.')
