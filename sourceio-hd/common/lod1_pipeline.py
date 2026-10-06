"""Config-driven accepted LOD0 -> lower-detail native LOD1 build/validation."""
import argparse,json,subprocess,sys
from pathlib import Path
from runtime import run
from install import validate,digest
COMMON=Path(__file__).resolve().parent
p=argparse.ArgumentParser();p.add_argument('--config',required=True);p.add_argument('--output',required=True)
p.add_argument('--exe',default='/Applications/Project Prime.app/Contents/MacOS/ProjectPrime')
p.add_argument('--userdata',required=True);p.add_argument('--blender',default='/Applications/Blender.app/Contents/MacOS/Blender')
a=p.parse_args();cp=Path(a.config).resolve();root=Path(a.output).resolve();cfg=json.loads(cp.read_text());root.mkdir(parents=True,exist_ok=True)
assert not (root/'RELEASE-LOCK.json').exists(),'Frozen output; choose a new directory'
for receipt in ['audit.json','build-result.json','material-preservation.json','validation.log']:
 (root/receipt).unlink(missing_ok=True)
inputs=[cp,*[COMMON/name for name in ['lod1_pipeline.py','lod1_convert.py','lod1_audit.py','lod1_materials.py','lod1_uv.py','binds.py','glb.py']]]
input_hashes={str(path):digest(path) for path in inputs}
(root/'pipeline-inputs.json').write_text(json.dumps(input_hashes,indent=2)+'\n')
if not (root/'kit/native-reference.json').exists():
 run(a.exe,['-charactermodelkit',cfg['hunter'],'-output',root/'kit'],root/'kit-generation.log',userdata=a.userdata)
exporter=root/'kit/prepare-biped-lod1-weighted4.py';exporter_sha=digest(exporter)
for script in ['lod1_convert','lod1_audit']:
 with (root/(script+'.log')).open('w') as log:
  subprocess.run([a.blender,'-b','--python-exit-code','1','--python',str(COMMON/(script+'.py')),'--',
   '--config',str(cp),'--output',str(root)],stdout=log,stderr=subprocess.STDOUT,check=True)
assert digest(exporter)==exporter_sha
assert {str(path):digest(path) for path in inputs}==input_hashes,'Conversion inputs changed while building'
run(a.exe,['-charactermodelvalidate',root/'kit/starter'],root/'validation.log',userdata=a.userdata)
print('LOD1_BUILD_VALIDATED',cfg['hunter'],root,flush=True)
